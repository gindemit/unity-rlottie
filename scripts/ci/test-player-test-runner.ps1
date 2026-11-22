# Exercise the actual runner without starting Unity or contacting a device.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'run-player-tests.ps1'
$dir = Join-Path ([IO.Path]::GetTempPath()) ('lottie player runner ' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $dir
$testXml = Join-Path $dir 'result.xml'
$editorLog = Join-Path $dir 'editor.log'
$buildLog = Join-Path $dir 'build.log'
$buildOutput = Join-Path $dir 'smoke.apk'
$suite = '<test-suite type="Assembly" name="Example.Tests.dll" result="Passed" total="1" passed="1" failed="0" skipped="0" inconclusive="0"><test-case fullname="Example.Tests.A" result="Passed"/></test-suite>'
$editorXml = '<test-run result="Passed" total="1" passed="1" failed="0" skipped="0" inconclusive="0">' + $suite + '</test-run>'
$heartbeat = 'Test execution timed out. No activity received from the player in 120 seconds.'
$adbCalls = New-Object 'System.Collections.Generic.List[string]'
$devices = @("test-device`tdevice")

function Invoke-FakeAdb {
    $adbCalls.Add(($args -join ' '))
    $global:LASTEXITCODE = 0
    if ($args[0] -eq 'devices') { return $devices }
    if ($args[0] -ne '-s' -or $args[1] -ne 'test-device') { throw 'Wrong device selected.' }
    if ($args[2] -eq 'pull') {
        if ($adbCalls.Count -eq 3) {
            $global:LASTEXITCODE = 1
            [IO.File]::WriteAllText($args[4], 'partial')
            return 'daemon not running'
        }
        [IO.File]::WriteAllText($args[4], $suite)
    }
}

function Start-Process {
    param($FilePath, $ArgumentList, [switch] $PassThru, $WindowStyle)
    if ($ArgumentList -contains '-ciOutputPath') {
        if (Test-Path -LiteralPath $buildLog) { throw 'Build started with a stale success log.' }
        $apk = $ArgumentList[[Array]::IndexOf($ArgumentList, '-ciOutputPath') + 1].Trim('"')
        if ([IO.File]::Exists($apk)) { throw 'Unity received a stale APK output path.' }
        if ([IO.File]::ReadAllText($buildOutput) -ne 'previous APK') { throw 'Existing APK changed before publication.' }
        if ($scenario -ne 'no-log') { [IO.File]::WriteAllText($buildLog, 'RLottie CI result: Succeeded') }
        if ($scenario -ne 'no-output') { [IO.File]::WriteAllText($apk, 'new output') }
    }
    else {
        if ($ArgumentList -notcontains ('"{0}"' -f $testXml)) { throw 'NUnit result path lost quoting.' }
        if (Test-Path -LiteralPath $testXml) { throw 'Runner left a stale local test result.' }
        if (Test-Path -LiteralPath $editorLog) { throw 'Runner left an earlier editor log.' }
        if ($scenario -eq 'editor') { [IO.File]::WriteAllText($testXml, $editorXml) }
        [IO.File]::WriteAllText($editorLog, $message)
    }
    $process = [pscustomobject]@{ ExitCode = $exitCode }
    $process | Add-Member ScriptMethod WaitForExit {}
    $process | Add-Member ScriptMethod Refresh {}
    return $process
}

function Assert-Throws {
    param([scriptblock] $Action, [string] $Expected)
    try { $null = & $Action }
    catch {
        if ($_.Exception.Message -notlike "*$Expected*") { throw }
        return
    }
    throw "Expected failure containing '$Expected'."
}

$parameters = @{
    Unity = $PSCommandPath; ProjectPath = $dir; Platform = 'Android'; ResultsFile = $testXml; LogFile = $editorLog
    Adb = 'Invoke-FakeAdb'; Serial = 'test-device'; AssemblyNames = 'Example.Tests'; PlayerHeartbeatTimeout = 120
}
try {
    $scenario = 'editor'
    $exitCode = 0
    $message = 'Done.'
    $summary = (& $runner @parameters) | ConvertFrom-Json
    if ($summary.source -ne 'editor' -or $summary.passed -ne 1) { throw 'Expected editor success.' }

    $devices = @("test-device`tdevice", "second-device`tdevice")
    $adbCalls.Clear()
    Assert-Throws { & $runner @parameters } 'one connected authorized device'
    if ($adbCalls.Count -ne 1 -or [IO.File]::ReadAllText($testXml) -ne $editorXml) {
        throw 'Multiple-device rejection changed a prior result or touched a device.'
    }
    $devices = @("different-device`tdevice")
    Assert-Throws { & $runner @parameters } 'differs from requested'
    $devices = @("test-device`tdevice")

    $scenario = 'usb'
    $exitCode = 3
    $message = $heartbeat
    $adbCalls.Clear()
    $summary = (& $runner @parameters) | ConvertFrom-Json
    if ($summary.source -ne 'android-usb' -or $summary.total -ne 1 -or
        $adbCalls.Count -ne 4 -or $adbCalls[1] -notlike '* shell rm -f *' -or
        $adbCalls[2] -notlike '* pull *' -or $adbCalls[2] -ne $adbCalls[3]) { throw "Expected USB recovery with fresh results. Calls: $adbCalls" }
    if (([xml][IO.File]::ReadAllText($testXml)).DocumentElement.LocalName -ne 'test-run') {
        throw 'Recovered output lost its NUnit test-run envelope.'
    }
    $message = 'Compilation failed.'
    Assert-Throws { & $runner @parameters } 'exited with code 3'
    $message = $heartbeat
    $exitCode = 1
    Assert-Throws { & $runner @parameters } 'exited with code 1'
    $exitCode = 0
    Assert-Throws { & $runner @parameters } 'result is missing'

    $scenario = 'editor'
    $editorXml = $editorXml.Replace('result="Passed"/></test-suite>', 'result="Failed"/></test-suite>')
    Assert-Throws { & $runner @parameters } 'count differs'
    $editorXml = $suite.Replace('Example.Tests.dll', 'Unrelated.Tests.dll')
    Assert-Throws { & $runner @parameters } 'missing the requested assembly'
    $editorXml = '<test-run result="Passed" total="0" passed="0" failed="0" skipped="0" inconclusive="0"/>'
    Assert-Throws { & $runner @parameters } 'No individual test cases'
    $editorXml = $suite.Replace(' failed="0"', '')
    Assert-Throws { & $runner @parameters } 'count differs'

    $builder = Join-Path $PSScriptRoot 'build-player.ps1'
    $build = @{ Unity = $PSCommandPath; ProjectPath = $dir; Target = 'Android'; OutputPath = $buildOutput; LogFile = $buildLog }
    [IO.File]::WriteAllText($buildLog, 'RLottie CI result: Succeeded')
    [IO.File]::WriteAllText($buildOutput, 'previous APK')
    foreach ($scenario in 'no-log', 'no-output') {
        $errorFragment = if ($scenario -eq 'no-log') { 'Unity build failed' } else { 'did not produce' }
        Assert-Throws { & $builder @build } $errorFragment
        if ([IO.File]::ReadAllText($buildOutput) -ne 'previous APK') { throw 'Failed build lost the previous APK.' }
    }
    $scenario = 'new-build'
    $null = & $builder @build
    if ([IO.File]::ReadAllText($buildOutput) -ne 'new output') { throw 'Successful build did not publish the new APK.' }
    Write-Output 'Player-test runner assertions passed.'
}
finally {
    foreach ($file in $testXml, $editorLog, $buildLog, $buildOutput) {
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
    }
    [IO.Directory]::Delete($dir)
}
