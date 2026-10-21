using SkiaSharp;

namespace Marquee.Core.Themes;

/// A shader file compiled, or why it can't be used ("is larger than 64 KB", "doesn't compile: …"), with what it was
/// measured to cost. ThemeAssetCache keeps one per file, so an edit that doesn't touch the shader neither compiles nor
/// measures it again.
sealed class CompiledShader(SKRuntimeEffect? effect, string? problem) : IDisposable
{
    readonly Dictionary<(int Width, int Height, int Fps), TimeSpan> _costs = [];

    /// Null when Problem says why.
    public SKRuntimeEffect? Effect { get; } = effect;

    public string? Problem { get; } = problem;

    /// The cost of a frame at this render size, measuring it the first time. The frame rate is part of the key: whether
    /// a slow first measurement is taken again depends on the frame budget (see ShaderAssets).
    public TimeSpan Cost((int Width, int Height) size, int fps, Func<TimeSpan> measure)
    {
        lock (_costs)
            if (_costs.TryGetValue((size.Width, size.Height, fps), out var known)) return known;

        // Outside the lock, which only guards the table: two loads measuring at once both keep a fair answer.
        var cost = measure();
        lock (_costs) _costs[(size.Width, size.Height, fps)] = cost;
        return cost;
    }

    public void Dispose() => Effect?.Dispose();
}
