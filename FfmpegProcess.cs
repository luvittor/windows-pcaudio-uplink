using System.Diagnostics;

namespace WindowsPcAudioUplink;

public static class FfmpegProcess
{
    public static Process Start(string ffmpeg, string inputFormat, int sampleRate, int channels, AppSettings settings)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = string.Join(' ', BuildArguments(inputFormat, sampleRate, channels, settings)),
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

    public static IReadOnlyList<string> BuildArguments(string inputFormat, int sampleRate, int channels, AppSettings settings)
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

        return arguments;
    }
}
