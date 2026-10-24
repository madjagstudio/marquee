<#
.SYNOPSIS
Builds Marquee's MSI and CLI zip: artifacts\Marquee-<version>-x64.msi and artifacts\marquee-<version>.zip.

.DESCRIPTION
Publishes marquee.exe (src\Marquee.Cli), MarqueeTray.exe (src\Marquee.App) and MarqueeEditor.exe (src\Marquee.Editor,
the theme editor) self-contained for win-x64, so users need no .NET installed, and stages them side by side in
artifacts\staging with the example themes, LICENSE and THIRD-PARTY-NOTICES.md. Checks that THIRD-PARTY-NOTICES.md names
every package a staged file came from, and copies each package's own license and notices files into licenses\. Then
checks that marquee.exe runs from there and builds installer\Marquee.Installer.wixproj.

Not single-file: three self-contained single-file exes would each carry the whole runtime (three times the size),
PresentMon and the themes must stay loose files anyway, and a single-file app that extracts its native libraries does
so into a folder under %TEMP%, which the user can write, for an exe that runs elevated. Not trimmed: YamlDotNet,
WinForms, WPF and the COM calls use reflection that trimming can break without a build error.

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
    $version = (& dotnet msbuild (Join-Path $root 'src\Marquee.Cli\Marquee.Cli.csproj') -nologo -p:UseSharedCompilation=false '-getProperty:Version' | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Could not read the version from Directory.Build.props (got '$version')."
    }
}
Write-Host "Marquee $version"

foreach ($folder in $publish, $staging) {
    if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
}

# Each app into a folder of its own first, so the three can be compared before they share one.
$apps = @(
    @{ Project = 'src\Marquee.Cli\Marquee.Cli.csproj'; Folder = 'cli'; Exe = 'marquee' },
    @{ Project = 'src\Marquee.App\Marquee.App.csproj'; Folder = 'tray'; Exe = 'MarqueeTray' },
    @{ Project = 'src\Marquee.Editor\Marquee.Editor.csproj'; Folder = 'editor'; Exe = 'MarqueeEditor' }
)
foreach ($app in $apps) {
    $out = Join-Path $publish $app.Folder
    Invoke-Native "Publishing $($app.Project)" {
        dotnet publish (Join-Path $root $app.Project) -nologo -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=false -p:PublishTrimmed=false -p:UseSharedCompilation=false @versionArgs -o $out
    }
}

# The apps load the runtime and Marquee's DLLs from the one folder, so every file any two of them have in common must be
# the same file. Only their own exe, DLL, deps.json and runtimeconfig.json differ, and those have different names. The
# tray's WinForms and the editor's WPF share some of the desktop runtime's files (WindowsBase.dll, say), which agree
# because both come from the one Microsoft.WindowsDesktop.App runtime pack. A file is hashed only once another app has
# one of the same name, and each copy only once.
$seen = @{} # a file's path below its app's folder, lowercased -> its first copy: @{ Path; Exe; Hash }
$mismatches = @()
foreach ($app in $apps) {
    $from = Join-Path $publish $app.Folder
    foreach ($file in Get-ChildItem $from -Recurse -File) {
        $relative = $file.FullName.Substring($from.Length + 1)
        $key = $relative.ToLowerInvariant()
        if (-not $seen.ContainsKey($key)) {
            $seen[$key] = @{ Path = $file.FullName; Exe = $app.Exe; Hash = $null }
            continue
        }
        $first = $seen[$key]
        if ($null -eq $first.Hash) { $first.Hash = (Get-FileHash $first.Path).Hash }
        if ((Get-FileHash $file.FullName).Hash -ne $first.Hash) { $mismatches += "$relative ($($first.Exe).exe and $($app.Exe).exe)" }
    }
}
if ($mismatches.Count -gt 0) {
    throw "The apps were published with different copies of: $($mismatches -join ', '). They share one folder, so they must agree; check the projects' package versions."
}

# Symbols stay out of the installer: they add size and nothing a user runs.
New-Item -ItemType Directory $staging | Out-Null
foreach ($app in $apps) {
    Copy-Item (Join-Path (Join-Path $publish $app.Folder) '*') $staging -Recurse -Force
}
Get-ChildItem $staging -Recurse -Filter '*.pdb' | Remove-Item

