using Marquee.Core.Rendering;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// An image widget casts its box's shadow, as a video widget does: the box's silhouette with its radius, under the fill
/// and the picture. Without a `shadow` it draws as it always has.
public class ImageWidgetShadowTests
{
    const string Landscape = "canvas: { orientation: landscape }\nwidgets:\n";

    static SKColor At(SKImage image, int x, int y) => Images.PixelAt(image, x, y);

    static SKImage Render(TempTheme folder)
    {
        using var theme = folder.Load();
        Assert.Empty(theme.Warnings);
        return Frames.Render(theme, TestSensors.Of());
    }

    [Fact]
    public void An_image_widget_casts_its_boxs_shadow_under_the_picture()
    {
        using var folder = new TempTheme(Landscape + """
              - type: image
                at: [100, 100]
                size: [200, 100]
                src: red.png
                fit: stretch
                style: { shadow: { color: "#FFFFFF", x: 20, y: 30, blur: 0 } }
            """);
        folder.AddImage("red.png", SKColors.Red);
        using var frame = Render(folder);

        Pixels.AssertNear(SKColors.Red, At(frame, 290, 190)); // the picture covers the shadow under it
        Pixels.AssertNear(SKColors.White, At(frame, 310, 150)); // right of the box, moved 20 px
        Pixels.AssertNear(SKColors.White, At(frame, 200, 220)); // below it, moved 30 px
        Pixels.AssertNear(SKColors.Black, At(frame, 110, 220), 0); // left of where the moved box starts
    }

    [Fact]
    public void The_shadow_is_the_boxs_so_a_contained_picture_with_no_fill_still_casts_the_whole_box()
    {
        using var folder = new TempTheme(Landscape + """
              - type: image
                at: [100, 100]
                size: [400, 100]
                src: red.png
                fit: contain
                style: { shadow: { color: "#FFFFFF", x: 0, y: 0, blur: 0 } }
            """);
        folder.AddImage("red.png", SKColors.Red); // 64×64, so contained it leaves the box's sides empty
        using var frame = Render(folder);

        Pixels.AssertNear(SKColors.Red, At(frame, 300, 150));
        Pixels.AssertNear(SKColors.White, At(frame, 120, 150)); // the empty side shows the box's shadow
    }

    [Fact]
    public void Without_a_shadow_nothing_is_drawn_outside_the_box()
    {
        using var folder = new TempTheme(Landscape + """
              - { type: image, at: [100, 100], size: [200, 100], src: red.png, fit: stretch }
            """);
        folder.AddImage("red.png", SKColors.Red);
        using var frame = Render(folder);

        Assert.Equal(0, Pixels.Count(frame, SKRectI.Create(300, 100, 100, 200), color => color != SKColors.Black));
        Assert.Equal(0, Pixels.Count(frame, SKRectI.Create(100, 200, 200, 100), color => color != SKColors.Black));
    }

    /// Soft shadows under images with each fit, a radius and a rotation, on a gradient so the shadow shows.
    [Fact]
    public void Image_shadows_snapshot()
    {
        using var folder = new TempTheme("""
            canvas: { orientation: landscape }
            background:
              - fill: { linear: ["#3A4A6A", "#8A9AB8"], angle: 90 }
            widgets:
              - type: image
                at: [60, 60]
                size: [360, 360]
                src: picture.png
                fit: cover
                style: { radius: 32, shadow: { color: "#000000C0", blur: 24, x: 12, y: 12 } }
              - type: image
                at: [500, 60]
                size: [480, 360]
                src: picture.png
                fit: contain
                style: { fill: "#36D1DC40", radius: 24, stroke: { width: 3, color: "#36D1DC" }, shadow: { color: "#00000080", blur: 16, x: 0, y: 16 } }
              - type: image
                at: [1060, 100]
                size: [400, 280]
                src: picture.png
                fit: stretch
                rotate: -6
                style: { opacity: 0.8, shadow: { color: "#000000C0", blur: 8, x: -10, y: 10 } }
              - type: image
                at: [1540, 100]
                size: [320, 280]
                src: picture.png
                fit: contain
                style: { shadow: { color: "#FFB347", blur: 0, x: 8, y: 8 } }
            """);
        folder.AddFile("picture.png", Picture());
        using var frame = Render(folder);

        Snapshots.Match(frame, "image-shadows");
    }

    /// A 160×90 picture: a blue frame with a stripe down each side and a white disc in the middle, as the video
    /// widgets' snapshot uses, so each fit shows what it keeps.
    static byte[] Picture()
    {
        using var surface = SKSurface.Create(new SKImageInfo(160, 90));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(40, 60, 160));
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(220, 40, 160) };
        canvas.DrawRect(0, 0, 16, 90, paint);
        paint.Color = new SKColor(240, 150, 30);
        canvas.DrawRect(144, 0, 16, 90, paint);
        paint.Color = SKColors.White;
        canvas.DrawCircle(80, 45, 30, paint);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
