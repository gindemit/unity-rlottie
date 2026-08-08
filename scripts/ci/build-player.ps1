[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Unity,
    [Parameter(Mandatory = $true)]
    [string] $ProjectPath,
    [ValidateSet('Windows64', 'Android', 'WebGL')]
    [string] $Target = 'Windows64',
    [ValidateSet('Auto', 'BuiltIn', 'URP', 'HDRP')]
    [string] $Pipeline = 'Auto',
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
    '-ciOutputPath', $OutputPath,
    '-logFile', $LogFile
)

$process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru -NoNewWindow
$process.WaitForExit()
if ($process.ExitCode -ne 0) {
    Get-Content -LiteralPath $LogFile -Tail 200 -ErrorAction SilentlyContinue
    throw "Unity build failed with exit code $($process.ExitCode). See $LogFile"
}

if (-not (Test-Path -LiteralPath $OutputPath)) {
    throw "Unity reported success but the build output is missing: $OutputPath"
}

Write-Output "Build succeeded: $OutputPath"
