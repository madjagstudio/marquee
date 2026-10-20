using Marquee.Core.Tests.TestSupport;
using Marquee.MakeClip;

// `dotnet run --project tools/Marquee.MakeClip -- <clip.mp4>`: draws example-showcase's radar (RadarClip) and encodes it
// as H.264 with ClipWriter, the writer the video tests use. The showcase plays themes/example-showcase/video/radar.mp4.
// Windows' encoder can change between versions, so a regenerated clip may differ in its bytes, but not in what it shows;
// the showcase's snapshots show its first frame.
if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/Marquee.MakeClip -- <clip.mp4>");
    return 2;
}

var path = Path.GetFullPath(args[0]);
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
// Written beside it and moved over it, so a failed run leaves the old clip as it was.
var partial = path + ".partial.mp4";
try
{
    ClipWriter.Write(partial, RadarClip.Width, RadarClip.Height, RadarClip.Frames, RadarClip.Draw, RadarClip.Fps,
        RadarClip.Bitrate);
}
catch (InvalidOperationException e)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

File.Move(partial, path, overwrite: true);
Console.WriteLine($"Wrote {path}: {RadarClip.Width}×{RadarClip.Height}, {RadarClip.Frames} frames at {RadarClip.Fps} fps, " +
    $"{new FileInfo(path).Length / 1024} KB.");
return 0;
