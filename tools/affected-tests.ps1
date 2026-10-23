# Runs the test projects a change could affect, and no others: only tests that would tell us something about it.
#
# The change is every file that differs from the base (committed on this branch since it left the base, staged,
# unstaged, or new). Each changed file belongs to the nearest project above it; a test project is affected when it is
# that project, or references it directly or through other projects. Files outside the projects map as below. With
# nothing affected (a plan, worknotes, the installer), no tests run and the script says so.
#
#   tools\affected-tests.ps1                 # run the affected test projects
#   tools\affected-tests.ps1 -List           # only say which ones, and why
#   tools\affected-tests.ps1 -Base origin/main
#
# The full suite still runs before a PR (Podium's full check) and on CI; this is the per-change test.
param(
    [string] $Base = 'origin/main',
    [switch] $List
)

$ErrorActionPreference = 'Stop'
$root = (git rev-parse --show-toplevel).Trim()
Set-Location $root

# --- What changed ------------------------------------------------------------------------------------------------
$mergeBase = (git merge-base HEAD $Base 2>$null)
if (-not $mergeBase) { throw "No common history with '$Base'. Pass -Base with a branch or commit this one started from." }
$changed = @(
    git diff --name-only "$($mergeBase.Trim())" HEAD
    git diff --name-only HEAD
    git ls-files --others --exclude-standard
) | Where-Object { $_ } | ForEach-Object { $_ -replace '\\', '/' } | Sort-Object -Unique

# --- The projects and what each references, transitively --------------------------------------------------------
function Get-Refs([string] $csproj) {
    $dir = Split-Path $csproj -Parent
    ([xml](Get-Content $csproj -Raw)).Project.ItemGroup.ProjectReference |
        Where-Object { $_ -and $_.Include } |
        ForEach-Object { (Resolve-Path (Join-Path $dir $_.Include)).Path }
}
$projects = Get-ChildItem -Path src, tests, tools -Recurse -Filter *.csproj | ForEach-Object { $_.FullName }
$refs = @{}
foreach ($p in $projects) { $refs[$p] = @(Get-Refs $p) }
function Get-Closure([string] $p) {
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $stack = [System.Collections.Generic.Stack[string]]::new(); $stack.Push($p)
    while ($stack.Count) { $x = $stack.Pop(); if ($seen.Add($x)) { foreach ($r in $refs[$x]) { $stack.Push($r) } } }
    return $seen
}
$testProjects = $projects | Where-Object { $_ -like (Join-Path $root 'tests\*') }

# --- Map each changed file ---------------------------------------------------------------------------------------
$projectDirs = $projects | ForEach-Object { [pscustomobject]@{ Dir = (Split-Path $_ -Parent) + '\'; Proj = $_ } } |
    Sort-Object { $_.Dir.Length } -Descending
$changedProjects = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$coreTests = Join-Path $root 'tests\Marquee.Core.Tests\Marquee.Core.Tests.csproj'
$forced = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$all = $false
$reasons = @()
foreach ($f in $changed) {
    $full = Join-Path $root ($f -replace '/', '\')
    $owner = $projectDirs | Where-Object { $full.StartsWith($_.Dir, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if ($owner) { [void]$changedProjects.Add($owner.Proj); continue }
    switch -Regex ($f) {
        '^(Directory\.Build\.(props|targets)|global\.json|nuget\.config|[^/]+\.sln)$' { $all = $true; $reasons += "$f affects every project"; break }
        # Example themes, the schema docs and the public docs are checked by Core's tests (themes load, docs drift, links).
        '^themes/' { [void]$forced.Add($coreTests); $reasons += "$f is checked by Core's theme tests"; break }
        '^docs/(superpowers|worknotes)/' { break }
        '^(docs/|README\.md$|THIRD-PARTY-NOTICES\.md$)' { [void]$forced.Add($coreTests); $reasons += "$f is checked by Core's docs tests"; break }
        default { }   # the installer, CI workflows and other tooling: CI covers them
    }
}

$selected = if ($all) { $testProjects } else {
    $testProjects | Where-Object {
        $t = $_
        $forced.Contains($t) -or (@(Get-Closure $t) | Where-Object { $changedProjects.Contains($_) }).Count -gt 0
    }
}

Write-Host "Changed: $($changed.Count) file(s); projects touched: $(($changedProjects | ForEach-Object { Split-Path $_ -Leaf }) -join ', ')"
foreach ($r in $reasons) { Write-Host "  $r" }
if (-not $selected) { Write-Host 'No test project is affected by this change, so no tests run.'; exit 0 }
Write-Host "Affected test projects: $(($selected | ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) }) -join ', ')"
if ($List) { exit 0 }

$failed = @()
foreach ($t in $selected) {
    & dotnet test $t -c Release -p:UseSharedCompilation=false --blame-hang-timeout 5m --blame-hang-dump-type none
    if ($LASTEXITCODE -ne 0) { $failed += ([IO.Path]::GetFileNameWithoutExtension($t)) }
}
if ($failed) { Write-Host "Failed: $($failed -join ', ')"; exit 1 }
Write-Host 'All affected tests passed.'
