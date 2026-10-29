using System.Runtime.InteropServices;
using System.Text;
using Marquee.Core.Rendering;
using Marquee.Core.Tests.TestSupport;
using SkiaSharp;

namespace Marquee.Core.Tests;

/// Shader layers drawn on the GPU match the CPU's. A machine with no usable OpenGL (CI) can't draw there: those tests
/// check it falls back to the CPU instead, which every other test already covers.
[Collection("Real-time render loop")]
public class ShaderGpuTests
{
    /// Smooth ramps, so GPU and CPU agree to rounding: red follows x, green follows y, blue is `level`.
    const string Ramp = """
        uniform float2 resolution;
        uniform float level;
        half4 main(float2 p) {
            return half4(half(p.x / resolution.x), half(p.y / resolution.y), half(level), 1.0);
        }
        """;

    static TempTheme Theme(string layer)
    {
        var theme = new TempTheme($"canvas: {{ orientation: landscape }}\nbackground:\n  - shader: ramp.sksl\n    uniforms: {{ level: 0.5 }}\n{layer}");
        theme.AddFile("ramp.sksl", Encoding.UTF8.GetBytes(Ramp));
        return theme;
    }

    static SKBitmap Render(TempTheme folder, ShaderGpu? gpu)
    {
        using var theme = folder.Load();
        using var renderer = new ThemeRenderer(theme) { Gpu = gpu };
        using var frame = renderer.Render(TestSensors.Of(), TimeSpan.FromSeconds(1));
        return SKBitmap.FromImage(frame);
    }

