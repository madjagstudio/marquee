<#
.SYNOPSIS
Builds Marquee's MSI: artifacts\Marquee-<version>-x64.msi.

.DESCRIPTION
Publishes marquee.exe (src\Marquee.Cli) and MarqueeTray.exe (src\Marquee.App) self-contained for win-x64, so users need
no .NET installed, and stages them side by side in artifacts\staging with the example themes, LICENSE and
THIRD-PARTY-NOTICES.md. Then checks that marquee.exe runs from there and builds installer\Marquee.Installer.wixproj.

Not single-file: two self-contained single-file exes would each carry the whole runtime (twice the size), PresentMon
and the themes must stay loose files anyway, and a single-file app that extracts its native libraries does so into a
folder under %TEMP%, which the user can write, for an exe that runs elevated. Not trimmed: YamlDotNet, WinForms and
the COM calls use reflection that trimming can break without a build error.

The version is Directory.Build.props's <Version>, the only place it is set, unless -Version overrides it for one
build. Windows PowerShell 5.1 or later; no prompts, and any failure stops the build with a non-zero exit code, so CI can
run it as it is:

    powershell -NoProfile -ExecutionPolicy Bypass -File installer\build.ps1

.PARAMETER Version
Builds this version (three numbers) instead of Directory.Build.props's, as artifacts\Marquee-<version>-x64.msi, so it
lands beside an MSI of another version rather than over it. CI builds an older one from the same source to test an
upgrade (installer\test-install.ps1). Never for a release, whose version is the one in Directory.Build.props.

.PARAMETER SkipValidation
Builds the MSI without WiX's ICE validation, which needs the Windows Installer service. Only for a PC where that
fails; CI and releases always validate.
#>
[CmdletBinding()]
param(
    [string] $Version,
    [switch] $SkipValidation
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$staging = Join-Path $artifacts 'staging'

function Invoke-Native([string] $what, [scriptblock] $command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed with exit code $LASTEXITCODE." }
}

# -Version reaches every project as a global property, which wins over the <Version> in Directory.Build.props and over
# the one the .wixproj reads from it: the apps' versions, the MSI's ProductVersion and its file name all follow it.
# PowerShell's names ignore case, so $version below is that parameter, set from Directory.Build.props when not given.
$versionArgs = @()
if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "-Version takes three numbers, such as 0.0.1 (got '$Version')." }
    $versionArgs = @("-p:Version=$Version")
}
else {
    $version = (& dotnet msbuild (Join-Path $root 'src\Marquee.Cli\Marquee.Cli.csproj') -nologo '-getProperty:Version' | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Could not read the version from Directory.Build.props (got '$version')."
    }
}
Write-Host "Marquee $version"

foreach ($folder in $publish, $staging) {
    if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
}

# Each app into a folder of its own first, so the two can be compared before they share one.
$apps = @(
    @{ Project = 'src\Marquee.Cli\Marquee.Cli.csproj'; Folder = 'cli'; Exe = 'marquee' },
    @{ Project = 'src\Marquee.App\Marquee.App.csproj'; Folder = 'tray'; Exe = 'MarqueeTray' }
)
foreach ($app in $apps) {
    $out = Join-Path $publish $app.Folder
    Invoke-Native "Publishing $($app.Project)" {
        dotnet publish (Join-Path $root $app.Project) -nologo -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=false -p:PublishTrimmed=false @versionArgs -o $out
    }
}

# Both apps load the runtime and Marquee's DLLs from the one folder, so every file they have in common must be the same
# file. Only their own exe, DLL, deps.json and runtimeconfig.json differ, and those have different names.
$cli = Join-Path $publish 'cli'
$tray = Join-Path $publish 'tray'
$mismatches = @()
foreach ($file in Get-ChildItem $cli -Recurse -File) {
    $relative = $file.FullName.Substring($cli.Length + 1)
    $other = Join-Path $tray $relative
    if ((Test-Path $other) -and (Get-FileHash $file.FullName).Hash -ne (Get-FileHash $other).Hash) { $mismatches += $relative }
}
if ($mismatches.Count -gt 0) {
    throw "marquee.exe and MarqueeTray.exe were published with different copies of: $($mismatches -join ', '). They share one folder, so they must agree; check the two projects' package versions."
}

