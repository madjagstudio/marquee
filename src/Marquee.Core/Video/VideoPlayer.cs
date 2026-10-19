using SkiaSharp;

namespace Marquee.Core.Video;

/// Plays a clip for a video layer at its own speed, looping forever. A background thread decodes a few frames ahead
/// and scales each to the size it is drawn at (see FrameScaler); the render thread asks for the frame for its current
/// time and draws it, never waiting on the decoder.
///
/// Whatever the render rate, the clip keeps to its own clock. Rendering slower than the clip's frame rate shows fewer of
/// its frames rather than playing it in slow motion: the decode thread drops frames already over by the render
/// thread's latest time before scaling them, scaling being the costly half. If decoding falls behind, the frame on
/// screen stays until the next one is ready, and once the player is more than MaxLag behind its clock it carries on
/// from the frame on screen.
///
/// Image ownership, which keeps every image alive exactly as long as someone can draw it:
/// - The decode thread creates each image and hands it over by queueing it. It never touches an image again, so it
///   can't dispose one the render thread holds.
/// - FrameAt runs on the render thread. The image it returns is valid until its next call (or Dispose): that next call
///   is where the image it replaces is disposed, once the render thread has finished drawing it. Queued images it
///   skips were never handed out, and it disposes them there too.
/// - Dispose stops the decode thread first, then disposes every image. The owner (the theme's assets) disposes the
///   player only when nothing renders the theme any more, so no FrameAt runs alongside it.
/// - Images wrap reused buffers (see FrameBuffers), and a buffer is reused only once its image is freed, so these rules
///   are also what keeps a frame from being overwritten while it is drawn.
public sealed class VideoPlayer : IDisposable
{
    /// Frames prepared ahead of the one on screen. Each is at most the canvas size (3.7 MB at 1920×480); three cover a
    /// hiccup of a few frames in the decoder without holding much memory.
    internal const int FramesAhead = 3;

    /// How far behind its clock the player may fall before it carries on from where it is, as the render loop does,
    /// rather than racing through frames to catch up (after the PC slept, or the decoder or the render thread stalled).
    static readonly TimeSpan MaxLag = TimeSpan.FromSeconds(1);

    /// How long a frame shows when the clip doesn't say, so each pass of a loop still moves time on.
    internal static readonly TimeSpan FallbackDuration = TimeSpan.FromSeconds(1.0 / 30);

    /// A prepared frame and when it shows, in clip time: time since the clip first started, across loops.
    sealed record Frame(SKImage Image, TimeSpan Start, TimeSpan End);

    /// A decoded frame not yet scaled, with its times in clip time. The pixels are the decoder's, valid until its next frame.
    readonly record struct Decoded(SKPixmap Pixels, TimeSpan Start, TimeSpan End);

    readonly IVideoDecoder _decoder;
    readonly FrameScaler _scaler;
    readonly FrameBuffers _buffers;
    readonly Thread _thread;

    // Shared between the threads, under _gate.
    readonly object _gate = new();
    readonly Queue<Frame> _ready = new();
    bool _disposed;
    TimeSpan? _clipNow; // the clip time of the render thread's latest FrameAt, for the decode thread's dropping
    bool _decodeEnded; // the decode thread is past its last use of the decoder
    bool _decoderLeftToThread; // Dispose stopped waiting, so the decode thread disposes the decoder when it ends

    // The render thread's (and Dispose's, once the decode thread has stopped).
    Frame _showing;
    TimeSpan? _clockAtStart; // the render clock's reading when the clip started

    // The decode thread's (and the constructor's, before the thread starts).
    TimeSpan _passStart; // clip time at which the current pass of the loop started
    TimeSpan _passEnd; // clip time at which the current pass ends, as far as decoded
    TimeSpan _passOrigin; // the decoder's time stamp for the first frame of the current pass
    int _passFrames;

    string? _error; // set once by the decode thread; taken once by the render thread

    /// Decodes the first frame now, so a clip that can't produce one fails here and FrameAt always has a frame to show,
    /// then starts decoding ahead. The player owns the decoder once constructed; if the constructor throws, the caller
    /// still does.
    public VideoPlayer(IVideoDecoder decoder, FrameScaler scaler)
    {
        _decoder = decoder;
        _scaler = scaler;
        _buffers = new FrameBuffers(scaler);
        try
        {
            var first = Decode() ?? throw new VideoDecodeException("The clip has no frames.");
            _showing = Prepare(first);
        }
        catch
        {
            _buffers.Dispose();
            throw;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "Marquee video decoder" };
        _thread.Start();
    }

    /// Where on the canvas the frames go; each image FrameAt returns is exactly this size.
    public SKRectI Destination => _scaler.Destination;

    /// How long Dispose waits for the decode thread to finish the frame it is on. Decoding a frame takes milliseconds;
    /// one that takes longer is stuck (see Dispose). A setting only for tests.
    internal TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// Frames prepared and waiting, for tests.
    internal int Queued
    {
        get
        {
            lock (_gate) return _ready.Count;
        }
    }

    /// Frame buffers allocated so far, for tests.
    internal int BuffersCreated => _buffers.Created;

