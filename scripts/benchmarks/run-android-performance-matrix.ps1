[CmdletBinding()]
param(
    [string] $WorkspaceRoot = '',
    [string] $ResultsRoot = '',
    [string] $Adb = 'C:\Work\SDKs\Android\platform-tools\adb.exe',
    [int] $Instances = 1,
    [int] $WarmupFrames = 30,
    [int] $SampleFrames = 180,
    [int] $RunTimeoutSeconds = 900,
    [int] $CooldownTemperatureTenthsC = 380,
    [string[]] $SkipRepos = @()
)

$ErrorActionPreference = 'Stop'
$package = 'com.DefaultCompany.LottiePlugin'
$activity = "$package/com.unity3d.player.UnityPlayerActivity"
$remoteCsv = "/sdcard/Android/data/$package/files/benchmark.csv"
$expectedCsvLines = 13

if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $WorkspaceRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}
if ([string]::IsNullOrWhiteSpace($ResultsRoot)) {
    $ResultsRoot = Join-Path $WorkspaceRoot ('results\android-benchmark-' + (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss'))
}

$primaryRepo = Join-Path $WorkspaceRoot 'unity-rlottie'
$overlayFiles = @(
    'scripts\ci\build-player.ps1',
    'unity\RLottieUnity\Assets\LottieBenchmark\LottieBenchmarkController.cs',
    'unity\RLottieUnity\Assets\LottiePlugin\Editor\src\CI\BuildMatrix.cs'
)
$restoreOnlyFiles = @(
    'unity\RLottieUnity\ProjectSettings\ProjectSettings.asset'
)
$matrix = @(
    @{ Repo = 'unity-rlottie-2019.4.41f2'; Version = '2019.4.41f2'; Editor = '2019.4.41f2'; Pipeline = 'BuiltIn' },
    @{ Repo = 'unity-rlottie-2021.3.45f2'; Version = '2021.3.45f2'; Editor = '2021.3.45f2'; Pipeline = 'BuiltIn' },
    @{ Repo = 'unity-rlottie'; Version = '2022.3.62f3'; Editor = '2022.3.62f3'; Pipeline = 'BuiltIn' },
    @{ Repo = 'unity-rlottie-6000.3.7f1'; Version = '6000.3.7f1'; Editor = '6000.3.7f1-x86_64'; Pipeline = 'BuiltIn' },
    @{ Repo = 'unity-rlottie-6000.3.7f1-urp'; Version = '6000.3.7f1'; Editor = '6000.3.7f1-x86_64'; Pipeline = 'URP' },
    @{ Repo = 'unity-rlottie-6000.4.5f1'; Version = '6000.4.5f1'; Editor = '6000.4.5f1'; Pipeline = 'BuiltIn' },
    @{ Repo = 'unity-rlottie-6000.4.5f1-urp'; Version = '6000.4.5f1'; Editor = '6000.4.5f1'; Pipeline = 'URP' },
    @{ Repo = 'unity-rlottie-6000.5.3f1'; Version = '6000.5.3f1'; Editor = '6000.5.3f1'; Pipeline = 'BuiltIn' },
    @{ Repo = 'unity-rlottie-6000.5.3f1-urp'; Version = '6000.5.3f1'; Editor = '6000.5.3f1'; Pipeline = 'URP' }
)

function Invoke-Adb {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)
    & $Adb @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "adb failed with exit code ${LASTEXITCODE}: $($Arguments -join ' ')"
    }
}

function Get-DeviceTemperature {
    $line = & $Adb shell dumpsys battery | Select-String '^\s*temperature:\s*(\d+)'
    if (-not $line) { return 0 }
    return [int] $line.Matches[0].Groups[1].Value
}

function Wait-ForCooldown {
    $deadline = (Get-Date).AddMinutes(5)
    while ((Get-Date) -lt $deadline) {
        $temperature = Get-DeviceTemperature
        if ($temperature -le 0 -or $temperature -le $CooldownTemperatureTenthsC) { return $temperature }
        Write-Output ("Cooling device: {0:F1} C" -f ($temperature / 10.0))
        Start-Sleep -Seconds 10
    }
    return Get-DeviceTemperature
}

