<#
.SYNOPSIS
    Merge the current worktree branch into its base branch (locally).

.DESCRIPTION
    This script is intended to be run from inside a git worktree directory
    (e.g. .worktrees/<task-id>/). It performs a local merge of the current
    worktree branch into its base branch in the main worktree, without
    pushing anything to remote.

    Steps:
      1. Verify we're in a git repo and on a non-base branch.
      2. Detect the base branch (from upstream tracking, branch description,
         or by asking the caller via -Base).
      3. Locate the main worktree (workspace root, not the .worktrees path).
      4. Refuse to proceed if the worktree has uncommitted changes.
      5. In the main worktree, ensure HEAD is on the base branch, then
         `git merge <current-branch>`.

    The script never pushes to origin. The caller decides when (and whether)
    to run `git push origin <base>` afterwards.

.PARAMETER Base
    Override the detected base branch. If omitted, the script tries to infer
    it from `git config branch.<current>.merge` or upstream tracking, and
    falls back to 'main'.

.EXAMPLE
    # From inside .worktrees/<task-id>/
    ./actions/worktree-to-base.ps1

.EXAMPLE
    # Force merging into a specific base
    ./actions/worktree-to-base.ps1 -Base develop
#>

[CmdletBinding()]
param(
    [string]$Base = ""
)

$ErrorActionPreference = 'Stop'

function Fail($msg) {
    Write-Host "ERROR: $msg" -ForegroundColor Red
    exit 1
}

# 1. Verify we're in a git repo.
if (-not (git rev-parse --git-dir 2>$null)) {
    Fail "Not inside a git repository."
}

$currentBranch = (git rev-parse --abbrev-ref HEAD).Trim()
if (-not $currentBranch -or $currentBranch -eq "HEAD") {
    Fail "Detached HEAD or unknown branch."
}

# 2. Detect base branch if not provided.
if (-not $Base) {
    # Try upstream tracking: branch.<current>.merge -> refs/heads/<base>
    $merge = (git config --get "branch.$currentBranch.merge" 2>$null)
    if ($merge) {
        $Base = $merge -replace '^refs/heads/', ''
    }
}
if (-not $Base) {
    # Try git's symbolic-ref of refs/remotes/origin/HEAD
    $originHead = (git symbolic-ref --quiet --short refs/remotes/origin/HEAD 2>$null)
    if ($originHead) {
        $Base = $originHead -replace '^origin/', ''
    }
}
if (-not $Base) {
    $Base = "main"
}

if ($Base -eq $currentBranch) {
    Fail "Current branch '$currentBranch' is the same as base '$Base'."
}

# 3. Locate the main worktree.
$worktreeLines = git worktree list --porcelain
$mainPath = ($worktreeLines | Where-Object { $_ -like "worktree *" } | Select-Object -First 1) -replace "^worktree ", ""
$mainPath = $mainPath.Trim()
if (-not $mainPath -or -not (Test-Path $mainPath)) {
    Fail "Could not locate main worktree path."
}

Write-Host "Current branch : $currentBranch" -ForegroundColor Cyan
Write-Host "Base branch    : $Base" -ForegroundColor Cyan
Write-Host "Main worktree  : $mainPath" -ForegroundColor Cyan
Write-Host ""

# 4. Refuse to merge if there are uncommitted changes.
$dirty = git status --porcelain
if ($dirty) {
    Write-Host "Uncommitted changes detected in the worktree:" -ForegroundColor Yellow
    git --no-pager status --short
    Fail "Commit or stash your changes before running this script."
}

# 5. Check if there are commits to merge.
$ahead = (git rev-list --count "$Base..$currentBranch" 2>$null)
if (-not $ahead) { $ahead = "0" }
$ahead = $ahead.Trim()
if ($ahead -eq "0") {
    Write-Host "Branch '$currentBranch' has no new commits beyond '$Base'. Nothing to merge." -ForegroundColor Yellow
    exit 0
}

Write-Host "Commits to merge into '$Base' ($ahead):" -ForegroundColor Cyan
git --no-pager log --oneline "$Base..$currentBranch"
Write-Host ""

# 6. Switch to main worktree, verify base branch, merge.
Push-Location $mainPath
try {
    $headBranch = (git rev-parse --abbrev-ref HEAD).Trim()
    if ($headBranch -ne $Base) {
        Fail "Main worktree is on '$headBranch', expected '$Base'. Switch it manually first."
    }

    Write-Host "Merging '$currentBranch' into '$Base'..." -ForegroundColor Cyan
    git merge $currentBranch
    Write-Host "Merge complete." -ForegroundColor Green
    Write-Host ""

    Write-Host "Latest '$Base' history:" -ForegroundColor Gray
    git --no-pager log --oneline -5
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "Done. The worktree and branch will be cleaned up when you close the task." -ForegroundColor Green
Write-Host "To publish, run manually:  git push origin $Base" -ForegroundColor Gray
