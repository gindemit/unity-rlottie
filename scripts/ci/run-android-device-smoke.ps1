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
    [ValidateSet('Vulkan', 'OpenGLES2', 'OpenGLES3')]
    [string] $ExpectedGraphicsApi,
    [string] $ExpectedUploadBackend,
    [switch] $RequireNativeVulkanUpload,
    [switch] $SkipInstall
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'assert-smoke-result.ps1')
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
    $remoteApk = "/data/local/tmp/rlottie-device-smoke-$PID.apk"
    try {
        Invoke-Adb -Arguments @('push', $Apk, $remoteApk) | Out-Host
        Invoke-Adb -Arguments @('shell', 'pm', 'install', '-r', $remoteApk) | Out-Host
    }
    finally {
        & $Adb @adbPrefix shell rm -f $remoteApk | Out-Null
    }
}
$startSeconds = (Invoke-Adb -Arguments @('shell', 'date', '+%s')).Trim()
if ($startSeconds -notmatch '^\d+$') {
    throw "Could not read Android device time: $startSeconds"
}
$logArguments = @('logcat', '-d', '-v', 'threadtime', '-T', "$startSeconds.000")
Invoke-Adb -Arguments @('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
Invoke-Adb -Arguments @('shell', 'wm', 'dismiss-keyguard')
Invoke-Adb -Arguments @('shell', 'am', 'force-stop', $Package)
$remoteResult = "/sdcard/Android/data/$Package/files/lottie-smoke-result.json"
Invoke-Adb -Arguments @('shell', 'rm', '-f', $remoteResult) | Out-Null
$launcherActivity = (& $Adb @adbPrefix shell cmd package resolve-activity --brief `
    -a android.intent.action.MAIN -c android.intent.category.LAUNCHER $Package 2>&1 | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or -not $launcherActivity.Contains('/')) {
    throw "Could not resolve the Android launcher activity for ${Package}: $launcherActivity"
}
Invoke-Adb -Arguments @(
    'shell', 'am', 'start', '-S',
    '-n', $launcherActivity,
    '-a', 'android.intent.action.MAIN',
    '-c', 'android.intent.category.LAUNCHER',
    '--ez', 'lottieSmoke', 'true'
) | Out-Host

$deadline = [DateTime]::UtcNow.AddSeconds($RunSeconds)
do {
    Start-Sleep -Milliseconds 500
    & $Adb @adbPrefix shell test -f $remoteResult
    $resultReady = $LASTEXITCODE -eq 0
} while (-not $resultReady -and [DateTime]::UtcNow -lt $deadline)

if (-not $resultReady) {
    $timeoutLog = (Invoke-Adb -Arguments $logArguments | Out-String)
    $timeoutLog | Set-Content -LiteralPath $LogFile -Encoding UTF8
    throw "Android smoke result was not produced within $RunSeconds seconds: $remoteResult"
}
Invoke-Adb -Arguments @('pull', $remoteResult, $ResultFile) | Out-Host

$pidValue = (& $Adb @adbPrefix shell pidof $Package 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or -not $pidValue) {
    throw "Android player is not running: $Package"
}

$log = (Invoke-Adb -Arguments $logArguments | Out-String)
$log | Set-Content -LiteralPath $LogFile -Encoding UTF8
$remoteScreenshot = "/sdcard/rlottie-device-smoke-$PID.png"
try {
    Invoke-Adb -Arguments @('shell', 'screencap', '-p', $remoteScreenshot)
    Invoke-Adb -Arguments @('pull', $remoteScreenshot, $Screenshot) | Out-Host
}
finally {
    & $Adb @adbPrefix shell rm -f $remoteScreenshot | Out-Null
}

$result = Get-Content -Raw -LiteralPath $ResultFile | ConvertFrom-Json
Assert-LottieSmokeResult -Result $result -Platform Android -ExpectedGraphicsApi $ExpectedGraphicsApi `
    -ExpectedUploadBackend $ExpectedUploadBackend -MinimumSchemaVersion 3 `
    -RequiredCheckNames semanticColorOverrides -RequireNativeVulkanUpload:$RequireNativeVulkanUpload
if ((Get-Item -LiteralPath $Screenshot).Length -lt 1024) {
    throw "Android screenshot is unexpectedly small: $Screenshot"
}

Write-Output "Android rendered-player smoke test passed. Result: $ResultFile; log: $LogFile; screenshot: $Screenshot"
