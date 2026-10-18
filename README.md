# Marquee

Source-available Windows driver and renderer for USB PC-accessory LCD panels.
First supported device: the 8.8" Universal Screen (USB `1CBE:A088`).

> Status: early development. Milestone 3 adds Claude Code and Codex usage limits and sessions, values pushed by
> scripts, and animated shader backgrounds to the live YAML themes of milestone 2.

## Usage

    marquee devices
    marquee show-image picture.png [--orientation portrait|portrait-flipped|landscape|landscape-flipped] [--brightness 0-100] [--repeat N] [--sink <folder>]
    marquee sensors [--sensors lhm|hwinfo|none] [--no-claude-usage]
    marquee run <theme-folder> [--orientation ...] [--fps 1-60] [--sensors lhm|hwinfo|none] [--preview] [--sink <folder>] [--frames N] [--http-port N | --no-http] [--no-claude-usage]
    marquee setup-claude [--undo] [--dry-run]
    marquee push <key> <value> [--ttl 30s|5m|2h] [--port N]

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
- **Background:** `fill` (color or gradient), `image` and `shader` layers, each with `opacity` and `blend`; up to 16
  layers, at most 4 of them shaders.
- **Colors** are quoted CSS hex (`"#RRGGBB"` or `"#RRGGBBAA"`): an unquoted `#` starts a YAML comment.
- **Fonts:** the bundled Inter by default, an installed font by name, or a `.ttf`/`.otf` file in the theme folder.
- **Times:** `resets_at` and `updated_at` keys are Unix seconds and `now` is the current time, so
  `"{claude.five_hour.resets_at - now|duration}"` reads "2h 14m".

See `themes/example-landscape`, `themes/example-portrait` and `themes/example-agents`. A full theme authoring guide
comes with the public release.

### Shader backgrounds

