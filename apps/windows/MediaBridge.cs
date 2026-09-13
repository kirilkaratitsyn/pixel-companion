using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Microsoft.Win32;

namespace PixelCompanion;

public sealed class MediaBridge : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? session;
    private readonly SemaphoreSlim gate = new(1, 1);
    private int sourceDirty = 1, metadataDirty = 1;
    private bool locked;
    private string? selected, sessionId, artHash;
    private string title = "", artist = "";
    private long revision;
    private byte[]? art;
    private DateTimeOffset lastMetadata = DateTimeOffset.MinValue;
    private DateTimeOffset lastMixer = DateTimeOffset.MinValue;
    private MixerApp[] mixer = [];
    private StateMessage latest = Empty(null);
    public StateMessage Latest => Volatile.Read(ref latest);
    public byte[]? Artwork => Volatile.Read(ref art);
    private static readonly Capabilities None = new(false, false, false, false, false, false);
    private static StateMessage Empty(string? error) => new("state", 1, Environment.MachineName, false, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        new(null, 0, "", "", "", "none", 0, 0, 1, null, None), [], null, null, [], ShortcutLauncher.Items, error);

    private MixerApp[] Mixer()
    {
        if (DateTimeOffset.UtcNow - lastMixer > TimeSpan.FromSeconds(1))
        {
            mixer = CoreAudio.GetMixer();
            lastMixer = DateTimeOffset.UtcNow;
        }
        return mixer;
    }

    public async Task Initialize()
    {
        manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        manager.CurrentSessionChanged += (_, _) => Interlocked.Exchange(ref sourceDirty, 1);
        manager.SessionsChanged += (_, _) => Interlocked.Exchange(ref sourceDirty, 1);
        SystemEvents.SessionSwitch += SessionSwitch;
        await Refresh();
    }
    private void SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock) locked = true;
        if (e.Reason == SessionSwitchReason.SessionUnlock) locked = false;
    }
    private void MetadataChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => Interlocked.Exchange(ref metadataDirty, 1);
    private void PlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => _ = Task.Run(Refresh);
    private void TimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => _ = Task.Run(Refresh);
    private void Detach()
    {
        if (session == null) return;
        session.MediaPropertiesChanged -= MetadataChanged;
        session.PlaybackInfoChanged -= PlaybackChanged;
        session.TimelinePropertiesChanged -= TimelineChanged;
    }
    private static string DisplayName(string id)
    {
        if (id.Contains("spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (id.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (id.Contains("msedge", StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (id.Contains("applemusic", StringComparison.OrdinalIgnoreCase)) return "Apple Music";
        return id.Split('!')[0].Split('_')[0];
    }
    public async Task Refresh()
    {
        await gate.WaitAsync();
        try { await RefreshCore(); }
        catch (Exception e) { Volatile.Write(ref latest, Empty("Медиасессия временно недоступна: " + e.GetType().Name) with { Locked = locked }); Interlocked.Exchange(ref sourceDirty, 1); }
        finally { gate.Release(); }
    }
    private async Task RefreshCore()
    {
        if (manager == null) return;
        var sessions = manager.GetSessions();
        var sources = sessions.Select(s => s.SourceAppUserModelId).Distinct().Select(id => new SourceInfo(id, DisplayName(id))).ToArray();
        if (Interlocked.Exchange(ref sourceDirty, 0) != 0)
        {
            Detach();
            session = selected == null ? manager.GetCurrentSession() : sessions.FirstOrDefault(s => s.SourceAppUserModelId == selected);
            sessionId = session == null ? null : Guid.NewGuid().ToString("N");
            title = artist = ""; artHash = null; Volatile.Write(ref art, null);
            revision++; Interlocked.Exchange(ref metadataDirty, 1);
            if (session != null) { session.MediaPropertiesChanged += MetadataChanged; session.PlaybackInfoChanged += PlaybackChanged; session.TimelinePropertiesChanged += TimelineChanged; }
        }
        if (session == null)
        {
            Volatile.Write(ref latest, Empty(null) with { Locked = locked, Sources = sources, SelectedSource = selected, Volume = CoreAudio.Get(), Mixer = Mixer() });
            return;
        }
        if (Interlocked.Exchange(ref metadataDirty, 0) != 0 || DateTimeOffset.UtcNow - lastMetadata > TimeSpan.FromSeconds(15))
        {
            var props = await session.TryGetMediaPropertiesAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            var newTitle = props.Title ?? ""; var newArtist = props.Artist ?? "";
            var newArt = await ReadArtwork(props.Thumbnail);
            var newHash = newArt == null ? null : Convert.ToHexString(SHA256.HashData(newArt));
            if (newTitle != title || newArtist != artist || newHash != artHash) revision++;
            title = newTitle; artist = newArtist; artHash = newHash; Volatile.Write(ref art, newArt);
            lastMetadata = DateTimeOffset.UtcNow;
        }
        var playback = session.GetPlaybackInfo(); var timeline = session.GetTimelineProperties(); var c = playback.Controls;
        double duration = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
        var status = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? "playing" : "paused";
        double rate = playback.PlaybackRate ?? 1;
        double position = (timeline.Position - timeline.StartTime).TotalSeconds;
        if (status == "playing" && timeline.LastUpdatedTime > DateTimeOffset.MinValue)
            position += Math.Max(0, (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalSeconds) * rate;
        position = duration > 0 ? Math.Clamp(position, 0, duration) : Math.Max(0, position);
        var media = new MediaState(sessionId, revision, DisplayName(session.SourceAppUserModelId), title, artist, status, position, duration, rate, artHash,
            new(c.IsPlayEnabled, c.IsPauseEnabled, c.IsPlayPauseToggleEnabled, c.IsPreviousEnabled, c.IsNextEnabled, c.IsPlaybackPositionEnabled && duration > 0));
        Volatile.Write(ref latest, new("state", 1, Environment.MachineName, locked, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), media, sources, selected, CoreAudio.Get(), Mixer(), ShortcutLauncher.Items, null));
    }
    private static async Task<byte[]?> ReadArtwork(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail == null) return null;
        try
        {
            using var stream = await thumbnail.OpenReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            if (stream.Size == 0 || stream.Size > 8 * 1024 * 1024) return null;
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            byte[] bytes = new byte[(int)stream.Size]; reader.ReadBytes(bytes);
            using var input = new MemoryStream(bytes); using var original = Image.FromStream(input);
            double scale = 1024d / Math.Max(original.Width, original.Height);
            int width = Math.Max(1, (int)Math.Round(original.Width * scale));
            int height = Math.Max(1, (int)Math.Round(original.Height * scale));
            using var resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(resized))
            {
                g.Clear(Color.Black); g.CompositingQuality = CompositingQuality.HighQuality; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.DrawImage(original, 0, 0, width, height);
            }
            if (scale > 1.01) Sharpen(resized);
            using var output = new MemoryStream();
            var codec = ImageCodecInfo.GetImageEncoders().First(item => item.FormatID == ImageFormat.Jpeg.Guid);
            using var quality = new EncoderParameters(1); quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
            resized.Save(output, codec, quality); return output.ToArray();
        }
        catch { return null; } // Artwork is optional; it must never disable transport controls.
    }
    private static void Sharpen(Bitmap bitmap)
    {
        var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            if (data.Stride <= 0) return;
            int size = data.Stride * data.Height;
            var source = new byte[size]; var output = new byte[size];
            Marshal.Copy(data.Scan0, source, 0, size); System.Buffer.BlockCopy(source, 0, output, 0, size);
            const double amount = .42;
            for (int y = 1; y < data.Height - 1; y++)
            {
                for (int x = 1; x < data.Width - 1; x++)
                {
                    int pixel = y * data.Stride + x * 3;
                    for (int channel = 0; channel < 3; channel++)
                    {
                        int index = pixel + channel;
                        double average = (source[index - 3] + source[index + 3] + source[index - data.Stride] + source[index + data.Stride]) / 4d;
                        output[index] = (byte)Math.Clamp(Math.Round(source[index] + amount * (source[index] - average)), 0, 255);
                    }
                }
            }
            Marshal.Copy(output, 0, data.Scan0, size);
        }
        finally { bitmap.UnlockBits(data); }
    }
    public async Task<(bool Ok, string? Error)> Command(ClientCommand command)
    {
        await gate.WaitAsync();
        try
        {
            if (command.Name == "launch") return ShortcutLauncher.Launch(command.ShortcutId) ? (true, null) : (false, "Не удалось открыть приложение");
            if (command.Name == "volume") return command.Value is { } v && double.IsFinite(v) && v >= 0 && v <= 1 && CoreAudio.Set((float)v) ? (true, null) : (false, "Громкость недоступна");
            if (command.Name == "mixer-volume")
            {
                bool ok = command.Value is { } appVolume && double.IsFinite(appVolume) && appVolume >= 0 && appVolume <= 1 && CoreAudio.SetMixer(command.MixerId, (float)appVolume, null);
                if (ok) lastMixer = DateTimeOffset.MinValue;
                return ok ? (true, null) : (false, "Громкость приложения недоступна");
            }
            if (command.Name == "mixer-mute")
            {
                bool ok = command.Muted is { } muted && CoreAudio.SetMixer(command.MixerId, null, muted);
                if (ok) lastMixer = DateTimeOffset.MinValue;
                return ok ? (true, null) : (false, "Звук приложения недоступен");
            }
            if (command.Name == "source")
            {
                if (command.SourceId != null && !Latest.Sources.Any(s => s.Id == command.SourceId)) return (false, "Источник больше не доступен");
                selected = command.SourceId; Interlocked.Exchange(ref sourceDirty, 1); await RefreshCore(); return (true, null);
            }
            // Resolve a pending session/track change BEFORE applying any delayed command.
            if (Volatile.Read(ref sourceDirty) != 0 || Volatile.Read(ref metadataDirty) != 0) await RefreshCore();
            if (session == null || command.SessionId != sessionId || command.Revision != revision) return (false, "Трек изменился. Повторите действие.");
            var controls = session.GetPlaybackInfo().Controls;
            bool result;
            switch (command.Name)
            {
                case "toggle" when controls.IsPlayPauseToggleEnabled:
                    result = await session.TryTogglePlayPauseAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)); break;
                case "play" when controls.IsPlayEnabled:
                    result = await session.TryPlayAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)); break;
                case "pause" when controls.IsPauseEnabled:
                    result = await session.TryPauseAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)); break;
                case "next" when controls.IsNextEnabled:
                    result = await session.TrySkipNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)); break;
                case "previous" when controls.IsPreviousEnabled:
                    result = await session.TrySkipPreviousAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)); break;
                case "seek" when controls.IsPlaybackPositionEnabled && command.Value is { } seconds && double.IsFinite(seconds):
                    var timeline = session.GetTimelineProperties(); var length = (timeline.EndTime - timeline.StartTime).TotalSeconds;
                    if (seconds < 0 || seconds > length || length <= 0) return (false, "Недопустимая позиция");
                    result = await session.TryChangePlaybackPositionAsync((timeline.StartTime + TimeSpan.FromSeconds(seconds)).Ticks).AsTask().WaitAsync(TimeSpan.FromSeconds(3)); break;
                default: return (false, "Команда не поддерживается этим плеером");
            }
            return result ? (true, null) : (false, "Плеер отклонил команду");
        }
        catch (Exception e) { return (false, e is TimeoutException ? "Плеер не ответил" : "Плеер временно недоступен"); }
        finally { gate.Release(); }
    }
    public void Dispose()
    {
        SystemEvents.SessionSwitch -= SessionSwitch;
        Detach();
    }
}
