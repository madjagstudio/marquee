using System.Windows;
using Marquee.Editor.Canvas;
using SkiaSharp;

namespace Marquee.Editor.Tests;

/// The one transform between the canvas view and theme pixels: zoom and DPI together, never rounded.
public class CanvasTransformTests
{
    [Theory]
    [InlineData(1, 1, 300, 120)]
    [InlineData(1, 1.5, 200, 80)]
    [InlineData(2, 1.5, 400, 160)]
    [InlineData(2, 1, 600, 240)]
    [InlineData(0.5, 1.25, 120, 48)]
    [InlineData(0.75, 2, 112.5, 45)]
    public void Theme_pixels_are_scaled_by_zoom_over_dpi(double zoom, double dpiScale, double viewX, double viewY)
    {
        var transform = new CanvasTransform(zoom, dpiScale);

        var view = transform.ToView(new SKPoint(300, 120));
        var theme = transform.ToTheme(new Point(viewX, viewY));

        Assert.Equal(viewX, view.X, 9);
        Assert.Equal(viewY, view.Y, 9);
        Assert.Equal(300, theme.X, 4);
        Assert.Equal(120, theme.Y, 4);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 1.25)]
    [InlineData(1, 1.75)]
    [InlineData(2, 1.5)]
    [InlineData(0.37, 1.5)]
    public void A_point_between_pixels_goes_there_and_back_without_rounding(double zoom, double dpiScale)
    {
        var transform = new CanvasTransform(zoom, dpiScale);
        var theme = new SKPoint(1234.25f, 77.5f);

        var back = transform.ToTheme(transform.ToView(theme));

        Assert.Equal(theme.X, back.X, 3);
        Assert.Equal(theme.Y, back.Y, 3);
    }

    [Theory]
    [InlineData(1, 1920, 480)]
    [InlineData(1.5, 1280, 320)]
    [InlineData(2, 960, 240)]
    public void At_100_percent_each_theme_pixel_is_one_screen_pixel(double dpiScale, double width, double height)
    {
        var transform = new CanvasTransform(CanvasTransform.ZoomFor(ZoomMode.Actual, new Size(100, 100), new SKSizeI(1920, 480), dpiScale), dpiScale);

        Assert.Equal(new Size(width, height), transform.ViewSize(new SKSizeI(1920, 480)));
    }

    [Theory]
    [InlineData(1920, 480, 960, 400, 1, 0.5)]       // the width limits a landscape canvas
    [InlineData(1920, 480, 960, 400, 1.5, 0.75)]
    [InlineData(1920, 480, 4000, 240, 1, 0.5)]      // the height does when the window is wide
    [InlineData(480, 1920, 1200, 700, 1.25, 1.25 * 700 / 1920)]
    public void Fit_makes_the_whole_canvas_fit_as_large_as_it_can(int canvasWidth, int canvasHeight, double width, double height,
        double dpiScale, double zoom)
    {
        var fit = CanvasTransform.ZoomFor(ZoomMode.Fit, new Size(width, height), new SKSizeI(canvasWidth, canvasHeight), dpiScale);

        Assert.Equal(zoom, fit, 9);
    }

    [Fact]
    public void Fit_never_goes_to_nothing()
    {
        Assert.Equal(CanvasTransform.MinZoom, CanvasTransform.ZoomFor(ZoomMode.Fit, new Size(1, 1), new SKSizeI(1920, 480), 1));
        Assert.Equal(1, CanvasTransform.ZoomFor(ZoomMode.Fit, new Size(0, 0), new SKSizeI(1920, 480), 1));
        Assert.Equal(2, CanvasTransform.ZoomFor(ZoomMode.Double, new Size(0, 0), new SKSizeI(1920, 480), 1));
    }
}
