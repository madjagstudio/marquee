# Marquee

Source-available Windows driver and renderer for USB PC-accessory LCD panels.
First supported device: the 8.8" Universal Screen (USB `1CBE:A088`).

> Status: early development. Milestone 4a adds settings in `config.yaml`, the frame rate of the game in front, video
> backgrounds, `marquee validate` for checking a theme, and log files to the Claude Code and Codex status, pushed
> values and shader backgrounds of milestone 3. A tray app and an installer come next.

## Usage

    marquee devices
    marquee show-image <file> [--orientation portrait|portrait-flipped|landscape|landscape-flipped] [--brightness 0-100] [--repeat N] [--sink <folder>]
    marquee sensors [--sensors lhm|hwinfo|none] [--no-claude-usage] [--config <file>]
    marquee run [<theme-folder>] [--orientation ...] [--fps 1-60] [--sensors lhm|hwinfo|none] [--preview] [--sink <folder>] [--frames N]
                [--http-port 1024-65535 | --no-http] [--no-claude-usage] [--no-fps] [--config <file>]
    marquee validate <theme-folder|zip> [--fps 1-60]
    marquee push <key> <value> [--ttl 30s|5m|2h] [--port 1024-65535] [--config <file>]
    marquee setup-claude [--undo] [--dry-run]
    marquee statusline        (Claude Code runs this; see setup-claude)

