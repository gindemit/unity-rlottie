[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Unity,
    [Parameter(Mandatory = $true)]
    [string] $ProjectPath,
    [ValidateSet('StandaloneWindows64', 'Android')]
    [string] $Platform,
    [Parameter(Mandatory = $true)]
    [string] $ResultsFile,
    [Parameter(Mandatory = $true)]
    [string] $LogFile
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ResultsFile) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $LogFile) | Out-Null

$arguments = @(
    '-batchmode',
    '-projectPath', $ProjectPath,
    '-runTests',
    '-testPlatform', $Platform,
    '-testResults', $ResultsFile,
    '-logFile', $LogFile
)

$process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru -NoNewWindow
$process.WaitForExit()
$process.Refresh()
if ($process.ExitCode -ne 0) {
    Get-Content -LiteralPath $LogFile -Tail 200 -ErrorAction SilentlyContinue
    throw "Unity player tests failed with exit code $($process.ExitCode). See $LogFile"
}
if (-not (Test-Path -LiteralPath $ResultsFile)) {
    throw "Unity player-test result is missing: $ResultsFile"
}

$xml = [xml](Get-Content -Raw -LiteralPath $ResultsFile)
$run = $xml.'test-run'
Write-Output ("total={0} passed={1} failed={2} skipped={3} result={4}" -f `
    $run.total, $run.passed, $run.failed, $run.skipped, $run.result)
if ([int]$run.failed -ne 0 -or $run.result -ne 'Passed') {
    throw "Unity player tests did not pass."
}