# Symbols stay out of the installer: they add size and nothing a user runs.
New-Item -ItemType Directory $staging | Out-Null
foreach ($source in $cli, $tray) {
    Copy-Item (Join-Path $source '*') $staging -Recurse -Force
}
Get-ChildItem $staging -Recurse -Filter '*.pdb' | Remove-Item

# The examples where ThemeLocator looks for installed ones (a themes folder beside the exe), and the license files.
$themes = New-Item -ItemType Directory (Join-Path $staging 'themes')
Get-ChildItem (Join-Path $root 'themes') -Directory -Filter 'example-*' | ForEach-Object { Copy-Item $_.FullName $themes -Recurse }
Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY-NOTICES.md') $staging

foreach ($required in 'marquee.exe', 'MarqueeTray.exe', 'LICENSE', 'THIRD-PARTY-NOTICES.md',
    'themes\example-landscape\layout.yaml', 'themes\example-portrait\layout.yaml', 'themes\example-agents\layout.yaml') {
    if (-not (Test-Path (Join-Path $staging $required))) { throw "The staging folder has no $required." }
}
# The FPS provider looks for PresentMon\PresentMon-<version>-x64.exe beside marquee.exe (PresentMonBundle).
if (-not (Get-ChildItem (Join-Path $staging 'PresentMon') -Filter 'PresentMon-*-x64.exe' -ErrorAction SilentlyContinue)) {
    throw 'The staging folder has no PresentMon\PresentMon-<version>-x64.exe.'
}

# Both exes carry the MSI's version: the update check compares the running tray's own version with a release's, so a
# stale exe would be offered its own release again. The SDK adds "+<commit>".
foreach ($exe in 'marquee.exe', 'MarqueeTray.exe') {
    $exeVersion = (Get-Item (Join-Path $staging $exe)).VersionInfo.ProductVersion
    if ($exeVersion -notmatch "^$([regex]::Escape($version))(\+|$)") { throw "$exe is version '$exeVersion', not $version." }
}

# marquee.exe runs from the staging folder on its own runtime: it validates an example theme, which loads the theme
# parser and Skia, and touches no screen. The tray isn't started: it would put an icon in this session.
Invoke-Native 'marquee.exe validate' { & (Join-Path $staging 'marquee.exe') validate (Join-Path $staging 'themes\example-landscape') }

# The MSI is built from scratch every time. Its obj folder lists the last build's outputs, and MSBuild deletes any of
# them the next build doesn't make again: building another version would delete the MSI of the one before (CI builds
# two). Everything in it is remade from the staging folder anyway.
$wixObj = Join-Path $PSScriptRoot 'obj'
if (Test-Path $wixObj) { Remove-Item $wixObj -Recurse -Force }

$wixArgs = @((Join-Path $PSScriptRoot 'Marquee.Installer.wixproj'), '-nologo', '-c', 'Release', "-p:StagingDir=$staging") + $versionArgs
if ($SkipValidation) { $wixArgs += '-p:SuppressValidation=true' }
Invoke-Native 'Building the MSI' { dotnet build @wixArgs }

$msi = Join-Path $artifacts "Marquee-$version-x64.msi"
if (-not (Test-Path $msi)) { throw "The build made no $msi." }
$size = (Get-Item $msi).Length
Write-Host ("Built {0} ({1:N1} MB, {2:N0} bytes) from {3:N1} MB staged." -f $msi, ($size / 1MB), $size,
    ((Get-ChildItem $staging -Recurse -File | Measure-Object Length -Sum).Sum / 1MB))
