using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Marquee.Core.Tests.TestSupport;

/// Approved-image comparison. Approved PNGs live in tests/Marquee.Core.Tests/Snapshots and are committed.
/// A missing or different snapshot writes NAME.received.png beside it (git-ignored) and fails the test;
/// look at it, and if it is right, rename it to NAME.png.
static class Snapshots
{
    /// A pixel counts as different when any channel is off by more than this.
    const int ChannelTolerance = 8;

    /// The test fails when more than this fraction of pixels differ (0.1%).
    const double PixelTolerance = 0.001;

    public static void Match(SKImage actual, string name, [CallerFilePath] string callerFile = "")
    {
        var folder = Path.Combine(Path.GetDirectoryName(callerFile)!, "Snapshots");
        Directory.CreateDirectory(folder);
        var approved = Path.Combine(folder, $"{name}.png");
        var received = Path.Combine(folder, $"{name}.received.png");

        if (!File.Exists(approved))
        {
            Save(actual, received);
            Assert.Fail($"No approved snapshot '{name}'. Look at {received}; if it is right, rename it to {name}.png and run the test again.");
        }

        using var expected = SKImage.FromEncodedData(File.ReadAllBytes(approved));
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            Save(actual, received);
            Assert.Fail($"Snapshot '{name}' is {actual.Width}x{actual.Height} but the approved image is {expected.Width}x{expected.Height}.");
        }

        var differing = CountDifferences(expected, actual);
        var total = actual.Width * actual.Height;
        if (differing > total * PixelTolerance)
        {
            Save(actual, received);
            Assert.Fail($"Snapshot '{name}' differs from the approved image in {differing} of {total} pixels. " +
                $"Compare {received} with {approved}; if the change is intended, replace the approved image.");
        }

        File.Delete(received);
    }

    static int CountDifferences(SKImage expected, SKImage actual)
    {
        using var a = SKBitmap.FromImage(expected);
        using var b = SKBitmap.FromImage(actual);
        var pa = a.Pixels;
        var pb = b.Pixels;
        var count = 0;
        for (var i = 0; i < pa.Length; i++)
        {
            var (x, y) = (pa[i], pb[i]);
            if (Math.Abs(x.Red - y.Red) > ChannelTolerance || Math.Abs(x.Green - y.Green) > ChannelTolerance
                || Math.Abs(x.Blue - y.Blue) > ChannelTolerance || Math.Abs(x.Alpha - y.Alpha) > ChannelTolerance)
                count++;
        }

        return count;
    }

    static void Save(SKImage image, string path)
    {
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, png.ToArray());
    }
}
