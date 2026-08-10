[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Apk,
    [string] $Adb = 'adb',
    [string] $Serial,
    [string] $Package = 'com.DefaultCompany.LottiePlugin',
    [Parameter(Mandatory = $true)]
    [string] $LogFile,
    [Parameter(Mandatory = $true)]
    [string] $Screenshot,
    [string] $ResultFile,
    [int] $RunSeconds = 20,
    [switch] $RequireNativeVulkanUpload,
    [switch] $SkipInstall
)

$ErrorActionPreference = 'Stop'
$Apk = (Resolve-Path -LiteralPath $Apk).Path
$LogFile = [IO.Path]::GetFullPath($LogFile)
$Screenshot = [IO.Path]::GetFullPath($Screenshot)
$ResultFile = if ($ResultFile) { [IO.Path]::GetFullPath($ResultFile) } else { "$LogFile.smoke.json" }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogFile) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Screenshot) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ResultFile) | Out-Null

$adbPrefix = @()
if ($Serial) {
    $adbPrefix = @('-s', $Serial)
}

function Invoke-Adb {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]] $Arguments)
    & $Adb @adbPrefix @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "adb failed with exit code ${LASTEXITCODE}: $($Arguments -join ' ')"
    }
}

$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$deviceStateOutput = (& $Adb @adbPrefix get-state 2>&1 | Out-String).Trim()
$deviceState = ($deviceStateOutput -split "`r?`n" | Where-Object { $_ -eq 'device' } | Select-Object -Last 1)
$ErrorActionPreference = $previousErrorActionPreference
if ($LASTEXITCODE -ne 0 -or $deviceState -ne 'device') {
    throw "No authorized Android device is available (state: $deviceStateOutput)."
}

if (-not $SkipInstall) {
    $remoteApk = '/data/local/tmp/rlottie-device-smoke.apk'
    try {
        Invoke-Adb -Arguments @('push', $Apk, $remoteApk) | Out-Host
        Invoke-Adb -Arguments @('shell', 'pm', 'install', '-r', $remoteApk) | Out-Host
    }
    finally {
        & $Adb @adbPrefix shell rm -f $remoteApk | Out-Null
    }
}
Invoke-Adb -Arguments @('logcat', '-c')
Invoke-Adb -Arguments @('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
Invoke-Adb -Arguments @('shell', 'wm', 'dismiss-keyguard')
Invoke-Adb -Arguments @('shell', 'am', 'force-stop', $Package)
$remoteResult = "/sdcard/Android/data/$Package/files/lottie-smoke-result.json"
& $Adb @adbPrefix shell rm -f $remoteResult | Out-Null
Invoke-Adb -Arguments @('shell', 'monkey', '-p', $Package, '-c', 'android.intent.category.LAUNCHER', '1') | Out-Host

$deadline = [DateTime]::UtcNow.AddSeconds($RunSeconds)
do {
    Start-Sleep -Milliseconds 500
    & $Adb @adbPrefix shell test -f $remoteResult
    $resultReady = $LASTEXITCODE -eq 0
} while (-not $resultReady -and [DateTime]::UtcNow -lt $deadline)

if (-not $resultReady) {
    throw "Android smoke result was not produced within $RunSeconds seconds: $remoteResult"
}
Invoke-Adb -Arguments @('pull', $remoteResult, $ResultFile) | Out-Host

$pidValue = (& $Adb @adbPrefix shell pidof $Package 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or -not $pidValue) {
    throw "Android player is not running: $Package"
}

$log = (& $Adb @adbPrefix logcat -d -v threadtime 2>&1 | Out-String)
$log | Set-Content -LiteralPath $LogFile -Encoding UTF8
$remoteScreenshot = "/sdcard/rlottie-device-smoke-$PID.png"
Invoke-Adb -Arguments @('shell', 'screencap', '-p', $remoteScreenshot)
Invoke-Adb -Arguments @('pull', $remoteScreenshot, $Screenshot) | Out-Host
Invoke-Adb -Arguments @('shell', 'rm', $remoteScreenshot)

$result = Get-Content -Raw -LiteralPath $ResultFile | ConvertFrom-Json
$failedChecks = @($result.checks | Where-Object { -not $_.passed })
if (-not $result.passed -or $failedChecks.Count -gt 0) {
    $details = $failedChecks | ForEach-Object { "$($_.name): $($_.details)" }
    throw "Android rendered-player smoke test failed:`n$($details -join "`n")"
}
if ($RequireNativeVulkanUpload) {
    if ($result.graphicsApi -ne 'Vulkan' -or $result.animatedImageUploadBackend -ne 'NativeVulkan') {
        throw "The Android player did not use native Vulkan upload for AnimatedImage: graphicsApi=$($result.graphicsApi), backend=$($result.animatedImageUploadBackend)"
    }
}
if ((Get-Item -LiteralPath $Screenshot).Length -lt 1024) {
    throw "Android screenshot is unexpectedly small: $Screenshot"
}

Write-Output "Android rendered-player smoke test passed. Result: $ResultFile; log: $LogFile; screenshot: $Screenshot"
