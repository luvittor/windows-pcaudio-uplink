using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.Wave;

var settings = AppSettings.Load(args);

using var enumerator = new MMDeviceEnumerator();
var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

if (settings.ListDevices)
{
    for (var i = 0; i < devices.Count; i++)
    {
        Console.WriteLine($"{i}: {devices[i].FriendlyName}");
    }

    return;
}

var device = settings.DeviceIndex >= 0 && settings.DeviceIndex < devices.Count
    ? devices[settings.DeviceIndex]
    : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

using var capture = new WasapiLoopbackCapture(device);
var inputFormat = capture.WaveFormat;
var ffmpeg = FindFfmpeg(settings.FfmpegPath);
var ffmpegFormat = inputFormat.Encoding switch
{
    WaveFormatEncoding.IeeeFloat when inputFormat.BitsPerSample == 32 => "f32le",
    WaveFormatEncoding.Pcm when inputFormat.BitsPerSample == 16 => "s16le",
    WaveFormatEncoding.Pcm when inputFormat.BitsPerSample == 24 => "s24le",
    WaveFormatEncoding.Pcm when inputFormat.BitsPerSample == 32 => "s32le",
    _ => throw new NotSupportedException($"Formato WASAPI nao suportado ainda: {inputFormat.Encoding}, {inputFormat.BitsPerSample} bits")
};

using var ffmpegProcess = StartFfmpeg(ffmpeg, ffmpegFormat, inputFormat.SampleRate, inputFormat.Channels, settings);
using var stopEvent = new ManualResetEventSlim(false);

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stopEvent.Set();
};

var bytesSent = 0L;
var silenceBytesSent = 0L;
var lastLevel = 0.0;
var lastStatus = Stopwatch.StartNew();
var lastAudioFrame = Stopwatch.StartNew();
var writeLock = new object();
var silenceBuffer = CreateSilenceBuffer(inputFormat, TimeSpan.FromMilliseconds(settings.SilenceChunkMs));
var gainMultiplier = Math.Pow(10, settings.GainDb / 20.0);

capture.DataAvailable += (_, eventArgs) =>
{
    if (ffmpegProcess.HasExited)
    {
        stopEvent.Set();
        return;
    }

    var buffer = ApplyGain(eventArgs.Buffer, eventArgs.BytesRecorded, inputFormat, gainMultiplier);

    lock (writeLock)
    {
        ffmpegProcess.StandardInput.BaseStream.Write(buffer, 0, eventArgs.BytesRecorded);
    }

    bytesSent += eventArgs.BytesRecorded;
    lastAudioFrame.Restart();
    lastLevel = EstimateLevel(buffer, eventArgs.BytesRecorded, inputFormat);

    if (lastStatus.Elapsed >= TimeSpan.FromSeconds(settings.StatusIntervalSeconds))
    {
        Console.WriteLine($"capturando {device.FriendlyName} | nivel {lastLevel:P0} | ganho {settings.GainDb:+0.0;-0.0;0.0} dB | pcm {bytesSent / 1024 / 1024} MiB | silencio {silenceBytesSent / 1024 / 1024} MiB");
        lastStatus.Restart();
    }
};

capture.RecordingStopped += (_, eventArgs) =>
{
    if (eventArgs.Exception is not null)
    {
        Console.Error.WriteLine(eventArgs.Exception.Message);
    }

    stopEvent.Set();
};

