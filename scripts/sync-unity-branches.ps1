[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [string] $SourceBranch = 'codex/remove-texture.apply-and-move-updates-to-native',
    [string] $SourceRepository = (Split-Path -Parent $PSScriptRoot),
    [string] $WorkspaceRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string[]] $TargetBranches,
    [switch] $NoStashDirty,
    [switch] $NoPush,
    [switch] $SkipFetch
)

$ErrorActionPreference = 'Stop'

# These files are owned by each Unity-version branch. When both the source and
# target changed one of them, keep the target version instead of guessing which
# Unity-generated representation is compatible with that editor.
$branchOwnedPaths = @(
    'unity/RLottieUnity/Packages/manifest.json',
    'unity/RLottieUnity/Packages/packages-lock.json',
    'unity/RLottieUnity/ProjectSettings/ProjectVersion.txt',
    'unity/RLottieUnity/ProjectSettings/SceneTemplateSettings.json',
    'unity/RLottieUnity/UserSettings/EditorUserSettings.asset'
)

function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Repository,
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    $output = & git -C $Repository @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in ${Repository}:`n$($output -join "`n")"
    }
    return $output
}

function Test-GitRef {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Repository,
        [Parameter(Mandatory = $true)]
        [string] $Ref
    )

    & git -C $Repository show-ref --verify --quiet $Ref
    return $LASTEXITCODE -eq 0
}

function Test-BranchOwnedPath {
    param([Parameter(Mandatory = $true)][string] $Path)
    return $branchOwnedPaths -contains ($Path -replace '\\', '/')
}

$sourceRepositoryPath = (Resolve-Path -LiteralPath $SourceRepository).Path
$workspaceRootPath = (Resolve-Path -LiteralPath $WorkspaceRoot).Path
$sourceGitDirectory = Join-Path $sourceRepositoryPath '.git'
if (-not (Test-Path -LiteralPath $sourceGitDirectory)) {
    throw "Source repository not found: $sourceRepositoryPath"
}

$checkedOutSourceBranch = (Invoke-Git -Repository $sourceRepositoryPath -Arguments @('branch', '--show-current')).Trim()
if ($checkedOutSourceBranch -ne $SourceBranch) {
    throw "Source repository is on '$checkedOutSourceBranch'; expected '$SourceBranch'."
}

$sourceCommit = (Invoke-Git -Repository $sourceRepositoryPath -Arguments @('rev-parse', 'HEAD')).Trim()
$sourceStatus = @(Invoke-Git -Repository $sourceRepositoryPath -Arguments @('status', '--short', '--untracked-files=all'))
if ($sourceStatus.Count -gt 0) {
    Write-Warning "The source worktree is dirty. Sync uses committed HEAD $sourceCommit only."
}

if (-not $NoPush -and $PSCmdlet.ShouldProcess($SourceBranch, 'push committed source branch to origin')) {
    Invoke-Git -Repository $sourceRepositoryPath -Arguments @(
        'push', 'origin', "HEAD:$SourceBranch"
    ) | Write-Output
}

$targetRepositories = Get-ChildItem -LiteralPath $workspaceRootPath -Directory |
    Where-Object { $_.Name -match '^unity-rlottie-(.+)$' } |
    ForEach-Object {
        $suffix = $_.Name.Substring('unity-rlottie-'.Length)
        [PSCustomObject]@{
            Directory = $_.FullName
            Name = $_.Name
            Branch = "unity/$suffix"
        }
    } |
    Where-Object { -not $TargetBranches -or $TargetBranches -contains $_.Branch } |
    Sort-Object Branch

if (-not $targetRepositories) {
    throw "No Unity branch repositories found under $workspaceRootPath."
}

$failures = [System.Collections.Generic.List[string]]::new()
$updated = 0
$unchanged = 0

