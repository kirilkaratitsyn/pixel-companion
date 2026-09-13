namespace PixelCompanion;

public sealed class BrowserController
{
    private readonly object sync = new();
    private BrowserTrack[] tracks = [];
    private DateTimeOffset updated = DateTimeOffset.MinValue;
    private BrowserCommand? command;
    private DateTimeOffset commandAt = DateTimeOffset.MinValue;
    private long sequence;

    public BrowserTrack[] Tracks
    {
        get { lock (sync) return DateTimeOffset.UtcNow - updated < TimeSpan.FromSeconds(8) ? tracks : []; }
    }

    public BrowserCommand? Update(BrowserTrack[]? incoming, long afterSequence)
    {
        lock (sync)
        {
            tracks = (incoming ?? []).Where(Valid).GroupBy(item => item.Id, StringComparer.Ordinal).Select(group => group.First()).Take(60).ToArray();
            updated = DateTimeOffset.UtcNow;
            return command is { } pending && pending.Sequence > afterSequence && DateTimeOffset.UtcNow - commandAt < TimeSpan.FromSeconds(10) ? pending : null;
        }
    }

    public bool Play(string? id)
    {
        lock (sync)
        {
            if (DateTimeOffset.UtcNow - updated >= TimeSpan.FromSeconds(8) || string.IsNullOrEmpty(id) || !tracks.Any(item => item.Id == id)) return false;
            command = new(++sequence, "play-track", id); commandAt = DateTimeOffset.UtcNow; return true;
        }
    }

    private static bool Valid(BrowserTrack item) => item.Id is { Length: > 0 and <= 80 } && item.Title is { Length: > 0 and <= 300 } && item.Artist is { Length: <= 300 };
}
