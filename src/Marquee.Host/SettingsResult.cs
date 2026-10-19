using System.Diagnostics.CodeAnalysis;

namespace Marquee.Host;

/// SettingsResolver's answer: the settings, or a message for the user saying why there are none.
public sealed record SettingsResult
{
    SettingsResult(EngineSettings? settings, string? error)
    {
        Settings = settings;
        Error = error;
    }

    public EngineSettings? Settings { get; }

    public string? Error { get; }

    [MemberNotNullWhen(true, nameof(Settings))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool Succeeded => Settings is not null;

    public static SettingsResult Success(EngineSettings settings) => new(settings, null);

    public static SettingsResult Failure(string error) => new(null, error);
}
