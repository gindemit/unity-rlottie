[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Player,
    [Parameter(Mandatory = $true)]
    [string] $LogFile,
    [string] $ResultFile,
    [int] $RunSeconds = 20,
    [ValidateRange(1, 10000)]
    [int] $LifecycleCycles = 12,
    [ValidateSet('Direct3D11', 'Direct3D12', 'OpenGLCore', 'Vulkan')]
    [string] $ExpectedGraphicsApi,
    [ValidateSet('Gamma', 'Linear')]
    [string] $ExpectedColorSpace,
    [string] $ExpectedUploadBackend,
    [string] $ExpectedGraphicsVendor,
    [ValidateRange(1, 2147483647)]
    [int] $MinimumSchemaVersion = 4,
    [string[]] $RequiredCheckNames,
    [switch] $RequireGraphicsDeviceMetadata,
    [switch] $RequireNativeUpload
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'assert-smoke-result.ps1')
$Player = (Resolve-Path -LiteralPath $Player).Path
$LogFile = [IO.Path]::GetFullPath($LogFile)
$ResultFile = if ($ResultFile) { [IO.Path]::GetFullPath($ResultFile) } else { "$LogFile.smoke.json" }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogFile) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ResultFile) | Out-Null
if (Test-Path -LiteralPath $ResultFile -PathType Leaf) {
    Remove-Item -LiteralPath $ResultFile -Force
}

$process = Start-Process -FilePath $Player `
    -ArgumentList @(
        '-logFile', "`"$LogFile`"",
        '-screen-width', '1280',
        '-screen-height', '720',
        '-lottieSmokeResult', "`"$ResultFile`"",
        '-lottieSmokeQuit',
        '-lottieLifecycleCycles', $LifecycleCycles
    ) `
    -WindowStyle Hidden -PassThru
try {
    $exited = $process.WaitForExit($RunSeconds * 1000)
    if (-not $exited) {
        throw "RLottie player did not finish and exit within $RunSeconds seconds. See $LogFile"
    }
    if ($process.ExitCode -ne 0 -and -not (Test-Path -LiteralPath $ResultFile -PathType Leaf)) {
        throw "RLottie player exited with code $($process.ExitCode)."
    }
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
}

if (-not (Test-Path -LiteralPath $ResultFile)) {
    throw "RLottie smoke result is missing: $ResultFile"
}

$result = Get-Content -Raw -LiteralPath $ResultFile | ConvertFrom-Json
$effectiveMinimumSchemaVersion = [Math]::Max(3, $MinimumSchemaVersion)
$expectedCycles = if ($result.schemaVersion -ge 4 -or $PSBoundParameters.ContainsKey('LifecycleCycles')) { $LifecycleCycles } else { 0 }
$requiredChecks = @($RequiredCheckNames) + 'semanticColorOverrides'
Assert-LottieSmokeResult -Result $result -Platform Windows -ExpectedGraphicsApi $ExpectedGraphicsApi `
    -ExpectedColorSpace $ExpectedColorSpace -ExpectedUploadBackend $ExpectedUploadBackend `
    -ExpectedGraphicsVendor $ExpectedGraphicsVendor `
    -MinimumSchemaVersion $effectiveMinimumSchemaVersion -RequiredCheckNames $requiredChecks `
    -ExpectedLifecycleCycles $expectedCycles `
    -RequireGraphicsDeviceMetadata:$RequireGraphicsDeviceMetadata `
    -RequireNativeUpload:$RequireNativeUpload

if ($process.ExitCode -ne 0) { throw "RLottie player exited with code $($process.ExitCode). See $LogFile" }
[ordered]@{
    platform = 'Windows'; result = 'Passed'; checks = $result.checks.Count; graphicsApi = $result.graphicsApi
    backend = $result.animatedImageUploadBackend; cycles = $result.completedLifecycleCycles; evidence = $ResultFile
} | ConvertTo-Json -Compress
