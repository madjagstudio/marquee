# Third-party notices

Marquee is licensed under the PolyForm Noncommercial License 1.0.0. It uses these components, each under its own license.

| Component | Use | License |
|---|---|---|
| [SkiaSharp](https://github.com/mono/SkiaSharp) | 2D rendering | MIT |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | Parsing `layout.yaml` | MIT |
| [System.ServiceProcess.ServiceController](https://github.com/dotnet/runtime) | Checking whether L-Connect's service holds the screen | MIT |
| [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | Hardware sensors, used unmodified as a NuGet library | MPL-2.0 |
| [Inter](https://github.com/rsms/inter) | Bundled default font (`src/Marquee.Core/Fonts`) | SIL Open Font License 1.1 (`src/Marquee.Core/Fonts/OFL.txt`) |
| [PresentMon](https://github.com/GameTechDev/PresentMon) 2.6.0 | Game FPS: Intel's console exe, redistributed unmodified as `PresentMon\PresentMon-2.6.0-x64.exe` next to `marquee.exe` | MIT (its copyright notice and license text are [below](#presentmon-license)) |

LibreHardwareMonitorLib's source is available from the link above, as MPL-2.0 requires. Marquee does not include or
modify it; NuGet downloads it at build time.

PawnIO and HWiNFO are separate programs that users install themselves. Marquee does not redistribute them.

Windows Media Foundation, which decodes MP4, M4V, MOV and WMV video clips, is part of Windows. Marquee calls it and
redistributes none of it.

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
