[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Unity,
    [Parameter(Mandatory = $true)]
    [string] $ProjectPath,
    [ValidateSet('Windows64', 'Linux64', 'Android', 'WebGL')]
    [string] $Target = 'Windows64',
    [ValidateSet('Auto', 'BuiltIn', 'URP', 'HDRP')]
    [string] $Pipeline = 'Auto',
    [ValidateSet('Auto', 'Vulkan', 'OpenGLES3')]
    [string] $GraphicsApi = 'Auto',
    [Parameter(Mandatory = $true)]
    [string] $OutputPath,
    [Parameter(Mandatory = $true)]
    [string] $LogFile
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Unity)) {
    throw "Unity executable not found: $Unity"
}
if (-not (Test-Path -LiteralPath $ProjectPath)) {
    throw "Unity project not found: $ProjectPath"
}

$outputDirectory = Split-Path -Parent $OutputPath
if ($Target -eq 'WebGL') {
    $outputDirectory = $OutputPath
}
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogFile) | Out-Null

$arguments = @(
    '-batchmode',
    '-quit',
    '-projectPath', $ProjectPath,
    '-executeMethod', 'RLottie.CI.BuildMatrix.Build',
    '-ciTarget', $Target,
    '-ciPipeline', $Pipeline,
    '-ciGraphicsApi', $GraphicsApi,
    '-ciOutputPath', $OutputPath,
    '-logFile', $LogFile
)

$process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru -NoNewWindow
$process.WaitForExit()
$process.Refresh()
$unityExitCode = $process.ExitCode
$successMarker = Select-String -LiteralPath $LogFile -SimpleMatch 'RLottie CI result: Succeeded' -Quiet -ErrorAction SilentlyContinue
if (($null -ne $unityExitCode -and $unityExitCode -ne 0) -or -not $successMarker) {
    Get-Content -LiteralPath $LogFile -Tail 200 -ErrorAction SilentlyContinue
    throw "Unity build failed with exit code $unityExitCode. See $LogFile"
}

if (-not (Test-Path -LiteralPath $OutputPath)) {
    throw "Unity reported success but the build output is missing: $OutputPath"
}

Write-Output "Build succeeded: $OutputPath"
