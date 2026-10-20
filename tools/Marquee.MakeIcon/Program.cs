using Marquee.MakeIcon;

// `dotnet run --project tools/Marquee.MakeIcon -- <icon.ico> [<png-folder>]`: draws the mark at every size Windows asks
// for (the small ones for the notification area at 100% to 200% scaling, the large ones for Explorer's views) and packs
// them into the .ico. With a folder, each size is also written there as a PNG to look at.
int[] sizes = [16, 20, 24, 32, 48, 64, 128, 256];

if (args.Length is not (1 or 2))
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/Marquee.MakeIcon -- <icon.ico> [<png-folder>]");
    return 2;
}

var images = sizes.Select(size => (size, IconMark.Png(size))).ToList();
File.WriteAllBytes(args[0], IcoFile.Pack(images));
if (args.Length == 2)
{
    Directory.CreateDirectory(args[1]);
    foreach (var (size, png) in images) File.WriteAllBytes(Path.Combine(args[1], $"marquee-{size}.png"), png);
}

Console.WriteLine($"Wrote {Path.GetFullPath(args[0])}: {string.Join(", ", sizes)} px.");
return 0;
