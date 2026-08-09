[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Player,
    [Parameter(Mandatory = $true)]
    [string] $LogFile,
    [int] $RunSeconds = 20
)

$ErrorActionPreference = 'Stop'
$Player = (Resolve-Path -LiteralPath $Player).Path
$LogFile = [IO.Path]::GetFullPath($LogFile)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogFile) | Out-Null

$process = Start-Process -FilePath $Player `
    -ArgumentList @('-logFile', $LogFile, '-screen-width', '1280', '-screen-height', '720') `
    -WindowStyle Hidden -PassThru
try {
    $exited = $process.WaitForExit($RunSeconds * 1000)
    if ($exited -and $process.ExitCode -ne 0) {
        throw "RLottie player exited with code $($process.ExitCode)."
    }
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
}

if (-not (Test-Path -LiteralPath $LogFile)) {
    throw "RLottie player log is missing: $LogFile"
}

$log = Get-Content -Raw -LiteralPath $LogFile
if ($log -notmatch '\[Lottie\] Successfully loaded animation') {
    throw 'The Windows player did not load a Lottie animation.'
}
if ($log -notmatch '\[Lottie\] Render data allocated successfully') {
    throw 'The Windows player did not allocate native render data.'
}
if ($log -match 'DllNotFoundException|EntryPointNotFoundException|\bCrash!!!|Failed to allocate render data') {
    throw 'The Windows player log contains a native-plugin or rendering failure.'
}

Write-Output "Windows rendered-player smoke test passed. Log: $LogFile"
