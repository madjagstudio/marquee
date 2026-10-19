<#
.SYNOPSIS
Checks a built Marquee MSI's tables against what the installer sources say it must hold.

.DESCRIPTION
Opens the MSI read-only through Windows Installer's own COM object (nothing is installed) and checks: the version (from
Directory.Build.props), the UpgradeCode (from Package.wxs), a per-machine install, the files Marquee needs where it
looks for them, marquee.exe's folder on the system PATH, the Start menu shortcut, the custom actions with their
conditions and flags (deferred or commit, as SYSTEM), and that Windows Installer makes nothing under %ProgramData%
(marquee task install makes %ProgramData%\Marquee itself).
Prints each check; exits 1 with every mismatch listed if any fails. No prompts, so CI can run it as it is:

    powershell -NoProfile -ExecutionPolicy Bypass -File installer\check-msi.ps1 artifacts\Marquee-0.1.0-x64.msi
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Msi
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$root = Split-Path -Parent $PSScriptRoot
$Msi = (Resolve-Path $Msi).Path

# What the sources say. Each value is read from the one place it is set.
$version = ([regex]::Match((Get-Content (Join-Path $root 'Directory.Build.props') -Raw), '<Version>([^<]+)</Version>')).Groups[1].Value
[xml] $package = Get-Content (Join-Path $PSScriptRoot 'Package.wxs') -Raw
$upgradeCode = '{' + $package.Wix.Package.UpgradeCode.ToUpperInvariant() + '}'
$presentMon = ([regex]::Match((Get-Content (Join-Path $root 'src\Marquee.Providers.Fps\PresentMon.targets') -Raw),
    '<PresentMonVersion>([^<]+)</PresentMonVersion>')).Groups[1].Value

# Windows Installer's COM object has no type library PowerShell can use, so every call goes through InvokeMember.
function Invoke-Com($target, [string] $member, [System.Reflection.BindingFlags] $kind, [object[]] $arguments) {
    $target.GetType().InvokeMember($member, $kind, $null, $target, $arguments)
}

$installer = New-Object -ComObject WindowsInstaller.Installer
# 0 = msiOpenDatabaseModeReadOnly.
$database = Invoke-Com $installer 'OpenDatabase' InvokeMethod @($Msi, 0)

# The rows of a query, each a hashtable of column name to text.
function Get-Rows([string] $query) {
    $view = Invoke-Com $database 'OpenView' InvokeMethod @($query)
    Invoke-Com $view 'Execute' InvokeMethod @() | Out-Null
    $names = Invoke-Com $view 'ColumnInfo' GetProperty @(0)
    $count = [int] (Invoke-Com $names 'FieldCount' GetProperty @())
    $columns = @(1..$count | ForEach-Object { [string] (Invoke-Com $names 'StringData' GetProperty @([int] $_)) })
    $rows = @()
    while ($null -ne ($record = Invoke-Com $view 'Fetch' InvokeMethod @())) {
        $row = @{}
        for ($i = 1; $i -le $count; $i++) { $row[$columns[$i - 1]] = [string] (Invoke-Com $record 'StringData' GetProperty @([int] $i)) }
        $rows += $row
    }
    Invoke-Com $view 'Close' InvokeMethod @() | Out-Null
    return $rows
}

$failures = New-Object System.Collections.Generic.List[string]
function Check([string] $what, [bool] $ok, [string] $detail) {
    if ($ok) { Write-Host "ok    $what" }
    else {
        Write-Host "FAIL  $what ($detail)"
        $failures.Add("$what ($detail)")
    }
}

# A long name from a Directory or File table "short|long" name.
function Get-LongName([string] $name) { ($name -split '\|')[-1] }

$properties = @{}
foreach ($row in Get-Rows 'SELECT `Property`, `Value` FROM `Property`') { $properties[$row.Property] = $row.Value }
function Get-Property([string] $name) { if ($properties.ContainsKey($name)) { $properties[$name] } else { '<none>' } }

