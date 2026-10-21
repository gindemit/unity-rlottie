[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Player,
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,
    [Parameter(Mandatory = $true)]
    [ValidateSet('Gamma', 'Linear')]
    [string] $ExpectedColorSpace,
    [string] $ExpectedGraphicsVendor,
    [ValidateRange(2, 100)]
    [int] $RelaunchCount = 3,
    [int] $RunSeconds = 45
)

$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'run-windows-player-smoke.ps1'
$Player = (Resolve-Path -LiteralPath $Player).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$runs = @()
for ($index = 1; $index -le $RelaunchCount; $index++) {
    $stem = 'vulkan-relaunch-{0:D2}' -f $index
    $logFile = Join-Path $OutputDirectory "$stem.log"
    $resultFile = Join-Path $OutputDirectory "$stem.json"
    & $runner -Player $Player -LogFile $logFile -ResultFile $resultFile `
        -RunSeconds $RunSeconds -ExpectedGraphicsApi Vulkan `
        -ExpectedColorSpace $ExpectedColorSpace -ExpectedUploadBackend NativeVulkan `
        -ExpectedGraphicsVendor $ExpectedGraphicsVendor -RequireGraphicsDeviceMetadata `
        -MinimumSchemaVersion 2 `
        -RequiredCheckNames exactColorCalibration,animationLifecycleStress `
        -RequireNativeUpload

    $result = Get-Content -Raw -LiteralPath $resultFile | ConvertFrom-Json
    $runs += [pscustomobject]@{
        run = $index
        passed = [bool] $result.passed
        graphicsApi = [string] $result.graphicsApi
        graphicsDevice = [string] $result.graphicsDevice
        graphicsDeviceVendor = [string] $result.graphicsDeviceVendor
        graphicsDeviceVendorId = [int] $result.graphicsDeviceVendorId
        graphicsDeviceId = [int] $result.graphicsDeviceId
        graphicsDeviceVersion = [string] $result.graphicsDeviceVersion
        operatingSystem = [string] $result.operatingSystem
        colorSpace = [string] $result.colorSpace
        resultFile = $resultFile
        logFile = $logFile
    }
}

$identities = @($runs | ForEach-Object {
    "$($_.graphicsDeviceVendorId):$($_.graphicsDeviceId):$($_.graphicsDeviceVersion)"
} | Select-Object -Unique)
if ($identities.Count -ne 1) {
    throw "Graphics-device identity changed across relaunches: $($identities -join ', ')"
}

$manifest = [pscustomobject]@{
    schemaVersion = 1
    testedAtUtc = [DateTime]::UtcNow.ToString('o')
    relaunchCount = $RelaunchCount
    processRestartScope = 'Player process teardown and relaunch; not an OS-level GPU driver reset.'
    runs = $runs
}
$manifestPath = Join-Path $OutputDirectory 'vulkan-relaunch-summary.json'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Output "Windows Vulkan relaunch coverage passed $RelaunchCount runs. Summary: $manifestPath"
