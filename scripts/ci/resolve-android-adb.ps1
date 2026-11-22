param([Parameter(Mandatory)][string] $Unity)

$onPath = Get-Command adb -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($onPath) { return $onPath.Path }
$bundled = Join-Path (Split-Path -Parent $Unity) 'Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb'
if ($env:OS -eq 'Windows_NT') { $bundled += '.exe' }
if (Test-Path -LiteralPath $bundled -PathType Leaf) { return $bundled }
throw 'Could not find adb on PATH or in the Unity Android SDK. Supply -Adb.'
