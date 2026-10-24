# Sensor key reference

Use these keys in a theme's expressions and value bindings. Keys are case-sensitive.
Availability depends on the selected providers and what this PC reports. Missing or
stale values display as `—`; a sample shown in the editor does not mean a live reading
is available. Percentages below are on a 0–100 scale. Timestamps are Unix seconds.
`*` in a table denotes a family of keys, not a literal key to bind.

## Hardware: CPU, GPU, RAM, fans and voltages

Choose LibreHardwareMonitor (`sensors: lhm`, the default) or HWiNFO (`sensors: hwinfo`).
HWiNFO must be running with Shared Memory Support enabled. Hardware values become
stale after five seconds without an update. LibreHardwareMonitor CPU temperatures
and motherboard sensors need the tray elevated and the PawnIO driver. If a sensor
is unavailable on your board, try HWiNFO; some AMD GPUs expose only load and memory
through LibreHardwareMonitor.

| Key | What it is | Unit | Source | Notes |
| --- | --- | --- | --- | --- |
| `cpu.temp` | CPU package or control temperature | °C | LibreHardwareMonitor or HWiNFO | Prefers CPU Package, then Tctl/Tdie or Tctl, then Core Max or Core Average. LibreHardwareMonitor needs elevation and PawnIO. |
| `cpu.load` | Total CPU usage | % | LibreHardwareMonitor or HWiNFO | CPU Total or Total CPU Usage. |
| `cpu.clock` | Fastest reported CPU core clock | MHz | LibreHardwareMonitor or HWiNFO | Excludes bus, ring, uncore, memory, LLC and cache clocks. |
| `cpu.power` | CPU package power | W | LibreHardwareMonitor or HWiNFO | Requires a matching package-power sensor. |
| `cpu.voltage` | CPU core voltage | V | LibreHardwareMonitor or HWiNFO | Prefers the CPU's core reading, then motherboard Vcore, then average requested voltage (VID). LibreHardwareMonitor needs elevation. |
| `gpu.temp` | GPU core temperature | °C | LibreHardwareMonitor or HWiNFO | GPU Core or GPU Temperature. |
| `gpu.load` | GPU usage | % | LibreHardwareMonitor or HWiNFO | Prefers GPU Core, GPU Core Load, GPU Utilization, then D3D 3D. |
| `gpu.clock` | GPU core clock | MHz | LibreHardwareMonitor or HWiNFO | GPU Core or GPU Clock. |
| `gpu.power` | GPU package power | W | LibreHardwareMonitor or HWiNFO | GPU Package or GPU Power. |
| `gpu.voltage` | GPU core voltage | V | LibreHardwareMonitor or HWiNFO | Uses the preferred GPU with a voltage reading; usually available through the GPU driver without elevation. |
| `gpu.fan` | GPU fan speed | RPM | LibreHardwareMonitor or HWiNFO | First fan on the preferred GPU with fan readings; also available under its `fan.*` name. |
| `gpu.vram_used` | Used dedicated GPU memory | bytes | LibreHardwareMonitor or HWiNFO | Converted from the source's memory unit. |
| `ram.used_pct` | Physical memory usage | % | LibreHardwareMonitor or HWiNFO | Excludes virtual memory. |
| `ram.used` | Used physical memory | bytes | LibreHardwareMonitor or HWiNFO | Converted from the source's memory unit. |
| `fan.*` | Each reported fan's speed | RPM | LibreHardwareMonitor or HWiNFO | Label becomes a lowercase slug: CPU Fan → `fan.cpu_fan`, Fan #1 → `fan.fan_1`. The first reading wins if labels produce the same key. |

For `gpu.voltage` and `gpu.fan`, the preferred GPU is the candidate reporting the
most dedicated memory (total or used, depending on the source); ties retain source
order. Other `gpu.*` keys choose the best matching label, then the first matching
reading. LibreHardwareMonitor orders GPUs by dedicated memory size, but these keys
are not guaranteed to come from one GPU on every multi-GPU PC.

## FPS

