# Marquee

Source-available Windows driver and renderer for USB PC-accessory LCD panels.
First supported device: the 8.8" Universal Screen (USB `1CBE:A088`).

> Status: early development. Milestone 4b adds an installer and a tray app that starts at sign-in, takes the screen
> from L-Connect if you let it, lights the screen's LED ring and keeps Marquee up to date. Milestone 4a added settings
> in `config.yaml`, the frame rate of the game in front, video backgrounds, `marquee validate` and log files.

## Installing

Run `Marquee-<version>-x64.msi` from the [releases page](https://github.com/madjagstudios/marquee/releases). It installs
Marquee for every user of the PC and asks for administrator rights once. It has no options and nothing else to
install: Marquee brings the .NET runtime it runs on.

The installer isn't signed with a code-signing certificate, so Windows SmartScreen may say it is from an unknown
publisher: choose "More info", then "Run anyway". Updates carry a signature of Marquee's own instead (see
[Updates](#updates)).

It installs:

- Marquee into `C:\Program Files\Marquee`, always there: `INSTALLFOLDER=` on msiexec's command line is ignored. The
  logon task below starts the tray from that folder with administrator rights, so it must be one only administrators
  can change.
- That folder at the end of the system PATH, so `marquee` works in any new terminal. The folder also holds the .NET
  runtime's DLLs, so a program that looks for a DLL by name along the PATH may find them there.
- "Marquee" in the Start menu, which starts the tray, or opens its menu if it is running, and "Marquee Theme Editor"
  (see [Theme editor](#theme-editor)).
- A logon task, `\Marquee\Marquee` in Task Scheduler, that starts the tray with administrator rights, without a UAC
  prompt, for every user who signs in (see [The tray](#the-tray)).
- `%ProgramData%\Marquee`, where a Marquee running as administrator keeps each user's log and data, and where updates
  are downloaded (see [Logs](#logs)).

When the install finishes, the tray starts for the user who installed it; everyone else gets theirs when they next sign
in. Installing a newer version upgrades in place, keeping your settings and themes. Installing an older one over a
newer one is refused. If an upgrade fails partway, Windows Installer puts the old version back as it was, with its
logon task and its tray.

Installing, upgrading, repairing and uninstalling close every user's tray first, the way Quit does: the screen is let
go, the ring goes dark and the Claude Code limits are saved. A tray that hasn't closed after a minute (an L-Connect
handover can take that long to finish) is ended, so the installer may pause for up to about 65 seconds. If the log
says another program made the event the installer uses to close the tray, the next install ends the tray instead.

### Uninstalling

Uninstall Marquee from Settings > Apps. It closes the trays and removes the program folder, the Start menu entries, the
PATH entry, the logon task (and its `\Marquee` folder in Task Scheduler) and any downloaded updates. It leaves what is
yours, so installing again picks up where you left off:

- `%LOCALAPPDATA%\Marquee` for each user: `config.yaml`, your themes, and the logs and data of a Marquee that didn't
  run as administrator.
- `%ProgramData%\Marquee\users\<SID>` for each user whose tray ran as administrator: `logs`, `claude-limits.json`,
  `last-update-check.txt` and the first-run markers (`first-run-hint-shown`, `handover-offer-shown`).

Delete those folders to remove everything (the second needs an administrator).

## The tray

`MarqueeTray.exe` is Marquee's tray icon. It runs what `marquee run` runs, with no window and no command line: its
settings are `config.yaml`'s, and what `marquee run` would print goes to its [log](#logs). Its menu, from the top:

- **The status:** the theme, its frame rate and the screen, or what stops it (paused, waiting for the screen, L-Connect
  using it, a theme that didn't load). Under it, a line for each problem until it is fixed: an error in `config.yaml`
  or the theme, game FPS off, a source of values that is failing (LibreHardwareMonitor or PresentMon missing, `claude`
  not found, and so on), or why taking the screen from L-Connect failed. An error in `config.yaml`, the theme or a
  handover also shows as a balloon, at most once a minute for each.
- **Themes** and **Orientation:** your themes, then the installed examples, and the four orientations. Picking one
  writes `theme:` or `orientation:` to `config.yaml`, changing only that setting, then shows it.
- **Open preview**, **Edit theme…** (the theme on show in the [theme editor](#theme-editor)), **Open themes folder**
  (`%LOCALAPPDATA%\Marquee\themes`, made if it isn't there), **Open logs** and **Edit settings** (`config.yaml` in your
  editor; if there is none, one naming the theme on show is written first).
- **Take over the screen from L-Connect** and **Always take over at start** (see
  [Sharing the screen with L-Connect](#sharing-the-screen-with-l-connect)).
- **Pause** lets go of the screen, so another program (L-Connect, or `marquee run`) can have it, and turns the ring
  off. **Resume** takes it back.
- **Check for updates**, or **Install Marquee <version>…** once there is one (see [Updates](#updates)).
- **About Marquee** and **Quit.** Quit lets go of the screen, darkens the ring, saves the Claude Code limits and ends
  the tray until you next sign in. The Start menu's "Marquee" starts it again, but without administrator rights (see
  [below](#without-administrator-rights)); `marquee task start` in an administrator terminal starts it as signing in
  does.

The first time, with no `config.yaml`, the tray shows `example-landscape` and a balloon says to pick your theme and the
screen's orientation from its menu. That balloon shows once; it waits for a later start if L-Connect has the screen
(the [handover's offer](#sharing-the-screen-with-l-connect) comes first) or if `config.yaml` has an error (whose
balloon comes instead). A `config.yaml` without a `theme:` line shows `example-landscape` too.

`marquee run` while the tray is showing the screen says so and stops: pause the tray, or quit it, first.

The logon task starts the tray as administrator, which game FPS and the L-Connect handover need. So the tray takes care
with what your own programs can change:

- Files and folders it opens (themes, logs, `config.yaml`, a release page) open through Explorer, so the folder, your
  editor and your browser run as you, not as administrator. If Explorer isn't running (after a crash, or with another
  shell), they open nothing and a balloon says why.
- It changes `config.yaml` only if neither the file nor a folder on its path is a junction or symbolic link, and the
  file isn't a hard link: such a link could send an administrator's write anywhere on the PC. A write it refuses is
  a balloon and a log line, and changes nothing; edit the file by hand instead. It makes a missing `themes` folder, or
  `%LOCALAPPDATA%\Marquee`, only through a path without a link. A themes folder that is a junction already (to keep
  your themes on another drive, say) opens as usual.
- Its log and data go in an admin-only folder under `%ProgramData%\Marquee` (see [Logs](#logs)).

### Without administrator rights

For a user who isn't an administrator, the logon task starts the tray with their own rights; so does the Start menu
when no tray is running. That tray shows themes as usual, but game FPS and the L-Connect handover are off, and its menu
says why: "Game FPS is off: Marquee isn't running as administrator." and, while L-Connect has the screen, that Marquee
can take it only as administrator. It can't install updates either: its update offer opens the release's page.
`marquee claim-screen` in an administrator terminal still hands it the screen once. To have an administrator's tray
back, quit this one and run `marquee task start` in an administrator terminal.

### More than one user

Every user who signs in gets a tray of their own, with their own `config.yaml`, themes and folders, and one tray each:
starting it again opens the running one's menu. There is one screen, though: the first tray to open it keeps it until
it pauses or quits, and the others wait for it.

## Usage

    marquee devices
    marquee show-image <file> [--orientation portrait|portrait-flipped|landscape|landscape-flipped] [--brightness 0-100] [--repeat N] [--sink <folder>]
    marquee sensors [--sensors lhm|hwinfo|none] [--no-claude-usage] [--raw] [--config <file>]
    marquee run [<theme-folder>] [--orientation ...] [--fps 1-60] [--sensors lhm|hwinfo|none] [--preview] [--sink <folder>] [--frames N]
                [--http-port 1024-65535 | --no-http] [--no-claude-usage] [--no-fps] [--config <file>]
    marquee validate <theme-folder|zip> [--fps 1-60]
    marquee edit [<theme-folder>]   (opens the theme editor; see Theme editor)
    marquee push <key> <value> [--ttl 30s|5m|2h] [--port 1024-65535] [--config <file>]
    marquee setup-claude [--undo] [--dry-run]
    marquee task install [--tray <file>] | remove [--updates] | status | start [--user <SID>] | stop
    marquee claim-screen      (in an administrator terminal; see Sharing the screen with L-Connect)
    marquee statusline        (Claude Code runs this; see setup-claude)
    MarqueeTray.exe           (the tray; see The tray)

Settings not given on the command line come from `%LOCALAPPDATA%\Marquee\config.yaml`, or the file `--config` names
(see [Settings](#settings)); a `--sink` run reads only the file `--config` names.

`marquee run themes/example-landscape --preview` shows a theme on the screen and in a preview window (press `1` for 1:1).
Edit `layout.yaml` and save: the screen updates within a second. If the file has a mistake, the last good layout stays
on screen and the error, with its line number, shows in the console and the preview. `--sink <folder>` writes PNG
frames instead of using the screen, and keeps writing them until stopped (Ctrl+C) unless `--frames N` is given;
`--frames N` renders N frames and exits. `marquee run` without a folder shows the theme set in `config.yaml`.

A `--sink` run is for tests and for looking at a theme, and often runs beside the tray, so it keeps out of your setup:
it reads no `config.yaml` unless `--config` names one (so it needs a theme folder, and its frames are landscape unless
`--orientation` says otherwise), and it neither shows nor saves the last Claude Code limits. It still logs, like any
`marquee run`.

The command line's, the tray's, the preview's and the theme editor's messages are in resource files that can be
translated;
[docs/localization.md](docs/localization.md) says how to add a language.

### The logon task

`marquee task` looks after the logon task, `\Marquee\Marquee` in Task Scheduler, that starts the tray. The installer
runs it, so it is seldom needed by hand. All but `status` need an administrator terminal.

- `install` makes the task (or replaces it): at every sign-in, for every user, it starts the `MarqueeTray.exe` next to
  `marquee.exe` with the highest rights that user has. It refuses a tray that isn't under Program Files, because the
  task gives administrator rights to whoever can change that file; `--tray <file>` names another one (for
  development), with a warning when it is outside Program Files. It also makes `%ProgramData%\Marquee` (see
  [Logs](#logs)).
- `remove` deletes the task, and its folder in Task Scheduler. `--updates` deletes the downloaded updates too, as
  uninstalling does.
- `status` says whether the task is installed and what it starts, and exits 0 if it is and 1 if it isn't.
- `start` runs the task now in your sessions, or with `--user <SID>` in that user's, so the tray starts as it does at
  sign-in. For a user whose tray is running already, that opens its menu.
- `stop` closes every tray running from this folder, for every user, as the installer does before it changes any
  files. Each is asked to quit, and one still running after a minute is ended.

## Sharing the screen with L-Connect

Only one program can drive the screen at a time. L-Connect 3's background service holds it, and closing the
L-Connect 3 window doesn't free it. Marquee can take the screen over while L-Connect keeps running your fans and RGB:
it stops L-Connect's service watcher and service, opens the screen, and starts them again straight away. L-Connect then
skips the screen and goes on running everything else. The cost is a pause of a few seconds while the service is
stopped, in which your fans keep their last speed and L-Connect doesn't adjust them or the RGB. Marquee waits up to 15
seconds for the watcher, then the service, to stop. It never stops another service that depends on L-Connect's: if one
is running, the handover fails and L-Connect keeps the screen. Pausing or quitting Marquee during a handover (an install
closing the tray included) starts L-Connect again at once, and doesn't count as a failure.

`lconnect.handover` in [`config.yaml`](#settings) says when Marquee does this:

- `off` (the default): not on its own. While L-Connect has the screen, the tray's menu still offers "Take over the
  screen from L-Connect". The first time L-Connect holds the screen, a user who hasn't set `lconnect.handover` gets the
  `ask` balloon below, once, so that they hear the handover exists. Its click takes the screen this once, and it adds:
  "To do this at every start, choose "Always take over at start" in Marquee's menu." Writing
  `lconnect: { handover: off }` yourself turns that balloon off too. Without the tray, the log says how to hand the screen over by hand (below).
- `ask`: whenever Marquee finds L-Connect using the screen, the tray shows a balloon: "L-Connect is using the screen.
  Marquee can take it over while L-Connect keeps running your fans and RGB. Click here to take over." Clicking it does.
- `auto`: at start, and whenever Marquee finds the screen busy again (after you unplug it, say). If a try fails, Marquee
  tries again for as long as L-Connect's service holds the screen: after 1 minute, then 2, then 5, then every 10
  minutes, so Marquee and L-Connect never fight over the screen and a problem that lasts pauses L-Connect's fan control
  less and less often. The waits start again at 1 minute whenever the screen is found busy afresh: after Marquee had it,
  or after a pause. "Take over the screen from L-Connect" never waits for them. This holds for `marquee run` too.

The tray's "Always take over at start" item writes `lconnect.handover: auto` to `config.yaml` when you check it, and
`ask` when you uncheck it. Checking it while L-Connect has the screen also takes the screen at once, rather than at the
next time it is found busy. It shows only where L-Connect is installed, or while it is checked, so it can be turned off.
"Take over the screen from L-Connect" shows only while L-Connect has the screen, in any mode, and does nothing once
Marquee has it. A handover that fails says why in a balloon (with `auto`, that Marquee will keep trying) and in a menu
line until Marquee gets the screen.

Stopping L-Connect's service takes administrator rights. The tray has them when its logon task starts it. A tray
without them offers neither item, and while L-Connect has the screen its menu says why; a `marquee run` without them
says once in its log that it can't take the screen over. `marquee claim-screen`, run in an administrator terminal,
takes the screen once for a Marquee without those rights (a `marquee run` in another window, or such a tray): it stops
L-Connect, waits until that Marquee has the screen (15 seconds at most), and starts L-Connect again. It does nothing
when no Marquee is running, or when Marquee has the screen already.

To turn the handover off, set `lconnect: { handover: off }`. It changes nothing in L-Connect itself: once Marquee
quits, stopping and starting the service from L-Connect's tray icon gives the screen back to L-Connect.

By hand, with the handover off:

1. Right-click L-Connect's tray icon and choose its stop-service option. Ending the service in Task Manager doesn't
   stick: a watcher service starts it again. Fans keep their last settings meanwhile.
2. Start Marquee (`marquee run …`, or Resume in the tray) and wait for the picture.
3. Start the service again from the same tray menu. L-Connect skips the screen and goes on running your fans and RGB.

While Marquee has the screen, L-Connect doesn't control it or its LED ring, and neither shows up in the L-Connect app.
To give the screen back, pause or quit Marquee, then stop and start the service from the tray again.

## Settings

`marquee run` and the tray read their settings from `%LOCALAPPDATA%\Marquee\config.yaml`. The file is optional, and so
is every key in it. Here is each key with its values; the comments give the defaults.

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
  and exits; the tray shows `example-landscape` instead.
- `http` is the local server for [custom values](#custom-values): `http: off` turns it off.
- `providers` turns sources of values on and off: `claude` is the Claude Code session counts (`claude.sessions.*`),
  `claude_usage` the `/usage` poll for Claude Code's limits, `codex` the Codex limits and sessions, and `fps` the
  [game FPS](#game-fps).
- `custom.max_keys` is how many different `custom.*` keys a running Marquee holds.
- `ring` is the screen's LED ring: `theme` lets each theme set it, `off` keeps it dark, and a color shows that color
  for every theme. Quote the color: an unquoted `#` starts a comment.
- `lconnect.handover` is whether Marquee takes the screen from L-Connect: `off` doesn't on its own, `ask` offers to in
  a tray notification, and `auto` takes it without asking (see
  [Sharing the screen with L-Connect](#sharing-the-screen-with-l-connect)).
- `updates: check` has the tray look for a newer release once a day; `updates: off` turns that off. See
  [Updates](#updates).

The tray writes `theme`, `orientation` and `lconnect.handover` when you pick them from its menu, changing only that
setting and keeping the rest of the file, comments included.

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

- **Widgets:** `text` (templates like `"{gpu.load:0}% · {gpu.vram_used|bytes}"`), `value`, `bar`, `gauge`, `graph`, `image`,
  `video` (a clip in a box), `shape` (a `rect`, `ellipse`, `line` or `arc`, which `when` rules can recolor), `donut`
  (a pie or donut of up to 12 slices), `group`.
- **Styles:** named in `styles:`, layered with `extends:`, changed by `when:` rules (`above`, `below`, `equals`, `stale`), animated with `pulse:`.
  Letter effects: an outline (`text_stroke`), a color or gradient for the letters alone (`text_fill`), `italic`,
  `underline`, `strike` and `case` (`upper`, `lower` or `title`).
- **Background:** `fill` (color or gradient), `image`, `shader` and `video` layers, each with `opacity` and `blend`; up
  to 16 layers, at most 4 of them shaders and 2 of them videos.
- **Colors** are quoted CSS hex (`"#RRGGBB"` or `"#RRGGBBAA"`): an unquoted `#` starts a YAML comment.
- **Gradients** go in `fill`, `track`, a stroke's `color` and fill layers:
  `{ linear: ["#000000", "#36D1DC", "#FFFFFF"], stops: [0, 0.8, 1], angle: 90 }` or
  `{ radial: ["#FFFFFF60", "#00000000"], center: [0.3, 0.4], radius: 0.8 }`. `stops` place the colors, and a radial
  gradient's `center` and `radius` move and size it.
- **Fonts:** the bundled Inter by default, an installed font by name, or a `.ttf`/`.otf` file in the theme folder.
  Characters the font lacks, such as Japanese with Inter, are drawn with a Windows font that has them. Those are the
  right letters, but Arabic letters aren't joined yet, Arabic and Hebrew run left to right, and Indic marks aren't yet
  moved into place. Color emoji keep their own colors, and take no glow, shadow or outline.
- **Names:** `name` (up to 64 characters) on a widget or a background layer is what the theme editor's layers list
  calls it; the screen ignores it.
- **Times:** `resets_at` and `updated_at` keys are Unix seconds and `now` is the current time, so
  `"{claude.five_hour.resets_at - now|duration}"` reads "2h 14m". `"{now|time}"` is the local short time, and
  `"{now|time:ddd HH:mm}"` or a value's `format: "time:HH:mm"` takes a .NET date and time pattern. Times follow the
  PC's time zone and regional format (day and month names, the short time, a 24-hour clock), as the taskbar clock does.
  Letters in a pattern are pattern letters, so quote words (`"{now|time:'Today is' dddd}"`) and write a letter alone
  as `%H` (`"{now|time:%H}"`). The pattern runs to the closing brace, so spaces before the brace are kept.

A value with a smaller unit, such as a big "52" with a small "°C", is a value widget with `spans` instead of
`format`. Each span has its own template and style, and they share a baseline, so the unit stays beside the number as
the number's width changes. `{0}` is the widget's value after `smooth` (`{0:0.0}` formats it), and other placeholders
still read their keys. A missing value shows a single "—" in the first span's style. The widget's style places the
spans, and its `when` rules change every span:

```yaml
- type: value
  at: [140, 500]
  size: [300, 90]
  source: cpu.temp
  smooth: 400
  style: { size: 72, color: "#F4F6FA", align: center }
  spans:
    - { text: "{0:0}" }
    - { text: "°C", style: { size: 30, color: "#36D1DC" } }
  when:
    - { above: 85, style: { color: "#FF5A36" } }
```

A text widget takes `spans` instead of `text` in the same way, for words in several styles; its placeholders show
each reading as it comes, as a text widget's always do, since only a value widget's `{0}` follows `smooth`. A span's
style sets how its letters look. The box keys (`fill`, `stroke`, `radius`, `padding`, `align`, `wrap`, `line_height`,
`opacity`, `blend` and `pulse`) are the widget's: a span's own style can't set them, and a named style's are left out.

Every key, with its values and default, is in [docs/themes-reference.md](docs/themes-reference.md). VS Code completes
and checks theme files once the YAML extension (Red Hat) is installed: the examples' first line,
`# yaml-language-server: $schema=../../docs/layout.schema.json`, points it at [the schema](docs/layout.schema.json).
That path works only inside this repo: a copied theme needs its first line pointed at the schema, until the public
repo gives it a URL.

See `themes/example-landscape`, `themes/example-portrait`, `themes/example-agents` and `themes/example-showcase`, a
tour of the format's newer parts: a clock with the weekday, an outlined title in gradient letters, a video box, a big
number with a small unit, shapes, dials, segmented meters, charts, a donut, wrapped text, rotation, blending, names
and hidden widgets. A full theme authoring guide comes with the public release.

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

A `video` widget plays a clip the same way inside a box of its own, over the style's `fill` and under its stroke,
clipped to its `radius`. Its `shadow` is the box's, as an `image` widget's is, so a clip that leaves part of the box
empty still casts the whole box's shadow:

```yaml
- type: video
  at: [470, 130]
  size: [360, 210]
  src: video/tunnel.mp4
  fit: cover          # as for a layer (default cover)
  style: { radius: 8, stroke: { width: 2, color: "#FFFFFF40" } }
```

A theme can have at most 2 video layers, and at most 4 videos in all, counting its video layers and video widgets
together; a hidden one isn't loaded, but it counts. A video widget's box can cover at most as many pixels as the
canvas (1920×480). A clip's file can be up to 200 MB, and its frames up to 3840×2160 either way round (2160×3840
suits a portrait theme). Each clip is decoded on its own thread, a few frames ahead and scaled to the
size it is drawn at, and the layer draws the frame for the current time, so drawing never waits on decoding and the
clip plays at its own speed.

A clip that can't be played skips its layer, with a warning in the console and the preview that says why: the file is
missing or over a limit, Windows has no decoder for its format, it is damaged, or Media Foundation isn't there at all
(Windows N editions need the Media Feature Pack, from Settings > System > Optional features). When the theme loads,
Marquee also times a few frames of each clip and skips one that decodes slower than it plays; a smaller clip, or one
with fewer frames per second, fixes that. A clip that fails partway through stays on its last good frame, and the
reason is logged once. Saving over a clip reloads the theme, like editing `layout.yaml`.

### LED ring

The screen's ring of 60 LEDs is set by a theme's `ring:` block:

```yaml
ring:
  color: "#3A7BFF"
  effect: breathe            # static | breathe | spin | rainbow
  speed: 1.0                 # 0.1-10
  brightness: 80             # 0-100
  source: cpu.temp
  when:
    - { above: 85, color: "#FF3A2E", effect: static }
```

- **Effects:** `static` is the color on every LED; `breathe` fades it between a fifth of the brightness and full,
  every 3 seconds; `spin` sends a bright segment with a fading tail round the ring, once every 2 seconds; `rainbow`
  spreads the hues round the ring and turns them, once every 6 seconds, ignoring `color`. `speed: 2` is twice as fast.
- **`when` rules** work as a widget's do (`above`, `below`, `equals`, `stale`), comparing the ring's `source`, and
  change any of `color`, `effect`, `speed` and `brightness`. Every matching rule applies, in order, so a later rule wins.
- **The default:** a theme without a `ring:` block shows a soft static warm white (`#FFF1E0` at 40%). A field a `ring:`
  block leaves out is `static`, white (`#FFFFFF`), speed 1 and brightness 100 instead, so `ring: { color: "#3A7BFF" }`
  shows that blue at full brightness.

`ring:` in `config.yaml` overrides every theme's ring: `ring: off` keeps it dark, and `ring: "#3A7BFF"` shows that
color on every LED, static, at full brightness. An alpha byte dims it: `"#3A7BFF80"` is about half as bright. Saving
`config.yaml` changes the ring at once, without restarting anything else.

Marquee updates the ring up to 30 times a second on a thread of its own, so the ring never slows the screen. A ring
that fails (held by another program, say) is tried again after 2 seconds, one that isn't plugged in is looked for
every 5 seconds, and a screen without one is fine. Taking the screen from L-Connect takes the ring too. The ring goes
dark when Marquee pauses or quits, and so while an update installs. `--sink` and `--frames` leave it alone.

### Theme editor

`MarqueeEditor.exe` edits a theme on a canvas that shows the theme's real frames, drawn by the same renderer as the
screen. Open it with:

- **Edit theme…** in the tray's menu, or **Marquee Theme Editor** in the Start menu: the theme `config.yaml` names, the
  one on the screen;
- `marquee edit` for that same theme, or `marquee edit <theme or folder>` for another, found as `marquee run` finds one.

Select, move, resize and rotate widgets on the canvas; reorder, group, hide and lock them in the layers panel; set
their properties, styles and gradients in the properties panel; import images, fonts and clips; and pin test values
to see a theme at a full CPU or a missing sensor. "Show on screen" writes the theme to `config.yaml`.

`layout.yaml` stays the source of truth. Each change is written into it about a second later, changing only the text
that edit is about: your comments, order and formatting stay as they were, and the tray shows the change as it does a
hand edit. You can keep editing the file by hand while the editor is open; it reloads what you saved, as one step you
can undo. Undo goes back 500 steps, "Revert to opened version" brings back the file as it was when you opened it, and
unsaved changes survive a crash. The installed examples are read-only: the editor offers to copy one into your themes
folder first.

The editor runs as you, never as administrator: started elevated, or by `marquee edit` from an administrator
terminal, it refuses, so your theme files never end up owned by Administrators.

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

Run as administrator, `marquee validate` refuses a zip: it would unpack it into your temp folder, which your other
programs can change. Validate the zip from a normal terminal, or unzip it and validate the folder. Folders and theme
names are checked as usual.

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

`marquee run` and the tray keep the last limits across restarts in `%LOCALAPPDATA%\Marquee\claude-limits.json`, or,
running as administrator, in `%ProgramData%\Marquee\users\<your SID>\claude-limits.json` (see [Logs](#logs) for why).
The file holds only
each window's percentage and reset time and when they were read (no login or account details). It saves them whenever
they change and when it stops, and loads them at start, so the bars show at once, with `claude.updated_at` still saying
how old they are. A window that reset while Marquee was stopped stays stale (`—`), and a window without a reset time
isn't saved, nor is the per-model weekly limit (it shows after the first `/usage` poll). Fresh values from `/usage` or
the status line replace the loaded ones as they arrive. A missing or
unreadable file is skipped with one log line; deleting it is safe. A `--sink` run neither loads nor saves them.

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
`cpu.power`, `cpu.voltage`, `gpu.temp`, `gpu.load`, `gpu.clock`, `gpu.power`, `gpu.voltage`, `gpu.fan`,
`gpu.vram_used`, `ram.used_pct`, `ram.used` and `fan.<name>`. A key whose source stops updating goes stale after
5 seconds and shows `—`.

`cpu.voltage` and `gpu.voltage` are the CPU's and the GPU's core voltage, in volts. With LibreHardwareMonitor, the CPU
voltage needs the elevated tray (or an elevated terminal), as CPU temperatures do, and is missing unelevated; the GPU
voltage usually doesn't, as the card's driver reports it. With HWiNFO, HWiNFO reads both. `gpu.fan` is the GPU's
fan speed in RPM: the first fan, on a card with several. The card's fans are still listed as `fan.<name>` too. On a
PC with two GPUs, such as a CPU's integrated graphics beside a card, `gpu.voltage` and `gpu.fan` come from the one with
the most dedicated memory, the card.

`marquee sensors --raw` then lists every reading the hardware source reported, sorted, as the source gives them: the
hardware (`Cpu`, `Gpu`, `Memory`, or `Other` for the motherboard and everything else), the source's type for the
reading, its label, its value in the source's own unit (HWiNFO names it; LibreHardwareMonitor's `Data` is GB and
`SmallData` MB), and the device. Readings no key uses are listed too, and so are the zeros an unelevated
LibreHardwareMonitor reports for sensors it can't read. Keys are picked by label, so when one is missing on your PC,
`--raw` shows what the source calls that sensor. Like the rest of `marquee sensors`, it only reads.

The source is `--sensors`, or `sensors:` in `config.yaml`.

- **LibreHardwareMonitor** (`--sensors lhm`, the default) reads sensors directly. CPU temperatures and motherboard
  sensors need Marquee running as administrator (the tray from its logon task, or an elevated terminal) and the
  [PawnIO](https://pawnio.eu/) driver; without them those keys show `—`.
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
administrator: the tray does when its logon task starts it, and `marquee run` does from an elevated terminal.
Unelevated, FPS is off with a warning saying why (in the tray's menu, too). `providers: { fps: off }` in `config.yaml`
or `--no-fps` turns FPS off, and the warning with it.

## Updates

The tray looks for a newer Marquee once a day, the first time 5 minutes after it starts, and remembers when it last
looked (`last-update-check.txt` in its data folder), so restarting the PC doesn't make it look again. That check is the
only request Marquee makes on its own: one `GET https://api.github.com/repos/madjagstudios/marquee/releases/latest`,
with no account and no cookies. All it sends about you is what any request does (your IP address) and a `User-Agent`
header saying `Marquee/<version>`, which GitHub requires. If the check fails, nothing else is affected, and it tries
again the next day.

`updates: off` in `config.yaml` turns the daily check off. "Check for updates" in the tray's menu still checks when you
click it, whatever `updates:` says, and says what it found.

A newer version shows as a balloon and as "Install Marquee <version>…" in the menu. Nothing is downloaded until you
click one. Then the tray downloads the installer and its signature from the GitHub release into
`%ProgramData%\Marquee\updates`, a folder only administrators can change, with its progress in the status line. It
checks the signature against the public key built into Marquee, and only then runs the installer. A missing or wrong
signature means nothing is installed, and the log says why. How releases are signed is in
[docs/releasing.md](docs/releasing.md).

While the installer runs, the tray pauses: the screen and the ring go dark. The installer closes the tray, replaces
Marquee's files and starts the new tray. If the update doesn't install (you cancel it, another installation is under
way, or it fails and rolls back), the screen comes back and a balloon gives the installer's exit code and its log,
`%ProgramData%\Marquee\updates\Marquee-<version>-x64.msi.log`. The offer stays in the menu, to try again. The next
download replaces that log, and uninstalling removes it.

A tray without administrator rights can't install an update: its offer opens the release's page instead, to download
and run the installer from there. A development build, one with the placeholder in place of the release key (see
[docs/releasing.md](docs/releasing.md)), never checks or installs anything: "Check for updates" points to the
releases page.

## Logs

`marquee run` writes what it prints to `%LOCALAPPDATA%\Marquee\logs\marquee-YYYYMMDD.log` as well, and so does the
tray (with no console of its own), one file per day, so a problem can still be read after the console
is closed. The files of the last 7 days, today's included, are kept; older ones are deleted. A day's file stops at
10 MB, with a last line saying so, and logging carries on in the next day's file. The file can be opened while
Marquee runs. "Open logs" in the tray's menu opens the folder.

Running as administrator (the tray from its logon task, or `marquee run` from an elevated terminal), Marquee keeps its
log and its data in `%ProgramData%\Marquee\users\<your SID>\` instead (`logs\marquee-YYYYMMDD.log`,
`claude-limits.json`, and the tray's `last-update-check.txt` and first-run markers), a folder only administrators can
change and you can read. `%LOCALAPPDATA%` belongs to your unelevated programs, and one of them could put a link there
that sends an elevated write anywhere on the PC. `config.yaml` and your themes stay in `%LOCALAPPDATA%\Marquee`, where
you edit them.

The installer makes `%ProgramData%\Marquee` (through `marquee task install`), so no other program can make it first:
any user may make folders in `%ProgramData%`. If `%ProgramData%\Marquee` or a folder in it already exists and someone
other than administrators can change it, Marquee leaves it alone and won't use it. The installer only warns in its log;
a Marquee running as administrator says so and stops (the tray in a message box). Delete that folder (as an
administrator) and Marquee makes it again, safely.

## Verified hardware

| Device | Firmware | Notes |
|---|---|---|
| 8.8" Universal Screen (`1CBE:A088`) | `lianli88_0001_0023` | `portrait` is upright when mounted vertically; brightness works; the image persists after Marquee exits; about 21 ms per frame (~47 fps) including JPEG encoding |
| 8.8" Universal Screen (`1CBE:A088`) with `marquee run` | `lianli88_0001_0023` | example themes at 30 fps for 12 min (render ~2.5 ms, send ~18 ms); `--fps 60` tops out at ~51 fps; hot reload in ~2 s with the last good layout kept on errors; letterboxing on an orientation mismatch; reconnects after unplugging |
| 8.8" Universal Screen (`1CBE:A088`) with `example-agents` | `lianli88_0001_0023` | shader theme at 30 fps (render ~11 ms with the aurora at `scale: 0.25`, send ~26 ms, overlapped); `--fps 60` topped out at ~43 fps with the earlier `scale: 0.5`; Claude Code limits through the status line from terminal sessions (the desktop app's Code tab doesn't run status lines), with `marquee run` elevated or not; Codex limits and sessions; `marquee push` |
| 8.8" Universal Screen (`1CBE:A088`) with config.yaml, video and game FPS | `lianli88_0001_0023` | `marquee run` with no folder, the theme named in config.yaml; fps changed live in config.yaml without the screen going dark, and a bad value refused with its line; an MP4 screen recording as a background layer at ~30 fps (render ~7 ms, send ~28 ms); game FPS, frame time, 1% low and app name from PresentMon with Marquee elevated, `—` on the desktop, and no PresentMon process or ETW session left after Ctrl+C |
| 8.8" Universal Screen (`1CBE:A088`) with `example-showcase` (theme format v2) | `lianli88_0001_0023` | from the tray and the MSI: a needle dial with ticks, a segmented ring and bars, a two-series chart, a memory donut, and rotated, blended and wrapped text all read well; `marquee validate` passes the showcase and a copy, and refuses `rotate: 400` with its line; fps not measured |
| 8.8" Universal Screen (`1CBE:A088`) with `example-showcase` (theme format v2.1), Ryzen 7 9800X3D and RX 7800 XT | `lianli88_0001_0023` | from the tray and the MSI: the clock and weekday, the outlined gradient title, the radar video box and the eased span readout read well; the clock followed a change of regional format and of time zone without a restart; `cpu.voltage` (the board's Vcore), `gpu.voltage` (the card, not the integrated GPU) and `gpu.fan` from LibreHardwareMonitor, checked with `marquee sensors --raw`; fps not measured |
| 8.8" Universal Screen (`1CBE:A088`) with the theme editor (E1) | `lianli88_0001_0023` | from the MSI and the tray: a copy of the showcase arranged, resized, rotated, grouped and restyled (colours, a font-size slide as one undo step, letter spacing, a gradient title), an image added, with every save on the screen about a second later; test values from the tray, pinned, and from the editor's own readings with the tray stopped; a hand edit reloaded as one undo step, a broken file pausing editing until fixed, crash recovery after ending the editor in Task Manager, revert to the opened version, one editor per theme, and `marquee edit` |
| 8.8" Universal Screen (`1CBE:A088`) with the theme editor (E2a) | `lianli88_0001_0023` | from the MSI: a background fill changed to a solid colour and a gradient; image and video layers added, reordered, deleted and restored with undo; a layer's kind switched; the aurora shader added with `heat` bound to a sensor expression, its scale slid with the cost hint following; portrait with the widgets rearranged into a column, and undone |
| 8.8" Universal Screen (`1CBE:A088`) and its LED ring (`0416:8050`) with the theme editor (E2b) | `lianli88_0001_0023` | from the MSI: a widget's style saved as a named style, renamed, applied to a second widget, edited for both, and deleted with no change on screen; a when rule (above 80 → red) switching on the canvas and the screen with pinned test values; a shader uniform bound to CPU load with the picker and no typing; the LED ring set to breathe in blue with a rule to red, the preview strip and the real ring following a pinned source |
| 8.8" Universal Screen (`1CBE:A088`) with the redesigned theme editor | `lianli88_0001_0023` | the owner's walkthroughs of the MadJag look and the layered panel in plain words: readings by plain name, inserting the time and date into text, groups that grow with their text, the start screen, a moving background picked from the gallery with its colour, speed and reaction controls, and modules added from the gallery with live previews |
| 8.8" Universal Screen (`1CBE:A088`) and its LED ring (`0416:8050`) with the tray and the MSI | `lianli88_0001_0023` | installed per-machine with no options; the tray started elevated at logon with no UAC prompt; the one-time offer, and later `lconnect.handover: auto` at logon, took the screen from L-Connect's service about 10 s after the tray started, with L-Connect's service and watcher running again afterwards; the LED ring lit from the theme and `config.yaml`, dark on pause and quit; themes and orientation switched from the menu; sleep and wake, unplug and replug; a 0.1.0 → 0.1.1 upgrade restarted the tray in 2 s; uninstall; 22 fps rather than 30 with the tray (send ~28–29 ms), still being looked into |

## Credits

The panel and LED ring protocols were documented by the community, notably
[sgtaziz/lian-li-linux](https://github.com/sgtaziz/lian-li-linux) (MIT). Marquee is an independent implementation.
Third-party components and their licenses, including everything the installer redistributes, are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Marquee is not affiliated with or endorsed by Lian Li Industrial Co., Ltd.
