using System.Text.Json;

namespace PixelCompanion;

public record Capabilities(bool Play, bool Pause, bool Toggle, bool Previous, bool Next, bool Seek);
public record SourceInfo(string Id, string Name);
public record MixerApp(string Id, string Name, float Volume, bool Muted, bool Active);
public record ShortcutInfo(string Id, string Name, string Kind);
public record BrowserTrack(string Id, string Title, string Artist, bool Active);
public record BrowserCommand(long Sequence, string Name, string TrackId);
public record BrowserStateRequest(long AfterSequence, BrowserTrack[]? Tracks);
public record ArtworkPayload(byte[] Bytes, string ContentType, string Hash);
public record MediaState(string? SessionId, long Revision, string Source, string Title, string Artist,
    string Status, double Position, double Duration, double Rate, string? ArtworkHash, Capabilities Controls);
public record StateMessage(string Type, int Version, string Computer, bool Locked, long Timestamp,
    MediaState Media, SourceInfo[] Sources, string? SelectedSource, float? Volume, MixerApp[] Mixer, ShortcutInfo[] Shortcuts, BrowserTrack[] Queue, string? Error);
public record ClientCommand(string Id, string Name, string? SessionId, long Revision, double? Value, string? SourceId, string? MixerId, bool? Muted, string? ShortcutId, string? BrowserTrackId);
public static class Wire
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
}