# The FPS provider looks for PresentMon\PresentMon-<version>-x64.exe beside marquee.exe (PresentMonBundle). Checked
# here, before the notices check below names it.
$presentMonExe = Get-ChildItem (Join-Path $staging 'PresentMon') -Filter 'PresentMon-*-x64.exe' -ErrorAction SilentlyContinue |
    Select-Object -First 1
if (-not $presentMonExe) { throw 'The staging folder has no PresentMon\PresentMon-<version>-x64.exe.' }

# The examples where ThemeLocator looks for installed ones (a themes folder beside the exe), and the license files.
$themes = New-Item -ItemType Directory (Join-Path $staging 'themes')
Get-ChildItem (Join-Path $root 'themes') -Directory -Filter 'example-*' | ForEach-Object { Copy-Item $_.FullName $themes -Recurse }
Copy-Item (Join-Path $root 'themes\backgrounds') $themes -Recurse
Get-ChildItem (Join-Path $root 'themes') -Directory -Filter 'start-*' | ForEach-Object { Copy-Item $_.FullName $themes -Recurse }
Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY-NOTICES.md') $staging

# Everything else in the folder came from a NuGet package or the .NET runtime, and THIRD-PARTY-NOTICES.md must name each
# one, so a new dependency can't ship without its notice. The three apps' deps.json say which library every file came
# from; a satellite assembly (de\X.resources.dll) isn't listed there and belongs to X.dll's. The build stops on a file
# that came from nowhere they list, or from a package the notices don't name. Each package's own license and notices
# files go into licenses\<package>, and Inter's (built into Marquee.Core.dll) into licenses\Inter.
$notices = Get-Content (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Raw
$packagesRoot = ((& dotnet nuget locals global-packages --list | Out-String).Trim() -replace '^global-packages:\s*', '').TrimEnd('\')
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $packagesRoot)) { throw "Could not find the NuGet packages folder (got '$packagesRoot')." }

$origins = @{} # a published file's name, lowercased -> the libraries (Id, Version, Type) that list it
foreach ($deps in $apps | ForEach-Object { "$($_.Exe).deps.json" }) {
    $json = Get-Content (Join-Path $staging $deps) -Raw | ConvertFrom-Json
    foreach ($target in $json.targets.PSObject.Properties) {
        foreach ($library in $target.Value.PSObject.Properties) {
            $id, $libraryVersion = $library.Name -split '/', 2
            $origin = @{ Id = $id -replace '^runtimepack\.', ''; Version = $libraryVersion; Type = $json.libraries.($library.Name).type }
            foreach ($kind in 'runtime', 'native', 'runtimeTargets') {
                $assets = $library.Value.PSObject.Properties[$kind]
                if ($null -eq $assets) { continue }
                foreach ($asset in $assets.Value.PSObject.Properties) {
                    $name = (Split-Path -Leaf $asset.Name).ToLowerInvariant()
                    if (-not $origins.ContainsKey($name)) { $origins[$name] = @() }
                    $origins[$name] += $origin
                }
            }
        }
    }
}

$ownFiles = @($apps | ForEach-Object { "$($_.Exe).exe", "$($_.Exe).deps.json", "$($_.Exe).runtimeconfig.json" }) +
    'LICENSE', 'THIRD-PARTY-NOTICES.md'
$unknown = @()
$shipped = @{} # "id/version" -> the package or runtime pack, for each one a staged file came from
foreach ($file in Get-ChildItem $staging -Recurse -File) {
    $relative = $file.FullName.Substring($staging.Length + 1)
    if ($relative -match '^(themes|PresentMon|licenses)\\' -or $ownFiles -contains $relative) { continue }
    $name = if ($relative -match '^[^\\]+\\(.+)\.resources\.dll$') { "$($Matches[1]).dll" } else { $relative }
    $from = $origins[$name.ToLowerInvariant()]
    if (-not $from) { $unknown += $relative; continue }
    foreach ($origin in $from | Where-Object { $_.Type -ne 'project' }) { $shipped["$($origin.Id)/$($origin.Version)"] = $origin }
}
if ($unknown.Count -gt 0) {
    throw "The staging folder has files no deps.json accounts for: $($unknown -join ', '). Find where each comes from and add it to THIRD-PARTY-NOTICES.md and to this check."
}

