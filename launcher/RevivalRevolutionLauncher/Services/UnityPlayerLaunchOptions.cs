namespace RevivalRevolution.Services;

public sealed record UnityPlayerLaunchOptions(string Fullscreen, string? Width, string? Height)
{
    private static readonly IReadOnlyDictionary<string, (string Width, string Height)> SupportedResolutions =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["1280 × 720"] = ("1280", "720"),
            ["1600 × 900"] = ("1600", "900"),
            ["1920 × 1080"] = ("1920", "1080"),
            ["2560 × 1440"] = ("2560", "1440"),
            ["3840 × 2160"] = ("3840", "2160")
        };

    public static UnityPlayerLaunchOptions FromSettings(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var fullscreen = settings.VideoFullscreen ? "1" : "0";
        if (settings.VideoResolution == "Padrão do jogo" ||
            !SupportedResolutions.TryGetValue(settings.VideoResolution, out var resolution))
            return new(fullscreen, null, null);

        return new(fullscreen, resolution.Width, resolution.Height);
    }
}
