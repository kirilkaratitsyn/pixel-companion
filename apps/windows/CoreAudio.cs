using System.Runtime.InteropServices;

namespace PixelCompanion;

// Endpoint volume is intentionally separate from GSMTC media commands.
public static class CoreAudio
{
    public static float? Get() => WithEndpoint(endpoint => { Marshal.ThrowExceptionForHR(endpoint.GetMasterVolumeLevelScalar(out float value)); return value; });
    public static bool Set(float value)
    {
        if (!float.IsFinite(value) || value < 0 || value > 1) return false;
        return WithEndpoint(endpoint => { var context = Guid.Empty; Marshal.ThrowExceptionForHR(endpoint.SetMasterVolumeLevelScalar(value, ref context)); return 1f; }) != null;
    }
    private static float? WithEndpoint(Func<IAudioEndpointVolume,float> action)
    {
        object? enumerator = null; IMMDevice? device = null; object? volume = null;
        try
        {
            enumerator = new MMDeviceEnumerator();
            Marshal.ThrowExceptionForHR(((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out device));
            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out volume));
            return action((IAudioEndpointVolume)volume);
        }
        catch (COMException) { return null; }
        finally
        {
            if (volume != null && Marshal.IsComObject(volume)) Marshal.ReleaseComObject(volume);
            if (device != null && Marshal.IsComObject(device)) Marshal.ReleaseComObject(device);
            if (enumerator != null && Marshal.IsComObject(enumerator)) Marshal.ReleaseComObject(enumerator);
        }
    }
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    { [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance); }
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
}
