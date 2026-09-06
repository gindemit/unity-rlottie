[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Unity,
    [Parameter(Mandatory = $true)]
    [string] $ProjectPath,
    [ValidateSet('1', '2')]
    [string] $WebGLVersion,
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,
    [string] $Browser = 'C:\Program Files\Google\Chrome\Application\chrome.exe',
    [int] $Port = 8900,
    [int] $VirtualTimeBudgetMilliseconds = 30000,
    [switch] $InteractiveEditor,
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'

foreach ($requiredPath in @($Unity, $ProjectPath, $Browser)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required path not found: $requiredPath"
    }
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
$playerDirectory = Join-Path $resolvedOutput 'player'
$buildLog = Join-Path $resolvedOutput 'unity-build.log'
$browserLog = Join-Path $resolvedOutput 'browser.log'
$browserStdout = Join-Path $resolvedOutput 'browser.stdout.log'
$profileDirectory = Join-Path $resolvedOutput 'browser-profile'
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

if (-not $SkipBuild) {
    $unityArguments = @(
        '-quit',
        '-projectPath', $ProjectPath,
        '-executeMethod', 'RLottie.CI.WebGLBuildMatrix.Build',
        '-ciTarget', 'WebGL',
        '-ciPipeline', 'Auto',
        '-ciGraphicsApi', 'Auto',
        '-ciWebGLVersion', $WebGLVersion,
        '-ciOutputPath', $playerDirectory,
        '-logFile', $buildLog
    )
    if (-not $InteractiveEditor) {
        $unityArguments = @('-batchmode') + $unityArguments
    }
    $unityProcess = Start-Process -FilePath $Unity -ArgumentList $unityArguments -PassThru -NoNewWindow
    $unityProcess.WaitForExit()
    $unityProcess.Refresh()
    $success = Select-String -LiteralPath $buildLog -SimpleMatch 'RLottie CI result: Succeeded' -Quiet -ErrorAction SilentlyContinue
    if (($null -ne $unityProcess.ExitCode -and $unityProcess.ExitCode -ne 0) -or -not $success) {
        Get-Content -LiteralPath $buildLog -Tail 200 -ErrorAction SilentlyContinue
        throw "Unity WebGL $WebGLVersion build failed. See $buildLog"
    }
}

$indexPath = Join-Path $playerDirectory 'index.html'
if (-not (Test-Path -LiteralPath $indexPath)) {
    throw "WebGL player is missing: $indexPath"
}

$serverScript = Join-Path $PSScriptRoot 'webgl-smoke-server.js'
$serverProcess = Start-Process -FilePath 'node' `
    -ArgumentList @($serverScript, $playerDirectory, $Port) `
    -PassThru -WindowStyle Hidden

try {
    $url = "http://127.0.0.1:$Port/"
    $browserArguments = @(
        '--headless=new',
        '--enable-logging=stderr',
        '--enable-unsafe-swiftshader',
        '--no-first-run',
        '--no-default-browser-check',
        "--user-data-dir=$profileDirectory",
        '--window-size=1280,720',
        $url
    )
    if ($WebGLVersion -eq '1') {
        $browserArguments = @('--disable-webgl2') + $browserArguments
    }

    $browserProcess = Start-Process -FilePath $Browser -ArgumentList $browserArguments `
        -RedirectStandardError $browserLog -RedirectStandardOutput $browserStdout `
        -PassThru -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddMilliseconds($VirtualTimeBudgetMilliseconds + 60000)
    $completed = $false
    while (-not $browserProcess.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 500
        $completed = Select-String -LiteralPath $browserLog `
            -SimpleMatch '[Lottie INFO] [Lottie] Frame rendered successfully' `
            -Quiet -ErrorAction SilentlyContinue
        if ($completed) {
            break
        }
    }
    if (-not $browserProcess.HasExited) {
        $browserProcess.Kill()
        $browserProcess.WaitForExit()
    }
    if (-not $completed) {
        throw "Browser did not complete the WebGL smoke test before timeout. See $browserLog"
    }
    $browserProcess.Refresh()
} finally {
    if (-not $serverProcess.HasExited) {
        $serverProcess.Kill()
        $serverProcess.WaitForExit()
    }
}

$browserOutput = Get-Content -LiteralPath $browserLog -Raw
$expectedContext = if ($WebGLVersion -eq '1') { 'Creating WebGL 1.0 context' } else { 'Creating WebGL 2.0 context' }
$requiredMarkers = @(
    $expectedContext,
    '[LottiePlugin] Unity-owned WebGL native upload enabled',
    '[Lottie INFO] [Lottie] Frame rendered successfully'
)
foreach ($marker in $requiredMarkers) {
    if (-not $browserOutput.Contains($marker)) {
        throw "Missing WebGL smoke marker '$marker'. See $browserLog"
    }
}

if ($browserOutput.Contains('RuntimeError: abort') -or $browserOutput.Contains('Aborted(')) {
    throw "WebGL runtime aborted. See $browserLog"
}

Write-Output "WebGL $WebGLVersion browser smoke passed: $browserLog"
