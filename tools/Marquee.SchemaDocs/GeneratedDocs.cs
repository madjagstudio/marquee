namespace Marquee.SchemaDocs;

/// The files the catalog generates, where they go, and how to regenerate them.
static class GeneratedDocs
{
    public const string Command = "dotnet run --project tools/Marquee.SchemaDocs";
    public const string SolutionFile = "Marquee.sln";
    public const string ReferencePath = "docs/themes-reference.md";
    public const string SchemaPath = "docs/layout.schema.json";

    /// Each file's path from the repository's root, with forward slashes, and its content with \n line ends.
    public static IReadOnlyList<(string Path, string Content)> Files() =>
        [(ReferencePath, ReferenceWriter.Write()), (SchemaPath, JsonSchemaWriter.Write())];

    /// Git may check a file out with \r\n line ends, which doesn't make it stale.
    public static bool SameText(string a, string b) => a.ReplaceLineEndings("\n") == b.ReplaceLineEndings("\n");

    public static string? FindRepository(string from)
    {
        for (var folder = new DirectoryInfo(from); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, SolutionFile)))
                return folder.FullName;
        return null;
    }
}
