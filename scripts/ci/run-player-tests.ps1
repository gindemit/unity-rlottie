[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Unity,
    [Parameter(Mandatory = $true)]
    [string] $ProjectPath,
    [Parameter(Mandatory = $true)]
    [ValidateSet('StandaloneWindows64', 'Android')]
    [string] $Platform,
    [Parameter(Mandatory = $true)]
    [string] $ResultsFile,
    [Parameter(Mandatory = $true)]
    [string] $LogFile,
    [string] $Adb,
    [string] $Serial,
    [string[]] $AssemblyNames,
    [ValidateRange(1, 86400)]
    [int] $PlayerHeartbeatTimeout
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'read-player-test-result.ps1')
$Unity = (Resolve-Path -LiteralPath $Unity).Path
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$ResultsFile = [IO.Path]::GetFullPath($ResultsFile)
$LogFile = [IO.Path]::GetFullPath($LogFile)
if ($ResultsFile -eq $LogFile) { throw 'Choose distinct log and NUnit output paths.' }

$buildTarget = if ($Platform -eq 'Android') { 'Android' } else { 'Win64' }
$arguments = @(
    '-batchmode',
    '-projectPath', "`"$ProjectPath`"",
    '-buildTarget', $buildTarget,
    '-runTests',
    '-testPlatform', $Platform,
    '-testResults', "`"$ResultsFile`"",
    '-logFile', "`"$LogFile`""
)
if ($AssemblyNames) { $arguments += @('-assemblyNames', ('"{0}"' -f ($AssemblyNames -join ';'))) }
if ($PlayerHeartbeatTimeout) { $arguments += @('-playerHeartbeatTimeout', $PlayerHeartbeatTimeout) }

$previousSerial = $env:ANDROID_SERIAL
$source = 'editor'
try {
    if ($Platform -eq 'Android') {
        if (-not $Adb) {
            $Adb = & (Join-Path $PSScriptRoot 'resolve-android-adb.ps1') -Unity $Unity
        }
        # Unity deploys through its editor target. ADB's serial only pins the USB calls.
        $devices = @(& $Adb devices | Where-Object { $_ -match '^\S+\s+device\s*$' })
        if ($LASTEXITCODE -ne 0 -or $devices.Count -ne 1) {
            throw 'The Unity runtime suite needs one connected authorized device for automatic deployment.'
        }
        $connectedSerial = ($devices[0] -split '\s+')[0]
        $requestedSerial = if ($Serial) { $Serial } else { $previousSerial }
        if ($requestedSerial -and $requestedSerial -cne $connectedSerial) {
            throw "The connected Unity runtime device '$connectedSerial' differs from requested '$requestedSerial'."
        }
        $Serial = $connectedSerial
        $env:ANDROID_SERIAL = $Serial
        # Unity Test Framework 1.1 uses this package regardless of the project's identifier.
        $remoteResult = '/sdcard/Android/data/com.UnityTestRunner.UnityTestRunner/files/lottie-player-tests.xml'
        & $Adb -s $Serial shell rm -f $remoteResult
        if ($LASTEXITCODE -ne 0) { throw 'Could not clear the previous device NUnit result.' }
    }
    foreach ($path in $ResultsFile, $LogFile) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
        if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
    }
    $windowOptions = @{}
    if ($env:OS -eq 'Windows_NT') { $windowOptions.WindowStyle = 'Hidden' }
    $process = Start-Process -FilePath $Unity -ArgumentList $arguments -PassThru @windowOptions
    $process.WaitForExit()
    $process.Refresh()
    $unityExitCode = $process.ExitCode
    $hasEditorResult = Test-Path -LiteralPath $ResultsFile -PathType Leaf
    if ($Platform -eq 'Android' -and -not $hasEditorResult -and
        (Test-PlayerHeartbeatTimeout -ExitCode $unityExitCode -LogFile $LogFile)) {
        # Unity can stop its adb daemon during editor shutdown. Retry only this
        # result transfer on the same serial; never restart the shared server.
        $staging = $ResultsFile + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        try {
            while ($true) {
                $pullMessage = & {
                    $ErrorActionPreference = 'Continue'
                    $PSNativeCommandUseErrorActionPreference = $false
                    & $Adb -s $Serial pull $remoteResult $staging 2>&1
                } | Out-String
                if ($LASTEXITCODE -eq 0 -and [IO.File]::Exists($staging)) { break }
                if ([DateTime]::UtcNow -ge $deadline) {
                    throw "Editor heartbeat timed out and device XML retrieval failed. $($pullMessage.Trim()) See $LogFile"
                }
                Start-Sleep -Seconds 2
            }
            [IO.File]::Move($staging, $ResultsFile)
        }
        finally { if ([IO.File]::Exists($staging)) { [IO.File]::Delete($staging) } }
        $source = 'android-usb'
    }
    elseif ($unityExitCode -ne 0 -and -not ($unityExitCode -eq 2 -and $hasEditorResult)) {
        throw "Unity player tests exited with code $unityExitCode. See $LogFile"
    }
}
finally {
    $env:ANDROID_SERIAL = $previousSerial
}
if (-not (Test-Path -LiteralPath $ResultsFile)) {
    throw "Unity player-test result is missing: $ResultsFile. See $LogFile"
}

$xml = New-Object System.Xml.XmlDocument
$xml.XmlResolver = $null
$xml.Load($ResultsFile)
$summary = Read-PlayerTestResult -Document $xml -AssemblyNames $AssemblyNames
if ($source -eq 'android-usb' -and $xml.DocumentElement.LocalName -eq 'test-suite') {
    # Keep the host output in the same NUnit test-run envelope as the editor.
    $suite = $xml.DocumentElement
    $run = $xml.CreateElement('test-run')
    foreach ($key in 'result', 'total', 'passed', 'failed', 'skipped', 'inconclusive', 'duration', 'testcasecount', 'start-time', 'end-time') {
        if ($suite.HasAttribute($key)) { $run.SetAttribute($key, $suite.GetAttribute($key)) }
    }
    $null = $xml.ReplaceChild($run, $suite)
    $null = $run.AppendChild($suite)
    $xml.Save($ResultsFile)
}
$summary | Add-Member -NotePropertyMembers @{ platform = $Platform; source = $source; editorExitCode = $unityExitCode; evidence = $ResultsFile }
$summary | ConvertTo-Json -Compress
if ($summary.result -ne 'Passed' -or $summary.passed -eq 0 -or $summary.failed -ne 0 -or $summary.inconclusive -ne 0 -or
    ($source -eq 'editor' -and $unityExitCode -ne 0)) {
    throw "Unity player tests did not pass. See $ResultsFile"
}
