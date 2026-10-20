<#
.SYNOPSIS
Installs, upgrades, refuses to downgrade and uninstalls Marquee's MSI for real, and checks what each step leaves behind.

.DESCRIPTION
For CI's runner only (the installer job in .github/workflows/ci.yml), a throwaway VM where the job is an administrator.
It installs Marquee machine-wide, registers and runs the logon task, writes to %ProgramData%\Marquee and uninstalls
again, so it refuses to run anywhere else: on a real PC it would replace or remove the Marquee installed there.

In order:
1. A %ProgramData%\Marquee made before the install, as any program could make it, is left exactly as it is and only
   warned about in the install's log; and the install folder can't be moved from the command line: installs and
   uninstalls the current MSI over such a folder, asking for another install folder, then deletes the folder.
2. Installs the older MSI: the files, their version, marquee.exe running from Program Files, the machine PATH, the
   Start menu shortcut, the logon task (its principal, action and settings) and the permissions marquee task install
   gave %ProgramData%\Marquee and its updates folder (SecureFolder's).
3. Upgrades to the current MSI: one Marquee in Settings > Apps, at the new version, its files updated, the task and
   one PATH entry kept, %ProgramData%\Marquee untouched.
4. Repairs it: the task is still there, and a tray that was running runs again.
5. Installs a rebuild of the current version (the same MSI with a new product code, as a second build of one version
   has): it replaces the installed one rather than installing beside it.
6. Installing the older MSI again is refused and leaves the current one installed.
7. Runs marquee task start and task stop, and reports what the runner did with them: whether it has a session for the
   tray is the runner's business, so only task stop is checked, and only if a tray was running.
8. Runs marquee run elevated for one frame into a file sink, which makes its log folder under %ProgramData%\Marquee
   through SecureFolder: the real check that Marquee accepts the folder the install made.
9. Uninstalls, with a tray started, a downloaded update and a user's log planted first: the trays close, the files,
   shortcut, PATH entry, task (and its folder) and downloaded updates go, the users' folders stay.

A check that passes only because there was nothing for it to see (no tray running, on a runner where the task can't
start one) says so with an INFO line, "not exercised", so a green run isn't read as more than it shows.

Every msiexec runs with /qn /norestart and a verbose log in -LogDir, and must exit 0: 3010 (restart needed) fails, as a
user would have been asked to restart. Each check prints PASS or FAIL, and what the runner did that isn't checked
prints INFO. A failed install, upgrade or uninstall stops the run, since what follows would only fail with it. The
summary lists every failure; the exit code is 1 if there was one.

    powershell -NoProfile -ExecutionPolicy Bypass -File installer\test-install.ps1 -OlderMsi artifacts\Marquee-0.0.1-x64.msi -CurrentMsi artifacts\Marquee-0.1.0-x64.msi
#>
[CmdletBinding()]
param(
    # An MSI of an older version, built from the same source with build.ps1 -Version.
    [string] $OlderMsi,
    # The MSI being tested.
    [string] $CurrentMsi,
    # Where the msiexec logs, the task's XML and Marquee's own logs go, for the workflow to upload.
    [string] $LogDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

# Before anything else, parameters included: nothing below may run on anyone's own PC. GITHUB_ACTIONS is true on a
# self-hosted runner too, which may be someone's PC; RUNNER_ENVIRONMENT says whether GitHub hosts it.
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    Write-Host 'test-install.ps1 installs, upgrades and uninstalls Marquee on this PC for real, and would replace or remove'
    Write-Host 'an installed Marquee. It runs only on a GitHub-hosted Actions runner (GITHUB_ACTIONS=true and'
    Write-Host 'RUNNER_ENVIRONMENT=github-hosted), a throwaway VM, as CI''s installer job runs it. Nothing was changed. To'
    Write-Host 'check an MSI without installing it, run check-msi.ps1.'
    exit 2
}
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not (New-Object Security.Principal.WindowsPrincipal $identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host 'test-install.ps1 must run elevated, as the runner''s job does: it installs and uninstalls per-machine. Nothing was changed.'
    exit 2
}
if (-not [Environment]::Is64BitProcess) {
    Write-Host 'test-install.ps1 must run in 64-bit PowerShell, which sees the 64-bit Program Files and registry. Nothing was changed.'
    exit 2
}
if (-not $OlderMsi -or -not $CurrentMsi) {
    Write-Host 'Usage: test-install.ps1 -OlderMsi <older .msi> -CurrentMsi <current .msi> [-LogDir <folder>]'
    exit 2
}

$root = Split-Path -Parent $PSScriptRoot
$OlderMsi = (Resolve-Path $OlderMsi).Path
$CurrentMsi = (Resolve-Path $CurrentMsi).Path
if (-not $LogDir) { $LogDir = Join-Path $root 'artifacts\install-logs' }
New-Item -ItemType Directory -Force $LogDir | Out-Null
$LogDir = (Resolve-Path $LogDir).Path

# What the sources say, each from the one place it is set.
[xml] $package = Get-Content (Join-Path $PSScriptRoot 'Package.wxs') -Raw
$upgradeCode = '{' + $package.Wix.Package.UpgradeCode.ToUpperInvariant() + '}'
$downgradeMessage = $package.Wix.Package.MajorUpgrade.DowngradeErrorMessage
# The permissions SecureFolder.Permissions gives the folders it makes, which marquee task install makes as SYSTEM:
# O:BA owned by Administrators; D:P nothing inherited; SYSTEM and Administrators full control (FA), each for the folder,
# its subfolders and files (OICI); and read and execute (0x1200a9) for Users on %ProgramData%\Marquee, but on the last
# folder, updates, only for the process that made it, SYSTEM, which already has full control.
$dataSddl = 'O:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)'
$updatesSddl = 'O:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)'

$installDir = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Marquee'
$marquee = Join-Path $installDir 'marquee.exe'
$trayExe = Join-Path $installDir 'MarqueeTray.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Marquee.lnk'
$dataDir = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'Marquee'
$updatesDir = Join-Path $dataDir 'updates'
$userSid = $identity.User.Value
$userData = Join-Path $dataDir "users\$userSid"
$taskName = '\Marquee\Marquee'

$results = New-Object System.Collections.Generic.List[object]
$script:step = 'start'

function Check([string] $what, [bool] $ok, [string] $detail = '') {
    if ($ok) { Write-Host "PASS  $what" }
    else { Write-Host "FAIL  $what$(if ($detail) { " ($detail)" })" }
    $results.Add([pscustomobject] @{ Ok = $ok; What = "$($script:step): $what"; Detail = $detail })
}

# A check the rest of the run depends on: a failure stops it.
function Require([string] $what, [bool] $ok, [string] $detail = '') {
    Check $what $ok $detail
    if (-not $ok) { throw "'$what' failed, so the rest can't be checked." }
}

function Info([string] $what) { Write-Host "INFO  $what" }

function Step([string] $name) {
    $script:step = $name
    Write-Host ''
    Write-Host "== $name"
}

# Polls `condition` every half second for up to `seconds`; true as soon as it holds.
function Wait-Until([scriptblock] $condition, [int] $seconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($seconds)
    while ($true) {
        if (& $condition) { return $true }
        if ([DateTime]::UtcNow -ge $deadline) { return $false }
        Start-Sleep -Milliseconds 500
    }
}

# A property of an object that may not have it (StrictMode throws on a missing one).
function Get-PropertyValue($object, [string] $name) {
    if ($null -eq $object) { return $null }
    $property = $object.PSObject.Properties[$name]
    if ($property) { $property.Value } else { $null }
}

# Runs a console program and returns its exit code and output (stdout, then stderr), printing both unless `quiet`.
function Invoke-Tool([string] $file, [string[]] $arguments, [int] $timeoutSeconds = 120, [Text.Encoding] $encoding = $null, [switch] $quiet) {
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $file
    $info.Arguments = (@($arguments) | ForEach-Object { if ($_ -match '\s' -or $_ -eq '') { "`"$_`"" } else { $_ } }) -join ' '
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    if ($encoding) {
        $info.StandardOutputEncoding = $encoding
        $info.StandardErrorEncoding = $encoding
    }
    Write-Host "> $([IO.Path]::GetFileName($file)) $($info.Arguments)"
    $process = [Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($timeoutSeconds * 1000)) {
        $process.Kill()
        throw "$file $($info.Arguments) didn't finish in $timeoutSeconds s."
    }
    $process.WaitForExit()
    $output = ($stdout.Result + $stderr.Result).Trim()
    if (-not $quiet) { foreach ($line in $output -split "`r?`n") { if ($line) { Write-Host "      $line" } } }
    [pscustomobject] @{ ExitCode = $process.ExitCode; Output = $output }
}

# marquee.exe writes UTF-8 (Program.cs sets it).
function Invoke-Marquee([string[]] $arguments, [int] $timeoutSeconds = 120) {
    Invoke-Tool $marquee $arguments $timeoutSeconds ([Text.Encoding]::UTF8)
}

# msiexec, silent, never restarting, with a verbose log named after the step. Returns its exit code.
function Invoke-Msiexec([string] $name, [string] $arguments) {
    $log = Join-Path $LogDir "$name.log"
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = Join-Path ([Environment]::SystemDirectory) 'msiexec.exe'
    $info.Arguments = "$arguments /qn /norestart /l*v `"$log`""
    $info.UseShellExecute = $false
    Write-Host "> msiexec $($info.Arguments)"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($info)
    if (-not $process.WaitForExit(15 * 60 * 1000)) {
        $process.Kill()
        throw "msiexec ($name) didn't finish in 15 minutes."
    }
    Info ('msiexec ({0}) exited {1} after {2:N0} s; log {3}' -f $name, $process.ExitCode, $watch.Elapsed.TotalSeconds, $log)
    return $process.ExitCode
}

# Windows Installer's COM object has no type library PowerShell can use, so every call goes through InvokeMember.
$installer = New-Object -ComObject WindowsInstaller.Installer
function Invoke-Com($target, [string] $member, [System.Reflection.BindingFlags] $kind, [object[]] $arguments) {
    $target.GetType().InvokeMember($member, $kind, $null, $target, $arguments)
}

# A property from an MSI's Property table, read without installing; every handle is released so msiexec can open it.
function Get-MsiProperty([string] $msi, [string] $name) {
    $database = Invoke-Com $installer 'OpenDatabase' InvokeMethod @($msi, 0)
    $view = Invoke-Com $database 'OpenView' InvokeMethod @(('SELECT `Value` FROM `Property` WHERE `Property` = ''{0}''' -f $name))
    try {
        Invoke-Com $view 'Execute' InvokeMethod @() | Out-Null
        $record = Invoke-Com $view 'Fetch' InvokeMethod @()
        if ($null -eq $record) { return $null }
        $value = [string] (Invoke-Com $record 'StringData' GetProperty @([int] 1))
        [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) | Out-Null
        return $value
    }
    finally {
        Invoke-Com $view 'Close' InvokeMethod @() | Out-Null
        [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) | Out-Null
        [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null
    }
}

# Gives the MSI `msi` a new product code and package code, as a second build of the same source has, and returns the
# product code. Only the copy made for it is changed.
function New-ProductCode([string] $msi) {
    $productCode = '{' + [guid]::NewGuid().ToString().ToUpperInvariant() + '}'
    # 1 = msiOpenDatabaseModeTransact: changes are written by Commit.
    $database = Invoke-Com $installer 'OpenDatabase' InvokeMethod @($msi, 1)
    try {
        $view = Invoke-Com $database 'OpenView' InvokeMethod @(('UPDATE `Property` SET `Value` = ''{0}'' WHERE `Property` = ''ProductCode''' -f $productCode))
        Invoke-Com $view 'Execute' InvokeMethod @() | Out-Null
        Invoke-Com $view 'Close' InvokeMethod @() | Out-Null
        [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) | Out-Null
        # The package code is the summary information's revision number (property 9): a different package must have
        # its own, or Windows Installer takes it for the one it already has.
        $summary = Invoke-Com $database 'SummaryInformation' GetProperty @([int] 1)
        Invoke-Com $summary 'Property' SetProperty @([int] 9, ('{' + [guid]::NewGuid().ToString().ToUpperInvariant() + '}')) | Out-Null
        Invoke-Com $summary 'Persist' InvokeMethod @() | Out-Null
        [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary) | Out-Null
        Invoke-Com $database 'Commit' InvokeMethod @() | Out-Null
    }
    finally {
        [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null
    }
    return $productCode
}

# The product codes of every installed Marquee, by the UpgradeCode they share.
function Get-InstalledProducts {
    # Not through Invoke-Com: PowerShell would unroll the StringList it returns, and an empty one would come back null.
    $list = $installer.GetType().InvokeMember('RelatedProducts', [System.Reflection.BindingFlags]::GetProperty, $null, $installer, @($upgradeCode))
    $count = [int] (Invoke-Com $list 'Count' GetProperty @())
    @(for ($i = 0; $i -lt $count; $i++) { [string] (Invoke-Com $list 'Item' GetProperty @([int] $i)) })
}

function Get-ProductVersion([string] $productCode) {
    [string] (Invoke-Com $installer 'ProductInfo' GetProperty @($productCode, 'VersionString'))
}

function Get-ProductsText {
    $products = @(Get-InstalledProducts)
    if ($products.Count -eq 0) { return 'none' }
    ($products | ForEach-Object { "$_ $(Get-ProductVersion $_)" }) -join ', '
}

# Settings > Apps' entries named Marquee, from both registry views.
function Get-ArpEntries {
    @(foreach ($key in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall') {
        Get-ChildItem $key -ErrorAction SilentlyContinue |
            ForEach-Object { Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue } |
            Where-Object { (Get-PropertyValue $_ 'DisplayName') -eq 'Marquee' }
    })
}

# The machine PATH's entries for the install folder, as stored (unexpanded), with or without a trailing backslash.
function Get-PathEntries {
    $key = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SYSTEM\CurrentControlSet\Control\Session Manager\Environment')
    try { $path = [string] $key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) }
    finally { $key.Close() }
    @($path -split ';' | Where-Object { $_.Trim().TrimEnd('\') -eq $installDir })
}

function Get-ShortcutTarget {
    if (-not (Test-Path -LiteralPath $shortcut)) { return '<no shortcut>' }
    $shell = New-Object -ComObject WScript.Shell
    $shell.CreateShortcut($shortcut).TargetPath
}

# The task's XML from Task Scheduler, or null when there is no task. Test-Task keeps the XML with the logs.
function Get-TaskXml {
    $result = Invoke-Tool 'schtasks.exe' @('/Query', '/TN', $taskName, '/XML') -quiet
    if ($result.ExitCode -ne 0) {
        Info "schtasks /Query exited $($result.ExitCode): $($result.Output)"
        return $null
    }
    [xml] $result.Output
}

# The task's folder in Task Scheduler, or null when there is none: schtasks can't tell, so Task Scheduler's own API.
function Get-TaskFolder {
    $service = New-Object -ComObject Schedule.Service
    $service.Connect()
    try { $service.GetFolder('\Marquee') }
    catch { $null }
}

# Every MarqueeTray.exe running, wherever it is from: on the runner only Marquee's can be.
function Get-Trays { @(Get-CimInstance Win32_Process -Filter "Name = 'MarqueeTray.exe'") }

function Get-TraysText {
    $trays = @(Get-Trays)
    if ($trays.Count -eq 0) { return 'none' }
    ($trays | ForEach-Object { "pid $($_.ProcessId) in session $($_.SessionId) from $($_.ExecutablePath)" }) -join '; '
}

function Get-Sddl([string] $path) { (Get-Acl -LiteralPath $path).GetSecurityDescriptorSddlForm('Owner, Access') }

# A folder's access rules as sorted text, to compare two sets regardless of order.
function Get-RulesText([System.Security.AccessControl.DirectorySecurity] $security) {
    (@($security.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]) | ForEach-Object {
        '{0} {1} 0x{2:X} {3} {4}{5}' -f $_.AccessControlType, $_.IdentityReference.Value, $_.FileSystemRights.value__,
            $_.InheritanceFlags, $_.PropagationFlags, $(if ($_.IsInherited) { ' inherited' } else { '' })
    }) | Sort-Object) -join '; '
}

function Test-Version([string] $file, [string] $version) {
    $actual = if (Test-Path -LiteralPath $file) { [string] (Get-Item -LiteralPath $file).VersionInfo.ProductVersion } else { '<missing>' }
    # The SDK adds "+<commit>" to the informational version.
    Check "$([IO.Path]::GetFileName($file)) is version $version" ($actual -match "^$([regex]::Escape($version))(\+|$)") "it is $actual"
}

# Exactly one Marquee installed, at `version`, in Windows Installer and in Settings > Apps.
function Test-Product([string] $version) {
    $products = @(Get-InstalledProducts)
    Check "one Marquee installed, version $version" ($products.Count -eq 1 -and (Get-ProductVersion $products[0]) -eq $version) "installed: $(Get-ProductsText)"
    $arp = @(Get-ArpEntries)
    $arpText = ($arp | ForEach-Object { "$($_.PSChildName) $(Get-PropertyValue $_ 'DisplayVersion')" }) -join ', '
    Check "one Marquee in Settings > Apps, DisplayVersion $version" ($arp.Count -eq 1 -and (Get-PropertyValue $arp[0] 'DisplayVersion') -eq $version) "entries: $arpText"
}

function Test-Files([string] $version) {
    foreach ($file in 'marquee.exe', 'MarqueeTray.exe', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'themes\example-landscape\layout.yaml',
        'themes\example-portrait\layout.yaml', 'themes\example-agents\layout.yaml', 'themes\example-showcase\layout.yaml',
        'themes\example-showcase\video\radar.mp4') {
        Check "installs $file" (Test-Path -LiteralPath (Join-Path $installDir $file) -PathType Leaf) 'missing'
    }
    $presentMon = @(Get-ChildItem (Join-Path $installDir 'PresentMon') -Filter 'PresentMon-*-x64.exe' -ErrorAction SilentlyContinue)
    Check 'installs PresentMon\PresentMon-<version>-x64.exe' ($presentMon.Count -eq 1) "found $($presentMon.Count)"
    Test-Version $marquee $version
    Test-Version $trayExe $version
}

function Test-PathEntry {
    $entries = @(Get-PathEntries)
    Check "the machine PATH has $installDir once" ($entries.Count -eq 1) "$($entries.Count) entries"
}

function Test-Shortcut {
    $target = Get-ShortcutTarget
    Check "the Start menu shortcut $shortcut starts MarqueeTray.exe" ($target -eq $trayExe) "target $target"
}

# The task as LogonTask.Xml makes it, read back from Task Scheduler; the XML is kept with the logs.
function Test-Task([string] $label) {
    $xml = Get-TaskXml
    Check "the logon task $taskName exists" ($null -ne $xml) 'schtasks /Query found none'
    if ($null -eq $xml) { return }
    $xml.Save((Join-Path $LogDir "task-$label.xml"))
    $ns = New-Object System.Xml.XmlNamespaceManager $xml.NameTable
    $ns.AddNamespace('t', 'http://schemas.microsoft.com/windows/2004/02/mit/task')
    $expected = [ordered] @{
        'Principals/t:Principal/t:GroupId' = 'S-1-5-32-545'
        'Principals/t:Principal/t:RunLevel' = 'HighestAvailable'
        'Triggers/t:LogonTrigger/t:Enabled' = 'true'
        'Actions/t:Exec/t:Command' = "`"$trayExe`""
        'Actions/t:Exec/t:WorkingDirectory' = $installDir
        'Settings/t:MultipleInstancesPolicy' = 'Parallel'
        'Settings/t:DisallowStartIfOnBatteries' = 'false'
        'Settings/t:StopIfGoingOnBatteries' = 'false'
        'Settings/t:ExecutionTimeLimit' = 'PT0S'
        'Settings/t:Priority' = '4'
        'Settings/t:Enabled' = 'true'
    }
    # Task Scheduler leaves out an element whose value is the schema's default when it writes the XML back, as the first
    # CI run showed for both Enabled elements: a missing one there means its default, true.
    $defaults = @{ 'Triggers/t:LogonTrigger/t:Enabled' = 'true'; 'Settings/t:Enabled' = 'true' }
    foreach ($path in $expected.Keys) {
        $node = $xml.SelectSingleNode("/t:Task/t:$path", $ns)
        $actual = if ($null -ne $node) { $node.InnerText.Trim() } elseif ($defaults.Contains($path)) { $defaults[$path] } else { '<none>' }
        Check "task $($path -replace 't:', '') is $($expected[$path])" ($actual -eq $expected[$path]) "it is $actual"
    }
}

# A folder as SecureFolder makes it, which marquee task install does for %ProgramData%\Marquee and its updates folder:
# a real folder owned by Administrators, nothing inherited from %ProgramData%, and exactly the permissions in `sddl`.
function Test-SecureFolder([string] $folder, [string] $sddl) {
    $exists = Test-Path -LiteralPath $folder -PathType Container
    Check "$folder exists" $exists 'missing'
    if (-not $exists) { return }
    Check "$folder is a real folder, not a link" (-not (Get-Item -LiteralPath $folder -Force).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) 'a reparse point'
    $acl = Get-Acl -LiteralPath $folder
    $owner = $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
    Check "$folder is owned by Administrators" ($owner -eq 'S-1-5-32-544') "owner $owner"
    Check "$folder inherits nothing from %ProgramData%" $acl.AreAccessRulesProtected 'inheritance is on'
    $expected = New-Object System.Security.AccessControl.DirectorySecurity
    $expected.SetSecurityDescriptorSddlForm($sddl)
    $want = Get-RulesText $expected
    $have = Get-RulesText $acl
    Check "$folder has exactly SecureFolder's permissions" ($have -eq $want) "has: $have; wants: $want"
    Info "$folder is $(Get-Sddl $folder)"
}

function Test-DataFolder {
    Test-SecureFolder $dataDir $dataSddl
    Test-SecureFolder $updatesDir $updatesSddl
}

$aborted = $null
$rebuildDir = $null
try {
    Step 'The runner'
    $olderVersion = Get-MsiProperty $OlderMsi 'ProductVersion'
    $currentVersion = Get-MsiProperty $CurrentMsi 'ProductVersion'
    Info "older MSI $OlderMsi is $olderVersion; current MSI $CurrentMsi is $currentVersion"
    Info "running as $($identity.Name) ($userSid) in session $((Get-Process -Id $PID).SessionId), $([Environment]::OSVersion.VersionString)"
    $lua = Get-PropertyValue (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -ErrorAction SilentlyContinue) 'EnableLUA'
    Info "UAC EnableLUA = $lua; $((whoami /groups | Select-String 'Mandatory Label') -replace '\s+', ' ')"
    Require "the older MSI ($olderVersion) is older than the current one ($currentVersion)" ([version] $olderVersion -lt [version] $currentVersion)
    Require 'no Marquee is installed yet' (@(Get-InstalledProducts).Count -eq 0) "installed: $(Get-ProductsText)"
    Require "there is no $installDir yet" (-not (Test-Path -LiteralPath $installDir))
    Require "there is no $dataDir yet" (-not (Test-Path -LiteralPath $dataDir))

    Step "A $dataDir that is already there is left as it is, and the install folder can't be moved"
    # Made as any program could make it: inheriting %ProgramData%'s permissions, which let every user add to it, so
    # marquee task install (SecureFolder) refuses it, leaves it alone and says so in the log, and the install goes on.
    New-Item -ItemType Directory $dataDir | Out-Null
    $planted = Join-Path $dataDir 'planted.txt'
    Set-Content -LiteralPath $planted 'Made before Marquee was installed; the installer must leave it.'
    $before = Get-Sddl $dataDir
    Info "made $dataDir first: $before"
    # A folder every user could change, asked for by the public name such a folder would have and by the private one
    # it has: msiexec must take neither from its command line.
    $elsewhere = Join-Path $env:SystemDrive "Marquee-elsewhere-$([guid]::NewGuid().ToString('N'))"
    $name = "install-$currentVersion-over-existing-folder"
    $exit = Invoke-Msiexec $name "/i `"$CurrentMsi`" INSTALLFOLDER=`"$elsewhere\`" InstallDir=`"$elsewhere\`""
    Require "installing $currentVersion exits 0" ($exit -eq 0) "exit $exit"
    Check "the install left $dataDir's permissions as they were" ((Get-Sddl $dataDir) -eq $before) "now $(Get-Sddl $dataDir)"
    Check "the install left what was in $dataDir" (Test-Path -LiteralPath $planted) 'planted.txt is gone'
    $installLog = Join-Path $LogDir "$name.log"
    $logText = if (Test-Path -LiteralPath $installLog) { Get-Content -LiteralPath $installLog -Raw } else { '' }
    Check "the install's log warns that Marquee won't keep files in $dataDir" ($logText.Contains("Marquee won't keep files in $dataDir")) "not in $installLog"
    Check "it installed into $installDir all the same" (Test-Path -LiteralPath $marquee) 'no marquee.exe there'
    Check "it installed nothing into $elsewhere" (-not (Test-Path -LiteralPath $elsewhere)) 'the folder is there'
    $products = @(Get-InstalledProducts)
    Require 'one Marquee installed' ($products.Count -eq 1) "installed: $(Get-ProductsText)"
    $exit = Invoke-Msiexec "uninstall-$currentVersion-over-existing-folder" "/x $($products[0])"
    Require "uninstalling $currentVersion exits 0" ($exit -eq 0) "exit $exit"
    Check "the uninstall left $dataDir's permissions as they were" ((Test-Path -LiteralPath $dataDir) -and (Get-Sddl $dataDir) -eq $before) 'changed or gone'
    Check "the uninstall left what was in $dataDir" (Test-Path -LiteralPath $planted) 'planted.txt is gone'
    Remove-Item -LiteralPath $dataDir -Recurse -Force
    Require "$dataDir is deleted again for the next install" (-not (Test-Path -LiteralPath $dataDir))

    Step "Install $olderVersion"
    $exit = Invoke-Msiexec "install-$olderVersion" "/i `"$OlderMsi`""
    Require "installing $olderVersion exits 0" ($exit -eq 0) "exit $exit"
    Test-Product $olderVersion
    Test-Files $olderVersion
    $validate = Invoke-Marquee @('validate', (Join-Path $installDir 'themes\example-landscape'))
    Check "marquee.exe runs from $installDir (validate exits 0)" ($validate.ExitCode -eq 0) "exit $($validate.ExitCode)"
    Test-PathEntry
    Test-Shortcut
    Test-Task $olderVersion
    Test-DataFolder
    # StartTray runs the task in the installing user's sessions as the install commits; the tray may take a moment.
    Wait-Until { @(Get-Trays).Count -gt 0 } 10 | Out-Null
    Info "trays running after the install (its StartTray action): $(Get-TraysText)"
    $installedSddl = Get-Sddl $dataDir

    Step "Upgrade to $currentVersion"
    $exit = Invoke-Msiexec "upgrade-to-$currentVersion" "/i `"$CurrentMsi`""
    Require "upgrading to $currentVersion exits 0" ($exit -eq 0) "exit $exit"
    Test-Product $currentVersion
    Test-Files $currentVersion
    Test-PathEntry
    Test-Shortcut
    Test-Task $currentVersion
    Check "the upgrade left $dataDir's permissions as they were" ((Get-Sddl $dataDir) -eq $installedSddl) "now $(Get-Sddl $dataDir)"
    Wait-Until { @(Get-Trays).Count -gt 0 } 10 | Out-Null
    Info "trays running after the upgrade: $(Get-TraysText)"

    Step "Repair $currentVersion"
    # A repair closes the trays (StopTrays), puts back the files and the task, and starts the tray again (StartTray).
    $traysBefore = @(Get-Trays).Count
    Info "trays running before the repair: $(Get-TraysText)"
    $exit = Invoke-Msiexec "repair-$currentVersion" "/fa `"$CurrentMsi`""
    Require "repairing $currentVersion exits 0" ($exit -eq 0) "exit $exit"
    Test-Product $currentVersion
    Test-Files $currentVersion
    Test-Task "repair-$currentVersion"
    if ($traysBefore -gt 0) {
        Check 'a tray runs again after the repair' (Wait-Until { @(Get-Trays).Count -gt 0 } 20) 'none running'
    }
    else {
        Info 'not exercised: no tray was running before the repair, so starting it again was not checked'
    }
    Info "trays running after the repair: $(Get-TraysText)"

    Step "Reinstall $currentVersion from a rebuild"
    # Every build has a new product code, so a second build of one version is a different product with the same
    # version. This makes one from the current MSI, as a rebuild would be: a copy with a new product code and package
    # code. Installing it must replace the installed one (AllowSameVersionUpgrades), not install beside it.
    $rebuildDir = Join-Path ([IO.Path]::GetTempPath()) "marquee-test-install-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory $rebuildDir | Out-Null
    $rebuilt = Join-Path $rebuildDir ([IO.Path]::GetFileName($CurrentMsi))
    Copy-Item -LiteralPath $CurrentMsi $rebuilt
    $rebuiltCode = New-ProductCode $rebuilt
    $installedCode = @(Get-InstalledProducts)
    Info "installed: $(Get-ProductsText); the rebuild is $rebuiltCode"
    $exit = Invoke-Msiexec "reinstall-$currentVersion-rebuilt" "/i `"$rebuilt`""
    Require "installing the rebuild of $currentVersion exits 0" ($exit -eq 0) "exit $exit"
    Test-Product $currentVersion
    Check 'the rebuild replaced the installed one' (@(Get-InstalledProducts) -contains $rebuiltCode -and -not (@(Get-InstalledProducts) -contains $installedCode[0])) `
        "installed: $(Get-ProductsText)"
    Test-Files $currentVersion
    Test-PathEntry
    Test-Task "reinstall-$currentVersion"
    Wait-Until { @(Get-Trays).Count -gt 0 } 10 | Out-Null
    Info "trays running after the reinstall: $(Get-TraysText)"

    Step "Downgrade to $olderVersion is refused"
    $exit = Invoke-Msiexec "downgrade-to-$olderVersion" "/i `"$OlderMsi`""
    # MajorUpgrade's DowngradeErrorMessage is a launch condition (NOT WIX_DOWNGRADE_DETECTED), which fails the install
    # with 1603; 1638 would be Windows Installer refusing the same product code.
    Check "installing $olderVersion over $currentVersion fails with 1603 or 1638" ($exit -eq 1603 -or $exit -eq 1638) "exit $exit"
    $downgradeLog = Join-Path $LogDir "downgrade-to-$olderVersion.log"
    $logText = if (Test-Path -LiteralPath $downgradeLog) { Get-Content -LiteralPath $downgradeLog -Raw } else { '' }
    Check 'its log has the DowngradeErrorMessage' ($logText.Contains($downgradeMessage)) "'$downgradeMessage' not in $downgradeLog"
    Test-Product $currentVersion
    Test-Version $marquee $currentVersion
    Check "the logon task $taskName is still there" ($null -ne (Get-TaskXml)) 'gone'

    Step 'marquee task start and task stop'
    # Whether the runner has a session for the job's user decides what task start does; it isn't checked, only shown.
    try {
        $sessions = Invoke-Tool 'query.exe' @('session')
        Info "query session exited $($sessions.ExitCode)"
    }
    catch {
        Info "query session couldn't run: $($_.Exception.Message)"
    }
    $start = Invoke-Marquee @('task', 'start', '--user', $userSid)
    Info "marquee task start --user $userSid exited $($start.ExitCode)"
    if ($start.ExitCode -eq 0) { Wait-Until { @(Get-Trays).Count -gt 0 } 20 | Out-Null }
    Info "trays running after task start: $(Get-TraysText)"
    $traysBefore = @(Get-Trays).Count
    $stop = Invoke-Marquee @('task', 'stop')
    Check 'marquee task stop exits 0' ($stop.ExitCode -eq 0) "exit $($stop.ExitCode)"
    Check 'no tray is left running after task stop' (Wait-Until { @(Get-Trays).Count -eq 0 } 15) "running: $(Get-TraysText)"
    if ($traysBefore -eq 0) { Info 'not exercised: no tray was running, so task stop had none to close' }

    Step "Marquee accepts $dataDir"
    # An elevated marquee run puts its log in users\<SID>\logs, made and checked by SecureFolder.Ensure from %ProgramData%
    # down, and refuses to start if a folder isn't safe: the same check the tray makes. One frame into a file sink, with
    # no sensors and no HTTP, touches no hardware and opens no port.
    $sink = Join-Path ([IO.Path]::GetTempPath()) "marquee-test-install-$([guid]::NewGuid().ToString('N'))"
    $run = Invoke-Marquee @('run', (Join-Path $installDir 'themes\example-landscape'), '--sensors', 'none', '--no-http', '--sink', $sink, '--frames', '1') 180
    Check 'an elevated marquee run accepts the folder and renders a frame (exit 0)' ($run.ExitCode -eq 0) "exit $($run.ExitCode)"
    Check "it logged to $userData\logs" (@(Get-ChildItem (Join-Path $userData 'logs') -File -ErrorAction SilentlyContinue).Count -gt 0) 'no log file'
    Check 'it rendered the frame' (Test-Path -LiteralPath (Join-Path $sink 'frame_000001.png')) "no frame in $sink"
    if (Test-Path -LiteralPath $userData) {
        $userAcl = Get-Acl -LiteralPath $userData
        $owner = $userAcl.GetOwner([Security.Principal.SecurityIdentifier]).Value
        Check "$userData is owned by Administrators and inherits nothing" ($owner -eq 'S-1-5-32-544' -and $userAcl.AreAccessRulesProtected) (Get-Sddl $userData)
    }

    Step "Uninstall $currentVersion"
    # What the tray would have left: a downloaded update, which the uninstaller deletes, and a user's log, which it keeps.
    New-Item -ItemType Directory -Force $updatesDir | Out-Null
    Set-Content -LiteralPath (Join-Path $updatesDir 'Marquee-9.9.9-x64.msi') 'Not an installer: test-install.ps1 checks that uninstalling deletes it.'
    $userFile = Join-Path $userData 'logs\test-install.log'
    New-Item -ItemType Directory -Force (Split-Path -Parent $userFile) | Out-Null
    Set-Content -LiteralPath $userFile 'A user''s log: test-install.ps1 checks that uninstalling keeps it.'
    # And a tray, where the runner can start one, for the uninstall's StopTrays to close.
    $start = Invoke-Marquee @('task', 'start', '--user', $userSid)
    if ($start.ExitCode -eq 0) { Wait-Until { @(Get-Trays).Count -gt 0 } 20 | Out-Null }
    $traysBefore = @(Get-Trays).Count
    Info "trays running before the uninstall: $(Get-TraysText)"
    $products = @(Get-InstalledProducts)
    Require 'one Marquee installed' ($products.Count -eq 1) "installed: $(Get-ProductsText)"
    $exit = Invoke-Msiexec "uninstall-$currentVersion" "/x $($products[0])"
    Require "uninstalling $currentVersion exits 0" ($exit -eq 0) "exit $exit"
    Check 'no Marquee is installed' (@(Get-InstalledProducts).Count -eq 0) "installed: $(Get-ProductsText)"
    Check 'no Marquee in Settings > Apps' (@(Get-ArpEntries).Count -eq 0) "$(@(Get-ArpEntries).Count) entries"
    $left = @(Get-ChildItem -LiteralPath $installDir -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
    Check "$installDir is gone" (-not (Test-Path -LiteralPath $installDir)) "left: $(($left | Select-Object -First 10) -join ', ')"
    Check 'the Start menu shortcut is gone' (-not (Test-Path -LiteralPath $shortcut)) 'still there'
    Check "the machine PATH no longer has $installDir" (@(Get-PathEntries).Count -eq 0) "$(@(Get-PathEntries).Count) entries"
    Check "the logon task $taskName is gone" ($null -eq (Get-TaskXml)) 'still there'
    Check 'the downloaded update is deleted' (@(Get-ChildItem $updatesDir -Filter 'Marquee-*' -ErrorAction SilentlyContinue).Count -eq 0) 'still there'
    Check "$updatesDir, empty now, is deleted" (-not (Test-Path -LiteralPath $updatesDir)) 'still there'
    Check "the user's log in $userData is kept" (Test-Path -LiteralPath $userFile) 'deleted'
    Check "$dataDir is kept" (Test-Path -LiteralPath $dataDir -PathType Container) 'deleted'
    Check 'no tray is running' (@(Get-Trays).Count -eq 0) "running: $(Get-TraysText)"
    if ($traysBefore -eq 0) { Info 'not exercised: no tray was running, so the uninstall had none to close' }
    Check "the logon task's folder \Marquee is gone too" ($null -eq (Get-TaskFolder)) 'still there'
}
catch {
    $aborted = "$($_.Exception.Message) (in '$($script:step)')"
    Write-Host "FAIL  stopped: $aborted"
    Write-Host $_.ScriptStackTrace
}
finally {
    # The rebuilt MSI is 60 MB, not a log; Windows Installer keeps its own copy of what it installed.
    if ($rebuildDir) { Remove-Item -LiteralPath $rebuildDir -Recurse -Force -ErrorAction SilentlyContinue }
    # Marquee's own logs (the trays' and marquee run's) go with the msiexec logs.
    if (Test-Path -LiteralPath $dataDir) {
        foreach ($logs in @(Get-ChildItem (Join-Path $dataDir 'users') -Directory -ErrorAction SilentlyContinue)) {
            $source = Join-Path $logs.FullName 'logs'
            if (Test-Path -LiteralPath $source) {
                try { Copy-Item -LiteralPath $source (Join-Path $LogDir "marquee-logs-$($logs.Name)") -Recurse -Force }
                catch { Write-Host "Couldn't copy $source`: $($_.Exception.Message)" }
            }
        }
    }
}

$failed = @($results | Where-Object { -not $_.Ok })
Write-Host ''
Write-Host '== Summary'
Write-Host "$($results.Count) checks: $($results.Count - $failed.Count) passed, $($failed.Count) failed."
foreach ($failure in $failed) { Write-Host "FAIL  $($failure.What)$(if ($failure.Detail) { " ($($failure.Detail))" })" }
if ($aborted) { Write-Host "Stopped early: $aborted" }
Write-Host "Logs: $LogDir"
if ($failed.Count -gt 0 -or $aborted) { exit 1 }
exit 0
