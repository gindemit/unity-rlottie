[CmdletBinding()]
param(
    [string] $WorkspaceRoot = '',
    [string] $ResultsRoot = '',
    [ValidateRange(1, 20)]
    [int] $Repeats = 3,
    [int] $Instances = 1,
    [int] $WarmupFrames = 30,
    [int] $SampleFrames = 180,
    [int] $RunTimeoutSeconds = 900,
    [ValidateRange(0, 100)]
    [double] $RegressionThresholdPercent = 5,
    [ValidateSet('Direct3D11', 'Direct3D12', 'OpenGLCore', 'Vulkan')]
    [string[]] $GraphicsApis = @('Direct3D11', 'Direct3D12', 'OpenGLCore', 'Vulkan'),
    [ValidateSet('BuiltIn', 'URP', 'HDRP')]
    [string[]] $Pipelines = @('BuiltIn', 'URP', 'HDRP')
)

$ErrorActionPreference = 'Stop'
$expectedCsvLines = 13
$version = '6000.5.3f1'
$unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"

if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $WorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}
if ([string]::IsNullOrWhiteSpace($ResultsRoot)) {
    $ResultsRoot = Join-Path $WorkspaceRoot ('results\windows-benchmark-' + (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss'))
}
if (-not (Test-Path -LiteralPath $unity)) { throw "Unity executable not found: $unity" }
New-Item -ItemType Directory -Force -Path $ResultsRoot | Out-Null

$pipelineRepos = @{
    BuiltIn = 'unity-rlottie-6000.5.3f1'
    URP = 'unity-rlottie-6000.5.3f1-urp'
    HDRP = 'unity-rlottie-6000.5.3f1-hdrp'
}

foreach ($pipeline in $Pipelines) {
    $repoName = $pipelineRepos[$pipeline]
    $repoPath = Join-Path $WorkspaceRoot $repoName
    $projectPath = Join-Path $repoPath 'unity\RLottieUnity'
    if (-not (Test-Path -LiteralPath (Join-Path $repoPath '.git'))) { throw "Repository not found: $repoPath" }

    foreach ($graphicsApi in $GraphicsApis) {
        $buildName = "unity-$version-$($pipeline.ToLowerInvariant())-$($graphicsApi.ToLowerInvariant())"
        $buildDirectory = Join-Path $ResultsRoot ("_players\" + $buildName)
        $player = Join-Path $buildDirectory 'RLottieBenchmark.exe'
        $buildLog = Join-Path $buildDirectory 'build.log'
        New-Item -ItemType Directory -Force -Path $buildDirectory | Out-Null
        if (-not (Test-Path -LiteralPath $player)) {
            & (Join-Path $repoPath 'scripts\ci\build-player.ps1') -Unity $unity -ProjectPath $projectPath `
                -Target Windows64 -Pipeline $pipeline -GraphicsApi $graphicsApi `
                -OutputPath $player -LogFile $buildLog
        }

        for ($repeat = 1; $repeat -le $Repeats; $repeat++) {
            $scenarios = if (($repeat % 2) -eq 1) { @('baseline', 'palette-at-load') } else { @('palette-at-load', 'baseline') }
            foreach ($scenario in $scenarios) {
                $runName = "$buildName-$scenario-r$repeat"
                $runDirectory = Join-Path $ResultsRoot $runName
                $csv = Join-Path $runDirectory 'benchmark.csv'
                $playerLog = Join-Path $runDirectory 'player.log'
                $metadata = Join-Path $runDirectory 'metadata.json'
                New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
                if ((Test-Path -LiteralPath $csv) -and
                    (Get-Content -LiteralPath $csv | Measure-Object -Line).Lines -ge $expectedCsvLines) {
                    Write-Output "SKIP  $runName (complete CSV already exists)"
                    continue
                }

                $arguments = @(
                    '-logFile', "`"$playerLog`"",
                    '-screen-width', '1280', '-screen-height', '720',
                    '-lottieBenchmarkMatrix',
                    '-lottieBenchmarkInstances', $Instances,
                    '-lottieBenchmarkWarmup', $WarmupFrames,
                    '-lottieBenchmarkSamples', $SampleFrames,
                    '-lottieBenchmarkUncapped', '-lottieBenchmarkQuit',
                    '-lottieBenchmarkOutput', "`"$csv`""
                )
                if ($scenario -eq 'palette-at-load') { $arguments += '-lottieBenchmarkColorOverrides' }
                Write-Output "START $runName"
                $startedUtc = (Get-Date).ToUniversalTime().ToString('O')
                $process = Start-Process -FilePath $player -ArgumentList $arguments -WindowStyle Hidden -PassThru
                try {
                    if (-not $process.WaitForExit($RunTimeoutSeconds * 1000)) {
                        throw "Benchmark timed out after $RunTimeoutSeconds seconds: $runName"
                    }
                    if ($process.ExitCode -ne 0) { throw "Benchmark exited with code $($process.ExitCode): $runName" }
                }
                finally {
                    if (-not $process.HasExited) {
                        Stop-Process -Id $process.Id -Force
                        $process.WaitForExit()
                    }
                }
                if (-not (Test-Path -LiteralPath $csv) -or
                    (Get-Content -LiteralPath $csv | Measure-Object -Line).Lines -lt $expectedCsvLines) {
                    throw "Benchmark CSV is incomplete: $csv"
                }
                $firstRow = Import-Csv -LiteralPath $csv | Select-Object -First 1
                [ordered]@{
                    run = $runName; repository = $repoName; commit = (git -C $repoPath rev-parse HEAD)
                    native_dependency_commit = (git -C $repoPath rev-parse 'HEAD:dependency/rlottie')
                    unity_version = $version; render_pipeline = $pipeline; graphics_api = $graphicsApi
                    color_scenario = $scenario; repeat = $repeat
                    instances = $Instances; warmup_frames = $WarmupFrames; sample_frames = $SampleFrames
                    started_utc = $startedUtc; completed_utc = (Get-Date).ToUniversalTime().ToString('O')
                    graphics_device = $firstRow.graphics_device; operating_system = $firstRow.operating_system
                } | ConvertTo-Json | Set-Content -LiteralPath $metadata -Encoding utf8
                Write-Output "DONE  $runName"
            }
        }
    }
}

& (Join-Path $PSScriptRoot 'compare-color-override-performance.ps1') `
    -ResultsRoot $ResultsRoot -RegressionThresholdPercent $RegressionThresholdPercent
Write-Output "Sequential Windows benchmark matrix complete: $ResultsRoot"
