[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Player,
    [Parameter(Mandatory = $true)]
    [string] $LogFile,
    [string] $ResultFile,
    [int] $RunSeconds = 20,
    [ValidateSet('Direct3D11', 'Direct3D12', 'OpenGLCore', 'Vulkan')]
    [string] $ExpectedGraphicsApi,
    [string] $ExpectedUploadBackend,
    [switch] $RequireNativeUpload
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'assert-smoke-result.ps1')
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
Assert-LottieSmokeResult -Result $result -Platform Windows -ExpectedGraphicsApi $ExpectedGraphicsApi `
    -ExpectedUploadBackend $ExpectedUploadBackend -RequireNativeUpload:$RequireNativeUpload

Write-Output "Windows rendered-player smoke test passed. Result: $ResultFile; log: $LogFile"
