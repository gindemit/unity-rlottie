[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsRoot,
    [ValidateRange(0, 100)]
    [double] $RegressionThresholdPercent = 5,
    [double] $MinimumAbsoluteRegressionMs = 0.05
)

$ErrorActionPreference = 'Stop'
$ResultsRoot = (Resolve-Path -LiteralPath $ResultsRoot).Path

function Get-Median {
    param([double[]] $Values)
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return 0.0 }
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if (($sorted.Count % 2) -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2.0
}

$observations = @()
foreach ($csvFile in Get-ChildItem -LiteralPath $ResultsRoot -Filter benchmark.csv -File -Recurse) {
    $metadataPath = Join-Path $csvFile.DirectoryName 'metadata.json'
    if (-not (Test-Path -LiteralPath $metadataPath)) { continue }
    $metadata = Get-Content -Raw -LiteralPath $metadataPath | ConvertFrom-Json
    foreach ($row in Import-Csv -LiteralPath $csvFile.FullName) {
        $scenario = if ($row.color_scenario) { $row.color_scenario } else { [string]$metadata.color_scenario }
        if ($scenario -notin @('baseline', 'palette_at_load', 'palette-at-load')) { continue }
        $observations += [pscustomobject]@{
            repository = [string]$metadata.repository
            unity_version = [string]$metadata.unity_version
            render_pipeline = [string]$metadata.render_pipeline
            graphics_api = [string]$metadata.graphics_api
            animation = [string]$row.animation
            instances = [int]$row.instances
            width = [int]$row.width
            height = [int]$row.height
            scenario = if ($scenario -eq 'baseline') { 'baseline' } else { 'palette_at_load' }
            mean_batch_ms = [double]::Parse($row.mean_batch_ms, [Globalization.CultureInfo]::InvariantCulture)
            p95_batch_ms = [double]::Parse($row.p95_batch_ms, [Globalization.CultureInfo]::InvariantCulture)
            load_ms = [double]::Parse($row.load_ms, [Globalization.CultureInfo]::InvariantCulture)
        }
    }
}

if ($observations.Count -eq 0) {
    throw "No paired color-scenario benchmark observations found under $ResultsRoot"
}

$comparisons = @()
$groups = $observations | Group-Object repository,unity_version,render_pipeline,graphics_api,animation,instances,width,height
foreach ($group in $groups) {
    $baseline = @($group.Group | Where-Object scenario -eq 'baseline')
    $palette = @($group.Group | Where-Object scenario -eq 'palette_at_load')
    if ($baseline.Count -eq 0 -or $palette.Count -eq 0) { continue }
    $baselineMean = Get-Median @($baseline | ForEach-Object mean_batch_ms)
    $paletteMean = Get-Median @($palette | ForEach-Object mean_batch_ms)
    $baselineP95 = Get-Median @($baseline | ForEach-Object p95_batch_ms)
    $paletteP95 = Get-Median @($palette | ForEach-Object p95_batch_ms)
    $meanDeltaMs = $paletteMean - $baselineMean
    $meanDeltaPercent = if ($baselineMean -gt 0) { 100.0 * $meanDeltaMs / $baselineMean } else { 0.0 }
    $p95DeltaPercent = if ($baselineP95 -gt 0) { 100.0 * ($paletteP95 - $baselineP95) / $baselineP95 } else { 0.0 }
    $regressed = $meanDeltaMs -gt $MinimumAbsoluteRegressionMs -and
        $meanDeltaPercent -gt $RegressionThresholdPercent
    $first = $group.Group[0]
    $comparisons += [pscustomobject]@{
        repository = $first.repository; unity_version = $first.unity_version
        render_pipeline = $first.render_pipeline; graphics_api = $first.graphics_api
        animation = $first.animation; instances = $first.instances; width = $first.width; height = $first.height
        samples_per_scenario = [Math]::Min($baseline.Count, $palette.Count)
        baseline_mean_batch_ms = $baselineMean; palette_mean_batch_ms = $paletteMean
        mean_delta_ms = $meanDeltaMs; mean_delta_percent = $meanDeltaPercent
        baseline_p95_batch_ms = $baselineP95; palette_p95_batch_ms = $paletteP95
        p95_delta_percent = $p95DeltaPercent; regressed = $regressed
    }
}

if ($comparisons.Count -eq 0) { throw 'No complete baseline/palette pairs were found.' }
$output = Join-Path $ResultsRoot 'color-override-comparison.csv'
$comparisons | Sort-Object repository,graphics_api,animation,width | Export-Csv -LiteralPath $output -NoTypeInformation -Encoding utf8
$regressions = @($comparisons | Where-Object regressed)
Write-Output "Compared $($comparisons.Count) paired cases; regressions=$($regressions.Count); report=$output"
if ($regressions.Count -gt 0) {
    $regressions | Format-Table repository,render_pipeline,graphics_api,animation,width,mean_delta_ms,mean_delta_percent -AutoSize | Out-String | Write-Output
    throw "Color-override render regression exceeded $RegressionThresholdPercent% and $MinimumAbsoluteRegressionMs ms in $($regressions.Count) paired cases."
}