PresentMon measures the foreground app. It needs the tray elevated and
`providers.fps` enabled (the default). Marquee itself and the desktop compositor
are excluded. Values expire two seconds after the selected process's last frame;
the desktop or an app that is not presenting frames has no fresh FPS reading.

| Key | What it is | Unit | Source | Notes |
| --- | --- | --- | --- | --- |
| `fps` | Frame rate over the last second | frames/s | PresentMon | Foreground app, not the theme's rendering rate. |
| `frametime` | Mean time between frames over the last second | ms | PresentMon | Lower is faster. |
| `fps.1pct_low` | 1% low frame rate over the last ten seconds, or the last 2,000 frames when that is fewer | frames/s | PresentMon | Converts the mean of the slowest 1% of frame times (at least one frame) to FPS. |
| `fps.app` | Foreground presenting process name | text | PresentMon, with Windows process lookup | Name without `.exe`. |

## Claude Code

The status-line bridge receives updates from terminal sessions. The desktop app's
Code sessions do not run status-line commands, so the usage provider also polls
`claude -p /usage`, normally every five minutes. It backs off when no limits are
returned. Whichever source writes a shared key last wins. Limit windows expire at
their reset time when one is supplied; without a reset time, values remain until
replaced. Limits may be absent for API-key logins or before the first response.

| Key | What it is | Unit | Source | Notes |
| --- | --- | --- | --- | --- |
| `claude.five_hour.pct` | Five-hour allowance used | % | Status line or `claude -p /usage` | Expires at the window's known reset time. |
| `claude.five_hour.resets_at` | Five-hour window reset time | Unix seconds | Status line or `claude -p /usage` | Only written when supplied. |
| `claude.seven_day.pct` | Weekly allowance used | % | Status line or `claude -p /usage` | Expires at the window's known reset time. |
| `claude.seven_day.resets_at` | Weekly window reset time | Unix seconds | Status line or `claude -p /usage` | Only written when supplied. |
| `claude.seven_day_model.pct` | Model-specific weekly allowance used | % | `claude -p /usage` | Only when a scoped weekly limit is returned. |
| `claude.seven_day_model.resets_at` | Model-specific weekly reset time | Unix seconds | `claude -p /usage` | Only written when supplied. |
| `claude.seven_day_model.name` | Name of the model with the scoped weekly limit | text | `claude -p /usage` | Only when named; expires with its percentage. |
| `claude.context.pct` | Session context window used | % | Status line | Latest status-line writer wins; no automatic expiry. |
| `claude.updated_at` | Time of the latest applied status or usage reading | Unix seconds | Status line or `claude -p /usage` | No automatic expiry; restored limits retain their original timestamp. |
| `claude.sessions.total` | Running local Claude Code sessions | count | Claude Code session files and Windows process checks | Excludes exited processes, reused PIDs when detectable, and other hosts. |
| `claude.sessions.busy` | Running sessions marked busy | count | Claude Code session files and Windows process checks | Unknown statuses count only toward total. |
| `claude.sessions.idle` | Running sessions marked idle | count | Claude Code session files and Windows process checks | Session counts refresh every two seconds and expire after ten seconds. |

Session files are read from `~/.claude/sessions`, or the `sessions` folder under
`CLAUDE_CONFIG_DIR` when set.

## Codex

Marquee reads Codex's rollout logs under `~/.codex/sessions` (or `CODEX_HOME/sessions`).
Limits come from the newest applicable `token_count` event by its timestamp, across
recent logs, and refresh while Codex works. Only the main account limit is used;
model-specific limits are skipped. Both providers poll every five seconds.

