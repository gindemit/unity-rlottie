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
    [int] $RunSeconds = 20,
    [switch] $RequireNativeVulkanUpload,
    [switch] $SkipInstall
)

$ErrorActionPreference = 'Stop'
$Apk = (Resolve-Path -LiteralPath $Apk).Path
$LogFile = [IO.Path]::GetFullPath($LogFile)
$Screenshot = [IO.Path]::GetFullPath($Screenshot)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogFile) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Screenshot) | Out-Null

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
Invoke-Adb -Arguments @('shell', 'monkey', '-p', $Package, '-c', 'android.intent.category.LAUNCHER', '1') | Out-Host
Start-Sleep -Seconds $RunSeconds

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

if ($log -notmatch '\[Lottie\] Successfully loaded animation') {
    throw 'The Android player did not load a Lottie animation.'
}
if ($log -notmatch '\[Lottie\] Render data allocated successfully') {
    throw 'The Android player did not allocate native render data.'
}
if ($log -match 'FATAL EXCEPTION|DllNotFoundException|EntryPointNotFoundException|\bCrash!!!|Failed to allocate render data') {
    throw 'The Android device log contains a native-plugin or rendering failure.'
}
if ($RequireNativeVulkanUpload) {
    if ($log -notmatch '\[LottiePlugin\] Vulkan native upload enabled') {
        throw 'The Android player did not enable the Vulkan native-upload path.'
    }
    if ($log -match '\[LottiePlugin\] Vulkan native upload unavailable; using Texture2D\.Apply fallback') {
        throw 'The Android player fell back to Texture2D.Apply instead of using Vulkan native upload.'
    }
}
if ((Get-Item -LiteralPath $Screenshot).Length -lt 1024) {
    throw "Android screenshot is unexpectedly small: $Screenshot"
}

Write-Output "Android rendered-player smoke test passed. Log: $LogFile; screenshot: $Screenshot"