Console.WriteLine("windows-pcaudio-uplink");
Console.WriteLine($"device: {device.FriendlyName}");
Console.WriteLine($"format: {inputFormat.Encoding} {inputFormat.SampleRate} Hz {inputFormat.Channels} ch {inputFormat.BitsPerSample} bit");
Console.WriteLine($"ffmpeg: {ffmpeg}");
Console.WriteLine($"target: tcp://{settings.Host}:{settings.Port} {settings.OutputFormat} {settings.AudioCodec} {settings.OutputSampleRate} Hz {settings.OutputChannels} ch{(string.IsNullOrWhiteSpace(settings.OutputSampleFormat) ? "" : $" {settings.OutputSampleFormat}")}{(string.IsNullOrWhiteSpace(settings.Bitrate) ? "" : $" {settings.Bitrate}")}");
Console.WriteLine($"gain: {settings.GainDb:+0.0;-0.0;0.0} dB");
Console.WriteLine(settings.DurationSeconds > 0 ? $"parada automatica em {settings.DurationSeconds}s." : "Ctrl+C para parar.");

if (settings.DurationSeconds > 0)
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(TimeSpan.FromSeconds(settings.DurationSeconds));
        stopEvent.Set();
    });
}

using var silenceTimer = new Timer(_ =>
{
    if (ffmpegProcess.HasExited || lastAudioFrame.Elapsed < TimeSpan.FromMilliseconds(settings.SilenceAfterMs))
    {
        return;
    }

    lock (writeLock)
    {
        if (!ffmpegProcess.HasExited)
        {
            ffmpegProcess.StandardInput.BaseStream.Write(silenceBuffer, 0, silenceBuffer.Length);
            silenceBytesSent += silenceBuffer.Length;
        }
    }
}, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(settings.SilenceChunkMs));

capture.StartRecording();
stopEvent.Wait();
capture.StopRecording();

try
{
    ffmpegProcess.StandardInput.Close();
}
catch
{
    // O ffmpeg pode ja ter encerrado quando o servidor fecha a conexao.
}

if (!ffmpegProcess.WaitForExit(settings.FfmpegExitTimeoutMs))
{
    ffmpegProcess.Kill(entireProcessTree: true);
}

static Process StartFfmpeg(string ffmpeg, string inputFormat, int sampleRate, int channels, AppSettings settings)
{
    var target = $"tcp://{settings.Host}:{settings.Port}";
    var arguments = new List<string>
    {
        "-hide_banner",
        $"-loglevel {settings.FfmpegLogLevel}",
        $"-f {inputFormat}",
        $"-ar {sampleRate}",
        $"-ac {channels}",
        "-i pipe:0",
        $"-ac {settings.OutputChannels}",
        $"-ar {settings.OutputSampleRate}",
        $"-codec:a {settings.AudioCodec}",
    };

    if (!string.IsNullOrWhiteSpace(settings.OutputSampleFormat))
    {
        arguments.Add($"-sample_fmt {settings.OutputSampleFormat}");
    }

    if (!string.IsNullOrWhiteSpace(settings.Bitrate))
    {
        arguments.Add($"-b:a {settings.Bitrate}");
    }

    arguments.Add($"-f {settings.OutputFormat}");
    arguments.Add(target);

    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            Arguments = string.Join(' ', arguments),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = false,
            RedirectStandardOutput = false,
            CreateNoWindow = true
        }
    };

    process.Start();
    return process;
}