function Install-Overlay {
    param([string] $RepoPath, [string] $BackupPath)
    if ($RepoPath -eq $primaryRepo) { return }
    foreach ($relativePath in $overlayFiles) {
        $target = Join-Path $RepoPath $relativePath
        $dirty = git -C $RepoPath status --short -- $relativePath
        if ($dirty) { throw "Cannot install benchmark overlay over a modified file: $target" }
        $backup = Join-Path $BackupPath $relativePath
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $backup) | Out-Null
        Copy-Item -LiteralPath $target -Destination $backup
        Copy-Item -LiteralPath (Join-Path $primaryRepo $relativePath) -Destination $target
    }
    foreach ($relativePath in $restoreOnlyFiles) {
        $target = Join-Path $RepoPath $relativePath
        $dirty = git -C $RepoPath status --short -- $relativePath
        if ($dirty) { throw "Cannot back up a modified file: $target" }
        $backup = Join-Path $BackupPath $relativePath
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $backup) | Out-Null
        Copy-Item -LiteralPath $target -Destination $backup
    }
}

function Restore-Overlay {
    param([string] $RepoPath, [string] $BackupPath)
    if ($RepoPath -eq $primaryRepo -or -not (Test-Path -LiteralPath $BackupPath)) { return }
    foreach ($relativePath in @($overlayFiles + $restoreOnlyFiles)) {
        $backup = Join-Path $BackupPath $relativePath
        if (Test-Path -LiteralPath $backup) {
            Copy-Item -LiteralPath $backup -Destination (Join-Path $RepoPath $relativePath)
        }
    }
}

if (-not (Test-Path -LiteralPath $Adb)) { throw "adb not found: $Adb" }
$device = (& $Adb devices | Select-String '\sdevice$' | Select-Object -First 1)
if (-not $device) { throw 'No authorized Android device is connected.' }

New-Item -ItemType Directory -Force -Path $ResultsRoot | Out-Null
Invoke-Adb shell input keyevent 224
Invoke-Adb shell wm dismiss-keyguard

