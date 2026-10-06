using System.Text.Json;

namespace WindowsPcAudioUplink;

public sealed class UplinkSettings
{
    public string Host { get; set; } = "192.168.15.14";
    public int Port { get; set; } = 18080;
    public string? Bitrate { get; set; } = "128k";
    public string? FfmpegPath { get; set; }
    public string FfmpegLogLevel { get; set; } = "warning";
    public string AudioCodec { get; set; } = "libmp3lame";
    public string OutputFormat { get; set; } = "mp3";
    public int OutputSampleRate { get; set; } = 44100;
    public int OutputChannels { get; set; } = 2;
    public string? OutputSampleFormat { get; set; }
    public int FfmpegExitTimeoutMs { get; set; } = 3000;

    public static UplinkSettings LoadFromJson(string json)
    {
        return JsonSerializer.Deserialize<UplinkSettings>(json, AppSettings.JsonOptions) ?? new UplinkSettings();
    }

    public void ApplyTo(AppSettings settings)
    {
        settings.Host = Host;
        settings.Port = Port;
        settings.Bitrate = Bitrate;
        settings.FfmpegPath = FfmpegPath;
        settings.FfmpegLogLevel = FfmpegLogLevel;
        settings.AudioCodec = AudioCodec;
        settings.OutputFormat = OutputFormat;
        settings.OutputSampleRate = OutputSampleRate;
        settings.OutputChannels = OutputChannels;
        settings.OutputSampleFormat = OutputSampleFormat;
        settings.FfmpegExitTimeoutMs = FfmpegExitTimeoutMs;
    }
}
