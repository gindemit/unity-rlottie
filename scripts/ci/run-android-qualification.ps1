[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Unity,
    [Parameter(Mandatory)][string] $ProjectPath,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [string] $Serial,
    [string] $Adb,
    [ValidateRange(1, 10000)][int] $LifecycleCycles = 12,
    [ValidateRange(1, 86400)][int] $RunSeconds = 120,
    [ValidateRange(1, 86400)][int] $PlayerHeartbeatTimeout = 120
)

$ErrorActionPreference = 'Stop'
$ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$Unity = (Resolve-Path -LiteralPath $Unity).Path
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Force -Path $OutputDirectory
$manifestPath = Join-Path $OutputDirectory 'qualification.json'
$manifest = [ordered]@{
    schemaVersion = 1; status = 'Running'; passed = $false; device = $null; renders = @(); runtime = $null; failures = @()
}
$testPackage = 'com.UnityTestRunner.UnityTestRunner'
$smokePackage = 'com.gindemit.rlottie.qualification.q' + [Guid]::NewGuid().ToString('N')
$owned = @()
$deviceArgs = @()
if ($Serial) { $deviceArgs = @('-s', $Serial) }

function Write-QualificationManifest {
    # Stage on the same volume so readers always get one complete JSON document.
    $stagingPath = Join-Path $OutputDirectory ('.qualification-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $stagingPath -Encoding UTF8
        if ([IO.File]::Exists($manifestPath)) {
            [IO.File]::Replace($stagingPath, $manifestPath, [NullString]::Value)
        } else {
            [IO.File]::Move($stagingPath, $manifestPath)
        }
    }
    finally { if ([IO.File]::Exists($stagingPath)) { [IO.File]::Delete($stagingPath) } }
}

function Invoke-DeviceAdb {
    $output = & $Adb @deviceArgs @args
    if ($LASTEXITCODE -ne 0) { throw "adb $($args -join ' ') failed (exit $LASTEXITCODE)." }
    $output
}

function Test-PackageInstalled([string] $Name) {
    $packages = @(Invoke-DeviceAdb shell pm list packages -u $Name)
    $packages -ccontains "package:$Name"
}

function Restore-TestPlayerSettings([string] $Path, [string] $Before) {
    # Test Framework 1.1 can exit before AndroidPlatformSetup.CleanUp, and its
    # identifier setter enables the override even when cleanup restores the name.
    $current = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($Path))
    $restored = $current
    foreach ($pattern in @(
        '(?m)^  applicationIdentifier:\r?\n(?:    (?!Android:)[^\r\n]*\r?\n)*    Android: (?<value>[^\r\n]*)',
        '(?m)^  overrideDefaultApplicationIdentifier: (?<value>[01])',
        '(?m)^  stripEngineCode: (?<value>[01])'
    )) {
        $original = [regex]::Match($Before, $pattern).Groups['value']
        $actual = [regex]::Match($restored, $pattern).Groups['value']
        if ($original.Success -and $actual.Success -and $original.Value -cne $actual.Value) {
            $restored = $restored.Remove($actual.Index, $actual.Length).Insert($actual.Index, $original.Value)
        }
    }
    if (($restored -replace '\r\n', "`n") -ceq ($Before -replace '\r\n', "`n")) { $restored = $Before }
    if ($current -cne $restored) { [IO.File]::WriteAllBytes($Path, [Text.Encoding]::UTF8.GetBytes($restored)) }
}