    static void AssertSame(SKBitmap cpu, SKBitmap gpu)
    {
        var worst = 0;
        for (var y = 0; y < cpu.Height; y += 7)
        for (var x = 0; x < cpu.Width; x += 7)
        {
            var (a, b) = (cpu.GetPixel(x, y), gpu.GetPixel(x, y));
            worst = Math.Max(worst, Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue))));
        }
        Assert.True(worst <= 2, $"GPU pixels are up to {worst}/255 from the CPU's");
    }

    [Theory]
    [InlineData("    scale: 1")]
    [InlineData("    scale: 0.5")]
    [InlineData("    scale: 0.5\n    opacity: 0.5\n    blend: screen")]
    public void A_shader_drawn_on_the_gpu_matches_the_cpu(string layer)
    {
        using var folder = Theme(layer);
        var log = new ListLog();
        using var gpu = new ShaderGpu(log);

        using var cpuFrame = Render(folder, gpu: null);
        using var gpuFrame = Render(folder, gpu);

        AssertSame(cpuFrame, gpuFrame);
        if (!gpu.HasContext)
        {
            // CI's runners have no usable OpenGL, so they can only check the CPU fallback. On a developer's PC, which
            // has a GPU, no context means the GPU path broke.
            Assert.True(Environment.GetEnvironmentVariable("CI") is not null, $"No GPU context: {string.Join(" | ", log.Entries)}");
            Assert.Contains(log.Entries, e => e.StartsWith("info: Shaders draw on the CPU"));
            return;
        }
        Assert.Contains("info: Shaders draw on the GPU", log.Entries[0]);
    }

    [Fact]
    public void A_theme_without_shaders_makes_no_gpu_context()
    {
        using var folder = new TempTheme("canvas: { orientation: landscape }\nbackground:\n  - fill: \"#203040\"");
        var log = new ListLog();
        using var gpu = new ShaderGpu(log);

        using var frame = Render(folder, gpu);

        Assert.False(gpu.HasContext);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public void A_disposed_gpu_leaves_shaders_on_the_cpu()
    {
        using var folder = Theme("    scale: 0.5");
        using var gpu = new ShaderGpu(new ListLog());
        using var theme = folder.Load();
        using var renderer = new ThemeRenderer(theme) { Gpu = gpu };

        for (var i = 0; i < 5; i++) renderer.Render(TestSensors.Of(), TimeSpan.FromSeconds(i)).Dispose();
        gpu.Dispose();
        using var frame = renderer.Render(TestSensors.Of(), TimeSpan.FromSeconds(5)); // still handed the disposed GPU
        using var pixels = SKBitmap.FromImage(frame);

        Assert.False(gpu.HasContext); // and it didn't make a new context
        Pixels.AssertNear(new SKColor(128, 128, 128), pixels.GetPixel(960, 240), 2);
    }

    [Fact]
    public void A_gpu_failure_is_logged_once_and_shaders_stay_on_the_cpu()
    {
        using var effect = SKRuntimeEffect.CreateShader(Ramp, out var errors) ?? throw new Exception(errors);
        using var uniforms = new SKRuntimeEffectUniforms(effect) { ["resolution"] = new float[] { 64, 64 }, ["level"] = 0.5f };
        using var shader = effect.ToShader(uniforms);
        var log = new ListLog();
        using var gpu = new ShaderGpu(log);

        using (var image = gpu.Render(shader, 64, 64))
            if (image is null) return; // no GPU here
        var tooBig = gpu.Render(shader, 100_000, 1); // past any GPU's texture size: the surface can't be made

        Assert.Null(tooBig);
        Assert.False(gpu.HasContext);
        Assert.Null(gpu.Render(shader, 64, 64));
        Assert.Single(log.Entries, e => e.StartsWith("warn: Drawing shaders on the GPU failed"));
    }

    [Theory]
    [InlineData("GDI Generic", true)]
    [InlineData("Microsoft Basic Render Driver", true)]
    [InlineData("llvmpipe (LLVM 15.0.7, 256 bits)", true)]
    [InlineData("AMD Radeon RX 7800 XT", false)]
    [InlineData("NVIDIA GeForce RTX 4070/PCIe/SSE2", false)]
    [InlineData("Intel(R) UHD Graphics 770", false)]
    public void Software_opengl_is_not_used(string renderer, bool software) =>
        Assert.Equal(software, ShaderGpu.IsSoftware(renderer));

    [Fact]
    public void The_render_loop_draws_shaders_on_its_own_thread()
    {
        using var folder = Theme("    scale: 0.5");
        using var device = new RecordingDevice();
        var log = new ListLog();
        using var loop = new RenderLoop(device, TestSensors.Store(), log, fpsOverride: 30);
        loop.SetTheme(folder.Load());

        loop.Start();
        Assert.True(Wait.Until(() => loop.FramesPushed >= 5));
        loop.Stop();

        Assert.DoesNotContain(log.Entries, e => e.StartsWith("warn") || e.StartsWith("error"));
        Pixels.AssertNear(new SKColor(128, 128, 128), device.LastFrame!.GetPixel(960, 240), 2);
    }

    [Fact]
    public void The_hidden_window_still_answers_after_a_switch_to_a_theme_without_shaders()
    {
        using var shaders = Theme("    scale: 0.5");
        using var plain = new TempTheme("canvas: { orientation: landscape }\nbackground:\n  - fill: \"#203040\"");
        using var device = new RecordingDevice();
        using var loop = new RenderLoop(device, TestSensors.Store(), new ListLog(), fpsOverride: 30);
        loop.SetTheme(shaders.Load());
        loop.Start();
        try
        {
            Assert.True(Wait.Until(() => loop.FramesPushed >= 5));
            var window = OurWindow();
            if (window == IntPtr.Zero) return; // no GPU here, so no window

            loop.SetTheme(plain.Load());
            var shown = loop.FramesPushed;
            Assert.True(Wait.Until(() => loop.FramesPushed >= shown + 5));

            // What a setting change broadcast does: send, and wait for the window's thread to answer.
            Assert.NotEqual(IntPtr.Zero, SendMessageTimeoutW(window, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero, 0x2 /* SMTO_ABORTIFHUNG */, 2000, out _));
        }
        finally
        {
            loop.Stop();
        }
    }

    /// This process's hidden GPU window: the installed tray has one of the same class.
    static IntPtr OurWindow()
    {
        var window = IntPtr.Zero;
        while ((window = FindWindowExW(IntPtr.Zero, window, "MarqueeShaderGpu", null)) != IntPtr.Zero)
        {
            GetWindowThreadProcessId(window, out var process);
            if (process == Environment.ProcessId) return window;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, string className, string? windowName);

    [DllImport("user32.dll", ExactSpelling = true)]
    static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    static extern IntPtr SendMessageTimeoutW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);
}
