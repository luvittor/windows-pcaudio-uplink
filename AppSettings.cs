using System.Globalization;
using System.Text.Json;

namespace WindowsPcAudioUplink;

public sealed class AppSettings
{
    public string Host { get; set; } = "192.168.15.14";
    public int Port { get; set; } = 18080;
    public string? Bitrate { get; set; } = "128k";
    public double GainDb { get; set; } = 6.0;
    public string CaptureMode { get; set; } = CaptureModes.Device;
    public int DeviceIndex { get; set; } = -1;
    public int? ProcessId { get; set; }
    public string? ProcessName { get; set; }
    public bool ListProcesses { get; set; } = false;
    public int CaptureBufferMs { get; set; } = 100;
    public int DurationSeconds { get; set; } = 0;
    public bool ListDevices { get; set; } = false;
    public bool RunTests { get; set; } = false;
    public string? FfmpegPath { get; set; }
    public string FfmpegLogLevel { get; set; } = "warning";
    public string AudioCodec { get; set; } = "libmp3lame";
    public string OutputFormat { get; set; } = "mp3";
    public int OutputSampleRate { get; set; } = 44100;
    public int OutputChannels { get; set; } = 2;
    public string? OutputSampleFormat { get; set; }
    public int SilenceAfterMs { get; set; } = 150;
    public int SilenceChunkMs { get; set; } = 100;
    public int StatusIntervalSeconds { get; set; } = 1;
    public int FfmpegExitTimeoutMs { get; set; } = 3000;
    public string? ConfigPath { get; set; }

    public static AppSettings Load(string[] args)
    {
        var settings = LoadFile(GetConfigPath(args));
        ApplyEnvironment(settings);
        ApplyArgs(settings, args);
        settings.CaptureMode = CaptureModes.Normalize(settings.CaptureMode);
        return settings;
    }

    internal static AppSettings LoadFromJson(string json)
    {
        return JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppSettings();
    }

    static AppSettings LoadFile(string? configuredPath)
    {
        var path = configuredPath;
        if (!string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path))
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), path);
        }

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            var settings = LoadFromJson(File.ReadAllText(path));
            settings.ConfigPath = path;
            return settings;
        }

        path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        }

        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        var defaultSettings = LoadFromJson(File.ReadAllText(path));
        defaultSettings.ConfigPath = path;
        return defaultSettings;
    }

    static void ApplyEnvironment(AppSettings settings)
    {
        SetString(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_HOST"), value => settings.Host = value);
        SetInt(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_PORT"), value => settings.Port = value);
        SetString(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_BITRATE"), value => settings.Bitrate = value);
        SetDouble(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_GAIN_DB"), value => settings.GainDb = value);
        SetString(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_CAPTURE_MODE"), value => settings.CaptureMode = value);
        SetInt(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_DEVICE_INDEX"), value => settings.DeviceIndex = value);
        SetNullableInt(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_PROCESS_ID"), value => settings.ProcessId = value);
        SetString(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_PROCESS_NAME"), value => settings.ProcessName = value);
        SetBool(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_LIST_PROCESSES"), value => settings.ListProcesses = value);
        SetInt(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_CAPTURE_BUFFER_MS"), value => settings.CaptureBufferMs = value);
        SetInt(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_DURATION_SECONDS"), value => settings.DurationSeconds = value);
        SetBool(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_LIST_DEVICES"), value => settings.ListDevices = value);
        SetString(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_FFMPEG_PATH"), value => settings.FfmpegPath = value);
        SetString(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_FFMPEG_LOG_LEVEL"), value => settings.FfmpegLogLevel = value);
        SetString(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_AUDIO_CODEC"), value => settings.AudioCodec = value);
        SetString(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_OUTPUT_FORMAT"), value => settings.OutputFormat = value);
        SetInt(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_OUTPUT_SAMPLE_RATE"), value => settings.OutputSampleRate = value);
        SetInt(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_OUTPUT_CHANNELS"), value => settings.OutputChannels = value);
        SetString(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_OUTPUT_SAMPLE_FORMAT"), value => settings.OutputSampleFormat = value);
        SetInt(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_SILENCE_AFTER_MS"), value => settings.SilenceAfterMs = value);
        SetInt(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_SILENCE_CHUNK_MS"), value => settings.SilenceChunkMs = value);
        SetInt(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_STATUS_INTERVAL_SECONDS"), value => settings.StatusIntervalSeconds = value);
        SetInt(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_FFMPEG_EXIT_TIMEOUT_MS"), value => settings.FfmpegExitTimeoutMs = value);
    }

    static void ApplyArgs(AppSettings settings, string[] args)
    {
        SetString(GetArgValue(args, "--host"), value => settings.Host = value);
        SetInt(GetArgValue(args, "--port"), value => settings.Port = value);
        SetString(GetArgValue(args, "--bitrate"), value => settings.Bitrate = value);
        SetDouble(GetArgValue(args, "--gain-db"), value => settings.GainDb = value);
        SetString(GetArgValue(args, "--capture-mode"), value => settings.CaptureMode = value);
        SetInt(GetArgValue(args, "--device-index"), value => settings.DeviceIndex = value);
        SetNullableInt(GetArgValue(args, "--process-id"), value => settings.ProcessId = value);
        SetString(GetArgValue(args, "--process-name"), value => settings.ProcessName = value);
        SetInt(GetArgValue(args, "--capture-buffer-ms"), value => settings.CaptureBufferMs = value);
        SetInt(GetArgValue(args, "--duration"), value => settings.DurationSeconds = value);
        SetString(GetArgValue(args, "--ffmpeg-path"), value => settings.FfmpegPath = value);
        SetString(GetArgValue(args, "--ffmpeg-log-level"), value => settings.FfmpegLogLevel = value);
        SetString(GetArgValue(args, "--audio-codec"), value => settings.AudioCodec = value);
        SetString(GetArgValue(args, "--output-format"), value => settings.OutputFormat = value);
        SetInt(GetArgValue(args, "--output-sample-rate"), value => settings.OutputSampleRate = value);
        SetInt(GetArgValue(args, "--output-channels"), value => settings.OutputChannels = value);
        SetString(GetArgValue(args, "--output-sample-format"), value => settings.OutputSampleFormat = value);
        SetInt(GetArgValue(args, "--silence-after-ms"), value => settings.SilenceAfterMs = value);
        SetInt(GetArgValue(args, "--silence-chunk-ms"), value => settings.SilenceChunkMs = value);
        SetInt(GetArgValue(args, "--status-interval-seconds"), value => settings.StatusIntervalSeconds = value);
        SetInt(GetArgValue(args, "--ffmpeg-exit-timeout-ms"), value => settings.FfmpegExitTimeoutMs = value);

        if (args.Contains("--list-devices"))
        {
            settings.ListDevices = true;
        }

        if (args.Contains("--list-processes"))
        {
            settings.ListProcesses = true;
        }

        if (args.Contains("--run-tests"))
        {
            settings.RunTests = true;
        }
    }

    static string? GetArgValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    static string? GetConfigPath(string[] args)
    {
        return GetArgValue(args, "--config") ?? Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_CONFIG");
    }

    static void SetString(string? raw, Action<string> set)
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            set(raw);
        }
    }

    static void SetInt(string? raw, Action<int> set)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            set(value);
        }
    }

    static void SetNullableInt(string? raw, Action<int?> set)
    {
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            set(value);
        }
    }

    static void SetDouble(string? raw, Action<double> set)
    {
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            set(value);
        }
    }

    static void SetBool(string? raw, Action<bool> set)
    {
        if (bool.TryParse(raw, out var value))
        {
            set(value);
        }
    }
}
