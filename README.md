# Marquee

Source-available Windows driver and renderer for USB PC-accessory LCD panels.
First supported device: the 8.8" Universal Screen (USB `1CBE:A088`).

> Status: early development. Milestone 1 pushes a static image to the screen.

## Usage

    marquee devices
    marquee show-image picture.png [--orientation portrait|portrait-flipped|landscape|landscape-flipped] [--brightness 0-100] [--repeat N] [--sink <folder>]

Only one program can drive the screen at a time. Quit L-Connect 3 before running Marquee.

## Orientation

Pick the orientation that matches how the screen is mounted. A vertical mount is `portrait`; a horizontal
mount is `landscape` (the default). If the picture comes out upside down, use the `-flipped` variant.
Layouts are designed at 480×1920 (portrait) or 1920×480 (landscape).

## Verified hardware

| Device | Firmware | Notes |
|---|---|---|
| 8.8" Universal Screen (`1CBE:A088`) | `lianli88_0001_0023` | `portrait` is upright when mounted vertically; brightness works; the image persists after Marquee exits; about 21 ms per frame (~47 fps) including JPEG encoding |

## Credits

The panel protocol was documented by the community, notably
[sgtaziz/lian-li-linux](https://github.com/sgtaziz/lian-li-linux) (MIT). Marquee is an independent implementation.

Marquee is not affiliated with or endorsed by Lian Li Industrial Co., Ltd.
