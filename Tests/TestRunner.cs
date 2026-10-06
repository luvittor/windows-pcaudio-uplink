using NAudio.Wave;
using WindowsPcAudioUplink.Audio;

namespace WindowsPcAudioUplink.Tests;

public static class TestRunner
{
    static int passed;

    public static void Run()
    {
        Test("config antiga continua em captureMode=device", LegacyConfigDefaultsToDevice);
        Test("config FLAC 48000 atual preserva parametros", LegacyFlac48000ConfigKeepsOutput);
        Test("aliases de captureMode normalizam", CaptureModeAliasesNormalize);
        Test("formatos PCM/WASAPI viram formato ffmpeg correto", FfmpegInputFormatMapping);
        Test("argumentos ffmpeg preservam saida FLAC sem bitrate", FfmpegArgumentsForFlac);
        Test("perfil novo captura Reprodutor de Midia por processo", MediaPlayerProfileUsesProcessMode);
        Test("ganho PCM16 satura sem estourar", GainClampsPcm16);
        Test("match de processo aceita nome e titulo", ProcessMatchingUsesNameAndTitle);

        Console.WriteLine($"tests: {passed} passed");
    }

    static void LegacyConfigDefaultsToDevice()
    {
        var settings = AppSettings.LoadFromJson("""
        {
          "host": "192.168.15.14",
          "port": 18080,
          "deviceIndex": -1
        }
        """);

        AssertEqual(CaptureModes.Device, CaptureModes.Normalize(settings.CaptureMode));
        AssertEqual(-1, settings.DeviceIndex);
    }

    static void LegacyFlac48000ConfigKeepsOutput()
    {
        var settings = AppSettings.LoadFromJson(File.ReadAllText("appsettings-flac-48000-stereo16.json"));

        AssertEqual(CaptureModes.Device, CaptureModes.Normalize(settings.CaptureMode));
        AssertEqual("flac", settings.AudioCodec);
        AssertEqual("flac", settings.OutputFormat);
        AssertEqual(48000, settings.OutputSampleRate);
        AssertEqual(2, settings.OutputChannels);
        AssertEqual("s16", settings.OutputSampleFormat);
        AssertEqual(null, settings.Bitrate);
    }

    static void CaptureModeAliasesNormalize()
    {
        AssertEqual(CaptureModes.Device, CaptureModes.Normalize("driver"));
        AssertEqual(CaptureModes.Process, CaptureModes.Normalize("app"));
    }

    static void FfmpegInputFormatMapping()
    {
        AssertEqual("f32le", WaveFormatUtilities.GetFfmpegInputFormat(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)));
        AssertEqual("s16le", WaveFormatUtilities.GetFfmpegInputFormat(new WaveFormat(44100, 16, 2)));
    }

    static void FfmpegArgumentsForFlac()
    {
        var settings = new AppSettings
        {
            Host = "127.0.0.1",
            Port = 18080,
            Bitrate = null,
            AudioCodec = "flac",
            OutputFormat = "flac",
            OutputSampleRate = 48000,
            OutputChannels = 2,
            OutputSampleFormat = "s16"
        };

        var args = FfmpegProcess.BuildArguments("f32le", 48000, 2, settings);

        AssertContains("-codec:a flac", args);
        AssertContains("-sample_fmt s16", args);
        AssertContains("-f flac", args);
        AssertContains("tcp://127.0.0.1:18080", args);
        AssertFalse(args.Any(arg => arg.StartsWith("-b:a", StringComparison.Ordinal)));
    }

    static void MediaPlayerProfileUsesProcessMode()
    {
        var settings = AppSettings.LoadFromJson(File.ReadAllText("appsettings-flac-48000-stereo16-media-player.json"));

        AssertEqual(CaptureModes.Process, CaptureModes.Normalize(settings.CaptureMode));
        AssertEqual("Microsoft.Media.Player", settings.ProcessName);
        AssertEqual("flac", settings.AudioCodec);
        AssertEqual(48000, settings.OutputSampleRate);
    }

    static void GainClampsPcm16()
    {
        var input = BitConverter.GetBytes((short)30000);
        var output = AudioProcessing.ApplyGain(input, input.Length, new WaveFormat(48000, 16, 1), 2.0);

        AssertEqual(short.MaxValue, BitConverter.ToInt16(output, 0));
    }

    static void ProcessMatchingUsesNameAndTitle()
    {
        var candidate = new ProcessTarget(123, "Microsoft.Media.Player", "Reprodutor de Midia");

        AssertTrue(ProcessCatalog.IsMatch(candidate, "Media.Player"));
        AssertTrue(ProcessCatalog.IsMatch(candidate, "Reprodutor"));
    }

    static void Test(string name, Action action)
    {
        try
        {
            action();
            passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
            Environment.ExitCode = 1;
            throw;
        }
    }

    static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"expected '{expected}', actual '{actual}'");
        }
    }

    static void AssertContains(string expected, IReadOnlyList<string> actual)
    {
        if (!actual.Contains(expected))
        {
            throw new InvalidOperationException($"expected item '{expected}'");
        }
    }

    static void AssertTrue(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("expected true");
        }
    }

    static void AssertFalse(bool condition)
    {
        if (condition)
        {
            throw new InvalidOperationException("expected false");
        }
    }
}
