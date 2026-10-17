# Marquee

Source-available Windows driver and renderer for USB PC-accessory LCD panels.
First supported device: the 8.8" Universal Screen (USB `1CBE:A088`).

> Status: early development. Milestone 1 pushes a static image to the screen.

## Usage

    marquee devices
    marquee show-image picture.png [--rotation cw|ccw] [--brightness 0-100] [--sink <folder>]

Only one program can drive the screen at a time. Quit L-Connect 3 before running Marquee.

## Credits

The panel protocol was documented by the community, notably
[sgtaziz/lian-li-linux](https://github.com/sgtaziz/lian-li-linux) (MIT). Marquee is an independent implementation.

Marquee is not affiliated with or endorsed by Lian Li Industrial Co., Ltd.