    /// The frame to draw at `time` on the render clock, or null once disposed. The clip starts at the first call,
    /// whatever the clock reads then: the render loop's clock runs from when the loop started, not from when this
    /// theme arrived. Never blocks on decoding. Render thread only; see the class comment for how long the image lives.
    public SKImage? FrameAt(TimeSpan time)
    {
        lock (_gate)
        {
            if (_disposed) return null;
            _clockAtStart ??= time;
            var clipTime = time - _clockAtStart.Value;

            var advanced = false;
            while (_ready.TryPeek(out var next) && next.Start <= clipTime)
            {
                _showing.Image.Dispose();
                _showing = _ready.Dequeue();
                advanced = true;
            }

            // Behind only once nothing is left to show: a frame still queued starts after clipTime, so the player is
            // on time (a clip with a gap in its timestamps waits out the gap rather than restarting it every second).
            if (_ready.Count == 0 && clipTime - _showing.End > MaxLag)
            {
                _clockAtStart = time - _showing.End;
                clipTime = _showing.End;
            }

            _clipNow = clipTime;
            if (advanced) Monitor.PulseAll(_gate); // room in the queue for the decode thread
            return _showing.Image;
        }
    }

    /// Why the clip stopped playing partway (a damaged frame later in the file, say), the first time it is asked after
    /// that happened; otherwise null. The last good frame stays on screen. The render loop logs it once.
    public string? TakeError() => Interlocked.Exchange(ref _error, null);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Monitor.PulseAll(_gate);
        }

        // At most the frame being decoded is still to finish, and after it the thread touches nothing else. A thread
        // stuck in a decoder call (a clip on a network share that went away, a codec that hangs) mustn't hold up the
        // caller, the render thread, and every theme switch and stop with it: after StopTimeout it is left to finish on
        // its own, and disposes the decoder itself if the call ever returns. If it never does, the decoder and the
        // thread stay until the process exits; it is a background thread, so it doesn't keep the process alive.
        var ended = _thread.Join(StopTimeout);
        lock (_gate)
        {
            if (!ended && !_decodeEnded) _decoderLeftToThread = true;
            foreach (var frame in _ready) frame.Image.Dispose();
            _ready.Clear();
        }

        if (!_decoderLeftToThread) _decoder.Dispose();
        _showing.Image.Dispose();
        _buffers.Dispose();
    }

    /// The decode thread: keeps FramesAhead frames prepared until the player is disposed, the clip turns out to be a
    /// single still frame, or decoding fails.
    void Run()
    {
        try
        {
            while (true)
            {
                lock (_gate)
                {
                    while (!_disposed && _ready.Count >= FramesAhead) Monitor.Wait(_gate);
                    if (_disposed) return;
                }

                // Outside the lock: decoding and scaling take milliseconds, and FrameAt must not wait for them.
                if (Decode() is not { } decoded) return; // a still: the frame on screen stays
                lock (_gate)
                {
                    if (_disposed) return;
                    // Already over by the render thread's latest time, so it would never be seen. With nothing queued,
                    // though, it is kept: it is newer than the frame on screen, so a decoder too slow for the clip
                    // still shows every frame it manages, a little behind, rather than none.
                    if (_ready.Count > 0 && decoded.End <= _clipNow) continue;
                }

                var frame = Prepare(decoded);
                lock (_gate)
                {
                    if (_disposed)
                    {
                        frame.Image.Dispose(); // never queued, so never seen by the render thread
                        return;
                    }

                    _ready.Enqueue(frame);
                }
            }
        }
        catch (Exception ex)
        {
            // An unhandled exception on this thread would end the process. Playback stops on the last good frame,
            // which is the right thing to show for a clip damaged partway, and the reason is reported once.
            Interlocked.Exchange(ref _error, ex.Message);
        }
        finally
        {
            bool dispose;
            lock (_gate)
            {
                _decodeEnded = true;
                dispose = _decoderLeftToThread;
            }

            if (dispose) _decoder.Dispose();
        }
    }

    /// The next frame in clip time, rewinding at the end of the clip; null for a clip with no frames, or at the end of
    /// a clip of one frame, which has nothing more to show.
    Decoded? Decode()
    {
        var decoded = _decoder.Next();
        if (decoded is null)
        {
            if (_passFrames <= 1) return null;
            _passStart = _passEnd;
            _passFrames = 0;
            _decoder.Rewind();
            decoded = _decoder.Next();
            if (decoded is null) return null;
        }

        var (pixels, start, duration) = decoded.Value;
        if (duration <= TimeSpan.Zero) duration = FallbackDuration;
        // Each pass is timed from its own first frame. A clip's first time stamp isn't always zero (H.264 with B-frames
        // and no edit list, a clip cut from a recording); timed from zero, its first frame would show that much late,
        // and its last frame would be held that much longer at every loop.
        if (_passFrames == 0) _passOrigin = start;
        var frameStart = _passStart + (start - _passOrigin);
        var frameEnd = frameStart + duration;
        if (frameEnd > _passEnd) _passEnd = frameEnd;
        _passFrames++;
        return new Decoded(pixels, frameStart, frameEnd);
    }

    Frame Prepare(Decoded decoded) => new(_buffers.Scale(decoded.Pixels), decoded.Start, decoded.End);
}
