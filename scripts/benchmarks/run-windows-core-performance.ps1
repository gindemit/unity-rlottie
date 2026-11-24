[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Baseline,
    [string] $Candidate = 'HEAD',
    [Parameter(Mandatory)][string] $Unity,
    [Parameter(Mandatory)][string] $ResultsRoot,
    [ValidateSet('cpu', 'd3d12')][string[]] $Mode = @('cpu', 'd3d12')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$results = [IO.Path]::GetFullPath($ResultsRoot)
if (Test-Path -LiteralPath $results) { throw 'Choose a new results directory.' }
if ($results.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choose a results directory outside the source checkout.'
}
$unity = (Resolve-Path -LiteralPath $Unity).Path
$projectPath = 'unity/RLottieUnity'
$players = @{}
$revisions = @{}
foreach ($entry in @(@{ Name = 'baseline'; Ref = $Baseline }, @{ Name = 'candidate'; Ref = $Candidate })) {
    $name = $entry.Name
    $sha = git -C $root rev-parse --verify --end-of-options "$($entry.Ref)^{commit}"
    if ($LASTEXITCODE -ne 0) { throw "Could not resolve $($entry.Ref)" }
    $revisions[$name] = $sha
    $stage = Join-Path $results $name
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    $archive = Join-Path $stage 'project.tar'
    git -C $root archive --format=tar --output=$archive $sha $projectPath
    if ($LASTEXITCODE -ne 0) { throw "Could not export $sha" }
    tar -xf $archive -C $stage
    if ($LASTEXITCODE -ne 0) { throw "Could not extract $archive" }
    Remove-Item -LiteralPath $archive

    $project = Join-Path $stage $projectPath
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'CorePerformanceProbe.cs') `
        -Destination (Join-Path $project 'Assets/LottiePlugin/Runtime/src')
    $player = Join-Path $stage 'player/RLottiePerf.exe'
    & (Join-Path $root 'scripts/ci/build-player.ps1') -Unity $unity -ProjectPath $project `
        -Pipeline BuiltIn -Target Windows64 -GraphicsApi Direct3D12 -OutputPath $player `
        -LogFile (Join-Path $stage 'build.log')
    $players[$name] = $player
}

$failed = $false
foreach ($benchmark in $Mode) {
    python (Join-Path $PSScriptRoot 'compare-core-performance.py') `
        --baseline $players.baseline --candidate $players.candidate `
        --baseline-ref $revisions.baseline --candidate-ref $revisions.candidate `
        --fixture (Join-Path $root "$projectPath/Assets/Resources/LottieBenchmark/patch-pal-iteration-b-vector-felt.json") `
        --mode $benchmark --output (Join-Path $results $benchmark)
    if ($LASTEXITCODE -ne 0) { $failed = $true }
}
if ($failed) { throw "Core performance comparison failed. See $results" }
