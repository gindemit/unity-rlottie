[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ResultsRoot,
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'

function Get-Average {
    param([object[]] $Rows, [string] $Property)
    return ($Rows | Measure-Object -Property $Property -Average).Average
}

function Format-Number {
    param([double] $Value, [int] $Decimals = 4)
    return $Value.ToString("F$Decimals", [Globalization.CultureInfo]::InvariantCulture)
}

$csvFiles = Get-ChildItem -LiteralPath $ResultsRoot -Recurse -Filter 'benchmark.csv' |
    Where-Object { $_.Directory.Name -like 'unity-*' } |
    Sort-Object FullName
if ($csvFiles.Count -eq 0) {
    throw "No benchmark CSV files found below $ResultsRoot"
}

$allRows = foreach ($csvFile in $csvFiles) {
    $metadataPath = Join-Path $csvFile.Directory.FullName 'metadata.json'
    if (-not (Test-Path -LiteralPath $metadataPath)) {
        throw "Missing metadata for $($csvFile.FullName)"
    }
    $metadata = Get-Content -Raw -LiteralPath $metadataPath | ConvertFrom-Json
    $rows = Import-Csv -LiteralPath $csvFile.FullName
    if ($rows.Count -ne 12) {
        throw "Expected 12 benchmark rows in $($csvFile.FullName), found $($rows.Count)"
    }
    foreach ($row in $rows) {
        [PSCustomObject][ordered]@{
            run = $metadata.run
            repository = $metadata.repository
            commit = $metadata.commit
            render_pipeline = $metadata.render_pipeline
            requested_graphics_api = $metadata.graphics_api
            temperature_before_c = $metadata.temperature_before_c
            temperature_after_c = $metadata.temperature_after_c
            timestamp_utc = $row.timestamp_utc
            animation = $row.animation
            instances = [int]$row.instances
            width = [int]$row.width
            height = [int]$row.height
            warmup_frames = [int]$row.warmup_frames
            sample_frames = [int]$row.sample_frames
            load_ms = [double]$row.load_ms
            memory_delta_bytes = [long]$row.memory_delta_bytes
            mean_batch_ms = [double]$row.mean_batch_ms
            p50_batch_ms = [double]$row.p50_batch_ms
            p95_batch_ms = [double]$row.p95_batch_ms
            max_batch_ms = [double]$row.max_batch_ms
            mean_per_animation_ms = [double]$row.mean_per_animation_ms
            renders_per_second = [double]$row.renders_per_second
            batches_over_16_67ms = [int]$row.batches_over_16_67ms
            mean_observed_frame_ms = [double]$row.mean_observed_frame_ms
            p95_observed_frame_ms = [double]$row.p95_observed_frame_ms
            platform = $row.platform
            device = $row.device
            operating_system = $row.operating_system
            graphics_device = $row.graphics_device
            graphics_api = $row.graphics_api
            unity_version = $row.unity_version
        }
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$allRows | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'all-results.csv') -NoTypeInformation -Encoding utf8

$variantSummary = foreach ($group in ($allRows | Group-Object run)) {
    $rows = @($group.Group)
    $totalSamples = ($rows | Measure-Object sample_frames -Sum).Sum
    $overBudget = ($rows | Measure-Object batches_over_16_67ms -Sum).Sum
    $metadata = $rows[0]
    [PSCustomObject][ordered]@{
        run = $group.Name
        unity_version = $metadata.unity_version
        render_pipeline = $metadata.render_pipeline
        graphics_api = $metadata.graphics_api
        mean_batch_ms_average = Get-Average $rows mean_batch_ms
        p95_batch_ms_average = Get-Average $rows p95_batch_ms
        mean_load_ms_average = Get-Average $rows load_ms
        renders_per_second_average = Get-Average $rows renders_per_second
        frames_within_16_67ms_percent = 100.0 * (1.0 - $overBudget / $totalSamples)
        temperature_before_c = [double]$metadata.temperature_before_c
        temperature_after_c = [double]$metadata.temperature_after_c
    }
}
$variantSummary = @($variantSummary | Sort-Object mean_batch_ms_average)
for ($index = 0; $index -lt $variantSummary.Count; $index++) {
    $variantSummary[$index] | Add-Member -NotePropertyName rank -NotePropertyValue ($index + 1)
}
$variantSummary | Select-Object rank,run,unity_version,render_pipeline,graphics_api,mean_batch_ms_average,p95_batch_ms_average,mean_load_ms_average,renders_per_second_average,frames_within_16_67ms_percent,temperature_before_c,temperature_after_c |
    Export-Csv -LiteralPath (Join-Path $OutputDirectory 'variant-summary.csv') -NoTypeInformation -Encoding utf8

$editorVersionSummary = foreach ($group in ($allRows | Group-Object unity_version)) {
    $rows = @($group.Group)
    [PSCustomObject][ordered]@{
        unity_version = $group.Name
        configurations = ($rows | Select-Object -ExpandProperty run -Unique).Count
        mean_batch_ms_average = Get-Average $rows mean_batch_ms
        p95_batch_ms_average = Get-Average $rows p95_batch_ms
        renders_per_second_average = Get-Average $rows renders_per_second
    }
}
$editorVersionSummary | Sort-Object mean_batch_ms_average | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'editor-version-summary.csv') -NoTypeInformation -Encoding utf8

$animationSummary = foreach ($group in ($allRows | Group-Object animation)) {
    $rows = @($group.Group)
    [PSCustomObject][ordered]@{
        animation = $group.Name
        mean_batch_ms_average = Get-Average $rows mean_batch_ms
        p95_batch_ms_average = Get-Average $rows p95_batch_ms
        mean_load_ms_average = Get-Average $rows load_ms
        renders_per_second_average = Get-Average $rows renders_per_second
    }
}
$animationSummary | Sort-Object mean_batch_ms_average | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'animation-summary.csv') -NoTypeInformation -Encoding utf8

