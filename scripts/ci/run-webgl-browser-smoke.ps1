[CmdletBinding()]
param(
    [string] $Unity,
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
. (Join-Path $PSScriptRoot 'assert-smoke-result.ps1')

$requiredPaths = @($Browser)
if (-not $SkipBuild) {
    if (-not $Unity -or -not $ProjectPath) {
        throw 'Unity and ProjectPath are required when building the WebGL player.'
    }
    $requiredPaths += @($Unity, $ProjectPath)
}
foreach ($requiredPath in $requiredPaths) {
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
    $url = "http://127.0.0.1:$Port/?lottieSmoke=true"
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
            -SimpleMatch 'RLottieSmokeResultBase64:' `
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
    'RLottieSmokeResultBase64:'
)
foreach ($marker in $requiredMarkers) {
    if (-not $browserOutput.Contains($marker)) {
        throw "Missing WebGL smoke marker '$marker'. See $browserLog"
    }
}

if ($browserOutput.Contains('RuntimeError: abort') -or $browserOutput.Contains('Aborted(')) {
    throw "WebGL runtime aborted. See $browserLog"
}

$payload = [regex]::Match($browserOutput, 'RLottieSmokeResultBase64:([A-Za-z0-9+/=]+)')
if (-not $payload.Success) {
    throw "Browser did not report completed WebGL assertions. See $browserLog"
}
$json = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload.Groups[1].Value))
$resultPath = Join-Path $resolvedOutput 'smoke-result.json'
[IO.File]::WriteAllText($resultPath, $json)
$result = $json | ConvertFrom-Json
$expectedApi = if ($WebGLVersion -eq '1') { 'OpenGLES2' } else { 'OpenGLES3' }
Assert-LottieSmokeResult -Result $result -Platform WebGL `
    -ExpectedGraphicsApi $expectedApi -ExpectedUploadBackend NativeWebGL `
    -MinimumSchemaVersion 3 -RequiredCheckNames @('exactColorCalibration') -RequireNativeUpload

Write-Output "WebGL $WebGLVersion browser smoke passed: $browserLog"
