namespace WindowsPcAudioUplink;

public static class ResolvedConfigPrinter
{
    public static void Print(AppSettings settings)
    {
        Console.WriteLine("windows-pcaudio-uplink config");
        Console.WriteLine($"defaults: {settings.DefaultsConfigPath ?? "(none)"}");
        Console.WriteLine($"uplink: {settings.UplinkConfigPath ?? "(none)"}");
        Console.WriteLine($"capture: {settings.CaptureConfigPath ?? "(none)"}");
        Console.WriteLine($"target: tcp://{settings.Host}:{settings.Port} {settings.OutputFormat} {settings.AudioCodec} {settings.OutputSampleRate} Hz {settings.OutputChannels} ch{(string.IsNullOrWhiteSpace(settings.OutputSampleFormat) ? "" : $" {settings.OutputSampleFormat}")}{(string.IsNullOrWhiteSpace(settings.Bitrate) ? "" : $" {settings.Bitrate}")}");
        Console.WriteLine($"captureMode: {settings.CaptureMode}");
        Console.WriteLine($"deviceIndex: {settings.DeviceIndex}");
        Console.WriteLine($"processName: {settings.ProcessName ?? "(none)"}");
        Console.WriteLine($"processId: {(settings.ProcessId.HasValue ? settings.ProcessId.Value.ToString() : "(none)")}");
        Console.WriteLine($"gainDb: {settings.GainDb:+0.0;-0.0;0.0}");
    }
}
