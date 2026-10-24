# Checks and signs a draft made by release.yml, then publishes it. Run from the owner's checkout of the release tag,
# after the key ceremony (docs/releasing.md). The private key and its password stay on this PC; CI never sees them.
#
#   tools\publish-release.ps1 -Tag v0.1.0 -Key D:\marquee-release-key.pem
param(
    [Parameter(Mandatory)] [string] $Tag,
    [Parameter(Mandatory)] [string] $Key
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

if ($Tag -cnotmatch '\Av[0-9]+\.[0-9]+\.[0-9]+\z') { throw "Invalid tag '$Tag': expected v and three numbers, such as v0.1.0." }
$keyPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Key)
if (-not (Test-Path -LiteralPath $keyPath -PathType Leaf)) { throw "Key file not found: $keyPath" }

function Invoke-Native([string] $what, [scriptblock] $command) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed with exit code $LASTEXITCODE." }
}

# gh uses this checkout's repository even if the script was called from another folder. Resolve the key first so a
# relative path still means what the owner typed. No release is edited until every hash and signature has passed.
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $release = (Invoke-Native 'Reading the release' { gh release view $Tag --json isDraft,url,tagName } | Out-String) | ConvertFrom-Json
    if ($release.isDraft -ne $true -or $release.tagName -cne $Tag) { throw "$Tag is not a draft release." }

    $version = $Tag.Substring(1)
    $names = @("Marquee-$version-x64.msi", "marquee-$version.zip")
    $folder = Join-Path ([IO.Path]::GetTempPath()) ("marquee-release-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $folder | Out-Null
    Write-Host "Downloading $Tag to $folder."
    Invoke-Native 'Downloading the release files' {
        gh release download $Tag --dir $folder --pattern $names[0] --pattern $names[1] --pattern SHA256SUMS
    }

    # Only the two expected basenames are accepted: no paths, duplicate rows, missing rows or extra entries. Check
    # both files before asking for the password, so a bad download cannot produce even one signature.
    $hashes = @{}
    foreach ($line in Get-Content -LiteralPath (Join-Path $folder 'SHA256SUMS')) {
        if ($line -cnotmatch '\A([0-9a-fA-F]{64})  (.+)\z') { throw 'Malformed SHA256SUMS line.' }
        $hash, $name = $Matches[1], $Matches[2]
        if ($names -cnotcontains $name -or $hashes.ContainsKey($name)) { throw "Unexpected or duplicate SHA256SUMS entry: $name" }
        $hashes[$name] = $hash
    }
    $files = @($names | ForEach-Object { Join-Path $folder $_ })
    foreach ($name in $names) {
        if (-not $hashes.ContainsKey($name)) { throw "SHA256SUMS has no entry for $name." }
        $actual = (Get-FileHash -LiteralPath (Join-Path $folder $name) -Algorithm SHA256).Hash
        if ($actual -ne $hashes[$name]) { throw "SHA-256 mismatch for $name. Nothing was signed or published." }
    }

    Invoke-Native 'Signing the release files' { & "$PSScriptRoot\sign-release.ps1" -Key $keyPath -Files $files }
    foreach ($file in $files) {
        Invoke-Native "Verifying $file.sig" {
            dotnet run --project "$PSScriptRoot\Marquee.ReleaseTool" -c Release -p:UseSharedCompilation=false -- verify $file
        }
    }

    $signatures = @($files | ForEach-Object { "$_.sig" })
    Invoke-Native 'Uploading the signatures' { gh release upload $Tag @signatures }
    Invoke-Native 'Publishing the release' { gh release edit $Tag --draft=false }
    Write-Host $release.url
}
finally {
    # Keep the downloaded files and signatures for inspection, including after a failure; the key is never copied.
    Pop-Location
}
