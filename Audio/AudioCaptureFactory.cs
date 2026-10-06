using NAudio.CoreAudioApi;

namespace WindowsPcAudioUplink.Audio;

public static class AudioCaptureFactory
{
    public static async Task<IAudioCaptureSource> CreateAsync(AppSettings settings)
    {
        return CaptureModes.Normalize(settings.CaptureMode) switch
        {
            CaptureModes.Device => CreateDeviceCapture(settings),
            CaptureModes.Process => await ProcessLoopbackCaptureSource.CreateAsync(settings),
            _ => throw new InvalidOperationException($"Modo de captura invalido: {settings.CaptureMode}")
        };
    }

    public static void PrintDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
        for (var i = 0; i < devices.Count; i++)
        {
            Console.WriteLine($"{i}: {devices[i].FriendlyName}");
        }
    }

    static IAudioCaptureSource CreateDeviceCapture(AppSettings settings)
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
        var device = settings.DeviceIndex >= 0 && settings.DeviceIndex < devices.Count
            ? devices[settings.DeviceIndex]
            : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        return new DeviceLoopbackCaptureSource(device);
    }
}
