# Third-party notices

Marquee is licensed under the PolyForm Noncommercial License 1.0.0 (`LICENSE`). The installer puts the components below in `Program Files\Marquee` beside it,
each under its own license. The license and notices files each NuGet package comes with are installed too, in
`licenses\<package>`, and building the installer stops if a file in it came from a package this page doesn't name.

## What the installer redistributes

| Component | NuGet package | What it is for | License |
|---|---|---|---|
| [.NET 8](https://github.com/dotnet/runtime) runtime | `Microsoft.NETCore.App.Runtime.win-x64` | Runs `marquee.exe` and `MarqueeTray.exe`, so no .NET needs installing: `coreclr.dll`, `hostfxr.dll`, `System.*.dll` and the rest of the runtime's files | MIT. The runtime's own notices, for the code it includes from others, are in `licenses\Microsoft.NETCore.App.Runtime.win-x64\THIRD-PARTY-NOTICES.TXT` |
| [.NET 8 Windows Desktop](https://github.com/dotnet/windowsdesktop) runtime: Windows Forms and WPF | `Microsoft.WindowsDesktop.App.Runtime.win-x64` | The tray icon, its menu and the preview window: `System.Windows.Forms*.dll`, `Presentation*.dll`, `D3DCompiler_47_cor3.dll`, `vcruntime140_cor3.dll`, the language folders (`de`, `ja` and so on) and the rest | MIT |
| .NET libraries | `System.ServiceProcess.ServiceController`, `System.Diagnostics.EventLog`, `System.IO.Ports`, `System.Management`, `System.CodeDom`, `System.Threading.AccessControl` | Checking L-Connect's service (the first two); the others are LibreHardwareMonitorLib's | MIT, with the notices in `licenses\<package>` |
| [SkiaSharp](https://github.com/mono/SkiaSharp) | `SkiaSharp`, `SkiaSharp.NativeAssets.Win32` | 2D rendering: `SkiaSharp.dll` and `libSkiaSharp.dll` | MIT. `libSkiaSharp.dll` is built from [Skia](https://skia.org) (BSD-3-Clause) and the libraries Skia uses, whose notices are in `licenses\SkiaSharp.NativeAssets.Win32\THIRD-PARTY-NOTICES.txt` |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | `YamlDotNet` | Reading `layout.yaml` and `config.yaml` | MIT ([below](#yamldotnet-license)) |
| [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 0.9.6 | `LibreHardwareMonitorLib` | Hardware sensors | MPL-2.0 ([below](#mpl-20-components)) |
| [DiskInfoToolkit](https://github.com/Blacktempel/DiskInfoToolkit), [RAMSPDToolkit](https://github.com/Blacktempel/RAMSPDToolkit) and [BlackSharp](https://github.com/Blacktempel/BlackSharp) | `DiskInfoToolkit`, `RAMSPDToolkit-NDD`, `BlackSharp.Core` | LibreHardwareMonitorLib's drive and memory sensors | MPL-2.0 ([below](#mpl-20-components)) |
| [HidSharp](https://software.seekye.com/hidsharp) | `HidSharp` | LibreHardwareMonitorLib's USB sensors | Apache License 2.0, in `licenses\HidSharp\LICENSE.txt`. Copyright 2010-2025 James F. Bellinger |
| Mono.Posix | `Mono.Posix.NETStandard` | A LibreHardwareMonitorLib dependency for Linux, unused on Windows: `Mono.Posix.NETStandard.dll`, `MonoPosixHelper.dll`, `libMonoPosixHelper.dll` | Mono's license, as the package's license link says (<https://go.microsoft.com/fwlink/?linkid=869050>). © Microsoft Corporation |
| [PresentMon](https://github.com/GameTechDev/PresentMon) 2.6.0 | none (downloaded from its GitHub release when Marquee is built) | Game FPS: Intel's console exe, unmodified, as `PresentMon\PresentMon-2.6.0-x64.exe` | MIT ([below](#presentmon-license)) |
| [Inter](https://github.com/rsms/inter) | none (`src/Marquee.Core/Fonts`) | The default font, built into `Marquee.Core.dll` | SIL Open Font License 1.1, in `licenses\Inter\OFL.txt` |
| [Rajdhani](https://github.com/google/fonts/tree/main/ofl/rajdhani) Medium, SemiBold and Bold, by the Indian Type Foundry | none (`src/Marquee.Editor/Fonts/Rajdhani`) | The theme editor's headings and numbers, built into `MarqueeEditor.exe` | SIL Open Font License 1.1, in `licenses\Rajdhani\OFL.txt` |
| [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono) Medium | none (`src/Marquee.Editor/Fonts/JetBrainsMono`) | The theme editor's small labels, built into `MarqueeEditor.exe` | SIL Open Font License 1.1, in `licenses\JetBrainsMono\OFL.txt` |

The MSI file also holds a custom-action DLL from the [WiX Toolset](https://github.com/wixtoolset/wix) 5.0.2 Util
extension (`Wix4UtilCA_X64`), which Windows Installer uses to run Marquee's own `marquee task` commands while it
installs and uninstalls. It isn't installed. WiX is under the Microsoft Reciprocal License (MS-RL), and its source is
at that link. The rest of WiX is a build tool and doesn't ship.

## Not redistributed

- [lian-li-linux](https://github.com/sgtaziz/lian-li-linux) (MIT): the screen's and the LED ring's protocol facts
  (for the ring, 60 LEDs in three 64-byte packets, from `crates/lianli-devices/src/winusb/led.rs`). Marquee is an
  independent implementation and copies none of its code.
- PawnIO and HWiNFO are separate programs that users install themselves.
- Windows Media Foundation, which decodes MP4, M4V, MOV and WMV video clips, is part of Windows. Marquee calls it and
  redistributes none of it.
- Claude Code and Codex are the user's own installs; Marquee reads their files and runs `claude`.

## MPL-2.0 components

LibreHardwareMonitorLib (copyright LibreHardwareMonitor), DiskInfoToolkit, RAMSPDToolkit-NDD and BlackSharp.Core
(copyright Florian K.) are covered by the [Mozilla Public License 2.0](https://mozilla.org/MPL/2.0/). Marquee
redistributes them unmodified, in the executable form their NuGet packages contain. Their source code is available
under the same license from their repositories, at the commits those packages were built from:

- LibreHardwareMonitorLib 0.9.6:
  <https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701>
- DiskInfoToolkit 1.1.2: <https://github.com/Blacktempel/DiskInfoToolkit/tree/25319eae5781e75bcf141e844ceab2afe94d40ea>
- RAMSPDToolkit-NDD 1.4.2: <https://github.com/Blacktempel/RAMSPDToolkit/tree/3b47b960e0830fef344624ad5e389675d5f0a1ce>
- BlackSharp.Core 1.0.7: <https://github.com/Blacktempel/BlackSharp/tree/c70b735c6cec123ee8a046ac4a0bc6c606f52cf0>

## YamlDotNet license

```
Copyright (c) Antoine Aubry and contributors

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
of the Software, and to permit persons to whom the Software is furnished to do
so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## PresentMon license

PresentMon's copyright notice and license, from `LICENSE.txt` in its repository, which MIT requires to accompany
every copy:

```
Copyright (C) 2017-2024 Intel Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom
the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL
THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES
OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE
OR OTHER DEALINGS IN THE SOFTWARE.
```
