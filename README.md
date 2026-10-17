# Marquee

Source-available Windows driver and renderer for USB PC-accessory LCD panels.
First supported device: the 8.8" Universal Screen (USB `1CBE:A088`).

> Status: early development. Milestone 2 renders YAML themes live from hardware sensors, with hot reload and a preview window.

## Usage

    marquee devices
    marquee show-image picture.png [--orientation portrait|portrait-flipped|landscape|landscape-flipped] [--brightness 0-100] [--repeat N] [--sink <folder>]
    marquee sensors [--sensors lhm|hwinfo]
    marquee run <theme-folder> [--orientation ...] [--fps 1-60] [--sensors lhm|hwinfo|none] [--preview] [--sink <folder>] [--frames N]

Only one program can drive the screen at a time. L-Connect 3's background service holds it, and closing the
L-Connect 3 window doesn't free it. Ending the service in Task Manager doesn't stick either: a watcher service starts
it again. To hand the screen to Marquee:

1. Right-click L-Connect's tray icon and choose its stop-service option. Fans keep their last settings meanwhile.
2. Start Marquee (`marquee run …`) and wait for the picture.
3. Start the service again from the same tray menu. L-Connect skips the screen and goes on running your fans and RGB.

While Marquee has the screen, L-Connect doesn't control it or its LED ring, and neither shows up in the L-Connect app.
To give the screen back, quit Marquee, then stop and start the service from the tray again.

`marquee run themes/example-landscape --preview` shows a theme on the screen and in a preview window (press `1` for 1:1).
Edit `layout.yaml` and save: the screen updates within a second. If the file has a mistake, the last good layout stays
on screen and the error, with its line number, shows in the console and the preview. `--sink <folder>` writes PNG
frames instead of using the screen, and keeps writing them until stopped (Ctrl+C) unless `--frames N` is given;
`--frames N` renders N frames and exits.

## Orientation

Pick the orientation that matches how the screen is mounted. A vertical mount is `portrait`; a horizontal
mount is `landscape` (the default). If the picture comes out upside down, use the `-flipped` variant.
Layouts are designed at 480×1920 (portrait) or 1920×480 (landscape). A theme declares which one it was designed
for; running it on the other shape prints a warning and shows it scaled down rather than stretched.

## Themes

A theme is a folder with a `layout.yaml` and any images or fonts it uses. A small example:

```yaml
canvas: { orientation: landscape, fps: 30 }
styles:
  big: { size: 84, weight: bold, color: "#F4F6FA" }
  hot: { color: "#FF5A36", glow: { color: "#FF5A36", radius: 16 }, pulse: { property: glow, period: 1200 } }
background:
  - fill: { linear: ["#070A12", "#141B2E"], angle: 90 }
widgets:
  - type: value
    at: [40, 40]
    size: [300, 110]
    source: cpu.temp
    format: "{0:0}°C"
    smooth: 400
    style: big
    when:
      - { above: 85, style: hot }
      - { stale: true, style: { color: "#5A6275" } }
```

- **Widgets:** `text` (templates like `"{gpu.load:0}% · {gpu.vram_used|bytes}"`), `value`, `bar`, `gauge`, `graph`, `image`, `group`.
- **Styles:** named in `styles:`, layered with `extends:`, changed by `when:` rules (`above`, `below`, `equals`, `stale`), animated with `pulse:`.
- **Background:** `fill` (color or gradient) and `image` layers, each with `opacity` and `blend`.
- **Colors** are quoted CSS hex (`"#RRGGBB"` or `"#RRGGBBAA"`): an unquoted `#` starts a YAML comment.
- **Fonts:** the bundled Inter by default, an installed font by name, or a `.ttf`/`.otf` file in the theme folder.

See `themes/example-landscape` and `themes/example-portrait`. A full theme authoring guide comes with the public release.

## Hardware sensors

`marquee sensors` lists every key and its current value. Keys include `cpu.temp`, `cpu.load`, `cpu.clock`,
`cpu.power`, `gpu.temp`, `gpu.load`, `gpu.clock`, `gpu.power`, `gpu.vram_used`, `ram.used_pct`, `ram.used` and
`fan.<name>`. A key whose source stops updating goes stale after 5 seconds and shows `—`.

- **LibreHardwareMonitor** (`--sensors lhm`, the default) reads sensors directly. CPU temperatures and motherboard
  sensors need an elevated terminal and the [PawnIO](https://pawnio.eu/) driver; without them those keys show `—`.
  On some AMD cards (verified: Radeon RX 7800 XT) it reports only GPU load and memory, so `gpu.temp`, `gpu.clock`
  and `gpu.power` stay `—`; use HWiNFO for those.
- **HWiNFO** (`--sensors hwinfo`) reads HWiNFO's shared memory instead. Start HWiNFO and turn on
  Settings → Shared Memory Support.

## Verified hardware

| Device | Firmware | Notes |
|---|---|---|
| 8.8" Universal Screen (`1CBE:A088`) | `lianli88_0001_0023` | `portrait` is upright when mounted vertically; brightness works; the image persists after Marquee exits; about 21 ms per frame (~47 fps) including JPEG encoding |
| 8.8" Universal Screen (`1CBE:A088`) with `marquee run` | `lianli88_0001_0023` | example themes at 30 fps for 12 min (render ~2.5 ms, send ~18 ms); `--fps 60` tops out at ~51 fps; hot reload in ~2 s with the last good layout kept on errors; letterboxing on an orientation mismatch; reconnects after unplugging |

## Credits

The panel protocol was documented by the community, notably
[sgtaziz/lian-li-linux](https://github.com/sgtaziz/lian-li-linux) (MIT). Marquee is an independent implementation.
Third-party components and their licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Marquee is not affiliated with or endorsed by Lian Li Industrial Co., Ltd.
