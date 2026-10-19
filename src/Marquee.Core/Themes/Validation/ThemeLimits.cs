namespace Marquee.Core.Themes.Validation;

/// How large a theme may be. Themes are shared, so these bound what installing or validating someone else's theme can
/// cost: per file (a clip is already capped at this size when it loads), in total, and in files. A zip also may not
/// expand any entry more than MaxCompressionRatio times, the mark of a zip bomb. Tests use smaller limits.
sealed record ThemeLimits(long MaxFileBytes, long MaxTotalBytes, int MaxFiles, int MaxCompressionRatio)
{
    public static ThemeLimits Default { get; } = new(
        MaxFileBytes: VideoAssets.MaxClipBytes,
        MaxTotalBytes: 512L * 1024 * 1024,
        MaxFiles: 1000,
        MaxCompressionRatio: 100);
}