$resolutionSummary = foreach ($group in ($allRows | Group-Object width)) {
    $rows = @($group.Group)
    [PSCustomObject][ordered]@{
        resolution = [int]$group.Name
        mean_batch_ms_average = Get-Average $rows mean_batch_ms
        p95_batch_ms_average = Get-Average $rows p95_batch_ms
        renders_per_second_average = Get-Average $rows renders_per_second
        batches_over_16_67ms_percent = 100.0 * (($rows | Measure-Object batches_over_16_67ms -Sum).Sum / ($rows | Measure-Object sample_frames -Sum).Sum)
    }
}
$resolutionSummary | Sort-Object resolution | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'resolution-summary.csv') -NoTypeInformation -Encoding utf8

$apiComparison = foreach ($group in ($variantSummary | Group-Object unity_version,render_pipeline)) {
    $gles = $group.Group | Where-Object graphics_api -eq 'OpenGLES3'
    $vulkan = $group.Group | Where-Object graphics_api -eq 'Vulkan'
    if ($gles -and $vulkan) {
        [PSCustomObject][ordered]@{
            unity_version = $gles.unity_version
            render_pipeline = $gles.render_pipeline
            opengles3_mean_batch_ms = $gles.mean_batch_ms_average
            vulkan_mean_batch_ms = $vulkan.mean_batch_ms_average
            vulkan_vs_opengles3_percent = 100.0 * ($vulkan.mean_batch_ms_average / $gles.mean_batch_ms_average - 1.0)
            faster_api = if ($vulkan.mean_batch_ms_average -lt $gles.mean_batch_ms_average) { 'Vulkan' } else { 'OpenGLES3' }
        }
    }
}
$apiComparison | Sort-Object unity_version,render_pipeline | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'graphics-api-comparison.csv') -NoTypeInformation -Encoding utf8

$pipelineComparison = foreach ($group in ($variantSummary | Where-Object unity_version -like '6000.*' | Group-Object unity_version,graphics_api)) {
    $builtIn = $group.Group | Where-Object render_pipeline -eq 'BuiltIn'
    $urp = $group.Group | Where-Object render_pipeline -eq 'URP'
    if ($builtIn -and $urp) {
        [PSCustomObject][ordered]@{
            unity_version = $builtIn.unity_version
            graphics_api = $builtIn.graphics_api
            builtin_mean_batch_ms = $builtIn.mean_batch_ms_average
            urp_mean_batch_ms = $urp.mean_batch_ms_average
            urp_vs_builtin_percent = 100.0 * ($urp.mean_batch_ms_average / $builtIn.mean_batch_ms_average - 1.0)
            faster_pipeline = if ($urp.mean_batch_ms_average -lt $builtIn.mean_batch_ms_average) { 'URP' } else { 'BuiltIn' }
        }
    }
}
$pipelineComparison | Sort-Object unity_version,graphics_api | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'render-pipeline-comparison.csv') -NoTypeInformation -Encoding utf8

$winnerCounts = @{}
$caseWinners = foreach ($group in ($allRows | Group-Object animation,width,height)) {
    $winner = $group.Group | Sort-Object mean_batch_ms | Select-Object -First 1
    if (-not $winnerCounts.ContainsKey($winner.run)) { $winnerCounts[$winner.run] = 0 }
    $winnerCounts[$winner.run]++
    [PSCustomObject][ordered]@{
        animation = $winner.animation
        width = $winner.width
        height = $winner.height
        fastest_run = $winner.run
        mean_batch_ms = $winner.mean_batch_ms
        p95_batch_ms = $winner.p95_batch_ms
    }
}
$caseWinners | Sort-Object animation,width | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'case-winners.csv') -NoTypeInformation -Encoding utf8

$winnerCounts.GetEnumerator() | ForEach-Object {
    [PSCustomObject]@{ run = $_.Key; case_wins = $_.Value }
} | Sort-Object @{ Expression = 'case_wins'; Descending = $true },run | Export-Csv -LiteralPath (Join-Path $OutputDirectory 'case-win-counts.csv') -NoTypeInformation -Encoding utf8

Write-Output "Analyzed $($csvFiles.Count) runs and $($allRows.Count) benchmark cases into $OutputDirectory"
