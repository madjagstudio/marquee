# Third-party notices

Marquee is licensed under the PolyForm Noncommercial License 1.0.0. It uses these components, each under its own license.

| Component | Use | License |
|---|---|---|
| [SkiaSharp](https://github.com/mono/SkiaSharp) | 2D rendering | MIT |
| [YamlDotNet](https://github.com/aaubry/YamlDotNet) | Parsing `layout.yaml` | MIT |
| [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | Hardware sensors, used unmodified as a NuGet library | MPL-2.0 |
| [Inter](https://github.com/rsms/inter) | Bundled default font (`src/Marquee.Core/Fonts`) | SIL Open Font License 1.1 (`src/Marquee.Core/Fonts/OFL.txt`) |

LibreHardwareMonitorLib's source is available from the link above, as MPL-2.0 requires. Marquee does not include or
modify it; NuGet downloads it at build time.

PawnIO and HWiNFO are separate programs that users install themselves. Marquee does not redistribute them.
