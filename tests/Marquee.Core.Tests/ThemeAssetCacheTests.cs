using System.Runtime.CompilerServices;
using System.Text;
using Marquee.Core.Rendering;
using Marquee.Core.Themes;
using Marquee.Core.Themes.Parsing;
using Marquee.Core.Themes.Validation;
using Marquee.Core.Tests.TestSupport;
using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// The theme editor loads unsaved text against the theme folder on every change, through one asset cache: a file that
/// hasn't changed on disk is reused rather than decoded, compiled, measured or opened again, and what no load uses any
/// more is disposed. VideoCacheThreadTests counts the players' threads.
public class ThemeAssetCacheTests
{
    const string Canvas = "canvas: { orientation: landscape }\n";
    const string Cheap = "half4 main(float2 p) { return half4(0, 0, 1, 1); }";
    const string Red = "half4 main(float2 p) { return half4(1, 0, 0, 1); }";

    static ThemeAssetCache FreeCache() => new(TestProbes.Free, TestProbes.Free);

    static string ImageLayers(params string[] paths) =>
        Canvas + "background:\n" + string.Concat(paths.Select(path => $"  - image: {path}\n"));

    /// Gives a file a modification time of its own, so a rewrite within the file system's clock resolution still counts.
    static void Touch(TempTheme folder, string path, int minutes) =>
        File.SetLastWriteTimeUtc(Path.Combine(folder.Folder, path), new DateTime(2026, 10, 20, 12, minutes, 0, DateTimeKind.Utc));

    [Fact]
    public void Text_loads_against_the_folder_without_reading_its_layout()
    {
        using var folder = new TempTheme(ImageLayers("on-disk.png"));
        folder.AddImage("unsaved.png", SKColors.Lime);
        using var cache = FreeCache();

        using (var theme = ThemeLoader.Load(folder.Folder, ImageLayers("unsaved.png"), cache))
        {
            Assert.Equal(Path.GetFullPath(folder.Folder), theme.Folder);
            Assert.Equal(SKColors.Lime, Images.PixelAt(theme.Assets.Image("unsaved.png")!, 1, 1));
            Assert.Empty(theme.Warnings);
        }

        // A theme being made in the editor has no layout.yaml until its first save.
        File.Delete(folder.LayoutPath);
        using var unsaved = ThemeLoader.Load(folder.Folder, ImageLayers("unsaved.png"), cache);
        Assert.NotNull(unsaved.Assets.Image("unsaved.png"));
    }

    [Fact]
    public void Text_that_does_not_parse_and_a_missing_folder_are_theme_errors()
    {
        using var folder = new TempTheme(Canvas);
        using var cache = FreeCache();

        Assert.Equal(3, Assert.Throws<ThemeException>(() => ThemeLoader.Load(folder.Folder, Canvas + "widgets:\n  - type: dial\n", cache)).Line);
        var nowhere = Path.Combine(Path.GetTempPath(), "marquee-nowhere-" + Guid.NewGuid());
        Assert.Contains("does not exist", Assert.Throws<ThemeException>(() => ThemeLoader.Load(nowhere, Canvas, cache)).Message);
    }

    [Fact]
    public void Warnings_come_with_every_load()
    {
        using var folder = new TempTheme(Canvas);
        folder.AddFile("broken.png", [1, 2, 3, 4]);
        using var cache = FreeCache();
        var text = ImageLayers("missing.png", "broken.png") + """
            widgets:
              - { type: text, at: [0, 0], size: [10, 10], text: hi, style: { font: NoSuchFamily123 } }
            """;
        string[] expected =
        [
            "Image 'missing.png' was not found and was skipped.",
            "Image 'broken.png' could not be read and was skipped.",
            "Font 'NoSuchFamily123' is not installed on this PC; using Inter instead.",
        ];

        using var first = ThemeLoader.Load(folder.Folder, text, cache);
        using var second = ThemeLoader.Load(folder.Folder, text, cache);

        Assert.Equal(expected, first.Warnings);
        Assert.Equal(expected, second.Warnings);
    }

