using Marquee.SchemaDocs;

// `dotnet run --project tools/Marquee.SchemaDocs [-- <repository folder>]`: writes docs/themes-reference.md and
// docs/layout.schema.json from ThemeSchema. Without a folder it finds the repository from the current folder up. Run it
// after changing the catalog; ThemeSchemaDocsTests fails until the committed files match.
if (args.Length > 1)
{
    Console.Error.WriteLine($"Usage: {GeneratedDocs.Command} [-- <repository folder>]");
    return 2;
}

var root = args.Length == 1 ? Path.GetFullPath(args[0]) : GeneratedDocs.FindRepository(Directory.GetCurrentDirectory());
if (root is null || !File.Exists(Path.Combine(root, GeneratedDocs.SolutionFile)))
{
    Console.Error.WriteLine($"No {GeneratedDocs.SolutionFile} found{(root is null ? " here or above" : $" in {root}")}. Run it from the repository, or pass the repository's folder.");
    return 2;
}

foreach (var (path, content) in GeneratedDocs.Files())
{
    var full = Path.Combine(root, path);
    var unchanged = File.Exists(full) && GeneratedDocs.SameText(File.ReadAllText(full), content);
    if (!unchanged) File.WriteAllText(full, content);
    Console.WriteLine($"{(unchanged ? "Unchanged" : "Wrote")} {path}");
}

return 0;