Check "ProductVersion is $version" ((Get-Property 'ProductVersion') -eq $version) "it is $(Get-Property 'ProductVersion')"
Check "UpgradeCode is $upgradeCode" ((Get-Property 'UpgradeCode') -eq $upgradeCode) "it is $(Get-Property 'UpgradeCode')"
Check 'ALLUSERS is 1 (per-machine)' ((Get-Property 'ALLUSERS') -eq '1') "it is $(Get-Property 'ALLUSERS')"
Check 'Manufacturer is MadJag Studios' ((Get-Property 'Manufacturer') -eq 'MadJag Studios') "it is $(Get-Property 'Manufacturer')"
Check 'ARPPRODUCTICON is set' ((Get-Property 'ARPPRODUCTICON') -ne '<none>') 'missing'
Check 'ARPNOMODIFY is 1' ((Get-Property 'ARPNOMODIFY') -eq '1') "it is $(Get-Property 'ARPNOMODIFY')"

# Each file's path below InstallDir, from its component's directory up.
$directories = @{}
foreach ($row in Get-Rows 'SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`') { $directories[$row.Directory] = $row }
function Get-InstallPath([string] $directory) {
    $parts = @()
    while ($directory -ne 'InstallDir') {
        if (-not $directories.ContainsKey($directory)) { return $null }
        $parts = , (Get-LongName ($directories[$directory].DefaultDir -split ':')[0]) + $parts
        $directory = $directories[$directory].Directory_Parent
    }
    return ($parts -join '\')
}
$installFolder = $directories['InstallDir']
Check 'InstallDir is ProgramFiles64Folder\Marquee' ($null -ne $installFolder -and $installFolder.Directory_Parent -eq 'ProgramFiles64Folder' -and
    (Get-LongName $installFolder.DefaultDir) -eq 'Marquee') 'it is somewhere else'
# A folder whose id is all capitals is a public property, which msiexec takes from its command line: with
# INSTALLFOLDER=C:\Marquee\ it would install there, in a folder every user can change, that the logon task runs elevated.
# Every id but TARGETDIR must have a lowercase letter; TARGETDIR is harmless, since ProgramFiles64Folder below it is set
# by Windows, not from it.
$publicFolders = @($directories.Keys | Where-Object { $_ -cnotmatch '[a-z]' -and $_ -cne 'TARGETDIR' })
Check 'no folder can be moved from the command line (every folder id but TARGETDIR is a private property)' ($publicFolders.Count -eq 0) `
    "public: $($publicFolders -join ', ')"

$componentDirectory = @{}
foreach ($row in Get-Rows 'SELECT `Component`, `Directory_` FROM `Component`') { $componentDirectory[$row.Component] = $row.Directory_ }
$installed = @{}
foreach ($row in Get-Rows 'SELECT `File`, `Component_`, `FileName` FROM `File`') {
    $folder = Get-InstallPath $componentDirectory[$row.Component_]
    if ($null -eq $folder) { continue }
    $path = if ($folder) { "$folder\$(Get-LongName $row.FileName)" } else { Get-LongName $row.FileName }
    $installed[$path.ToLowerInvariant()] = $row.File
}
foreach ($file in 'marquee.exe', 'MarqueeTray.exe', 'marquee.dll', 'MarqueeTray.dll', 'coreclr.dll', 'libSkiaSharp.dll',
    "PresentMon\PresentMon-$presentMon-x64.exe", 'themes\example-agents\layout.yaml', 'themes\example-landscape\layout.yaml',
    'themes\example-portrait\layout.yaml', 'LICENSE', 'THIRD-PARTY-NOTICES.md') {
    Check "installs $file" ($installed.ContainsKey($file.ToLowerInvariant())) 'not in the File table'
}
Check 'installs no .pdb' (-not ($installed.Keys | Where-Object { $_ -like '*.pdb' })) 'symbols are in the File table'

$path = @(Get-Rows 'SELECT `Name`, `Value`, `Component_` FROM `Environment`')
# "=-*PATH": set at install (=), removed at uninstall (-), system-wide (*). "[~];…": appended to what is there.
Check "PATH gets [InstallDir] appended, system-wide, removed on uninstall" ($path.Count -eq 1 -and $path[0].Name -eq '=-*PATH' -and
    $path[0].Value -eq '[~];[InstallDir]' -and $componentDirectory[$path[0].Component_] -eq 'InstallDir') `
    "Environment rows: $(($path | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join '; ')"

$shortcuts = @(Get-Rows 'SELECT `Shortcut`, `Directory_`, `Name`, `Target` FROM `Shortcut`')
$trayFile = $installed['marqueetray.exe']
Check 'Start menu shortcut "Marquee" to MarqueeTray.exe' ($shortcuts.Count -eq 1 -and $shortcuts[0].Directory_ -eq 'ProgramMenuFolder' -and
    (Get-LongName $shortcuts[0].Name) -eq 'Marquee' -and $shortcuts[0].Target -eq "[#$trayFile]") `
    "Shortcut rows: $(($shortcuts | ForEach-Object { "$($_.Name) in $($_.Directory_) -> $($_.Target)" }) -join '; ')"

# Custom action type bits (msidbCustomActionType*).
$inScript = 0x400; $noImpersonate = 0x800; $rollback = 0x100; $commit = 0x200; $continue = 0x40
$actions = @{}
foreach ($row in Get-Rows 'SELECT `Action`, `Type`, `Source`, `Target` FROM `CustomAction`') { $actions[$row.Action] = $row }
# Type 35 sets a folder's path, which would move it as surely as the command line could.
$setsFolder = @($actions.Values | Where-Object { ([int] $_.Type -band 0x3F) -eq 35 } | ForEach-Object { "$($_.Action) sets $($_.Source)" })
Check 'no custom action sets a folder' ($setsFolder.Count -eq 0) ($setsFolder -join '; ')
$sequence = @{}
foreach ($row in Get-Rows 'SELECT `Action`, `Condition`, `Sequence` FROM `InstallExecuteSequence`') { $sequence[$row.Action] = $row }
function Get-Sequence([string] $action) { if ($sequence.ContainsKey($action)) { [int] $sequence[$action].Sequence } else { -1 } }

$expected = @(
    @{ Id = 'StartTrayRollback'; Command = 'task start --user [UserSID]'; Kind = $rollback; Ignore = $true; Condition = 'Installed' },
    @{ Id = 'StopTrays'; Command = 'task stop'; Kind = 0; Ignore = $true; Condition = 'Installed' },
    @{ Id = 'RemoveTaskRollback'; Command = 'task install'; Kind = $rollback; Ignore = $true; Condition = 'REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE' },
    @{ Id = 'RemoveTask'; Command = 'task remove --updates'; Kind = 0; Ignore = $true; Condition = 'REMOVE="ALL" AND NOT UPGRADINGPRODUCTCODE' },
    @{ Id = 'InstallTaskRollback'; Command = 'task remove'; Kind = $rollback; Ignore = $true; Condition = 'NOT Installed AND NOT REMOVE="ALL" AND NOT WIX_UPGRADE_DETECTED' },
    @{ Id = 'InstallTask'; Command = 'task install'; Kind = 0; Ignore = $false; Condition = 'NOT REMOVE="ALL"' },
    @{ Id = 'StartTray'; Command = 'task start --user [UserSID]'; Kind = $commit; Ignore = $true; Condition = 'NOT REMOVE="ALL"' }
)
foreach ($action in $expected) {
    $id = $action.Id
    if (-not $actions.ContainsKey($id)) {
        Check "custom action $id" $false 'not in the CustomAction table'
        continue
    }
    $type = [int] $actions[$id].Type
    $flags = $inScript -bor $noImpersonate -bor $action.Kind -bor $(if ($action.Ignore) { $continue } else { 0 })
    # The low six bits are the action's kind: 1 is a DLL from the Binary table.
    Check "$id runs WixQuietExec from Wix4UtilCA_X64" (($type -band 0x3F) -eq 1 -and $actions[$id].Source -eq 'Wix4UtilCA_X64' -and
        $actions[$id].Target -eq 'WixQuietExec') "type $type, $($actions[$id].Source)!$($actions[$id].Target)"
    Check "$id is deferred, as SYSTEM$(if ($action.Kind -eq $rollback) { ', rollback' } elseif ($action.Kind -eq $commit) { ', commit' }), $(if ($action.Ignore) { 'ignoring' } else { 'checking' }) its exit code" `
        (($type -band ($inScript -bor $noImpersonate -bor $rollback -bor $commit -bor $continue)) -eq $flags) ('type 0x{0:X}, expected 0x{1:X} flags' -f $type, $flags)
    $command = "`"[InstallDir]marquee.exe`" $($action.Command)"
    $setter = $actions["Set$id"]
    Check "$id runs $command" ($null -ne $setter -and [int] $setter.Type -eq 51 -and $setter.Source -eq $id -and $setter.Target -eq $command) `
        "Set$id sets $(if ($setter) { "$($setter.Source) to $($setter.Target)" } else { 'nothing' })"
    Check "$id runs when $($action.Condition)" ((Get-Sequence $id) -gt 0 -and $sequence[$id].Condition -eq $action.Condition) `
        "sequence $(Get-Sequence $id), condition '$(if ($sequence.ContainsKey($id)) { $sequence[$id].Condition })'"
    Check "Set$id comes before $id" ((Get-Sequence "Set$id") -gt 0 -and (Get-Sequence "Set$id") -lt (Get-Sequence $id)) 'out of order'
}
# Rollback runs backwards, so each rollback action comes before what it undoes, and StartTrayRollback before the rest:
# it then runs last, once the files and the task are back.
Check 'trays stop and the task goes before the files do, each after its rollback' ((Get-Sequence 'StartTrayRollback') -lt (Get-Sequence 'StopTrays') -and
    (Get-Sequence 'StopTrays') -lt (Get-Sequence 'RemoveTaskRollback') -and (Get-Sequence 'RemoveTaskRollback') -lt (Get-Sequence 'RemoveTask') -and
    (Get-Sequence 'RemoveTask') -lt (Get-Sequence 'RemoveFiles') -and (Get-Sequence 'StartTrayRollback') -gt (Get-Sequence 'RemoveExistingProducts')) 'out of order'
Check 'the task is installed after the files, with its rollback just before' ((Get-Sequence 'InstallTask') -gt (Get-Sequence 'InstallFiles') -and
    (Get-Sequence 'InstallTaskRollback') -lt (Get-Sequence 'InstallTask') -and (Get-Sequence 'InstallTaskRollback') -gt (Get-Sequence 'InstallFiles')) 'out of order'
# Inside the new install's transaction, so a failed upgrade rolls back to the old version (Package.wxs).
Check 'the old version is removed right after InstallInitialize, inside the transaction' ((Get-Sequence 'RemoveExistingProducts') -gt (Get-Sequence 'InstallInitialize') -and
    (Get-Sequence 'RemoveExistingProducts') -lt (Get-Sequence 'StartTrayRollback') -and (Get-Sequence 'RemoveExistingProducts') -lt (Get-Sequence 'InstallFiles')) `
    "RemoveExistingProducts at $(Get-Sequence 'RemoveExistingProducts'), InstallInitialize at $(Get-Sequence 'InstallInitialize')"

# The Upgrade table: every older Marquee and a rebuilt one of this same version (AllowSameVersionUpgrades) is replaced;
# a newer one is only detected, which the launch condition then refuses.
$versionMaxInclusive = 0x200; $versionMinInclusive = 0x100; $onlyDetect = 0x2
$upgrades = @(Get-Rows 'SELECT `UpgradeCode`, `VersionMin`, `VersionMax`, `Attributes`, `ActionProperty` FROM `Upgrade`')
$upgradeText = ($upgrades | ForEach-Object { "$($_.ActionProperty) $($_.VersionMin)..$($_.VersionMax) 0x$('{0:X}' -f [int] $_.Attributes)" }) -join '; '
$replaces = @($upgrades | Where-Object { $_.ActionProperty -eq 'WIX_UPGRADE_DETECTED' })
Check "it replaces every Marquee up to and including $version" ($upgrades.Count -eq 2 -and $replaces.Count -eq 1 -and $replaces[0].UpgradeCode -eq $upgradeCode -and
    $replaces[0].VersionMin -eq '' -and $replaces[0].VersionMax -eq $version -and ([int] $replaces[0].Attributes -band $versionMaxInclusive) -ne 0 -and
    ([int] $replaces[0].Attributes -band $onlyDetect) -eq 0) "Upgrade rows: $upgradeText"
$newer = @($upgrades | Where-Object { $_.ActionProperty -eq 'WIX_DOWNGRADE_DETECTED' })
Check "it only detects a Marquee newer than $version" ($newer.Count -eq 1 -and $newer[0].UpgradeCode -eq $upgradeCode -and $newer[0].VersionMin -eq $version -and
    $newer[0].VersionMax -eq '' -and ([int] $newer[0].Attributes -band $versionMinInclusive) -eq 0 -and ([int] $newer[0].Attributes -band $onlyDetect) -ne 0) "Upgrade rows: $upgradeText"
$launch = @(Get-Rows 'SELECT `Condition`, `Description` FROM `LaunchCondition`')
$downgradeMessage = $package.Wix.Package.MajorUpgrade.DowngradeErrorMessage
Check 'installing over a newer Marquee is refused with the message in Package.wxs' (@($launch | Where-Object {
    $_.Condition -eq 'NOT WIX_DOWNGRADE_DETECTED' -and $_.Description -eq $downgradeMessage }).Count -eq 1) `
    "LaunchCondition rows: $(($launch | ForEach-Object { "$($_.Condition): $($_.Description)" }) -join '; ')"

# Windows Installer runs as SYSTEM and would make or set permissions on whatever is at a path in %ProgramData%, which
# every user can make folders in, even a junction someone planted there. marquee task install makes %ProgramData%\Marquee
# with SecureFolder instead, which refuses such a folder.
$underProgramData = @($directories.Values | Where-Object { $_.Directory_Parent -eq 'CommonAppDataFolder' } | ForEach-Object { $_.Directory })
$tables = @(Get-Rows 'SELECT `Name` FROM `_Tables`' | ForEach-Object { $_.Name })
$createFolders = @(if ($tables -contains 'CreateFolder') { Get-Rows 'SELECT `Directory_` FROM `CreateFolder`' })
$locks = @(foreach ($table in 'MsiLockPermissionsEx', 'LockPermissions') {
    if ($tables -contains $table) { Get-Rows "SELECT ``LockObject`` FROM ``$table``" }
})
Check 'nothing is made under %ProgramData%, and no folder gets permissions set' ($underProgramData.Count -eq 0 -and $createFolders.Count -eq 0 -and $locks.Count -eq 0) `
    "folders under CommonAppDataFolder: $($underProgramData -join ', '); CreateFolder: $(($createFolders | ForEach-Object { $_.Directory_ }) -join ', '); permission rows: $($locks.Count)"

[System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null
[System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host "$Msi failed $($failures.Count) check(s):"
    $failures | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host ''
Write-Host "$Msi passed every check."
exit 0