# Invalidate earlier success before changing any evidence in a reused directory.
Write-QualificationManifest
try {
    if (-not $Adb) { $Adb = & (Join-Path $PSScriptRoot 'resolve-android-adb.ps1') -Unity $Unity }
    $Serial = (Invoke-DeviceAdb get-serialno | Out-String).Trim()
    if (-not $Serial -or $Serial -eq 'unknown') { throw 'An authorized Android device is required.' }
    $deviceArgs = @('-s', $Serial)
    $manifest.device = [ordered]@{ serial = $Serial; model = (Invoke-DeviceAdb shell getprop ro.product.model | Out-String).Trim() }
    foreach ($api in 'Vulkan', 'OpenGLES3') {
        $dir = Join-Path $OutputDirectory $api
        $null = New-Item -ItemType Directory -Force -Path $dir
        $apk = Join-Path $dir 'smoke.apk'
        $log = Join-Path $dir 'runner.log'
        try {
            & (Join-Path $PSScriptRoot 'build-player.ps1') -Unity $Unity -ProjectPath $ProjectPath `
                -Target Android -GraphicsApi $api -AndroidPackage $smokePackage -OutputPath $apk `
                -LogFile (Join-Path $dir 'build.log') *> $log
            if ($owned -notcontains $smokePackage) {
                if (Test-PackageInstalled $smokePackage) { throw "$smokePackage is already installed." }
                $owned += $smokePackage
            }
            $backend = if ($api -eq 'Vulkan') { 'NativeVulkan' } else { 'NativeOpenGL' }
            $screenshot = Join-Path $dir 'screen.png'
            $evidence = Join-Path $dir 'smoke.json'
            & (Join-Path $PSScriptRoot 'run-android-device-smoke.ps1') -Apk $apk -Adb $Adb -Serial $Serial `
                -Package $smokePackage -ExpectedGraphicsApi $api -ExpectedUploadBackend $backend `
                -LogFile (Join-Path $dir 'player.log') -Screenshot $screenshot -ResultFile $evidence `
                -LifecycleCycles $LifecycleCycles -RunSeconds $RunSeconds *>> $log
            $result = Get-Content -Raw -LiteralPath $evidence | ConvertFrom-Json
            $manifest.renders += [ordered]@{ api = $api; backend = $backend; checks = $result.checks.Count
                cycles = $result.completedLifecycleCycles; result = $evidence; screenshot = $screenshot }
        }
        catch { $manifest.failures += "$api ($log): $($_.Exception.Message)" }
    }

    $suiteLog = Join-Path $OutputDirectory 'runtime-runner.log'
    try {
        if (Test-PackageInstalled $testPackage) { throw "The Unity runtime suite package $testPackage is already installed." }
        $settings = Join-Path $ProjectPath 'ProjectSettings/ProjectSettings.asset'
        $baseline = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($settings))
        $owned += $testPackage
        try {
            & (Join-Path $PSScriptRoot 'run-player-tests.ps1') -Unity $Unity -ProjectPath $ProjectPath `
                -Platform Android -Adb $Adb -Serial $Serial -AssemblyNames LottiePlugin.Tests.Runtime `
                -ResultsFile (Join-Path $OutputDirectory 'runtime.xml') -LogFile (Join-Path $OutputDirectory 'runtime-editor.log') `
                -PlayerHeartbeatTimeout $PlayerHeartbeatTimeout *> $suiteLog
            $manifest.runtime = Get-Content -LiteralPath $suiteLog -Tail 1 | ConvertFrom-Json
        }
        finally { Restore-TestPlayerSettings -Path $settings -Before $baseline }
    }
    catch { $manifest.failures += "Runtime ($suiteLog): $($_.Exception.Message)" }
}
catch { $manifest.failures += $_.Exception.Message }
finally {
    foreach ($name in $owned) {
        try {
            if (Test-PackageInstalled $name) {
                $null = Invoke-DeviceAdb uninstall $name
                if (Test-PackageInstalled $name) { throw 'Package is still installed.' }
            }
        }
        catch { $manifest.failures += "Cleanup $name failed. $($_.Exception.Message)" }
    }
    $manifest.passed = $manifest.failures.Count -eq 0 -and $manifest.renders.Count -eq 2 -and $null -ne $manifest.runtime
    $manifest.status = 'Completed'
    Write-QualificationManifest
    $manifest | ConvertTo-Json -Depth 6 -Compress
}
if (-not $manifest.passed) { throw "Android qualification failed. See $manifestPath" }
