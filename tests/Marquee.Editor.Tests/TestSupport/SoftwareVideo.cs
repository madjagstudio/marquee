using System.Runtime.CompilerServices;
using Marquee.Core.Video;

namespace Marquee.Editor.Tests.TestSupport;

static class SoftwareVideo
{
    /// Clips opened through VideoDecoders decode in software in every test (see VideoDecoders.UseHardware). Tests of
    /// the hardware path ask MediaFoundationDecoder for it directly.
    [ModuleInitializer]
    internal static void Use() => VideoDecoders.UseHardware = false;
}
