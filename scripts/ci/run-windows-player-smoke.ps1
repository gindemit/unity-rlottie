[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Player,
    [Parameter(Mandatory = $true)]
    [string] $LogFile,
    [string] $ResultFile,
    [int] $RunSeconds = 20
)

$ErrorActionPreference = 'Stop'
$Player = (Resolve-Path -LiteralPath $Player).Path
$LogFile = [IO.Path]::GetFullPath($LogFile)
$ResultFile = if ($ResultFile) { [IO.Path]::GetFullPath($ResultFile) } else { "$LogFile.smoke.json" }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogFile) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ResultFile) | Out-Null
Remove-Item -LiteralPath $ResultFile -Force -ErrorAction SilentlyContinue

$process = Start-Process -FilePath $Player `
    -ArgumentList @(
        '-logFile', "`"$LogFile`"",
        '-screen-width', '1280',
        '-screen-height', '720',
        '-lottieSmokeResult', "`"$ResultFile`"",
        '-lottieSmokeQuit'
    ) `
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

if (-not (Test-Path -LiteralPath $ResultFile)) {
    throw "RLottie smoke result is missing: $ResultFile"
}

$result = Get-Content -Raw -LiteralPath $ResultFile | ConvertFrom-Json
$failedChecks = @($result.checks | Where-Object { -not $_.passed })
if (-not $result.passed -or $failedChecks.Count -gt 0) {
    $details = $failedChecks | ForEach-Object { "$($_.name): $($_.details)" }
    throw "Windows rendered-player smoke test failed:`n$($details -join "`n")"
}

Write-Output "Windows rendered-player smoke test passed. Result: $ResultFile; log: $LogFile"
