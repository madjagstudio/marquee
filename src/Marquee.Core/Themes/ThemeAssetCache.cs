using Marquee.Core.Video;
using SkiaSharp;

namespace Marquee.Core.Themes;

/// Keeps a theme's files loaded from one load to the next, for the theme editor, which loads its unsaved text on every
/// change (ThemeLoader.Load with text and a cache): decoded images, font files, compiled shaders with what they were
/// measured to cost, and playing clips with their decode measurement. Each is found again by its file's full path, size
/// and modification time, so a file changed on disk is read again. A playing clip is scaled to whatever box it is drawn
/// in, so moving or resizing its widget keeps it too. An edit that doesn't touch a file then costs no decoding,
/// compiling, measuring or reopening, and a clip plays on where it was.
///
/// Counted references: each load holds what it uses until its theme is disposed, and whatever no load holds any more is
/// disposed then, a changed file's old copy included. So the editor loads the new text, swaps it in, then disposes the
/// theme it replaced: what both use carries over, and the last good theme stays whole if the new text doesn't load.
/// (Generations, with a sweep after each load, would dispose what the theme still on screen uses before it is swapped.)
///
/// A player hands out one frame at a time, so one load never shares a player between two of its widgets, and the themes
/// loaded through one cache are drawn one at a time, from one thread, as the editor's canvas does. Disposing the cache
/// disposes everything in it, so the order is: the renderer, then the theme, then the cache. A theme drawn after its
/// cache is disposed anyway draws without the files it had from it (ThemeAssets.Detach), but the cache must never be
/// disposed while a frame is being drawn.
public sealed class ThemeAssetCache : IDisposable
{
    enum Kind { Image, Font, Shader, Clip }

    /// A file as it was when it was read. A file that can't be looked at gets a length of -1, which still names one
    /// entry, so what was loaded from it is released like anything else.
    readonly record struct FileStamp(string Path, long Length, DateTime Modified)
    {
        public static FileStamp Of(string full)
        {
            try
            {
                // FileInfo describes a symbolic link itself, which an edit to its target doesn't change; the file read
                // is the target. (A copy that keeps the size and the time, which some tools and file systems make, is
                // still taken for the same file: hashing every file on every load would cost what the cache saves.)
                var file = new FileInfo(full);
                if (file.LinkTarget is not null) file = file.ResolveLinkTarget(returnFinalTarget: true) as FileInfo ?? file;
                if (file.Exists) return new FileStamp(full, file.Length, file.LastWriteTimeUtc);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Read as missing; loading it reports why.
            }

            return new FileStamp(full, -1, default);
        }
    }

    /// Detail is what else the asset depends on: a refused clip's fit and box size (see Clip).
    readonly record struct Key(Kind Kind, FileStamp File, object? Detail);

    /// One loaded asset and how many loads hold it. Exclusive ones (playing clips) are held by at most one widget per load.
    sealed class Entry(Key key, object value, bool exclusive)
    {
        public Key Key { get; } = key;
        public object Value { get; } = value;
        public bool Exclusive { get; } = exclusive;
        public int Leases { get; set; }
    }

    readonly object _gate = new();
    readonly Dictionary<Key, List<Entry>> _entries = [];
    readonly Dictionary<ThemeAssets, HashSet<Entry>> _leases = new(ReferenceEqualityComparer.Instance);
    bool _disposed;

    /// Measures shaders and clips as `marquee run` does, the first time each is loaded.
    public ThemeAssetCache()
        : this(ShaderCost.Estimate, VideoCost.Measure)
    {
    }

    internal ThemeAssetCache(ShaderProbe shaderProbe, VideoProbe videoProbe)
    {
        ShaderProbe = shaderProbe;
        VideoProbe = videoProbe;
    }

    internal ShaderProbe ShaderProbe { get; }

    internal VideoProbe VideoProbe { get; }

    /// Assets held, for tests.
    internal int Count
    {
        get
        {
            lock (_gate) return _entries.Values.Sum(entries => entries.Count);
        }
    }

