using System.Globalization;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

public sealed record BackgroundEntry(string File, ShaderInfo Info);

public sealed class BackgroundLibrary(string folder)
{
    public IReadOnlyList<BackgroundEntry> All { get; } = Directory.Exists(folder)
        ? Directory.GetFiles(folder, "*.sksl").Order(StringComparer.Ordinal).Select(file =>
            new BackgroundEntry(file, ShaderControls.Read(System.IO.File.ReadAllText(file))))
            .Where(entry => entry.Info.Name is not null && entry.Info.Controls.Count > 0).ToList() : [];

    public static void Use(EditorSession session, string file, YamlPath? replaceLayer)
    {
        var info = ShaderControls.Read(File.ReadAllText(file));
        var import = new AssetImport(session.Folder);
        var copied = import.Import(file, AssetKind.Shader);
        try { session.Apply(new UseBackground(copied.Path, info, replaceLayer, AddLayer.AddIndex(session))); }
        catch
        {
            if (copied.Copied) import.Discard(copied);
            throw;
        }
    }

    public static string Defaults(ShaderInfo info)
    {
        var entries = info.Controls.Select(control => $"{control.Uniform}: {Binding(control)}").ToList();
        if (info.Reacts is { } reacts) entries.Add($"{reacts.Uniform}: \"cpu.load / 100\"");
        return "{ " + string.Join(", ", entries) + " }";
    }

    static string Binding(ShaderControl control)
    {
        if (control.Kind == ControlKind.Number) return control.Default;
        var color = SKColor.Parse(control.Default);
        return "[" + string.Join(", ", new[] { color.Red, color.Green, color.Blue }
            .Select(component => (component / 255d).ToString("R", CultureInfo.InvariantCulture))) + "]";
    }

    sealed record UseBackground(string File, ShaderInfo Info, YamlPath? Replace, int Index) : EditorCommand
    {
        public override string Label => Info.Name ?? Path.GetFileName(File);
        public override CommandResult Run(CommandContext context)
        {
            var result = Replace is { } path
                ? new ChangeLayerKind(path, LayerKindChoice.Shader, File).Run(context)
                : new AddLayer(LayerKindChoice.Shader, File, Index).Run(context);
            var layer = Replace ?? YamlPath.Root.Child("background").Item(Index);
            var document = result.Document.AddKey(layer, "uniforms", Defaults(Info)).AddKey(layer, "scale", "0.5");
            return new(document, [layer]);
        }
    }
}