| Key | What it is | Unit | Source | Notes |
| --- | --- | --- | --- | --- |
| `codex.primary.pct` | Primary allowance used | % | Codex's logs | The primary window depends on the plan; do not assume five hours. |
| `codex.primary.resets_at` | Primary window reset time | Unix seconds | Codex's logs | Window keys expire at this time when supplied. |
| `codex.primary.window_min` | Primary window length | minutes | Codex's logs | Only written when supplied. |
| `codex.secondary.pct` | Secondary allowance used | % | Codex's logs | Only when a secondary window is present. |
| `codex.secondary.resets_at` | Secondary window reset time | Unix seconds | Codex's logs | Window keys expire at this time when supplied. |
| `codex.secondary.window_min` | Secondary window length | minutes | Codex's logs | Only written when supplied. |
| `codex.updated_at` | Timestamp of the selected limit event | Unix seconds | Codex's logs | Event time, not poll time; no automatic expiry. |
| `codex.sessions.total` | Sessions with activity in the last ten minutes | count | Codex's logs | Excludes subagent and review logs that name a parent thread. |
| `codex.sessions.busy` | Recently active sessions with an unfinished turn | count | Codex's logs | Last turn event is `task_started`; also considered busy if no turn event is found in the read tail. |
| `codex.sessions.idle` | Recently active sessions whose turn ended | count | Codex's logs | Last turn event is `task_complete` or `turn_aborted`. |

Session counts describe recent log activity, not a process count. They expire after
15 seconds without a provider update. A limit window without a reset time remains
until replaced. Agent log formats can change; missing readings are not zero usage.

## Custom

| Key | What it is | Unit | Source | Notes |
| --- | --- | --- | --- | --- |
| `custom.*` | A value supplied by your script | Your choice, or text | `marquee push` or `POST /values` | Numeric or text value; optional TTL controls freshness. |

### Push values from a script

With Marquee running and its local HTTP server enabled, send one value with:

```powershell
marquee push queue 12 --ttl 30s
marquee push custom.build ok --ttl 5m
```

The CLI adds `custom.` when omitted. Finite numeric input is sent as a number;
other input is text. It uses `http.port` from the config, defaulting to `47913`.
Use `--port N` to select a port explicitly, or `--config <file>` for another config.
`http: off` disables the server.

To set several keys, send `POST http://127.0.0.1:47913/values` with
`Content-Type: application/json` and a body such as:

```json
{"custom.build":"ok","custom.queue":12,"ttl":"30s"}
```

Only local requests are accepted; browser requests carrying an `Origin` header are
refused. The full `custom.` prefix is required in JSON. Each dot-separated segment
must contain one or more ASCII letters, digits or underscores. Keys are at most
64 characters including the prefix. Values must be finite numbers or text up to
256 characters; booleans, null, arrays and objects are not accepted. A request may
contain up to 100 values, and its body must fit within 64 KiB. The whole request is
validated before any values are written.

`ttl` applies to every value in that request. It accepts positive seconds (a JSON
number) or a duration such as `30s`, `5m` or `2h`, up to 365 days. Each new push sets
the value and its expiry again. After expiry the key is stale, so a theme shows
`—` or can hide it with a stale condition. Omitting the TTL makes the value stay
fresh until replaced or Marquee restarts; pushed values are not persisted.

`custom.max_keys` limits the number of distinct custom keys held at once. The
default is 1,000; the allowed range is 1–10,000. For example, in the config:

```yaml
custom:
  max_keys: 1000
```

A push that would exceed the limit is refused. Expired keys free their places
after a ten-minute stale grace period, when the server next prunes them during a
push. Keys with no TTL keep their places until restart. Reuse a fixed set of names
or supply a TTL when creating temporary keys.

### List raw readings on a PC

```powershell
marquee sensors --raw
```

This lists the mapped keys and then every raw reading reported by the selected
hardware source, including readings that have no mapped key. The raw list includes
hardware kind, source reading type, label, value in the source's own unit, and
device. Raw memory units can differ from the bytes used by the mapped keys.
Use `--sensors hwinfo` or `--sensors lhm` to select a source, and
`--no-claude-usage` to skip the `claude -p /usage` poll.

The one-off sensor listing does not include live FPS, status-line context usage,
or custom pushes. With `sensors: none`, there are no raw hardware readings to list.

The key mappings above follow `HardwareKeyMap`, `FpsProvider`, the agent providers
and the custom-values parser. `KeysDocTests` checks every table key against the
editor's `Samples.Knows` and requires every concrete `Samples.Keys` entry here.