    internal void ThrowIfDisposed()
    {
        lock (_gate) ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// The image decoded from `full`, decoding it with `decode` unless the file is unchanged since it last was. A
    /// failure (null) isn't kept, so its warning comes with every load.
    internal SKImage? Image(ThemeAssets owner, string full, Func<SKImage?> decode) => Lease(owner, Kind.Image, full, null, decode);

    internal SKTypeface? Font(ThemeAssets owner, string full, Func<SKTypeface?> load) => Lease(owner, Kind.Font, full, null, load);

    /// A compiled shader, or why it doesn't compile; null (a read error) isn't kept. Its cost is kept on it.
    internal CompiledShader? Shader(ThemeAssets owner, string full, Func<CompiledShader?> compile) =>
        Lease(owner, Kind.Shader, full, null, compile);

    /// A playing clip, or why it can't play, which is kept too, so a clip too slow to play isn't measured again on every
    /// load; but not a refusal for now (a file another program holds locked), which the next load tries again. A
    /// playing one is never shared within a load.
    ///
    /// A player is kept by its file alone: in a box of another size or place, or with another fit, it scales its frames
    /// to the box it is drawn in (ThemeAssets.Video, VideoPlayer.FitTo), so moving or resizing a widget keeps its clip
    /// playing. A refusal is kept by the fit and the size of the box, which are what can make a clip too slow, so
    /// moving a refused clip doesn't measure it again, and resizing it does.
    internal OpenedClip Clip(ThemeAssets owner, string full, ImageFit fit, SKRect area, Func<OpenedClip> open)
    {
        var file = FileStamp.Of(full);
        var refused = new Key(Kind.Clip, file, (fit, area.Size));
        var playing = new Key(Kind.Clip, file, null);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if ((TryLease(owner, refused) ?? TryLease(owner, playing)) is OpenedClip held) return held;
        }

        var clip = open();
        if (clip.ForNow) return clip; // nothing to dispose: a refusal has no player
        return Add(owner, clip.Player is null ? refused : playing, clip);
    }

    /// Lets go of everything `owner` holds, and disposes what no load holds any more. Does nothing after Dispose.
    internal void Release(ThemeAssets owner)
    {
        var unused = new List<object>();
        lock (_gate)
        {
            if (!_leases.Remove(owner, out var held)) return;
            foreach (var entry in held)
            {
                if (--entry.Leases > 0) continue;
                var entries = _entries[entry.Key];
                entries.Remove(entry);
                if (entries.Count == 0) _entries.Remove(entry.Key);
                unused.Add(entry.Value);
            }
        }

        DisposeAll(unused);
    }

    /// Disposes everything held, in use or not. The themes loaded through the cache are told first (ThemeAssets.Detach),
    /// so one drawn after this draws without its files rather than touching what was freed.
    public void Dispose()
    {
        List<object> all;
        List<ThemeAssets> owners;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            all = _entries.Values.SelectMany(entries => entries).Select(entry => entry.Value).ToList();
            owners = [.. _leases.Keys];
            _entries.Clear();
            _leases.Clear();
        }

        foreach (var owner in owners) owner.Detach();
        DisposeAll(all);
    }

    T? Lease<T>(ThemeAssets owner, Kind kind, string full, object? detail, Func<T?> create) where T : class
    {
        var key = new Key(kind, FileStamp.Of(full), detail);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (TryLease(owner, key) is { } held) return (T)held;
        }

        // Outside the lock: decoding, compiling and measuring take a while, and a release on another thread needn't wait.
        // Two loads creating the same asset at once each keep their own, which costs memory, not correctness.
        var value = create();
        return value is null ? null : Add(owner, key, value);
    }

    /// What `owner` may use under `key`, now leased to it, or null. Under the lock.
    object? TryLease(ThemeAssets owner, Key key)
    {
        var held = Held(owner);
        foreach (var entry in _entries.TryGetValue(key, out var entries) ? entries : [])
        {
            // A clip that stopped with an error stays stopped in its player; it is opened again rather than shared, and
            // the stopped player goes with the last load holding it.
            if (entry.Value is OpenedClip { Player.Failed: true }) continue;
            if (held.Contains(entry))
            {
                if (entry.Exclusive) continue;
                return entry.Value;
            }

            entry.Leases++;
            held.Add(entry);
            return entry.Value;
        }

        return null;
    }

    /// Keeps `value`, just created, under `key`, leased to `owner`; disposes it if the cache was disposed meanwhile.
    T Add<T>(ThemeAssets owner, Key key, T value) where T : class
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                var entry = new Entry(key, value, exclusive: value is OpenedClip { Player: not null }) { Leases = 1 };
                if (!_entries.TryGetValue(key, out var entries)) _entries[key] = entries = [];
                entries.Add(entry);
                Held(owner).Add(entry);
                return value;
            }
        }

        DisposeAll([value]);
        throw new ObjectDisposedException(nameof(ThemeAssetCache));
    }

    HashSet<Entry> Held(ThemeAssets owner)
    {
        if (!_leases.TryGetValue(owner, out var held)) _leases[owner] = held = [];
        return held;
    }

    /// Players are stopped together (ThemeAssets.StopVideos), so their threads wind down at once.
    static void DisposeAll(IReadOnlyList<object> values)
    {
        ThemeAssets.StopVideos(values.OfType<OpenedClip>().Select(clip => clip.Player).OfType<VideoPlayer>().ToList());
        foreach (var disposable in values.OfType<IDisposable>()) disposable.Dispose();
    }
}
