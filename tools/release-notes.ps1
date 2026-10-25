# Write a release's description, in Markdown, to standard output (docs/systems/releases.md).
# Usage: tools/release-notes.ps1 -Version <n> [-Previous <tag>] [-Head <ref>] [-RepositoryUrl <url>]
#
# One bullet per commit since the previous release's tag (the highest v<number> below
# this one), newest first, each the first sentence of its message with a link to the
# commit. With no previous release it says this is the first alpha and links the
# history instead of listing every commit. Needs the full history (fetch-depth: 0).
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$Version,
    [string]$Previous,
    [string]$Head = 'HEAD',
    [string]$RepositoryUrl = "$env:GITHUB_SERVER_URL/$env:GITHUB_REPOSITORY"
)
$ErrorActionPreference = 'Stop'
$maxCommits = 100
$maxHeadline = 280

if (-not $Previous) {
    $earlier = @(git tag --list 'v*' |
        Where-Object { $_ -match '^v(\d+)$' -and [int]$Matches[1] -lt $Version } |
        Sort-Object { [int]$_.Substring(1) })
    if ($earlier.Count -gt 0) { $Previous = $earlier[-1] }
}

# The repository's messages are one long paragraph: the first sentence is the headline.
function Get-Headline([string]$subject) {
    $headline = $subject
    if ($subject -match '^(.+?[.!?])\s+(?=[A-Z])') { $headline = $Matches[1] }
    if ($headline.Length -gt $maxHeadline) {
        $cut = $headline.Substring(0, $maxHeadline)
        $space = $cut.LastIndexOf(' ')
        if ($space -gt $maxHeadline / 2) { $cut = $cut.Substring(0, $space) }
        $headline = $cut.TrimEnd(',', ';', ':') + '…'
    }
    # Angle brackets would read as HTML, and asterisks as emphasis.
    return $headline.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;').Replace('*', '\*')
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("An early build of HBHT: expect rough edges. HBHT doesn't include the game. When it first runs, you import your own ROM of Aidyn Chronicles: The First Mage.")
$lines.Add('')
$lines.Add('**Windows:** the `.msi` installs HBHT for everyone on the PC. The `-portable.zip` runs from wherever you unzip it and keeps its data there. ' +
    '**Linux:** unpack the `.tar.gz` and run `hbht`. ' +
    "**macOS:** open the ``.dmg`` and drag both apps to Applications. They aren't notarized, so macOS asks before it opens them the first time: [what to do]($RepositoryUrl/blob/v$Version/docs/systems/releases.md#macos).")
$lines.Add('')

if (-not $Previous) {
    $lines.Add('## Changes')
    $lines.Add('')
    $lines.Add("This is the first alpha. The [history so far]($RepositoryUrl/commits/v$Version) has every change that led to it.")
} else {
    $commits = @(git log --no-merges --format='%H%x09%s' "$Previous..$Head")
    if ($LASTEXITCODE -ne 0) { throw "git log $Previous..$Head failed" }
    $lines.Add("## Changes since $Previous")
    $lines.Add('')
    if ($commits.Count -eq 0) { $lines.Add('No changes since the last alpha.') }
    foreach ($commit in $commits | Select-Object -First $maxCommits) {
        $sha, $subject = $commit -split "`t", 2
        $lines.Add("- $(Get-Headline $subject) ([``$($sha.Substring(0, 7))``]($RepositoryUrl/commit/$sha))")
    }
    if ($commits.Count -gt $maxCommits) {
        $lines.Add('')
        $lines.Add("And $($commits.Count - $maxCommits) older ones.")
    }
    $lines.Add('')
    $lines.Add("[Every change since $Previous]($RepositoryUrl/compare/$Previous...v$Version), with the commits' full messages.")
}

$lines -join "`n"
