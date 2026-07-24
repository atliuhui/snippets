#requires -version 5.0
<#
.SYNOPSIS
    Git tag manager for mcp-actions.

.DESCRIPTION
    Create, remove, or list Git tags. Local and remote (origin) are always
    kept in sync.

    Tag format: [{group}-]{version}[-{suffix}]
      * group   : optional; dot-separated lowercase words (e.g. runtime, core,
                  provider.sqlite, plugin.pocketbase, plugin.my.cool.plugin).
      * version : required; digits and dots, at least one dot (e.g. 0.1.0).
      * suffix  : optional; single lowercase word (e.g. test, debug, rc1).

    The top-level separator is '-'. Group internals use '.' so the three parts
    never share a separator, making the parse unambiguous.

    Behavior follows PowerShell advanced-function conventions:
      -WhatIf    show planned actions without touching anything
      -Confirm   prompt before each action
      -Verbose   extra progress detail

.PARAMETER Tag
    Tag name to create or remove. Omit to list existing tags.

.PARAMETER Remove
    Remove the tag (local + remote) instead of creating it.

.PARAMETER Remote
    Git remote to push to / remove from. Defaults to 'origin'.

.EXAMPLE
    .\manage-tag.ps1
    # List all tags as objects (auto-rendered as a table).

.EXAMPLE
    .\manage-tag.ps1 | Where-Object { -not $_.InRemote }
    # Pipeline-compose: find tags that exist only locally.

.EXAMPLE
    .\manage-tag.ps1 -Tag 'provider.sqlite-0.1.1' -WhatIf
    # Preview without executing.

.EXAMPLE
    .\manage-tag.ps1 -Tag 'runtime-1.0.0' -Confirm
    # Prompt before each step.

.EXAMPLE
    .\manage-tag.ps1 -Tag 'core-0.1.0-test' -Remove
    # Remove local + remote.
#>

[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Position = 0)]
    [string]$Tag,

    [switch]$Remove,

    [string]$Remote = 'origin'
)

$ErrorActionPreference = 'Stop'

# ---------- tag model ----------

# Shape rules only. What the values mean (which workflow they trigger, whether
# a suffix is a dry-run marker, etc.) is a concern of the consumer, not this
# script.
#   group   ::= word ('.' word)*           (optional; dot-separated lowercase words)
#   version ::= \d+ (\.\d+)+               (digits and dots, at least one dot)
#   suffix  ::= word                       (optional; single lowercase word)
#   word    ::= [a-z] [a-z0-9]*
$script:WordRegex    = '[a-z][a-z0-9]*'
$script:GroupRegex   = "$script:WordRegex(?:\.$script:WordRegex)*"
$script:VersionRegex = '\d+(?:\.\d+)+'
$script:TagRegex     = "(?-i)^(?:($script:GroupRegex)-)?($script:VersionRegex)(?:-($script:WordRegex))?$"

function Get-TagParts {
    param([string]$Name)
    if ($Name -notmatch $script:TagRegex) { return $null }
    [pscustomobject]@{
        Tag     = $Name
        Group   = if ($Matches[1]) { $Matches[1] } else { '' }
        Version = $Matches[2]
        Suffix  = if ($Matches[3]) { $Matches[3] } else { '' }
    }
}

# ---------- symbol-prefix output (gh / npm / cargo style) ----------
function Write-Ok   ($m) { Write-Host 'OK  ' -NoNewline -ForegroundColor Green;  Write-Host $m }
function Write-Act  ($m) { Write-Host '->  ' -NoNewline -ForegroundColor Cyan;   Write-Host $m }
function Write-Warn2($m) { Write-Host '!   ' -NoNewline -ForegroundColor Yellow; Write-Host $m }
function Write-Err2 ($m) { Write-Host '!!  ' -NoNewline -ForegroundColor Red;    Write-Host $m }
function Write-Info2($m) { Write-Host '(i) ' -NoNewline -ForegroundColor Blue;   Write-Host $m }

# ---------- git helpers ----------
function Get-LocalTags {
    $out = git tag 2>$null
    if ($LASTEXITCODE -ne 0) { return @() }
    @($out | Where-Object { $_ -and $_.Trim() } | Sort-Object)
}

