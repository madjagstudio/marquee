using Marquee.Core;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Editing;
using SkiaSharp;

namespace Marquee.Editor.Model;

/// Self-contained ordinary widget snippets. The root is supplied by the application (or a temporary test folder).
public sealed class UserModules(string root)
{
    readonly string _root = Path.GetFullPath(root);
    public string? NameProblem(string name)
    {
        if (!ValidName(name)) return ModuleText.Get("ModuleInvalidName");
        var folder = Path.Combine(_root, name.Trim());
        return Directory.Exists(folder) || File.Exists(folder) ? ModuleText.Get("ModuleNameTaken") : null;
    }
    static bool ValidName(string name)
    {
        name = name.Trim();
        var stem = name.Split('.')[0].TrimEnd();
        return name.Length is > 0 and <= 64 && !name.StartsWith('.') && !name.EndsWith('.')
            && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
            && !new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }
    public string Folder(string name)
    {
        if (!ValidName(name)) throw new ArgumentException(ModuleText.Get("ModuleInvalidName"), nameof(name));
        var path = Path.GetFullPath(Path.Combine(_root, name.Trim()));
        Safe(path); return path;
    }
    void Safe(string path)
    {
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || ReparseTags.Windows.FirstLink(full) is not null)
            throw new IOException(ModuleText.Get("ModuleUnsafePath"));
    }
    public IReadOnlyList<Module> All()
    {
        if (!Directory.Exists(_root)) return [];
        return Directory.EnumerateDirectories(_root).Select(Path.GetFileName).Where(name => name is not null && ValidName(name))
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Where(name => File.Exists(Path.Combine(Folder(name!), "module.yaml"))).Select(name => Read(name!)).ToList();
    }
    Module Read(string name)
    {
        var path = Path.Combine(Folder(name), "module.yaml"); Safe(path);
        return new(name, name, ModuleCategory.MyModules, File.ReadAllText(path), false);
    }
    public void Save(EditorSession session, YamlPath widget, string name)
    {
        if (NameProblem(name) is { } problem) throw new ArgumentException(problem, nameof(name));
        var target = Folder(name);
        var staging = Path.Combine(_root, "." + Guid.NewGuid().ToString("N") + ".tmp"); Safe(staging);
        Directory.CreateDirectory(staging);
        try
        {
            var flat = Flatten(session.Document, widget) as YamlMap ?? throw new ArgumentException(ModuleText.Get("ModuleWidgetRequired"));
            flat = flat.With("style", StyleValues.Drawn(session.Document, widget)).With("at", new YamlList([YamlValue.Number(0), YamlValue.Number(0)]));
            var document = LayoutDocument.Open(YamlWriter.Block(flat));
            var files = Path.Combine(staging, "files"); Directory.CreateDirectory(files);
            document = Carry(document, session.Folder, new AssetImport(files), "files/");
            File.WriteAllBytes(Path.Combine(staging, "module.yaml"), document.ToBytes());
            Directory.Move(staging, target);
        }
        finally { if (Directory.Exists(staging)) { Safe(staging); Directory.Delete(staging, true); } }
    }
    static YamlValue Flatten(LayoutDocument document, YamlPath at)
    {
        var node = document.GetNode(at)!;
        if (node.Kind == LayoutNodeKind.Mapping)
            return new YamlMap(node.Keys.Select(key => new KeyValuePair<string, YamlValue>(key,
                key == "style" ? StyleValues.Effective(document, at.Child(key)) : Flatten(document, at.Child(key)))).ToList());
        if (node.Kind == LayoutNodeKind.List)
            return new YamlList(Enumerable.Range(0, node.Count).Select(index => Flatten(document, at.Item(index))).ToList());
        return document.ValueAt(at)!;
    }
    static IEnumerable<YamlPath> Leaves(LayoutDocument document, YamlPath at)
    {
        var node = document.GetNode(at)!;
        if (node.Kind == LayoutNodeKind.Mapping)
            return node.Keys.SelectMany(key => Leaves(document, at.Child(key)));
        if (node.Kind == LayoutNodeKind.List)
            return Enumerable.Range(0, node.Count).SelectMany(index => Leaves(document, at.Item(index)));
        return [at];
    }
    static LayoutDocument Carry(LayoutDocument document, string from, AssetImport import, string prefix, List<ImportedFile>? copied = null)
    {
        var assets = new Dictionary<string, ImportedFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Leaves(document, YamlPath.Root).ToList())
        {
            if (AssetImport.KindFor(document, path) is not { } kind || !document.TryGetScalar(path, out var relative) || relative.Length == 0) continue;
            // Family names use the destination PC's font; explicit font files travel with the module.
            if (kind == AssetKind.Font && !Path.HasExtension(relative) && !relative.Contains('/') && !relative.Contains('\\')) continue;
            if (!ThemeAssets.TryInsideFolder(from, relative, out var source)) throw new IOException(ModuleText.Get("ModuleUnsafePath"));
            if (!assets.TryGetValue(source!, out var file))
            {
                file = import.Import(source!, kind); assets.Add(source!, file); if (file.Copied) copied?.Add(file);
            }
            document = document.SetScalar(path, prefix + file.Path);
        }
        return document;
    }
    public void Add(EditorSession session, string name, SKPoint? at = null, YamlPath? group = null)
    {
        var module = Read(name);
        var import = new AssetImport(session.Folder);
        var copies = new List<ImportedFile>();
        try
        {
            var document = Carry(LayoutDocument.Open(module.Yaml), Folder(name), import, "", copies);
            session.Apply(new AddModule(module with { Yaml = document.Text }, at, group));
        }
        catch { foreach (var file in copies) import.Discard(file); throw; }
    }
    public void Rename(string name, string newName)
    {
        var from = Folder(name);
        if (name == newName.Trim()) return;
        if (NameProblem(newName) is { } problem) throw new ArgumentException(problem, nameof(newName));
        Directory.Move(from, Folder(newName));
    }
    public void Delete(string name)
    {
        var folder = Folder(name);
        CheckTree(folder);
        Directory.Delete(folder, true);
    }
    void CheckTree(string folder)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(folder))
        {
            Safe(path);
            if (Directory.Exists(path)) CheckTree(path);
        }
    }
}
