# Signs release files with the release signing key, writing <file>.sig beside each one and checking it with the key's
# public half. It asks for the key's password, and refuses a key that isn't the one built into Marquee
# (src/Marquee.Host/Updates/release-key.pub.pem, which the tool is built with). See docs/releasing.md.
#
#   tools\sign-release.ps1 D:\marquee-release-key.pem out\Marquee-0.1.0.msi out\marquee-0.1.0.zip
param(
    [Parameter(Mandatory)] [string] $Key,
    [Parameter(Mandatory, ValueFromRemainingArguments)] [string[]] $Files
)

$ErrorActionPreference = 'Stop'

function Get-FullPath([string] $Path) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path) }

$keyPath = Get-FullPath $Key
$filePaths = @($Files | ForEach-Object { Get-FullPath $_ })
dotnet run --project "$PSScriptRoot\Marquee.ReleaseTool" -c Release -- sign $keyPath @filePaths
exit $LASTEXITCODE