# A name counts only whole: SkiaSharp.NativeAssets.Win32 doesn't name SkiaSharp. A sentence's closing period may follow.
$unnamed = @($shipped.Values | Where-Object { $notices -notmatch ('(?i)(?<![\w.-])' + [regex]::Escape($_.Id) + '(?![\w-]|\.\w)') } |
    ForEach-Object { $_.Id } | Sort-Object -Unique)
if (-not $notices.Contains($presentMonExe.Name)) { $unnamed += $presentMonExe.Name }
if ($unnamed.Count -gt 0) {
    throw "THIRD-PARTY-NOTICES.md doesn't name $($unnamed -join ', '), which the installer would redistribute. Add each, with its license."
}

$licenses = Join-Path $staging 'licenses'
foreach ($package in $shipped.Values | Sort-Object { $_.Id }) {
    $folder = Join-Path $packagesRoot "$($package.Id.ToLowerInvariant())\$($package.Version)"
    if (-not (Test-Path $folder)) { throw "Package $($package.Id) $($package.Version) is not in $packagesRoot, so its license files can't be copied." }
    $texts = @(Get-ChildItem $folder -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES|ThirdPartyNotices|NOTICE)' })
    if ($texts.Count -gt 0) {
        $into = New-Item -ItemType Directory -Force (Join-Path $licenses $package.Id)
        $texts | Copy-Item -Destination $into
    }
    Write-Host ("Notices: {0} {1} is named{2}" -f $package.Id, $package.Version,
        $(if ($texts.Count -gt 0) { "; licenses\$($package.Id) has $(($texts | ForEach-Object Name) -join ', ')" } else { ' (it ships no license file)' }))
}
New-Item -ItemType Directory -Force (Join-Path $licenses 'Inter') | Out-Null
Copy-Item (Join-Path $root 'src\Marquee.Core\Fonts\OFL.txt') (Join-Path $licenses 'Inter')
foreach ($font in 'Rajdhani', 'JetBrainsMono') {
    New-Item -ItemType Directory -Force (Join-Path $licenses $font) | Out-Null
    Copy-Item (Join-Path $root "src\Marquee.Editor\Fonts\$font\OFL.txt") (Join-Path $licenses $font)
}
Write-Host "Notices: $($presentMonExe.Name) is named; licenses\Inter has OFL.txt"

foreach ($required in 'marquee.exe', 'MarqueeTray.exe', 'MarqueeEditor.exe', 'LICENSE', 'THIRD-PARTY-NOTICES.md',
    'licenses\Microsoft.NETCore.App.Runtime.win-x64\THIRD-PARTY-NOTICES.TXT', 'licenses\Inter\OFL.txt',
    'licenses\Rajdhani\OFL.txt', 'licenses\JetBrainsMono\OFL.txt',
    'themes\example-landscape\layout.yaml', 'themes\example-portrait\layout.yaml', 'themes\example-agents\layout.yaml',
    'themes\example-showcase\layout.yaml', 'themes\example-showcase\video\radar.mp4') {
    if (-not (Test-Path (Join-Path $staging $required))) { throw "The staging folder has no $required." }
}

# Every exe carries the MSI's version: the update check compares the running tray's own version with a release's, so a
# stale exe would be offered its own release again. The SDK adds "+<commit>".
foreach ($exe in 'marquee.exe', 'MarqueeTray.exe', 'MarqueeEditor.exe') {
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

$wixArgs = @((Join-Path $PSScriptRoot 'Marquee.Installer.wixproj'), '-nologo', '-c', 'Release', '-p:UseSharedCompilation=false', "-p:StagingDir=$staging") + $versionArgs
if ($SkipValidation) { $wixArgs += '-p:SuppressValidation=true' }
Invoke-Native 'Building the MSI' { dotnet build @wixArgs }

$msi = Join-Path $artifacts "Marquee-$version-x64.msi"
if (-not (Test-Path $msi)) { throw "The build made no $msi." }
$size = (Get-Item $msi).Length
Write-Host ("Built {0} ({1:N1} MB, {2:N0} bytes) from {3:N1} MB staged." -f $msi, ($size / 1MB), $size,
    ((Get-ChildItem $staging -Recurse -File | Measure-Object Length -Sum).Sum / 1MB))

# The CLI's self-contained publish, with marquee.exe at the zip's root so it can run after extraction.
$zip = Join-Path $artifacts "marquee-$version.zip"
Compress-Archive -Path (Join-Path $publish 'cli\*') -DestinationPath $zip -Force
Write-Host "Built $zip."
