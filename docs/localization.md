# Localization

Marquee's own text is in English today. The text that people read in the command line, the tray and the preview
window lives in `.resx` resource files, so it can be translated without touching the code.

## Where the strings live

| Project | Resources | Read through |
|---|---|---|
| `marquee.exe` (the command line) | `src/Marquee.Cli/Resources/Strings.resx` | `src/Marquee.Cli/Resources/Strings.cs` |
| `MarqueeTray.exe` (the tray icon: its menu, balloons and log lines) | `src/Marquee.App/Resources/Strings.resx` | `src/Marquee.App/Resources/Strings.cs` |
| The preview window | `src/Marquee.Preview/Resources/Strings.resx` | `src/Marquee.Preview/Resources/Strings.cs` |
| The engine's log messages (`marquee run` and the tray) | `src/Marquee.Host/Resources/Strings.resx` | `src/Marquee.Host/Resources/Strings.cs` |

Each entry has a name (`NoSupportedScreens`) and an English value. Values with parts that change, such as a file name
or a frame count, use numbered placeholders: `Sent {0} to {1}.` The `<comment>` beside an entry says what each
placeholder holds. Some placeholders carry a number format, such as `{1:F1}`; keep the format as it is.

Messages from the libraries (`Marquee.Core` and the sensor providers), such as theme errors, are still English only.

## Adding a language

1. Copy `Strings.resx` to `Strings.<culture>.resx` in the same folder, for example `Strings.de.resx` for German or
   `Strings.pt-BR.resx` for Brazilian Portuguese. Do this for each project in the table.
2. Translate each `<value>`. Leave the `name` attributes alone.
3. Keep every placeholder (`{0}`, `{1:F1}` and so on). You may move them within the sentence, but not drop or add any.
4. Leave command names, option names (`--sink`, `--fps`), file names and product names (Claude Code, Codex) as they
   are.
5. An entry you leave out falls back to English, so a partial translation still works.
6. Run `dotnet build -c Release` and `dotnet test -c Release`. The build puts the translation in a folder named for
   the culture beside `marquee.exe` (`de\marquee.resources.dll`, `de\MarqueeTray.resources.dll`,
   `de\Marquee.Preview.resources.dll` and `de\Marquee.Host.resources.dll`), and Marquee picks it when that is Windows'
   display language.

## Adding a message

Add the entry to the English `Strings.resx` and a member of the same name to `Strings.cs`: a property for plain
text, or a method with one parameter per placeholder. The tests fail if the two lists differ, if a placeholder has
no matching parameter, or if code in `src/Marquee.Cli` writes a message to the console as a string literal.

## What stays culture-invariant

Translations change words, not data. Theme files (`layout.yaml`), the number formats inside them (`"{0:0.0}°C"`),
sensor values and every number Marquee prints keep the invariant culture: a decimal point, never a comma, on every
PC, so a theme looks the same wherever it runs.