A `shader` layer runs an [SkSL](https://skia.org/docs/user/sksl/) file from the theme folder over the whole canvas.
Marquee sets `uniform float time` (seconds) and `uniform float2 resolution` (pixels). `time` wraps back to 0 once a
day so it stays precise, which shows as one jump a day. Bind any other uniform to a number or an expression, one value
per component. Marquee binds floats only, so declare bound uniforms as `float`, `float2`, `float3` or `float4`; a layer
that binds an `int` uniform is skipped with a warning (an unbound one only gets the warning that it stays 0). A binding
whose sensor is missing or stale reads as 0.

```yaml
background:
  - shader: shaders/aurora.sksl
    uniforms:
      heat: "clamp(max(cpu.load, gpu.load) / 100, 0, 1)"   # float
      tint: [0.2, 0.5, "gpu.load / 100"]                 # float3
    scale: 0.5        # render at half size and stretch it up (0.1–1, default 1)
    opacity: 0.55
    blend: screen
```

Shaders run on the CPU. A smooth full-canvas shader costs about 10–15 ms per frame at `scale: 0.5`; Marquee draws the
next frame while the last one goes to the screen, so 30 fps holds, and it prints a warning if a theme can't keep up.
A shader that doesn't compile is skipped with the error in the console and the preview, and editing the `.sksl` file
reloads the theme like editing `layout.yaml`.

## Claude Code and Codex

`marquee run` reads Claude Code and Codex status on its own. `marquee sensors` prints the session counts, the Codex
values and Claude Code's limits (through `/usage`, below), but not `claude.context.pct` or custom values: those exist
only inside a running `marquee run`.

| Keys | Source |
|---|---|
| `claude.five_hour.pct`, `claude.five_hour.resets_at`, `claude.seven_day.pct`, `claude.seven_day.resets_at`, `claude.updated_at` | Claude Code's `/usage`, which Marquee polls (every 5 minutes while it lists fresh limits), and its status line, through `marquee statusline` (below); the newest write wins |
| `claude.context.pct` | Claude Code's status line, through `marquee statusline` (below) |
| `claude.sessions.total`, `claude.sessions.busy`, `claude.sessions.idle` | Running sessions in `~/.claude/sessions` |
| `codex.primary.pct`, `codex.primary.resets_at`, `codex.primary.window_min`, the same for `codex.secondary`, `codex.updated_at` | The newest usage report in Codex's logs (`~/.codex/sessions`) |
| `codex.sessions.total`, `codex.sessions.busy`, `codex.sessions.idle` | Codex threads (CLI, desktop app or IDE) whose newest log event is under 10 minutes old |

A Claude Code session is busy or idle as its session file says. A Codex session is busy while a turn is in progress
(its last turn event is `task_started`) and idle after the turn completes or is interrupted.

Claude Code's usage limits exist only on claude.ai subscriptions (its docs name Pro and Max; API-key sessions have
none). Marquee checks them every 5 minutes by running `claude -p /usage` (no model call and no cost). That needs
Claude Code installed (`claude.exe` on `PATH`, in `~/.local/bin`, or the desktop app's bundled copy) and logged in to a
subscription, and it works for every Claude Code surface, so limits show up for desktop-app users too. An npm install
provides only `claude.cmd`, which Marquee doesn't run; use the native installer or the desktop app. Add
`--no-claude-usage` to `marquee run` or `marquee sensors` if you don't want Marquee starting `claude`. Marquee never
starts `claude` elevated (unless Windows' UAC is off, where there is no boundary to protect): when Marquee runs elevated, it starts `claude` with your normal unelevated rights (taken from
Explorer), and if it can't (no Explorer yet, for example), it logs that once and uses only the status line. `claude`
runs with its auto-updater turned off, and one still running when Marquee exits is stopped with it.

When Claude Code can't fetch fresh limits (`/usage` then falls back to its last-known bars and lists none), Marquee
writes nothing, keeps the last values until they reset, and logs that once. It then checks less often, after 5, 10 and
20 minutes and then every 30, and goes back to every 5 minutes once fresh limits arrive (and logs that it is working
again).

Claude Code also hands its limits, and the context-window percentage, to its status-line command as a session runs,
which updates them sooner than the 5-minute poll. To use that, run `marquee setup-claude` once, from `marquee.exe`
itself (setup refuses to run from anything else; `--undo` runs from anywhere). It points Claude Code's `statusLine` at
`marquee statusline`, which hands the data to Marquee and then runs your previous status-line command, so your status
line looks the same as before. It edits only `statusLine` in `~/.claude/settings.json`, keeps a timestamped backup, and
`marquee setup-claude --undo` puts the old setting back. `--dry-run` shows the change first. If it can't rewrite
`settings.json` exactly (comments, trailing commas, repeated keys, a file that isn't UTF-8, a link, or a `statusLine`
that isn't an object), it changes nothing and says why. A project's `.claude/settings.json` or managed settings with
their own `statusLine` take precedence over this one.

Status-line updates arrive only while `marquee run` is going (elevated or not) and a Claude Code session is updating
its status line; only programs running as your Windows account can hand them to Marquee. Terminal sessions (`claude`)
run the status line; the Claude desktop app's Code tab doesn't, which is why Marquee also polls `/usage`. Limits are
lost when `marquee run` restarts, until a `/usage` poll with fresh limits (the first is a few seconds after start) or a
status-line update. In between they keep their last value (`claude.updated_at` says how old it is), and each window
goes stale (`—`) when it resets. If `claude` can't be found, isn't logged in or fails, Marquee logs that once and tries
again after 5 minutes. Which Codex window is primary depends on the plan: `codex.primary.window_min` is 300 for a
5-hour window and 10080 for a weekly one.

The session files and Codex logs are undocumented formats that may change. If Marquee can't read them any more, it
logs a warning and those keys go stale; everything else keeps working.

## Custom values

Scripts can put their own values on the screen as `custom.*` keys. `marquee push` adds the `custom.` prefix, so these
set `custom.build` and `custom.queue`:

    marquee push build "passing" --ttl 10m
    marquee push queue 12

`push` needs `marquee run` to be going, and fails with an explanation if it isn't. A value that reads as a number is
sent as a number; to send text that looks like a number, use HTTP. Over HTTP the key is written in full:

    Invoke-RestMethod -Method Post -Uri http://127.0.0.1:47913/values -ContentType application/json `
      -Body '{"custom.queue": 12, "ttl": "30s"}'

Any HTTP client works if it sends `Content-Type: application/json`. Values are numbers or text (up to 256
characters). Without `ttl` (`--ttl` for `push`) a value never goes stale; with it, the value goes stale (`—`) after
that long. The endpoint listens on 127.0.0.1 only and refuses requests from other machines and from web pages. The
default port is 47913; `--http-port` changes it (pass the same `--port` to `marquee push`), and `--no-http` turns it
off.

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
| 8.8" Universal Screen (`1CBE:A088`) with `example-agents` | `lianli88_0001_0023` | shader theme at 30 fps (render ~16 ms and send ~23 ms, overlapped); `--fps 60` tops out at ~43 fps; Claude Code limits through the status line from terminal sessions (the desktop app's Code tab doesn't run status lines), with `marquee run` elevated or not; Codex limits and sessions; `marquee push` |

## Credits

The panel protocol was documented by the community, notably
[sgtaziz/lian-li-linux](https://github.com/sgtaziz/lian-li-linux) (MIT). Marquee is an independent implementation.
Third-party components and their licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Marquee is not affiliated with or endorsed by Lian Li Industrial Co., Ltd.
