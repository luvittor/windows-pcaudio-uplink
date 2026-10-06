using System.Text.Json;

namespace WindowsPcAudioUplink;

public sealed class CaptureSettings
{
    public double GainDb { get; set; } = 6.0;
    public string CaptureMode { get; set; } = CaptureModes.Device;
    public int DeviceIndex { get; set; } = -1;
    public int? ProcessId { get; set; }
    public string? ProcessName { get; set; }
    public int CaptureBufferMs { get; set; } = 100;
    public int SilenceAfterMs { get; set; } = 150;
    public int SilenceChunkMs { get; set; } = 100;
    public int StatusIntervalSeconds { get; set; } = 1;

    public static CaptureSettings LoadFromJson(string json)
    {
        return JsonSerializer.Deserialize<CaptureSettings>(json, AppSettings.JsonOptions) ?? new CaptureSettings();
    }

    public static CaptureSettings From(AppSettings settings)
    {
        return new CaptureSettings
        {
            GainDb = settings.GainDb,
            CaptureMode = settings.CaptureMode,
            DeviceIndex = settings.DeviceIndex,
            ProcessId = settings.ProcessId,
            ProcessName = settings.ProcessName,
            CaptureBufferMs = settings.CaptureBufferMs,
            SilenceAfterMs = settings.SilenceAfterMs,
            SilenceChunkMs = settings.SilenceChunkMs,
            StatusIntervalSeconds = settings.StatusIntervalSeconds
        };
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.GainDb = GainDb;
        settings.CaptureMode = CaptureModes.Normalize(CaptureMode);
        settings.DeviceIndex = DeviceIndex;
        settings.ProcessId = ProcessId;
        settings.ProcessName = ProcessName;
        settings.CaptureBufferMs = CaptureBufferMs;
        settings.SilenceAfterMs = SilenceAfterMs;
        settings.SilenceChunkMs = SilenceChunkMs;
        settings.StatusIntervalSeconds = StatusIntervalSeconds;
    }
}