for ($matrixIndex = 0; $matrixIndex -lt $matrix.Count; $matrixIndex++) {
    $entry = $matrix[$matrixIndex]
    if ($SkipRepos -contains $entry.Repo) {
        Write-Warning "Skipping $($entry.Repo) by request."
        continue
    }
    $repoPath = Join-Path $WorkspaceRoot $entry.Repo
    $projectPath = Join-Path $repoPath 'unity\RLottieUnity'
    $unity = "C:\Program Files\Unity\Hub\Editor\$($entry.Editor)\Editor\Unity.exe"
    $androidPlayer = "C:\Program Files\Unity\Hub\Editor\$($entry.Editor)\Editor\Data\PlaybackEngines\AndroidPlayer"
    if (-not (Test-Path -LiteralPath $unity) -or -not (Test-Path -LiteralPath $androidPlayer)) {
        Write-Warning "Skipping $($entry.Repo): editor or Android module is unavailable."
        continue
    }

    $backupPath = Join-Path $ResultsRoot ('_overlay-backups\' + $entry.Repo)
    try {
        Install-Overlay -RepoPath $repoPath -BackupPath $backupPath
        $apiOrder = if (($matrixIndex % 2) -eq 0) { @('Vulkan', 'OpenGLES3') } else { @('OpenGLES3', 'Vulkan') }
        foreach ($graphicsApi in $apiOrder) {
            $apiSlug = if ($graphicsApi -eq 'Vulkan') { 'vulkan' } else { 'opengles3' }
            $runName = "unity-$($entry.Version)-$($entry.Pipeline.ToLowerInvariant())-$apiSlug"
            $runDirectory = Join-Path $ResultsRoot $runName
            $apk = Join-Path $runDirectory 'RLottieBenchmark.apk'
            $buildLog = Join-Path $runDirectory 'build.log'
            $deviceLog = Join-Path $runDirectory 'device.log'
            $localCsv = Join-Path $runDirectory 'benchmark.csv'
            New-Item -ItemType Directory -Force -Path $runDirectory | Out-Null
            if (Test-Path -LiteralPath $localCsv) {
                $existingLines = (Get-Content -LiteralPath $localCsv | Measure-Object -Line).Lines
                if ($existingLines -ge $expectedCsvLines) {
                    Write-Output "SKIP  $runName (complete CSV already exists)"
                    continue
                }
            }

            $temperatureBefore = Wait-ForCooldown
            $startedUtc = (Get-Date).ToUniversalTime().ToString('O')
            Write-Output ("START {0} at {1:F1} C" -f $runName, ($temperatureBefore / 10.0))
            if (Test-Path -LiteralPath $apk) {
                Write-Output "REUSE $runName APK"
            }
            else {
                & (Join-Path $repoPath 'scripts\ci\build-player.ps1') -Unity $unity -ProjectPath $projectPath `
                    -Target Android -Pipeline $entry.Pipeline -GraphicsApi $graphicsApi -OutputPath $apk -LogFile $buildLog
            }

            Invoke-Adb install -r -t $apk
            Invoke-Adb shell rm -f $remoteCsv
            Invoke-Adb logcat -c
            $benchmarkArguments = "-lottieBenchmarkMatrix -lottieBenchmarkInstances $Instances -lottieBenchmarkWarmup $WarmupFrames -lottieBenchmarkSamples $SampleFrames -lottieBenchmarkUncapped -lottieBenchmarkQuit -lottieBenchmarkOutput $remoteCsv"
            Invoke-Adb shell "am start -S -n $activity -e lottieBenchmarkArguments '$benchmarkArguments'"

            $deadline = (Get-Date).AddSeconds($RunTimeoutSeconds)
            $completed = $false
            while ((Get-Date) -lt $deadline) {
                Start-Sleep -Seconds 3
                $pidText = (& $Adb shell pidof $package) -join ''
                if ([string]::IsNullOrWhiteSpace($pidText)) {
                    $lineCountText = (& $Adb shell "if [ -f '$remoteCsv' ]; then wc -l < '$remoteCsv'; else echo 0; fi") -join ''
                    $lineCount = 0
                    [void][int]::TryParse($lineCountText.Trim(), [ref] $lineCount)
                    if ($lineCount -ge $expectedCsvLines) { $completed = $true }
                    break
                }
            }

            & $Adb logcat -d -v time | Out-File -LiteralPath $deviceLog -Encoding utf8
            if (-not $completed) { throw "Benchmark did not complete successfully: $runName" }
            Invoke-Adb pull $remoteCsv $localCsv
            $temperatureAfter = Get-DeviceTemperature
            [ordered]@{
                run = $runName; repository = $entry.Repo; commit = (git -C $repoPath rev-parse HEAD)
                unity_version = $entry.Version; render_pipeline = $entry.Pipeline; graphics_api = $graphicsApi
                instances = $Instances; warmup_frames = $WarmupFrames; sample_frames = $SampleFrames
                started_utc = $startedUtc; completed_utc = (Get-Date).ToUniversalTime().ToString('O')
                temperature_before_c = $temperatureBefore / 10.0; temperature_after_c = $temperatureAfter / 10.0
                device_serial = ((& $Adb get-serialno) -join '').Trim()
                device_model = ((& $Adb shell getprop ro.product.model) -join '').Trim()
                android_version = ((& $Adb shell getprop ro.build.version.release) -join '').Trim()
                android_api = ((& $Adb shell getprop ro.build.version.sdk) -join '').Trim()
            } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDirectory 'metadata.json') -Encoding utf8
            Write-Output ("DONE  {0} at {1:F1} C" -f $runName, ($temperatureAfter / 10.0))
        }
    }
    finally {
        Restore-Overlay -RepoPath $repoPath -BackupPath $backupPath
    }
}

Write-Output "Sequential Android benchmark matrix complete: $ResultsRoot"