foreach ($target in $targetRepositories) {
    Write-Output "[$($target.Branch)] $($target.Directory)"
    try {
        if (-not (Test-Path -LiteralPath (Join-Path $target.Directory '.git'))) {
            throw "Not a Git repository: $($target.Directory)"
        }

        $checkedOutBranch = (Invoke-Git -Repository $target.Directory -Arguments @('branch', '--show-current')).Trim()
        if ($checkedOutBranch -ne $target.Branch) {
            throw "Checked out branch is '$checkedOutBranch'; expected '$($target.Branch)'."
        }

        $status = @(Invoke-Git -Repository $target.Directory -Arguments @('status', '--short', '--untracked-files=all'))
        if ($status.Count -gt 0) {
            if ($NoStashDirty) {
                throw "Worktree is dirty and -NoStashDirty was requested."
            }

            $stashMessage = "pre-sync $($target.Branch) $(Get-Date -Format 'yyyy-MM-ddTHH:mm:ssK')"
            if ($PSCmdlet.ShouldProcess($target.Branch, "stash dirty work as '$stashMessage'")) {
                Invoke-Git -Repository $target.Directory -Arguments @(
                    'stash', 'push', '--include-untracked', '--message', $stashMessage
                ) | Write-Output
                $remainingStatus = @(Invoke-Git -Repository $target.Directory -Arguments @('status', '--short'))
                if ($remainingStatus.Count -gt 0) {
                    throw "Worktree remains dirty after stashing:`n$($remainingStatus -join "`n")"
                }
            }
        }

        if (-not $SkipFetch -and $PSCmdlet.ShouldProcess($target.Branch, 'fetch its origin branch')) {
            Invoke-Git -Repository $target.Directory -Arguments @(
                'fetch', '--prune', 'origin', $target.Branch
            ) | Write-Output
            Invoke-Git -Repository $target.Directory -Arguments @(
                'merge', '--ff-only', "origin/$($target.Branch)"
            ) | Write-Output
        }

        $sourceRemoteUrl = & git -C $target.Directory remote get-url local-source 2>$null
        if ($LASTEXITCODE -eq 0) {
            if ($sourceRemoteUrl.Trim() -ne $sourceRepositoryPath) {
                if ($PSCmdlet.ShouldProcess($target.Branch, 'update local-source remote')) {
                    Invoke-Git -Repository $target.Directory -Arguments @(
                        'remote', 'set-url', 'local-source', $sourceRepositoryPath
                    ) | Write-Output
                }
            }
        }
        elseif ($PSCmdlet.ShouldProcess($target.Branch, 'add local-source remote')) {
            Invoke-Git -Repository $target.Directory -Arguments @(
                'remote', 'add', 'local-source', $sourceRepositoryPath
            ) | Write-Output
        }

        if ($PSCmdlet.ShouldProcess($target.Branch, "fetch $SourceBranch from local source")) {
            Invoke-Git -Repository $target.Directory -Arguments @(
                'fetch', 'local-source', $SourceBranch
            ) | Write-Output
        }

        $sourceTrackingRef = "refs/remotes/local-source/$SourceBranch"
        if (-not (Test-GitRef -Repository $target.Directory -Ref $sourceTrackingRef)) {
            if ($WhatIfPreference) {
                Write-Output "  WHATIF: would inspect and merge $sourceCommit after fetching it."
                continue
            }
            throw "Fetched source ref is missing: $sourceTrackingRef"
        }

        $pendingCount = [int](Invoke-Git -Repository $target.Directory -Arguments @(
            'rev-list', '--count', "HEAD..$sourceTrackingRef"
        ))
        if ($pendingCount -eq 0) {
            Write-Output '  Already contains the committed source branch.'
            $unchanged++
            if (-not $NoPush -and $PSCmdlet.ShouldProcess($target.Branch, 'push to origin')) {
                Invoke-Git -Repository $target.Directory -Arguments @(
                    'push', 'origin', "HEAD:$($target.Branch)"
                ) | Write-Output
            }
            continue
        }

        if (-not $PSCmdlet.ShouldProcess($target.Branch, "merge $pendingCount source commits")) {
            continue
        }

        $mergeOutput = & git -C $target.Directory merge --no-commit --no-ff $sourceTrackingRef 2>&1
        $mergeExitCode = $LASTEXITCODE
        $unmergedPaths = @(& git -C $target.Directory diff --name-only --diff-filter=U)
        $unexpectedConflicts = @($unmergedPaths | Where-Object { -not (Test-BranchOwnedPath -Path $_) })

        if ($mergeExitCode -ne 0 -and ($unmergedPaths.Count -eq 0 -or $unexpectedConflicts.Count -gt 0)) {
            if (Test-Path -LiteralPath (Join-Path $target.Directory '.git/MERGE_HEAD')) {
                & git -C $target.Directory merge --abort | Out-Null
            }
            $details = if ($unexpectedConflicts) { $unexpectedConflicts -join ', ' } else { $mergeOutput -join "`n" }
            throw "Merge has conflicts outside branch-owned metadata: $details"
        }

        # Restore every branch-owned path from the target's pre-merge HEAD. This
        # also protects cleanly merged files and removes source-only files that
        # do not exist in an older Unity branch.
        foreach ($path in $branchOwnedPaths) {
            & git -C $target.Directory cat-file -e "HEAD:$path" 2>$null
            if ($LASTEXITCODE -eq 0) {
                Invoke-Git -Repository $target.Directory -Arguments @('checkout', 'HEAD', '--', $path) | Out-Null
                Invoke-Git -Repository $target.Directory -Arguments @('add', '--', $path) | Out-Null
            }
            else {
                & git -C $target.Directory rm --force --ignore-unmatch -- $path | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    throw "Failed to remove source-only branch-owned file: $path"
                }
            }
        }

        Write-Output '  Preserved branch-owned package and Unity-version metadata.'
        Invoke-Git -Repository $target.Directory -Arguments @('commit', '--no-edit') | Write-Output

        $updated++
        if (-not $NoPush -and $PSCmdlet.ShouldProcess($target.Branch, 'push to origin')) {
            Invoke-Git -Repository $target.Directory -Arguments @(
                'push', 'origin', "HEAD:$($target.Branch)"
            ) | Write-Output
        }
    }
    catch {
        $message = "$($target.Branch): $($_.Exception.Message)"
        $failures.Add($message)
        Write-Error $message -ErrorAction Continue
    }
}

Write-Output "Sync summary: updated=$updated unchanged=$unchanged failed=$($failures.Count)"
if ($failures.Count -gt 0) {
    throw "One or more branches failed to sync:`n$($failures -join "`n")"
}
