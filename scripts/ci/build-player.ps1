[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Unity,
    [Parameter(Mandatory = $true)]
    [string] $ProjectPath,
    [ValidateSet('Windows64', 'Linux64', 'Android', 'WebGL', 'macOS', 'iOS')]
    [string] $Target = 'Windows64',
    [ValidateSet('Auto', 'BuiltIn', 'URP', 'HDRP')]
    [string] $Pipeline = 'Auto',
    [ValidateSet('Auto', 'Direct3D11', 'Direct3D12', 'OpenGLCore', 'Vulkan', 'OpenGLES2', 'OpenGLES3')]
    [string] $GraphicsApi = 'Auto',
    [ValidateSet('Auto', 'Gamma', 'Linear')]
    [string] $ColorSpace = 'Auto',
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$')]
    [string] $AndroidPackage,
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

$Unity = (Resolve-Path -LiteralPath $Unity).Path
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$LogFile = [IO.Path]::GetFullPath($LogFile)
$entryPoint = switch ($Target) {
    'WebGL' { Join-Path $OutputPath 'index.html' }
    'iOS' { Join-Path $OutputPath 'Unity-iPhone.xcodeproj/project.pbxproj' }
    'macOS' { Join-Path $OutputPath 'Contents/Info.plist' }
    default { $OutputPath }
}
if ($entryPoint -eq $LogFile) { throw 'Choose different build and log output paths.' }

$outputDirectory = Split-Path -Parent $OutputPath
if ($Target -eq 'WebGL') {
    $outputDirectory = $OutputPath
}
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogFile) | Out-Null

$buildPath = $OutputPath
$stageApk = $Target -eq 'Android' -and [IO.File]::Exists($OutputPath)
if ($stageApk) {
    # Publish a replacement APK only after Unity has finished writing it.
    $buildPath = Join-Path $outputDirectory ('rlottie-' + [Guid]::NewGuid().ToString('N') + [IO.Path]::GetExtension($OutputPath))
    $entryPoint = $buildPath
}
$arguments = @(
    '-batchmode',
    '-quit',
    '-projectPath', "`"$ProjectPath`"",
    '-executeMethod', 'RLottie.CI.BuildMatrix.Build',
    '-ciTarget', $Target,
    '-ciPipeline', $Pipeline,
    '-ciGraphicsApi', $GraphicsApi,
    '-ciColorSpace', $ColorSpace,
    '-ciOutputPath', "`"$buildPath`"",
    '-logFile', "`"$LogFile`""
)
if ($AndroidPackage) {
    if ($Target -ne 'Android') { throw '-AndroidPackage requires -Target Android.' }
    $arguments += @('-ciAndroidPackage', $AndroidPackage)
}

try {
    if (Test-Path -LiteralPath $LogFile -PathType Leaf) { Remove-Item -LiteralPath $LogFile -Force }
    $windowOptions = @{}
    if ($env:OS -eq 'Windows_NT') { $windowOptions.WindowStyle = 'Hidden' }
    $process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru @windowOptions
    $process.WaitForExit()
    $process.Refresh()
    $unityExitCode = $process.ExitCode
    $successMarker = Select-String -LiteralPath $LogFile -SimpleMatch 'RLottie CI result: Succeeded' -Quiet -ErrorAction SilentlyContinue
    if ($null -eq $unityExitCode -or $unityExitCode -ne 0 -or -not $successMarker) {
        Get-Content -LiteralPath $LogFile -Tail 200 -ErrorAction SilentlyContinue
        throw "Unity build failed with exit code $unityExitCode. See $LogFile"
    }
    if (-not [IO.File]::Exists($entryPoint) -or (Get-Item -LiteralPath $entryPoint).Length -eq 0) {
        throw "Unity did not produce the expected build output: $entryPoint"
    }
    if ($stageApk) { [IO.File]::Replace($buildPath, $OutputPath, [NullString]::Value) }
}
finally {
    if ($stageApk -and [IO.File]::Exists($buildPath)) { [IO.File]::Delete($buildPath) }
}

Write-Output "Build succeeded: $OutputPath"
