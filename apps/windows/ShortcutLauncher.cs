using System.Diagnostics;

namespace PixelCompanion;

public static class ShortcutLauncher
{
    public static readonly ShortcutInfo[] Items =
    [
        new("discord", "Discord", "app"),
        new("youtube-music", "YouTube Music", "music"),
        new("spotify", "Spotify", "music")
    ];

    private static readonly IReadOnlyDictionary<string, string> Targets = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["discord"] = "discord://-/channels/@me",
        ["youtube-music"] = "https://music.youtube.com/",
        ["spotify"] = "spotify:"
    };

    public static bool Launch(string? id)
    {
        if (id == null || !Targets.TryGetValue(id, out string? target)) return false;
        try { return Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }) != null; }
        catch { return false; }
    }
}
