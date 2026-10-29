using System.Runtime.InteropServices;
using Marquee.Core.Logging;
using SkiaSharp;

namespace Marquee.Core.Rendering;

/// Draws shader layers on the GPU and reads them back into the CPU-drawn frame. A shader such as a starfield costs about
/// 9 ms of CPU a frame in Skia's raster pipeline and under 1 ms here. Skia's Windows build has OpenGL but no Direct3D,
/// so this makes a WGL context on a hidden window.
///
/// Thread-affine: create, use and dispose it on one thread (the render loop's). The context is made on first use, so a
/// theme without shaders never pays for one. After any failure (no OpenGL driver, a software renderer, a lost device)
/// it logs the reason once and Render returns null for good; ShaderPainter then draws the layer on the CPU, that frame
/// included. GPU pixels differ slightly from the CPU's (sin-based hashes lose precision differently), so tests and the
/// editor, which compare frames with snapshots, draw on the CPU.
public sealed class ShaderGpu(ILog log) : IDisposable
{
    const int MaxTargets = 4; // sizes kept; a theme switch can leave the last theme's behind

    readonly Dictionary<(int Width, int Height), (SKSurface Surface, SKBitmap Pixels)> _targets = [];
    Wgl? _wgl;
    GRGlInterface? _interface;
    GRContext? _context;
    bool _failed;

    /// Whether a GPU context exists: made by the first Render, gone after a failure or Dispose.
    internal bool HasContext => _context is not null;

    /// The shader drawn at width×height, or null to draw it on the CPU. The image shares its pixels with a buffer
    /// reused by the next Render at the same size, so draw it before then.
    public SKImage? Render(SKShader shader, int width, int height)
    {
        if (_failed || !EnsureContext()) return null;
        try
        {
            if (_context!.IsAbandoned) return Fail("the GPU was reset");
            _wgl!.MakeCurrent();
            var (surface, pixels) = Target(width, height);
            surface.Canvas.Clear(SKColors.Transparent);
            using (var paint = new SKPaint { Shader = shader })
                surface.Canvas.DrawRect(0, 0, width, height, paint);
            if (!surface.ReadPixels(pixels.Info, pixels.GetPixels(), pixels.RowBytes, 0, 0))
                return Fail(_context!.IsAbandoned ? "the GPU was reset" : "reading the pixels back failed");
            using var pixmap = pixels.PeekPixels();
            return SKImage.FromPixels(pixmap);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
    }

    /// Answers messages sent to the hidden window. Call it once a frame on the owning thread, whether or not a shader
    /// drew: after a switch to a theme without shaders the window is still there, and an app broadcasting a setting
    /// change with SendMessage would otherwise wait on it.
    public void Pump() => _wgl?.Pump();

    /// OpenGL drawn by the CPU: no faster than Skia's own raster pipeline, and slower for most shaders.
    internal static bool IsSoftware(string renderer) =>
        renderer.Contains("GDI Generic", StringComparison.OrdinalIgnoreCase) ||
        renderer.Contains("Basic Render", StringComparison.OrdinalIgnoreCase) ||
        renderer.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase);

    bool EnsureContext()
    {
        if (_context is not null) return true;
        try
        {
            _wgl = Wgl.Create();
            if (IsSoftware(_wgl.Renderer))
            {
                log.Info($"Shaders draw on the CPU: OpenGL here is the software renderer '{_wgl.Renderer}'.");
                _failed = true;
                Release();
                return false;
            }
            _interface = GRGlInterface.Create() ?? throw new InvalidOperationException("Skia found no OpenGL functions");
            _context = GRContext.CreateGl(_interface) ?? throw new InvalidOperationException("Skia could not use OpenGL");
            log.Info($"Shaders draw on the GPU ({_wgl.Renderer}).");
            return true;
        }
        catch (Exception ex)
        {
            // A PC without an OpenGL driver (a VM, a server, CI) is not a fault: it just draws as before.
            log.Info($"Shaders draw on the CPU: no usable OpenGL here ({ex.Message}).");
            _failed = true;
            Release();
            return false;
        }
    }

