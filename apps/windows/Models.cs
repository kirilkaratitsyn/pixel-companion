using System.Text.Json;

namespace PixelCompanion;

public record Capabilities(bool Play, bool Pause, bool Toggle, bool Previous, bool Next, bool Seek);
public record SourceInfo(string Id, string Name);
public record MediaState(string? SessionId, long Revision, string Source, string Title, string Artist,
    string Status, double Position, double Duration, double Rate, string? ArtworkHash, Capabilities Controls);
public record StateMessage(string Type, int Version, string Computer, bool Locked, long Timestamp,
    MediaState Media, SourceInfo[] Sources, string? SelectedSource, float? Volume, string? Error);
public record ClientCommand(string Id, string Name, string? SessionId, long Revision, double? Value, string? SourceId);
public static class Wire
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static string Serialize(object value) => JsonSerializer.Serialize(value, Json);
}