Settings not given on the command line come from `%LOCALAPPDATA%\Marquee\config.yaml`, or the file `--config` names
(see [Settings](#settings)).

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
`--frames N` renders N frames and exits. `marquee run` without a folder shows the theme set in `config.yaml`.

The command line's and the preview's messages are in resource files that can be translated;
[docs/localization.md](docs/localization.md) says how to add a language.

## Settings

`marquee run` reads its settings from `%LOCALAPPDATA%\Marquee\config.yaml`. The file is optional, and so is every key
in it. Here is each key with its values; the comments give the defaults.

```yaml
theme: example-agents         # a theme folder's name or path; no default
orientation: landscape        # portrait | portrait-flipped | landscape | landscape-flipped; default landscape
fps: 30                       # 1-60; default: each theme's canvas.fps
sensors: lhm                  # lhm | hwinfo | none; default lhm
http: { port: 47913 }         # port 1024-65535, or http: off; default on, port 47913
providers: { claude: on, claude_usage: on, codex: on, fps: on }   # each on or off; default on
custom: { max_keys: 1000 }    # 1-10000; default 1000
ring: theme                   # theme | off | a quoted color like "#3A7BFF"; default theme
lconnect: { handover: off }   # off | ask | auto; default off
updates: check                # check | off; default check
```

- `theme` is the theme `marquee run` shows when no folder is given on its command line. Without either, it says so
  and exits.
- `http` is the local server for [custom values](#custom-values): `http: off` turns it off.
- `providers` turns sources of values on and off: `claude` is the Claude Code session counts (`claude.sessions.*`),
  `claude_usage` the `/usage` poll for Claude Code's limits, `codex` the Codex limits and sessions, and `fps` the
  [game FPS](#game-fps).
- `custom.max_keys` is how many different `custom.*` keys a running Marquee holds.
- `ring` is the screen's LED ring: `theme` lets each theme set it, `off` keeps it dark, and a color shows that color
  for every theme. Quote the color: an unquoted `#` starts a comment.
- `lconnect.handover` is whether Marquee takes the screen from L-Connect: `off` leaves it alone, `ask` offers to in a
  tray notification, and `auto` takes it without asking.
- `updates: check` has the tray look for a newer release once a day; `updates: off` turns that off.

`ring`, `lconnect` and `updates` take effect with the tray and the LED ring, which arrive in milestone 4b.

A command-line flag wins over `config.yaml`, `config.yaml` over the theme, and the theme over the default: `fps` in
`config.yaml` overrides every theme's `canvas.fps`, and `--fps` overrides both. `--no-http`, `--http-port`,
`--no-claude-usage` and `--no-fps` likewise win over `http` and `providers`. Orientation is the exception: a theme
never sets it (see [Orientation](#orientation)).

A theme given by name (`example-agents`), on the command line or as `theme:`, is looked for in
`%LOCALAPPDATA%\Marquee\themes`, then in a `themes` folder next to `marquee.exe`, then in the current folder. A value
with a `\` or `/` in it, a full path, `.` or `..` is a path instead, relative to the current folder. In `config.yaml`,
"the current folder" is the folder `config.yaml` is in, so the file means the same thing however Marquee was started.

An unknown key or a bad value is an error with its line number (`config.yaml line 3: …`). At startup, a broken file
means the default settings, with the error in the console and the log. `marquee run` picks up edits while it runs and
changes only what the edit touches, keeping the values read so far: a new theme or `fps` is loaded first and then
swapped in while the screen stays on, a new `orientation` reopens the screen, and a provider or `http` change restarts
only that provider or the server. If the new settings can't be applied (the new theme doesn't load, say), it keeps
the previous ones; a new theme that doesn't load is watched, and the new settings apply as soon as it is fixed and
saved, with no need to save `config.yaml` again. An edit that breaks the file, or names a theme that can't be found, is logged with the reason (and
the line) and changes nothing until the file is fixed; flags on the command line still win over whatever the file
says.

`marquee sensors` reads `sensors` and `providers.claude_usage` from `config.yaml` too, and `marquee push` reads `http`.
`--config <file>` makes `run`, `sensors` and `push` read that file instead, to try settings out or keep several sets
side by side; `marquee run` then watches that file. A file named that way that doesn't exist gets a warning, and the
defaults are used.

## Orientation

Pick the orientation that matches how the screen is mounted, with `--orientation` or `orientation:` in `config.yaml`. A
vertical mount is `portrait`; a horizontal mount is `landscape` (the default). If the picture comes out upside down,
use the `-flipped` variant.
Layouts are designed at 480×1920 (portrait) or 1920×480 (landscape). A theme declares which one it was designed
for; running it on the other shape prints a warning and shows it scaled down rather than stretched. The theme's shape
never changes the orientation: that is how the screen is mounted, which only you know.

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
- **Background:** `fill` (color or gradient), `image`, `shader` and `video` layers, each with `opacity` and `blend`; up
  to 16 layers, at most 4 of them shaders and 2 of them videos.
- **Colors** are quoted CSS hex (`"#RRGGBB"` or `"#RRGGBBAA"`): an unquoted `#` starts a YAML comment.
- **Fonts:** the bundled Inter by default, an installed font by name, or a `.ttf`/`.otf` file in the theme folder.
- **Times:** `resets_at` and `updated_at` keys are Unix seconds and `now` is the current time, so
  `"{claude.five_hour.resets_at - now|duration}"` reads "2h 14m".

See `themes/example-landscape`, `themes/example-portrait` and `themes/example-agents`. A full theme authoring guide
comes with the public release.

### Shader backgrounds

A `shader` layer runs an [SkSL](https://skia.org/docs/user/sksl/) file from the theme folder over the whole canvas.
Marquee sets `uniform float time` (seconds), `uniform float day` (whole days) and `uniform float2 resolution` (pixels);
declare only the ones a shader uses. `time` wraps back to 0 once a day so it stays precise, and `day` counts up by one
at the same moment. A cycle whose period divides a day (a minute, an hour) carries on across the wrap with `time`
alone; for any other period, add the day's offset: `fract(fract(day * 86400.0 / period) + time / period)`. Bind any
other uniform to a number or an expression, one value
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
A shader that takes too long would freeze the screen, so when a theme loads Marquee renders each shader layer once at
1/8 of its size each way and estimates what a full frame costs. A layer whose estimate is over the frame budget
(1000 ms divided by the fps, from `canvas.fps` or `--fps`) is skipped, with its estimated cost in the console and the
preview; lower its `scale` or the fps, or simplify the shader. The budget is per layer, and the test frame runs with
`time`, `day` and bound uniforms at 0. Marquee never lowers a layer's scale on its own.
A shader that doesn't compile is skipped with the error in the console and the preview, and editing the `.sksl` file
reloads the theme like editing `layout.yaml`.

### Video backgrounds

A `video` layer plays a clip from the theme folder, over and over for as long as the theme shows.

```yaml
background:
  - video: clips/rain.mp4
    fit: cover        # cover | contain | stretch | none, as for images (default cover)
    opacity: 0.8
    blend: screen
```

MP4, M4V, MOV and WMV clips go through Windows' own decoder (Media Foundation), so nothing is bundled for them. H.264
needs nothing extra; HEVC, VP9 and AV1 need Microsoft's extension for that format from the Microsoft Store. Animated
GIF and WebP go through Skia. Any other extension is an error in `layout.yaml`, with its line.

A theme can have at most 2 video layers. A clip's file can be up to 200 MB, and its frames up to 3840×2160 either way
round (2160×3840 suits a portrait theme). Each clip is decoded on its own thread, a few frames ahead and scaled to the
size it is drawn at, and the layer draws the frame for the current time, so drawing never waits on decoding and the
clip plays at its own speed.

A clip that can't be played skips its layer, with a warning in the console and the preview that says why: the file is
missing or over a limit, Windows has no decoder for its format, it is damaged, or Media Foundation isn't there at all
(Windows N editions need the Media Feature Pack, from Settings > System > Optional features). When the theme loads,
Marquee also times a few frames of each clip and skips one that decodes slower than it plays; a smaller clip, or one
with fewer frames per second, fixes that. A clip that fails partway through stays on its last good frame, and the
reason is logged once. Saving over a clip reloads the theme, like editing `layout.yaml`.

### Checking a theme

    marquee validate themes/example-agents
    marquee validate neon.zip --fps 60
    marquee validate my-theme

A bare name is looked up where `marquee run` looks for one (see Settings). `marquee validate` loads a theme the way
`marquee run` does, but it lists every problem instead of stopping at the
first: each independent mistake in `layout.yaml` with its line; images, fonts, shaders and clips that are missing or
can't be used; shaders that don't compile or are over the frame budget (at `--fps` if given, otherwise the theme's
`canvas.fps`); clips that can't be decoded or can't keep up; files over the limits below; and symbolic links or
junctions, which it doesn't follow. Anything `marquee run` would skip is an error. Anything it works around, such as a
font that isn't installed or a shader uniform left unbound, is a warning. It needs no screen and no `config.yaml`.

It prints one problem a line, sorted by file and then line, and ends with the theme's name, the orientation and size it
is designed for, and the counts:

    layout.yaml:4: error: fill: 'nope' is not a color. Use "#RRGGBB", "#RRGGBBAA" or "transparent"
    layout.yaml: warning: Font 'NoSuchFamily123' is not installed on this PC; using Inter instead.
    logo.png: error: Image 'logo.png' was not found and was skipped.
    neon (landscape, 1920×480): 2 errors, 1 warning.

It exits 0 when there are no errors (warnings alone are fine) and 1 when there are any.

A theme, in a folder or a zip, can have at most 1,000 files, each up to 200 MB and 512 MB in all. A zip holds the theme
at its top level (`layout.yaml` next to everything else) or in its one top-level folder. A zip file over 512 MB, or one
listing more than 1,000 entries, is refused before its entries are read. Before anything is extracted, Marquee checks
every entry: its name must stay inside the theme, it must not be a link, and it must not expand more
than 100 times its compressed size (the mark of a zip bomb). The zip is extracted into a temporary folder, which is
deleted afterwards. `marquee validate` never changes the theme or the zip.

## Claude Code and Codex

`marquee run` reads Claude Code and Codex status on its own. `marquee sensors` prints the session counts, the Codex
values and Claude Code's limits (through `/usage`, below), but not `claude.context.pct` or custom values: those exist
only inside a running `marquee run`.

| Keys | Source |
|---|---|
| `claude.five_hour.pct`, `claude.five_hour.resets_at`, `claude.seven_day.pct`, `claude.seven_day.resets_at`, `claude.updated_at` | Claude Code's `/usage`, which Marquee polls (every 5 minutes while it lists fresh limits), and its status line, through `marquee statusline` (below); the newest write wins |
| `claude.seven_day_model.pct`, `claude.seven_day_model.resets_at`, `claude.seven_day_model.name` | Claude Code's `/usage`: the per-model weekly limit (`weekly_scoped`) on plans that have one, the most used one if there are several; `name` is the model as `/usage` shows it ("Opus", say) |
| `claude.context.pct` | Claude Code's status line, through `marquee statusline` (below) |
| `claude.sessions.total`, `claude.sessions.busy`, `claude.sessions.idle` | Running sessions in `~/.claude/sessions` |
| `codex.primary.pct`, `codex.primary.resets_at`, `codex.primary.window_min`, the same for `codex.secondary`, `codex.updated_at` | The newest usage report in Codex's logs (`~/.codex/sessions`) |
| `codex.sessions.total`, `codex.sessions.busy`, `codex.sessions.idle` | Codex threads (CLI, desktop app or IDE) whose newest log event is under 10 minutes old |

A Claude Code session is busy or idle as its session file says. A Codex session is busy while a turn is in progress
(its last turn event is `task_started`) and idle after the turn completes or is interrupted.

Claude Code's usage limits exist only on claude.ai subscriptions (its docs name Pro and Max; API-key sessions have
none). Marquee checks them every 5 minutes by running `claude -p /usage` (no model call and no cost). That needs
Claude Code installed (`claude.exe` on `PATH`, in `~/.local/bin`, or the desktop app's bundled copy) and logged in to a
subscription, and it works for every Claude Code surface, so limits show up for desktop-app users too. The limits are
those of the account the `claude` command line is logged in to (`/status` in a terminal `claude` session shows which).
The desktop app keeps a login of its own, so if it uses a different account (a Team seat, say, next to a personal
plan), Marquee shows the command line's account; log the command line in to the same account (`/login`) to match. An npm install
provides only `claude.cmd`, which Marquee doesn't run; use the native installer or the desktop app. Add
`--no-claude-usage` to `marquee run` or `marquee sensors`, or set `providers: { claude_usage: off }` in `config.yaml`,
if you don't want Marquee starting `claude`. Marquee never
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
run the status line; the Claude desktop app's Code tab doesn't, which is why Marquee also polls `/usage`. Between
updates the limits keep their last value (`claude.updated_at` says how old it is), and each window goes stale (`—`)
when it resets. If `claude` can't be found, isn't logged in or fails, Marquee logs that once and tries again after 5
minutes. Which Codex window is primary depends on the plan: `codex.primary.window_min` is 300 for a 5-hour window and
10080 for a weekly one.

`marquee run` keeps the last limits across restarts in `%LOCALAPPDATA%\Marquee\claude-limits.json`, which holds only
each window's percentage and reset time and when they were read (no login or account details). It saves them whenever
they change and when it stops, and loads them at start, so the bars show at once, with `claude.updated_at` still saying
how old they are. A window that reset while Marquee was stopped stays stale (`—`), and a window without a reset time
isn't saved, nor is the per-model weekly limit (it shows after the first `/usage` poll). Fresh values from `/usage` or
the status line replace the loaded ones as they arrive. A missing or
unreadable file is skipped with one log line; deleting it is safe.

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
default port is 47913; `http: { port: N }` in `config.yaml` or `--http-port` changes it, and `http: off` or
`--no-http` turns it off. `marquee push` uses the port in `config.yaml`; if `marquee run` was started with
`--http-port`, pass the same `--port` to `marquee push`.

A running `marquee run` holds at most 1,000 different custom keys. Setting a key it already has always works; a push
that would add a key past the limit is refused with a message saying so. A key frees its place 10 minutes after it goes
stale, so keys pushed with a ttl come and go on their own. A key pushed without a ttl never goes stale, so it counts
until `marquee run` restarts. Reuse a fixed set of key names rather than making new ones (per build number, say), or
give them a ttl. `custom: { max_keys: N }` in `config.yaml` sets the limit, from 1 to 10,000.

## Hardware sensors

`marquee sensors` lists every key and its current value. Keys include `cpu.temp`, `cpu.load`, `cpu.clock`,
`cpu.power`, `gpu.temp`, `gpu.load`, `gpu.clock`, `gpu.power`, `gpu.vram_used`, `ram.used_pct`, `ram.used` and
`fan.<name>`. A key whose source stops updating goes stale after 5 seconds and shows `—`.

The source is `--sensors`, or `sensors:` in `config.yaml`.

- **LibreHardwareMonitor** (`--sensors lhm`, the default) reads sensors directly. CPU temperatures and motherboard
  sensors need an elevated terminal and the [PawnIO](https://pawnio.eu/) driver; without them those keys show `—`.
  On some AMD cards (verified: Radeon RX 7800 XT) it reports only GPU load and memory, so `gpu.temp`, `gpu.clock`
  and `gpu.power` stay `—`; use HWiNFO for those.
- **HWiNFO** (`--sensors hwinfo`) reads HWiNFO's shared memory instead. Start HWiNFO and turn on
  Settings → Shared Memory Support.

## Game FPS

`marquee run` measures the frame rate of the game in front:

| Key | Value |
|---|---|
| `fps` | Frames per second over the last second |
| `frametime` | The average time between frames over the last second, in ms |
| `fps.1pct_low` | The 1% low: the average of the slowest 1% of frame times (at least one frame) over the last 10 seconds, as frames per second |
| `fps.app` | The game's process name, without `.exe` |

The game is whichever process owns the window in front, checked every 250 ms; Marquee itself and `dwm.exe`, which
draws the desktop, are left out. A game that presents more than one swap chain (an overlay or a launcher next to the
game, say) is measured on the one with the most frames in the last second. The keys go stale 2 seconds after the
game's last frame, so on the desktop they show `—`; a theme can hide them with `when: stale`. `marquee sensors`
doesn't list them, since they need a game in front and a running, elevated `marquee run`.

Frames are counted by [PresentMon](https://github.com/GameTechDev/PresentMon) 2.6.0, Intel's open-source frame-time
tool (MIT). It is bundled next to `marquee.exe` as `PresentMon\PresentMon-2.6.0-x64.exe`, and Marquee checks its SHA-256
before every start and won't start a file that doesn't match. It runs without a window as Marquee's child and ends with
Marquee, even if Marquee crashes. If it exits, Marquee logs why and starts it again. Building Marquee downloads it once
from its GitHub release into `.cache\presentmon` and checks the same hash.

PresentMon reads Windows' event tracing, which needs administrator rights, so FPS works only when Marquee runs as
administrator. A logon task that starts Marquee elevated comes with the tray app; until then, start `marquee run` from
an elevated terminal. Unelevated, FPS is off with a warning saying why. `providers: { fps: off }` in `config.yaml` or
`--no-fps` turns FPS off, and the warning with it.

## Logs

`marquee run` writes what it prints to `%LOCALAPPDATA%\Marquee\logs\marquee-YYYYMMDD.log` as well, one file per day,
so a problem can still be read after the console is closed. The files of the last 7 days, today's included, are kept;
older ones are deleted. A day's file stops at 10 MB, with a last line saying so, and logging carries on in the next
day's file. The file can be opened while Marquee runs.

## Verified hardware

| Device | Firmware | Notes |
|---|---|---|
| 8.8" Universal Screen (`1CBE:A088`) | `lianli88_0001_0023` | `portrait` is upright when mounted vertically; brightness works; the image persists after Marquee exits; about 21 ms per frame (~47 fps) including JPEG encoding |
| 8.8" Universal Screen (`1CBE:A088`) with `marquee run` | `lianli88_0001_0023` | example themes at 30 fps for 12 min (render ~2.5 ms, send ~18 ms); `--fps 60` tops out at ~51 fps; hot reload in ~2 s with the last good layout kept on errors; letterboxing on an orientation mismatch; reconnects after unplugging |
| 8.8" Universal Screen (`1CBE:A088`) with `example-agents` | `lianli88_0001_0023` | shader theme at 30 fps (render ~11 ms with the aurora at `scale: 0.25`, send ~26 ms, overlapped); `--fps 60` topped out at ~43 fps with the earlier `scale: 0.5`; Claude Code limits through the status line from terminal sessions (the desktop app's Code tab doesn't run status lines), with `marquee run` elevated or not; Codex limits and sessions; `marquee push` |
| 8.8" Universal Screen (`1CBE:A088`) with config.yaml, video and game FPS | `lianli88_0001_0023` | `marquee run` with no folder, the theme named in config.yaml; fps changed live in config.yaml without the screen going dark, and a bad value refused with its line; an MP4 screen recording as a background layer at ~30 fps (render ~7 ms, send ~28 ms); game FPS, frame time, 1% low and app name from PresentMon with Marquee elevated, `—` on the desktop, and no PresentMon process or ETW session left after Ctrl+C |

## Credits

The panel protocol was documented by the community, notably
[sgtaziz/lian-li-linux](https://github.com/sgtaziz/lian-li-linux) (MIT). Marquee is an independent implementation.
Third-party components and their licenses are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Marquee is not affiliated with or endorsed by Lian Li Industrial Co., Ltd.