    (SKSurface Surface, SKBitmap Pixels) Target(int width, int height)
    {
        if (_targets.TryGetValue((width, height), out var target)) return target;
        if (_targets.Count >= MaxTargets) ReleaseTargets();
        var surface = SKSurface.Create(_context!, false, new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException($"no {width}×{height} GPU surface");
        target = (surface, new SKBitmap(new SKImageInfo(width, height)));
        _targets[(width, height)] = target;
        return target;
    }

    SKImage? Fail(string reason)
    {
        log.Warn($"Drawing shaders on the GPU failed ({reason}), so they draw on the CPU from now on.");
        _failed = true;
        Release();
        return null;
    }

    void ReleaseTargets()
    {
        foreach (var (surface, pixels) in _targets.Values)
        {
            surface.Dispose();
            pixels.Dispose();
        }
        _targets.Clear();
    }

    void Release()
    {
        try
        {
            ReleaseTargets();
            _context?.Dispose();
            _interface?.Dispose();
        }
        finally
        {
            _context = null;
            _interface = null;
            _wgl?.Dispose();
            _wgl = null;
        }
    }

    /// Render returns null after this.
    public void Dispose()
    {
        _failed = true;
        Release();
    }

    /// An OpenGL context on a hidden window, current on the thread that made it.
    sealed class Wgl : IDisposable
    {
        const string ClassName = "MarqueeShaderGpu";
        const uint CS_OWNDC = 0x20;
        const uint PFD_DRAW_TO_WINDOW = 0x4, PFD_SUPPORT_OPENGL = 0x20, PFD_DOUBLEBUFFER = 0x1;
        const uint GL_RENDERER = 0x1F01;
        const uint PM_REMOVE = 0x1;
        const int ERROR_CLASS_ALREADY_EXISTS = 1410;

        IntPtr _hwnd, _hdc, _hglrc;

        public string Renderer { get; private set; } = "";

        public static Wgl Create()
        {
            var wgl = new Wgl();
            try
            {
                var instance = GetModuleHandleW(null);
                var windowClass = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    style = CS_OWNDC, // OpenGL wants a DC of the window's own, held for the context's life
                    lpfnWndProc = NativeLibrary.GetExport(
                        NativeLibrary.Load("user32.dll", typeof(Wgl).Assembly, DllImportSearchPath.System32), "DefWindowProcW"),
                    hInstance = instance,
                    lpszClassName = ClassName,
                };
                if (RegisterClassExW(ref windowClass) == 0 && Marshal.GetLastWin32Error() != ERROR_CLASS_ALREADY_EXISTS)
                    throw new InvalidOperationException($"no window class (error {Marshal.GetLastWin32Error()})");

                // Never shown.
                wgl._hwnd = CreateWindowExW(0, ClassName, "Marquee shaders", 0, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
                if (wgl._hwnd == IntPtr.Zero) throw new InvalidOperationException($"no window (error {Marshal.GetLastWin32Error()})");
                wgl._hdc = GetDC(wgl._hwnd);

                var format = new PIXELFORMATDESCRIPTOR
                {
                    nSize = (ushort)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(),
                    nVersion = 1,
                    dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER,
                    cColorBits = 32,
                    cAlphaBits = 8,
                    cStencilBits = 8,
                };
                var index = ChoosePixelFormat(wgl._hdc, ref format);
                if (index == 0 || !SetPixelFormat(wgl._hdc, index, ref format)) throw new InvalidOperationException("no OpenGL pixel format");
                wgl._hglrc = wglCreateContext(wgl._hdc);
                if (wgl._hglrc == IntPtr.Zero || !wglMakeCurrent(wgl._hdc, wgl._hglrc)) throw new InvalidOperationException("no OpenGL context");
                wgl.Renderer = Marshal.PtrToStringAnsi(glGetString(GL_RENDERER)) ?? "";
                return wgl;
            }
            catch
            {
                wgl.Dispose();
                throw;
            }
        }

        public void Pump()
        {
            while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                TranslateMessage(ref message);
                DispatchMessageW(ref message);
            }
        }

        /// Makes this context current again if anything on the thread changed it, so Skia's calls reach it.
        public void MakeCurrent()
        {
            if (wglGetCurrentContext() != _hglrc && !wglMakeCurrent(_hdc, _hglrc))
                throw new InvalidOperationException("the OpenGL context could not be made current");
        }

        public void Dispose()
        {
            if (_hglrc != IntPtr.Zero)
            {
                wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
                wglDeleteContext(_hglrc);
                _hglrc = IntPtr.Zero;
            }
            if (_hdc != IntPtr.Zero)
            {
                ReleaseDC(_hwnd, _hdc);
                _hdc = IntPtr.Zero;
            }
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASSEXW
        {
            public uint cbSize, style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra, cbWndExtra;
            public IntPtr hInstance, hIcon, hCursor, hbrBackground;
            public string? lpszMenuName, lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PIXELFORMATDESCRIPTOR
        {
            public ushort nSize, nVersion;
            public uint dwFlags;
            public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift, cAlphaBits,
                cAlphaShift, cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits, cDepthBits,
                cStencilBits, cAuxBuffers, iLayerType, bReserved;
            public uint dwLayerMask, dwVisibleMask, dwDamageMask;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam, lParam;
            public uint time;
            public int x, y;
            public uint lPrivate;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern IntPtr GetModuleHandleW(string? moduleName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern ushort RegisterClassExW(ref WNDCLASSEXW windowClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y,
            int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool PeekMessageW(out MSG message, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);

        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool TranslateMessage(ref MSG message);

        [DllImport("user32.dll", ExactSpelling = true)]
        static extern IntPtr DispatchMessageW(ref MSG message);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR format);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetPixelFormat(IntPtr hdc, int index, ref PIXELFORMATDESCRIPTOR format);

        [DllImport("opengl32.dll", ExactSpelling = true)]
        static extern IntPtr wglCreateContext(IntPtr hdc);

        [DllImport("opengl32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);

        [DllImport("opengl32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool wglDeleteContext(IntPtr hglrc);

        [DllImport("opengl32.dll", ExactSpelling = true)]
        static extern IntPtr wglGetCurrentContext();

        [DllImport("opengl32.dll", ExactSpelling = true)]
        static extern IntPtr glGetString(uint name);
    }
}
