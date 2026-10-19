# Releasing

Every release file (the MSI, and the CLI zip) is published with a signature beside it: `Marquee-0.1.0-x64.msi` and
`Marquee-0.1.0-x64.msi.sig`. Marquee's updater downloads both, checks the signature against the public key built into
Marquee, and only then runs the installer. A missing or wrong signature means nothing runs.

The updater finds the two files by name, so the names are fixed: the installer is `Marquee-<version>-x64.msi`, where
`<version>` is the release's tag without its `v` (the tag `v0.1.0` has `Marquee-0.1.0-x64.msi`), and its signature is
that name plus `.sig`. The tag is always `v` and three numbers, with no `-rc.1` or other suffix. A release that is a
draft or a prerelease, or lacks either file under those names, is never offered (`ReleaseFeed`).

The signature is ECDSA P-256 over the file's SHA-256, made with one private key that only the owner holds. There is no
Authenticode: a first install shows Windows SmartScreen's "unknown publisher" prompt, and updates are protected by this
signature instead. Authenticode can be added later without changing any of this.

The tool for all of it is `tools/Marquee.ReleaseTool`. It is not shipped. Two scripts run it:
`tools/new-signing-key.ps1` and `tools/sign-release.ps1`.

## The key

- The private key lives in a password-encrypted PEM file (AES-256, its key derived from the password with 600,000
  rounds of PBKDF2-SHA256). The key is never written to disk unencrypted. Signing asks for the password and decrypts
  the key in memory only.
- Two copies are kept: one as an attachment in the owner's password manager, in the same entry as the password, and one
  on a USB stick kept offline. No other copies.
- The public half is `src/Marquee.Host/Updates/release-key.pub.pem`, built into every Marquee as a resource
  (`ReleaseKey`). A build trusts that key and no other.

## Before the key ceremony: the placeholder

Until the ceremony below, `release-key.pub.pem` holds a placeholder public key. Its private half was thrown away the
moment the pair was made, so nothing anyone has can sign for it, and no private key is committed anywhere: the tests
sign with keys they make in memory, and `ReleaseKeyTests` fails if a private key ever turns up in the repository.
Marquee knows the placeholder by its fingerprint (`ReleaseKey.IsPlaceholder`), and while it is built in the updater
installs nothing, whatever a signature says. Never publish a release built with the placeholder.

## The key ceremony (once)

On the owner's PC, from the root of the repository:

1. Generate a password in the password manager (long and random) and save it in a new entry, "Marquee release signing
   key".
2. Make the key, outside the repository:

   ```powershell
   tools\new-signing-key.ps1 $HOME\marquee-release-key.pem
   ```

   It asks for the password twice, without showing it. It never overwrites an existing file, and it refuses a path
   inside a git work tree (this one or any other), so the key file can't be committed by mistake; `.gitignore` ignores
   `*.pem` too, all but `release-key.pub.pem`. It prints the public key and its fingerprint.
3. Replace the contents of `src/Marquee.Host/Updates/release-key.pub.pem` with the public key the tool printed (the
   `-----BEGIN PUBLIC KEY-----` block). Leave `ReleaseKey`'s placeholder fingerprint as it is: it is how a build that
   still carries the placeholder knows it has no release key.
4. Attach `$HOME\marquee-release-key.pem` to the password manager entry. Paste the fingerprint into the entry's notes.
5. Copy `$HOME\marquee-release-key.pem` to the USB stick, and check that copy and the password together work, outside
   the repository. The release tool is built from this working tree each time the scripts run it, so it now trusts the
   public key from step 3, as Marquee will: `sign` refuses any key file but that key's (and refuses to sign at all while
   the placeholder is built in), and `verify` checks against it when it is given no public key.

   ```powershell
   Set-Content $env:TEMP\scratch.txt 'key check'
   tools\sign-release.ps1 E:\marquee-release-key.pem $env:TEMP\scratch.txt
   dotnet run --project tools\Marquee.ReleaseTool -c Release -- verify $env:TEMP\scratch.txt
   Remove-Item $env:TEMP\scratch.txt, $env:TEMP\scratch.txt.sig
   ```

6. Delete the working copy: `Remove-Item $HOME\marquee-release-key.pem`. It is encrypted, but the fewer copies the
   better.
7. Run `dotnet test -c Release`. `ReleaseKeyTests` checks that the embedded key is the one in the file and that
   no private key file is left in the repository, and `IsPlaceholder` turns false on its own.
8. Commit `release-key.pub.pem`. From then on every build trusts only the new key.

## Signing a release

1. Get the key file: save the attachment from the password manager to a local folder, or use the USB stick.
2. Sign the release files:

   ```powershell
   tools\sign-release.ps1 D:\marquee-release-key.pem out\Marquee-0.1.0-x64.msi out\marquee-0.1.0.zip
   ```

   It refuses a key file that isn't the key built into Marquee (`release-key.pub.pem`) once it has the password, and
   signs nothing then. It writes `<file>.sig` beside each file and checks each signature with the public half as soon as
   it is written. It prints the key's fingerprint: compare it with the one in the password manager entry.
3. Upload each file together with its `.sig` to the GitHub release.
4. Delete the key file you saved in step 1 (not the USB copy).

M4c extends `sign-release.ps1` to check the CI build's hashes and publish the release with `gh release`.

## Checking a signature

```powershell
dotnet run --project tools\Marquee.ReleaseTool -c Release -- verify out\Marquee-0.1.0-x64.msi
```

It reads `out\Marquee-0.1.0-x64.msi.sig` and says whether it is good under the key built into Marquee
(`release-key.pub.pem`, which the tool is built with too). It needs no password. To check against another public key,
give its PEM file first: `verify <pub.pem> <file>`.

## If the key is lost or leaks

There is no way to rotate the key in place. Every installed Marquee trusts the one public key it was built with, and
only that key can sign an update it will accept.

- **Lost** (both copies, or the password): make a new key with the ceremony above and ship a release with the new
  public key built in. Installed copies can't verify that release, so they will refuse it, and every user has to
  download and install the new MSI by hand, once. The installer upgrades in place and keeps their themes, config and
  logs. Say so plainly in the release notes and the README.
- **Leaked** (the key file and its password, or a suspicion of it): the same, as soon as possible, and say why in the
  release notes. Until users reinstall, whoever has the key could sign an update their Marquee accepts, though they
  would still need to publish it as this repository's latest release for the updater to find it.
