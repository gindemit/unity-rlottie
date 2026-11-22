# Exercise package ownership, failure reporting and settings cleanup without a device.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$dir = Join-Path $tempRoot ('lottie-qualification-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $dir
$settingsDir = Join-Path $dir 'ProjectSettings'
$null = New-Item -ItemType Directory -Path $settingsDir
$settings = Join-Path $settingsDir 'ProjectSettings.asset'
$before = "  applicationIdentifier:`n    Android: com.original.app`n  overrideDefaultApplicationIdentifier: 0`n  stripEngineCode: 1`n  other: 0`n"
$testPackage = 'com.UnityTestRunner.UnityTestRunner'
$smokePackage = 'com.gindemit.rlottie.qualification'
$packages = [Collections.Generic.HashSet[string]]::new()
$uninstalled = [Collections.Generic.List[string]]::new()
$runPackages = [Collections.Generic.List[string]]::new()
function Invoke-FakeAdb {
    $command = @($args | Select-Object -Skip $(if ($args[0] -eq '-s') { 2 } else { 0 }))
    $global:LASTEXITCODE = 0
    switch ($command[0]) {
        'get-serialno' { return 'test-device' }
        'devices' { return "test-device`tdevice" }
        'shell' {
            if ($command[1] -eq 'getprop') { return 'Test device' }
            if ($packages.Contains($command[-1])) { return 'package:' + $command[-1] }
        }
        'uninstall' { $uninstalled.Add($command[1]); $null = $packages.Remove($command[1]) }
        default { throw "Unexpected fake adb arguments: $args" }
    }
}
try {
    $runner = Join-Path $dir 'run-android-qualification.ps1'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'run-android-qualification.ps1') -Destination $runner
    @'
$pending = Get-Content -Raw -LiteralPath (Join-Path $OutputDirectory 'qualification.json') | ConvertFrom-Json
if ($pending.passed -or $pending.status -cne 'Running' -or $pending.renders.Count -ne 0) {
    throw 'An earlier qualification is still published during the build.'
}
'@ | Set-Content -LiteralPath (Join-Path $dir 'build-player.ps1')
    @'
param($Package, $ExpectedGraphicsApi, $ResultFile, $LifecycleCycles)
if ($ExpectedGraphicsApi -eq 'Vulkan') { $runPackages.Add($Package) }
$null = $packages.Add($Package)
if ($failVulkan -and $ExpectedGraphicsApi -eq 'Vulkan') { throw 'Mocked Vulkan failure' }
@{ checks = @(1, 2); completedLifecycleCycles = [int]$LifecycleCycles } | ConvertTo-Json | Set-Content -LiteralPath $ResultFile
'@ | Set-Content -LiteralPath (Join-Path $dir 'run-android-device-smoke.ps1')
    @'
$null = $packages.Add($testPackage)
$changed = $before.Replace('com.original.app', $testPackage).Replace('Identifier: 0', 'Identifier: 1').Replace('stripEngineCode: 1', 'stripEngineCode: 0').Replace('other: 0', 'other: 27')
[IO.File]::WriteAllText($settings, $changed)
'{"result":"Passed","total":1,"passed":1}'
'@ | Set-Content -LiteralPath (Join-Path $dir 'run-player-tests.ps1')
    [IO.File]::WriteAllText($settings, $before)
    $output = Join-Path $dir 'results'
    $null = New-Item -ItemType Directory -Path $output
    [IO.File]::WriteAllText((Join-Path $output 'qualification.json'), '{"passed":true,"status":"Completed"}')
    $argsForRun = @{ Unity = $PSCommandPath; ProjectPath = $dir; OutputDirectory = $output; Serial = 'test-device'; Adb = 'Invoke-FakeAdb' }
    $null = $packages.Add($smokePackage)
    $failVulkan = $false
    $pass = (& $runner @argsForRun) | ConvertFrom-Json
    if (-not $pass.passed -or $pass.status -cne 'Completed' -or $pass.renders.Count -ne 2 -or $pass.runtime.passed -ne 1) {
        throw 'Missing suite success.'
    }
    if ($packages.Count -ne 1 -or -not $packages.Contains($smokePackage) -or $uninstalled.Contains($smokePackage)) {
        throw 'Pre-existing smoke package was changed.'
    }
    if ([IO.File]::ReadAllText($settings) -cne $before.Replace('other: 0', 'other: 27')) { throw 'Incorrect settings restoration.' }

    $null = $packages.Add($testPackage)
    $uninstalled.Clear()
    $failVulkan = $true
    $failed = $false
    try { $null = & $runner @argsForRun } catch { $failed = $true }
    $report = Get-Content -Raw -LiteralPath (Join-Path $output 'qualification.json') | ConvertFrom-Json
    if (-not $failed -or $report.passed -or $report.status -cne 'Completed' -or $report.failures.Count -ne 2 -or
        $report.renders.Count -ne 1 -or $report.renders[0].api -ne 'OpenGLES3') {
        throw 'Independent stages or failure report missing.'
    }
    if ($packages.Count -ne 2 -or -not $packages.Contains($testPackage) -or $uninstalled.Contains($testPackage)) {
        throw 'Pre-existing test runner package was changed.'
    }
    if ($runPackages.Count -ne 2 -or $runPackages[0] -eq $runPackages[1]) { throw 'Smoke qualification reused a package ID.' }
    if (Get-ChildItem -LiteralPath $output -Filter '.qualification-*.tmp' -Force) { throw 'Staged manifest left behind.' }
    Write-Output 'Android qualification orchestration assertions passed.'
}
finally {
    if ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($dir)) -ne $tempRoot.TrimEnd('\', '/') -or
        [IO.Path]::GetFileName($dir) -notmatch '^lottie-qualification-[0-9a-f]{32}$') { throw 'Unexpected fixture directory.' }
    Remove-Item -LiteralPath $dir -Recurse -Force
}
