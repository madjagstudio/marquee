# Makes the release signing key: a P-256 key in a password-encrypted PEM file. Run it once, on the owner's PC, as the
# key ceremony in docs/releasing.md describes. It asks for the password twice, never overwrites a file, and refuses a
# path inside a git work tree (this repository included), so the key is written outside it.
#
#   tools\new-signing-key.ps1 $HOME\marquee-release-key.pem
param(
    [Parameter(Mandatory)] [string] $OutFile
)

$ErrorActionPreference = 'Stop'

# The tool runs from the repo, so pass it a full path; a relative one means relative to where this was run.
$path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile)
dotnet run --project "$PSScriptRoot\Marquee.ReleaseTool" -c Release -- keygen $path
exit $LASTEXITCODE