    [Fact]
    public void An_unchanged_image_is_reused()
    {
        using var folder = new TempTheme(Canvas);
        folder.AddImage("a.png", SKColors.Red);
        using var cache = FreeCache();

        using var first = ThemeLoader.Load(folder.Folder, ImageLayers("a.png"), cache);
        using var second = ThemeLoader.Load(folder.Folder, ImageLayers("a.png") + "  - fill: \"#00000080\"\n", cache);

        Assert.Same(first.Assets.Image("a.png"), second.Assets.Image("a.png"));
    }

    [Fact]
    public void An_image_changed_on_disk_is_read_again()
    {
        using var folder = new TempTheme(Canvas);
        folder.AddImage("a.png", SKColors.Red);
        using var cache = FreeCache();
        using var first = ThemeLoader.Load(folder.Folder, ImageLayers("a.png"), cache);

        folder.AddImage("a.png", SKColors.Blue, 32, 32);
        using var resized = ThemeLoader.Load(folder.Folder, ImageLayers("a.png"), cache);
        // The same size, and only the modification time to tell it apart.
        folder.AddImage("a.png", SKColors.Lime, 32, 32);
        Touch(folder, "a.png", 1);
        using var repainted = ThemeLoader.Load(folder.Folder, ImageLayers("a.png"), cache);

        Assert.Equal(SKColors.Red, Images.PixelAt(first.Assets.Image("a.png")!, 1, 1));
        Assert.Equal(SKColors.Blue, Images.PixelAt(resized.Assets.Image("a.png")!, 1, 1));
        Assert.Equal(SKColors.Lime, Images.PixelAt(repainted.Assets.Image("a.png")!, 1, 1));
    }

    [Fact]
    public void The_same_file_named_two_ways_is_one_image()
    {
        using var folder = new TempTheme(Canvas);
        folder.AddImage("art/a.png", SKColors.Red);
        using var cache = FreeCache();

        using var theme = ThemeLoader.Load(folder.Folder, ImageLayers("art/a.png", "art/../art/a.png"), cache);

        Assert.Same(theme.Assets.Image("art/a.png"), theme.Assets.Image("art/../art/a.png"));
    }

    [Fact]
    public void An_unchanged_font_file_is_reused_and_a_changed_one_read_again()
    {
        using var folder = new TempTheme(Canvas);
        folder.AddFile("fonts/Mine.ttf", BundledFonts.ReadBytes("Inter-Bold.ttf"));
        using var cache = FreeCache();
        var text = Canvas + "widgets:\n  - { type: text, at: [0, 0], size: [10, 10], text: hi, style: { font: fonts/Mine.ttf } }\n";

        using var first = ThemeLoader.Load(folder.Folder, text, cache);
        using var second = ThemeLoader.Load(folder.Folder, text, cache);
        folder.AddFile("fonts/Mine.ttf", BundledFonts.ReadBytes("Inter-Regular.ttf"));
        using var third = ThemeLoader.Load(folder.Folder, text, cache);

        var typeface = first.Assets.Typeface("fonts/Mine.ttf", 400, italic: false);
        Assert.Same(typeface, second.Assets.Typeface("fonts/Mine.ttf", 400, italic: false));
        Assert.NotSame(typeface, third.Assets.Typeface("fonts/Mine.ttf", 400, italic: false));
        Assert.True(typeface.FontWeight > third.Assets.Typeface("fonts/Mine.ttf", 400, italic: false).FontWeight);
    }