function Get-RemoteTags {
    param([string]$RemoteName)
    $out = git ls-remote --tags $RemoteName 2>$null
    if ($LASTEXITCODE -ne 0) { return @() }
    @($out | ForEach-Object {
        if ($_ -match 'refs/tags/(.+?)(?:\^\{\})?$') { $Matches[1] }
    } | Sort-Object -Unique)
}

# ---------- collect state ----------
$localTags  = @(Get-LocalTags)
$remoteTags = @(Get-RemoteTags -RemoteName $Remote)

# Default display: table with 4 core columns; full object is still available via
# Format-List, Select-Object, Where-Object, etc.
if (-not (Get-TypeData -TypeName 'McpActions.Tag')) {
    Update-TypeData -TypeName 'McpActions.Tag' -DefaultDisplayPropertySet Group, Tag, InLocal, InRemote -Confirm:$false -WhatIf:$false
}

# ---------- list mode ----------
if (-not $Tag) {
    $union = @($localTags + $remoteTags) | Sort-Object -Unique
    if (-not $union) {
        Write-Info2 "No tags found."
        return
    }

    $union | ForEach-Object {
        $n = $_
        $p = Get-TagParts $n
        [pscustomobject]@{
            PSTypeName = 'McpActions.Tag'
            Group      = if ($p) { $p.Group }   else { '(invalid)' }
            Tag        = $n
            Version    = if ($p) { $p.Version } else { '' }
            Suffix     = if ($p) { $p.Suffix }  else { '' }
            InLocal    = $localTags  -contains $n
            InRemote   = $remoteTags -contains $n
        }
    } | Sort-Object Group, Tag
    return
}

# ---------- validate ----------
$parts = Get-TagParts $Tag
if (-not $parts) {
    Write-Err2 "Tag '$Tag' does not match '[{group}-]{version}[-{suffix}]'."
    Write-Info2 "  group:   dot-separated lowercase words (e.g. provider.sqlite)"
    Write-Info2 "  version: digits and dots (e.g. 0.1.0)"
    Write-Info2 "  suffix:  single lowercase word (e.g. test)"
    exit 1
}

$inLocal  = $localTags  -contains $Tag
$inRemote = $remoteTags -contains $Tag

Write-Info2 ("{0}  (local: {1}, remote: {2})" -f $Tag, $inLocal, $inRemote)

# ---------- act ----------
if ($Remove) {
    if (-not $inLocal -and -not $inRemote) {
        Write-Ok "Nothing to remove."
        return
    }
    if ($inLocal -and $PSCmdlet.ShouldProcess("local:$Tag", 'delete tag')) {
        Write-Act "Deleting local tag $Tag"
        git tag -d $Tag | Out-Null
        if ($LASTEXITCODE -eq 0) { Write-Ok "Deleted." } else { Write-Err2 "Local delete failed."; exit 1 }
    }
    if ($inRemote -and $PSCmdlet.ShouldProcess("$Remote/$Tag", 'delete remote tag')) {
        Write-Act "Deleting $Remote/$Tag"
        git push $Remote --delete $Tag | Out-Null
        if ($LASTEXITCODE -eq 0) { Write-Ok "Deleted." } else { Write-Err2 "Remote delete failed. Re-run to retry."; exit 1 }
    }
}
else {
    if ($inLocal -and $inRemote) {
        Write-Ok "Tag already exists locally and on $Remote."
        return
    }
    if (-not $inLocal -and $PSCmdlet.ShouldProcess("local:$Tag", 'create tag')) {
        Write-Act "Creating local tag $Tag"
        git tag -a $Tag -m "Release $Tag" | Out-Null
        if ($LASTEXITCODE -eq 0) { Write-Ok "Created." } else { Write-Err2 "Local tag creation failed."; exit 1 }
    }
    if (-not $inRemote -and $PSCmdlet.ShouldProcess("$Remote/$Tag", 'push tag')) {
        Write-Act "Pushing $Tag to $Remote"
        git push $Remote $Tag | Out-Null
        if ($LASTEXITCODE -eq 0) { Write-Ok "Pushed." } else { Write-Err2 "Remote push failed. Re-run to retry."; exit 1 }
    }
}
