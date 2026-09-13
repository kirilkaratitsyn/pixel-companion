using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PixelCompanion;

// Endpoint and per-application volume are intentionally separate from GSMTC media commands.
public static class CoreAudio
{
    private const int ClsCtxAll = 23;
    private static readonly Guid EndpointVolumeId = typeof(IAudioEndpointVolume).GUID;
    private static readonly Guid SessionManagerId = typeof(IAudioSessionManager2).GUID;

    public static float? Get()
    {
        float value = 0;
        bool ok = WithDefaultDevice(EndpointVolumeId, instance =>
        {
            var endpoint = (IAudioEndpointVolume)instance;
            return endpoint.GetMasterVolumeLevelScalar(out value) >= 0;
        });
        return ok ? value : null;
    }

    public static bool Set(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1) return false;
        return WithDefaultDevice(EndpointVolumeId, instance =>
        {
            var context = Guid.Empty;
            return ((IAudioEndpointVolume)instance).SetMasterVolumeLevelScalar(value, ref context) >= 0;
        });
    }

    public static MixerApp[] GetMixer()
    {
        var entries = new List<(string Id, string Name, float Volume, bool Muted, bool Active)>();
        WithDefaultDevice(SessionManagerId, instance => WithSessions((IAudioSessionManager2)instance, (control, volume) =>
        {
            if (control.GetState(out int state) < 0 || state == 2) return;
            var identity = Identity(control);
            if (identity == null || volume.GetMasterVolume(out float level) < 0 || volume.GetMute(out bool muted) < 0) return;
            entries.Add((identity.Value.Id, identity.Value.Name, Math.Clamp(level, 0, 1), muted, state == 1));
        }));

        return entries.GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MixerApp(group.Key, group.Select(x => x.Name).First(),
                (float)group.Average(x => x.Volume), group.All(x => x.Muted), group.Any(x => x.Active)))
            .OrderByDescending(app => app.Active).ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(32).ToArray();
    }

    public static bool SetMixer(string? id, float? level, bool? muted)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || level is { } value && (!float.IsFinite(value) || value < 0 || value > 1)) return false;
        bool matched = false, succeeded = true;
        bool enumerated = WithDefaultDevice(SessionManagerId, instance => WithSessions((IAudioSessionManager2)instance, (control, volume) =>
        {
            var identity = Identity(control);
            if (identity == null || !string.Equals(identity.Value.Id, id, StringComparison.OrdinalIgnoreCase)) return;
            matched = true; var context = Guid.Empty;
            if (level is { } target && volume.SetMasterVolume(target, ref context) < 0) succeeded = false;
            if (muted is { } mute && volume.SetMute(mute, ref context) < 0) succeeded = false;
        }));
        return enumerated && matched && succeeded;
    }

    private static (string Id, string Name)? Identity(IAudioSessionControl2 control)
    {
        try
        {
            if (control.IsSystemSoundsSession() == 0) return ("system-sounds", "Системные звуки");
            if (control.GetProcessId(out uint processId) < 0 || processId == 0 || processId > int.MaxValue) return null;
            using var process = Process.GetProcessById((int)processId);
            string raw = process.ProcessName;
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return (raw.ToLowerInvariant(), FriendlyName(raw));
        }
        catch { return null; }
    }

    private static string FriendlyName(string process) => process.ToLowerInvariant() switch
    {
        "chrome" => "Google Chrome",
        "msedge" => "Microsoft Edge",
        "firefox" => "Firefox",
        "spotify" => "Spotify",
        "applemusic" => "Apple Music",
        "vlc" => "VLC",
        "discord" => "Discord",
        "teams" or "ms-teams" => "Microsoft Teams",
        _ => char.ToUpperInvariant(process[0]) + process[1..]
    };

    private static bool WithDefaultDevice(Guid interfaceId, Func<object, bool> action)
    {
        object? enumerator = null; IMMDevice? device = null; object? instance = null;
        try
        {
            enumerator = new MMDeviceEnumerator();
            if (((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out device) < 0) return false;
            if (device.Activate(ref interfaceId, ClsCtxAll, IntPtr.Zero, out instance) < 0) return false;
            return action(instance);
        }
        catch (COMException) { return false; }
        finally { Release(instance); Release(device); Release(enumerator); }
    }

    private static bool WithSessions(IAudioSessionManager2 manager, Action<IAudioSessionControl2, ISimpleAudioVolume> action)
    {
        IAudioSessionEnumerator? sessions = null;
        try
        {
            if (manager.GetSessionEnumerator(out sessions) < 0 || sessions.GetCount(out int count) < 0) return false;
            for (int index = 0; index < count; index++)
            {
                IAudioSessionControl? session = null;
                try
                {
                    if (sessions.GetSession(index, out session) < 0) continue;
                    if (session is IAudioSessionControl2 control && session is ISimpleAudioVolume volume) action(control, volume);
                }
                catch (COMException) { }
                finally { Release(session); }
            }
            return true;
        }
        catch (COMException) { return false; }
        finally { Release(sessions); }
    }

    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, uint flags, out IAudioSessionControl control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, uint flags, out ISimpleAudioVolume volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
        [PreserveSig] int RegisterSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterSessionNotification(IntPtr notification);
        [PreserveSig] int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionId, IntPtr notification);
        [PreserveSig] int UnregisterDuckNotification(IntPtr notification);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl session);
    }

    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid grouping);
        [PreserveSig] int SetGroupingParam(ref Guid grouping, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notification);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint processId);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