    [Fact]
    public void An_unchanged_shader_is_compiled_and_measured_once()
    {
        using var folder = new TempTheme(Canvas);
        folder.AddFile("s.sksl", Encoding.UTF8.GetBytes(Cheap));
        var probe = new CountingProbe(5);
        using var cache = new ThemeAssetCache(probe.Measure, TestProbes.Free);
        var text = Canvas + "background:\n  - shader: s.sksl\n    opacity: 0.5\n";

        using var first = ThemeLoader.Load(folder.Folder, text, cache);
        using var second = ThemeLoader.Load(folder.Folder, text.Replace("0.5", "0.8"), cache);

        Assert.Equal(1, probe.Calls);
        Assert.Same(first.Assets.Shader((ShaderLayer)first.Spec.Background[0]),
            second.Assets.Shader((ShaderLayer)second.Spec.Background[0]));
        Assert.NotNull(second.Assets.Shader((ShaderLayer)second.Spec.Background[0]));
    }

    [Fact]
    public void A_shader_is_measured_again_for_a_new_scale_or_frame_rate_or_a_changed_file()
    {
        using var folder = new TempTheme(Canvas);
        folder.AddFile("s.sksl", Encoding.UTF8.GetBytes(Cheap));
        var probe = new CountingProbe(5);
        using var cache = new ThemeAssetCache(probe.Measure, TestProbes.Free);
        const string Text = "canvas: { orientation: landscape, fps: 30 }\nbackground:\n  - shader: s.sksl\n    scale: 0.5\n";

        using var first = ThemeLoader.Load(folder.Folder, Text, cache);
        using var scaled = ThemeLoader.Load(folder.Folder, Text.Replace("0.5", "0.25"), cache);
        using var faster = ThemeLoader.Load(folder.Folder, Text.Replace("30", "60"), cache);
        folder.AddFile("s.sksl", Encoding.UTF8.GetBytes(Red));
        using var changed = ThemeLoader.Load(folder.Folder, Text, cache);

        Assert.Equal(4, probe.Calls);
        var effect = changed.Assets.Shader((ShaderLayer)changed.Spec.Background[0])!;
        Assert.NotSame(first.Assets.Shader((ShaderLayer)first.Spec.Background[0]), effect);
        using var frame = Frames.Render(changed, TestSensors.Of());
        Assert.Equal(SKColors.Red, Images.PixelAt(frame, 960, 240));
    }

    [Fact]
    public void A_shader_too_slow_for_the_frame_rate_is_skipped_on_every_load_without_measuring_again()
    {
        using var folder = new TempTheme(Canvas);
        folder.AddFile("s.sksl", Encoding.UTF8.GetBytes(Cheap));
        var probe = new CountingProbe(1250);
        using var cache = new ThemeAssetCache(probe.Measure, TestProbes.Free);
        var text = Canvas + "background:\n  - shader: s.sksl\n";

        using var first = ThemeLoader.Load(folder.Folder, text, cache);
        using var second = ThemeLoader.Load(folder.Folder, text, cache);

        Assert.Equal(2, probe.Calls); // the first load's measurement, and the one it takes again before skipping
        Assert.Equal(first.Warnings, second.Warnings);
        Assert.Contains("would take about 1,250 ms to render a frame", Assert.Single(second.Warnings));
        Assert.Null(second.Assets.Shader((ShaderLayer)second.Spec.Background[0]));
    }

    [Fact]
    public void A_shader_that_does_not_compile_is_reported_on_every_load()
    {
        using var folder = new TempTheme(Canvas);
        folder.AddFile("bad.sksl", Encoding.UTF8.GetBytes("half4 main(float2 p) { return nope; }"));
        using var cache = FreeCache();
        var text = Canvas + "background:\n  - shader: bad.sksl\n";

        using var first = ThemeLoader.Load(folder.Folder, text, cache);
        using var second = ThemeLoader.Load(folder.Folder, text, cache);

        Assert.StartsWith("Shader 'bad.sksl' doesn't compile:", Assert.Single(first.Warnings));
        Assert.Equal(first.Warnings, second.Warnings);
    }

