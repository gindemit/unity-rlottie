[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $FirebaseLog,
    [Parameter(Mandatory = $true)][string] $ProjectId,
    [Parameter(Mandatory = $true)][string] $OutputDirectory,
    [Parameter(Mandatory = $true)]
    [ValidateSet('Vulkan', 'OpenGLES3')][string] $ExpectedGraphicsApi
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'assert-smoke-result.ps1')
$log = Get-Content -LiteralPath $FirebaseLog -Raw
$location = [regex]::Match($log,
    'https://console\.(?:developers|cloud)\.google\.com/storage/browser/([a-zA-Z0-9._/-]+)')
if (-not $location.Success) {
    throw 'Firebase did not report its results storage location.'
}
$root = 'gs://' + $location.Groups[1].Value.TrimEnd('/') + '/'
$objects = @(& gcloud storage ls --recursive $root "--project=$ProjectId")
if ($LASTEXITCODE -ne 0) { throw 'Could not list Firebase results.' }
$results = @($objects | Where-Object {
    $_.StartsWith($root, [StringComparison]::Ordinal) -and
    $_.EndsWith('/lottie-smoke-result.json', [StringComparison]::Ordinal)
})
if ($results.Count -eq 0) {
    throw 'Firebase did not collect a completed Android rendering result.'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null
$backend = if ($ExpectedGraphicsApi -eq 'Vulkan') { 'NativeVulkan' } else { 'NativeOpenGL' }
for ($index = 0; $index -lt $results.Count; $index++) {
    $destination = Join-Path $output "smoke-result-$index.json"
    & gcloud storage cp $results[$index] $destination "--project=$ProjectId"
    if ($LASTEXITCODE -ne 0) { throw 'Could not download Android rendering results.' }
    $result = Get-Content -LiteralPath $destination -Raw | ConvertFrom-Json
    Assert-LottieSmokeResult -Result $result -Platform Android `
        -ExpectedGraphicsApi $ExpectedGraphicsApi -ExpectedUploadBackend $backend `
        -MinimumSchemaVersion 3 -RequireNativeUpload `
        -RequiredCheckNames @('exactColorCalibration', 'semanticColorOverrides', 'animationLifecycleStress')
    Write-Output "Android $ExpectedGraphicsApi rendering assertions passed: $destination"
}