static string FindFfmpeg(string? configuredPath)
{
    if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
    {
        return configuredPath;
    }

    if (CommandExists("ffmpeg"))
    {
        return "ffmpeg";
    }

    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var wingetPackages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
    if (Directory.Exists(wingetPackages))
    {
        var ffmpeg = Directory
            .EnumerateFiles(wingetPackages, "ffmpeg.exe", SearchOption.AllDirectories)
            .Where(path => path.Contains("Gyan.FFmpeg", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (ffmpeg is not null)
        {
            return ffmpeg;
        }
    }

    throw new FileNotFoundException("FFmpeg nao encontrado no PATH nem nos pacotes do Winget.");
}

static bool CommandExists(string command)
{
    var path = Environment.GetEnvironmentVariable("PATH") ?? "";
    var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE").Split(';', StringSplitOptions.RemoveEmptyEntries);

    foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
        foreach (var extension in extensions)
        {
            if (File.Exists(Path.Combine(directory, command + extension)))
            {
                return true;
            }
        }
    }

    return false;
}

static byte[] CreateSilenceBuffer(WaveFormat format, TimeSpan duration)
{
    var frames = Math.Max(1, (int)(format.SampleRate * duration.TotalSeconds));
    var bytes = frames * format.BlockAlign;
    return new byte[bytes];
}

static byte[] ApplyGain(byte[] source, int bytesRecorded, WaveFormat format, double gainMultiplier)
{
    var output = new byte[bytesRecorded];

    if (Math.Abs(gainMultiplier - 1.0) < 0.0001)
    {
        Buffer.BlockCopy(source, 0, output, 0, bytesRecorded);
        return output;
    }

    if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
    {
        for (var offset = 0; offset + 3 < bytesRecorded; offset += 4)
        {
            var sample = BitConverter.ToSingle(source, offset) * gainMultiplier;
            sample = Math.Clamp(sample, -1.0, 1.0);
            Buffer.BlockCopy(BitConverter.GetBytes((float)sample), 0, output, offset, 4);
        }

        return output;
    }

    if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
    {
        for (var offset = 0; offset + 1 < bytesRecorded; offset += 2)
        {
            var sample = (int)Math.Round(BitConverter.ToInt16(source, offset) * gainMultiplier);
            sample = Math.Clamp(sample, short.MinValue, short.MaxValue);
            Buffer.BlockCopy(BitConverter.GetBytes((short)sample), 0, output, offset, 2);
        }

        return output;
    }

    Buffer.BlockCopy(source, 0, output, 0, bytesRecorded);
    return output;
}

static double EstimateLevel(byte[] buffer, int bytesRecorded, WaveFormat format)
{
    if (bytesRecorded == 0)
    {
        return 0;
    }

    if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
    {
        var samples = bytesRecorded / 4;
        var sum = 0.0;
        for (var offset = 0; offset + 3 < bytesRecorded; offset += 4)
        {
            var sample = BitConverter.ToSingle(buffer, offset);
            sum += sample * sample;
        }

        return Math.Min(1.0, Math.Sqrt(sum / Math.Max(1, samples)));
    }

    if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
    {
        var samples = bytesRecorded / 2;
        var sum = 0.0;
        for (var offset = 0; offset + 1 < bytesRecorded; offset += 2)
        {
            var sample = BitConverter.ToInt16(buffer, offset) / 32768.0;
            sum += sample * sample;
        }

        return Math.Min(1.0, Math.Sqrt(sum / Math.Max(1, samples)));
    }

    return 0;
}

sealed class AppSettings
{
    public string Host { get; set; } = "192.168.15.14";
    public int Port { get; set; } = 18080;
    public string? Bitrate { get; set; } = "128k";
    public double GainDb { get; set; } = 6.0;
    public int DeviceIndex { get; set; } = -1;
    public int DurationSeconds { get; set; } = 0;
    public bool ListDevices { get; set; } = false;
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

    public static AppSettings Load(string[] args)
    {
        var settings = LoadFile();
        ApplyEnvironment(settings);
        ApplyArgs(settings, args);
        return settings;
    }

    static AppSettings LoadFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
        }

        if (!File.Exists(path))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppSettings();
    }

    static void ApplyEnvironment(AppSettings settings)
    {
        SetString(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_HOST"), value => settings.Host = value);
        SetInt(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_PORT"), value => settings.Port = value);
        SetString(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_BITRATE"), value => settings.Bitrate = value);
        SetDouble(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_GAIN_DB"), value => settings.GainDb = value);
        SetInt(Environment.GetEnvironmentVariable("PCAUDIO_UPLINK_DEVICE_INDEX"), value => settings.DeviceIndex = value);
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
        SetInt(GetArgValue(args, "--device-index"), value => settings.DeviceIndex = value);
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
    }

    static string? GetArgValue(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
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
