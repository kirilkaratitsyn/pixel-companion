using Windows.Media;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PixelCompanion;

// Explicit opt-in integration fixture. Never used by the normal application.
internal static class MediaFixture
{
    public static void Run(string directory)
    {
        // WinRT activation may marshal to the main STA; start only after its message pump exists.
        EventHandler? start = null;
        start = (_, _) => { Application.Idle -= start; Task.Run(async () => { try { await Create(directory); } catch (Exception e) { File.WriteAllText(Path.Combine(directory, "error.log"), e.ToString()); } }); };
        Application.Idle += start;
        Application.Run(new ApplicationContext());
    }
    private static async Task Create(string directory)
    {
        File.AppendAllText(Path.Combine(directory, "startup.log"), "Creating MediaPlayer\n");
        using var player = new MediaPlayer();
        player.CommandManager.IsEnabled = false;
        var smtc = player.SystemMediaTransportControls;
        string imagePath = Path.Combine(directory, "fixture-cover.png");
        using (var image = new Bitmap(64, 64)) { using var g = Graphics.FromImage(image); g.Clear(Color.DarkSlateGray); image.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png); }
        smtc.DisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromFile(await StorageFile.GetFileFromPathAsync(Path.GetFullPath(imagePath)));
        smtc.IsEnabled = true; smtc.IsPlayEnabled = true; smtc.IsPauseEnabled = true; smtc.IsNextEnabled = true; smtc.IsPreviousEnabled = true;
        int index = 1;
        void Publish()
        {
            smtc.DisplayUpdater.Type = MediaPlaybackType.Music;
            smtc.DisplayUpdater.MusicProperties.Title = "Pixel integration track " + index;
            smtc.DisplayUpdater.MusicProperties.Artist = "Local test fixture";
            smtc.DisplayUpdater.Update();
            smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties { StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromMinutes(4), MinSeekTime = TimeSpan.Zero, MaxSeekTime = TimeSpan.FromMinutes(4), Position = TimeSpan.FromSeconds(30) });
        }
        smtc.ButtonPressed += (_, e) =>
        {
            if (e.Button == SystemMediaTransportControlsButton.Play) smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
            if (e.Button == SystemMediaTransportControlsButton.Pause) smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
            if (e.Button == SystemMediaTransportControlsButton.Next) { index++; Publish(); }
            if (e.Button == SystemMediaTransportControlsButton.Previous) { index--; Publish(); }
            File.WriteAllText(Path.Combine(directory, "fixture-command.txt"), e.Button.ToString());
            File.WriteAllText(Path.Combine(directory, "fixture-status.txt"), smtc.PlaybackStatus.ToString());
        };
        smtc.PlaybackPositionChangeRequested += (_, e) =>
        {
            smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties { StartTime = TimeSpan.Zero, EndTime = TimeSpan.FromMinutes(4), MinSeekTime = TimeSpan.Zero, MaxSeekTime = TimeSpan.FromMinutes(4), Position = e.RequestedPlaybackPosition });
            File.WriteAllText(Path.Combine(directory, "fixture-seek.txt"), e.RequestedPlaybackPosition.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        };
        Publish(); smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
        File.WriteAllText(Path.Combine(directory, "fixture-ready.txt"), "ready");
        await Task.Delay(Timeout.Infinite);
    }
}