    [Fact]
    public void A_clip_that_can_not_keep_up_is_skipped_on_every_load_without_measuring_again()
    {
        using var folder = new TempTheme(Canvas);
        folder.AddFile("clip.gif", Gif());
        var probe = new SlowClipProbe();
        using var cache = new ThemeAssetCache(TestProbes.Free, probe.Measure);
        var text = Canvas + "background:\n  - video: clip.gif\n";

        using var first = ThemeLoader.Load(folder.Folder, text, cache);
        using var second = ThemeLoader.Load(folder.Folder, text, cache);

        Assert.Equal(2, probe.Calls); // the first load's measurement, and the one it takes again before skipping
        Assert.StartsWith("Video 'clip.gif' was skipped: Decoding a frame takes about 45 ms", Assert.Single(first.Warnings));
        Assert.Equal(first.Warnings, second.Warnings);
    }

    [Fact]
    public void An_asset_stays_while_any_load_uses_it_and_is_disposed_with_the_last()
    {
        using var folder = new TempTheme(Canvas);
        folder.AddImage("a.png", SKColors.Red);
        folder.AddImage("b.png", SKColors.Blue);
        using var cache = FreeCache();

        var first = ThemeLoader.Load(folder.Folder, ImageLayers("a.png", "b.png"), cache);
        var a = first.Assets.Image("a.png")!;
        var b = first.Assets.Image("b.png")!;
        using var second = ThemeLoader.Load(folder.Folder, ImageLayers("b.png"), cache);
        Assert.Equal(2, cache.Count);

        first.Dispose();

        Assert.Equal(IntPtr.Zero, a.Handle);
        Assert.NotEqual(IntPtr.Zero, b.Handle);
        Assert.Same(b, second.Assets.Image("b.png"));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void A_changed_file_is_released_with_the_loads_that_used_it()
    {
        using var folder = new TempTheme(Canvas);
        folder.AddImage("a.png", SKColors.Red);
        using var cache = FreeCache();

        var first = ThemeLoader.Load(folder.Folder, ImageLayers("a.png"), cache);
        var old = first.Assets.Image("a.png")!;
        folder.AddImage("a.png", SKColors.Blue, 32, 32);
        using var second = ThemeLoader.Load(folder.Folder, ImageLayers("a.png"), cache);
        Assert.Equal(2, cache.Count);
        first.Dispose();

        Assert.Equal(IntPtr.Zero, old.Handle);
        Assert.Equal(1, cache.Count);
    }

    /// The same spec, warnings and first frame, clip and shader included, as `marquee run` loads from the file.
    [Theory]
    [InlineData("example-landscape")]
    [InlineData("example-portrait")]
    [InlineData("example-agents")]
    [InlineData("example-showcase")]
    public void Loading_an_example_from_its_text_matches_loading_its_file(string name)
    {
        var folder = ThemeFolder(name);
        var text = File.ReadAllText(Path.Combine(folder, ThemeLoader.LayoutFileName));
        using var cache = FreeCache();

        using var fromFile = ThemeLoader.Load(folder, null, TestProbes.Free, TestProbes.Free);
        using var fromText = ThemeLoader.Load(folder, text, cache);

        Assert.Equal(fromFile.Folder, fromText.Folder);
        Assert.Equal(fromFile.Warnings, fromText.Warnings);
        Structural.Equal(fromFile.Spec, fromText.Spec);
        Assert.True(FirstFrame(fromFile).AsSpan().SequenceEqual(FirstFrame(fromText)), "the frames differ");
    }

    static string ThemeFolder(string name, [CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "themes", name));

    /// The pixels of the frame at animation time 0, with typical readings, at the pinned time zone and culture.
    static byte[] FirstFrame(LoadedTheme theme)
    {
        var sensors = TestSensors.Of(("cpu.temp", 64), ("cpu.load", 23), ("cpu.clock", 5250), ("gpu.temp", 58), ("gpu.load", 41),
            ("gpu.vram_used", 9.2 * 1024 * 1024 * 1024), ("ram.used_pct", 71), ("ram.used", 22.7 * 1024 * 1024 * 1024));
        using var renderer = new ThemeRenderer(theme) { Locale = TestClock.Locale() };
        using var frame = renderer.Render(sensors, TimeSpan.Zero);
        using var bitmap = SKBitmap.FromImage(frame);
        return bitmap.Bytes;
    }

    [Fact]
    public void Disposing_the_cache_disposes_everything_it_holds()
    {
        using var folder = new TempTheme(Canvas);
        folder.AddImage("a.png", SKColors.Red);
        var cache = FreeCache();
        var theme = ThemeLoader.Load(folder.Folder, ImageLayers("a.png"), cache);
        var image = theme.Assets.Image("a.png")!;

        cache.Dispose();
        cache.Dispose();

        Assert.Equal(IntPtr.Zero, image.Handle);
        Assert.Equal(0, cache.Count);
        theme.Dispose(); // after the cache, it has nothing left to give back
        Assert.Throws<ObjectDisposedException>(() => ThemeLoader.Load(folder.Folder, ImageLayers("a.png"), cache));
    }

    /// The editor should dispose the renderer, then the theme, then the cache; but a render that comes after the cache
    /// is gone (a callback at shutdown, say) must not draw native objects the cache already freed, which kills the
    /// process with an access violation no handler can catch. It draws without them.
    [Fact]
    public void Drawing_a_theme_after_its_cache_is_disposed_leaves_out_what_the_cache_held()
    {
        using var folder = new TempTheme(Canvas);
        folder.AddImage("a.png", SKColors.Red);
        folder.AddFile("s.sksl", Encoding.UTF8.GetBytes(Red));
        folder.AddFile("fonts/Mine.ttf", BundledFonts.ReadBytes("Inter-Bold.ttf"));
        const string Text = Canvas + """
            background:
              - shader: s.sksl
            widgets:
              - { type: image, at: [0, 0], size: [100, 100], src: a.png }
              - { type: text, at: [200, 0], size: [400, 100], text: hi, style: { font: fonts/Mine.ttf } }
            """;
        var cache = FreeCache();
        using var theme = ThemeLoader.Load(folder.Folder, Text, cache);
        using var renderer = new ThemeRenderer(theme);
        using (var before = renderer.Render(TestSensors.Of(), TimeSpan.Zero))
            Assert.Equal(SKColors.Red, Images.PixelAt(before, 960, 240));

        cache.Dispose();
        using var after = renderer.Render(TestSensors.Of(), TimeSpan.FromSeconds(1));

        Assert.NotEqual(SKColors.Red, Images.PixelAt(after, 50, 50));
        Assert.NotEqual(SKColors.Red, Images.PixelAt(after, 960, 240));
        Assert.Null(theme.Assets.Image("a.png"));
        Assert.Null(theme.Assets.Shader((ShaderLayer)theme.Spec.Background[0]));
        Assert.Same(BundledFonts.ForWeight(400), theme.Assets.Typeface("fonts/Mine.ttf", 400, italic: false));
    }

    /// FileInfo describes a symbolic link itself, which an edit to its target doesn't change, so the target is what is
    /// looked at. Windows makes symbolic links only for administrators or with developer mode on; elsewhere this test
    /// has nothing to check.
    [Fact]
    public void An_image_through_a_symbolic_link_is_read_again_when_its_target_changes()
    {
        using var folder = new TempTheme(Canvas);
        folder.AddImage("real/a.png", SKColors.Red);
        if (!Links.TryMakeFileSymbolicLink(Path.Combine(folder.Folder, "a.png"), Path.Combine(folder.Folder, "real", "a.png"))) return;
        using var cache = FreeCache();

        using var first = ThemeLoader.Load(folder.Folder, ImageLayers("a.png"), cache);
        folder.AddImage("real/a.png", SKColors.Blue, 32, 32);
        using var changed = ThemeLoader.Load(folder.Folder, ImageLayers("a.png"), cache);

        Assert.Equal(SKColors.Red, Images.PixelAt(first.Assets.Image("a.png")!, 1, 1));
        Assert.Equal(SKColors.Blue, Images.PixelAt(changed.Assets.Image("a.png")!, 1, 1));
    }

    /// Every other file fails the load once the cache is gone; a font file did too, rather than coming back as a font
    /// that couldn't be read.
    [Fact]
    public void A_cache_disposed_while_a_theme_loads_fails_the_load_at_a_font_file_too()
    {
        using var folder = new TempTheme(Canvas);
        folder.AddFile("fonts/Mine.ttf", BundledFonts.ReadBytes("Inter-Bold.ttf"));
        var spec = LayoutParser.Parse(Canvas + "widgets:\n  - { type: text, at: [0, 0], size: [10, 10], text: hi, style: { font: fonts/Mine.ttf } }\n");
        var cache = FreeCache();

        // Asked about each file just before it is loaded: the font file is the only one.
        Assert.Throws<ObjectDisposedException>(() => ThemeAssets.Load(folder.Folder, spec, [], 30, TestProbes.Free, TestProbes.Free,
            refused: _ =>
            {
                cache.Dispose();
                return false;
            }, cache));
    }

    /// A font file from a cache is copied into memory, so it isn't kept open and the user can still replace it. A file
    /// over the limit for any file in a theme isn't read at all.
    [Fact]
    public void A_font_file_over_the_size_limit_is_not_read_into_memory()
    {
        using var folder = new TempTheme(Canvas);
        Directory.CreateDirectory(Path.Combine(folder.Folder, "fonts"));
        using (var huge = File.Create(Path.Combine(folder.Folder, "fonts", "Huge.ttf")))
            huge.SetLength(ThemeLimits.Default.MaxFileBytes + 1); // takes no time: nothing is written
        using var cache = FreeCache();

        using var theme = ThemeLoader.Load(folder.Folder,
            Canvas + "widgets:\n  - { type: text, at: [0, 0], size: [10, 10], text: hi, style: { font: fonts/Huge.ttf } }\n", cache);

        Assert.Equal("Font file 'fonts/Huge.ttf' is over the 200 MB limit for a file in a theme; using Inter instead.",
            Assert.Single(theme.Warnings));
    }

    [Fact]
    public void Loading_from_a_file_still_loads_assets_of_its_own()
    {
        using var folder = new TempTheme(ImageLayers("a.png"));
        folder.AddImage("a.png", SKColors.Red);
        using var cache = FreeCache();
        using var cached = ThemeLoader.Load(folder.Folder, ImageLayers("a.png"), cache);
        var image = cached.Assets.Image("a.png")!;

        using (var own = folder.Load())
            Assert.NotSame(image, own.Assets.Image("a.png"));

        Assert.NotEqual(IntPtr.Zero, image.Handle);
    }

    static byte[] Gif() => GifWriter.Write(16, 4, [SKColors.Red, SKColors.Lime],
        GifFrame.Solid(SKRectI.Create(16, 4), 0, 10), GifFrame.Solid(SKRectI.Create(16, 4), 1, 10));

    /// Every shader costs the same, and the calls are counted.
    sealed class CountingProbe(double milliseconds)
    {
        public int Calls { get; private set; }

        public TimeSpan Measure(SKRuntimeEffect effect, ShaderLayer layer, SKSizeI canvas)
        {
            Calls++;
            return TimeSpan.FromMilliseconds(milliseconds);
        }
    }

    /// Every clip takes 45 ms a frame against 33 ms frames, and the calls are counted.
    sealed class SlowClipProbe
    {
        public int Calls { get; private set; }

        public DecodeCost Measure(IVideoDecoder decoder, FrameScaler scaler)
        {
            Calls++;
            return new DecodeCost(TimeSpan.FromMilliseconds(45), TimeSpan.FromMilliseconds(33));
        }
    }
}
