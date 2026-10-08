using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace WindowsPcAudioUplink;

public static class FfmpegProcess
{
    const int StderrTailCapacity = 20;
    static readonly ConditionalWeakTable<Process, Diagnostics> ProcessDiagnostics = new();

    public static Process Start(string ffmpeg, string inputFormat, int sampleRate, int channels, AppSettings settings)
    {
        var arguments = string.Join(' ', BuildArguments(inputFormat, sampleRate, channels, settings));
        var diagnostics = new Diagnostics();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                RedirectStandardOutput = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            diagnostics.AddStderr(eventArgs.Data);
            Console.Error.WriteLine($"ffmpeg stderr: {eventArgs.Data}");
        };

        Console.WriteLine($"ffmpeg comando: {Quote(ffmpeg)} {arguments}");
        try
        {
            process.Start();
            ProcessDiagnostics.Add(process, diagnostics);
            process.BeginErrorReadLine();
        }
        catch (Exception exception)
        {
            process.Dispose();
            throw new InvalidOperationException(
                $"Nao foi possivel iniciar o FFmpeg '{ffmpeg}': {exception.Message}",
                exception);
        }

        return process;
    }

    public static string DescribeUnexpectedExit(Process process)
    {
        try
        {
            // Aguarda os callbacks da leitura assincrona para nao perder as ultimas linhas do stderr.
            if (process.HasExited)
            {
                process.WaitForExit();
            }
        }
        catch (InvalidOperationException)
        {
            // O resumo abaixo ainda consegue informar os dados disponiveis.
        }

        var exitCode = TryGetExitCode(process);
        var summary = $"O FFmpeg encerrou durante a transmissao: pid {TryGetProcessId(process)}, codigo de saida {exitCode}";

        if (!ProcessDiagnostics.TryGetValue(process, out var diagnostics))
        {
            return summary + ".";
        }

        summary += $", tempo em execucao {diagnostics.Elapsed:hh\\:mm\\:ss\\.fff}";
        var stderr = diagnostics.GetStderrTail();
        return stderr.Count == 0
            ? summary + ". Nenhuma mensagem foi recebida no stderr do FFmpeg."
            : summary + $". Ultimo stderr: {stderr[^1]}";
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

    static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

    static string TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode.ToString() : "indisponivel (processo ainda ativo)";
        }
        catch (InvalidOperationException)
        {
            return "indisponivel";
        }
    }

    static string TryGetProcessId(Process process)
    {
        try
        {
            return process.Id.ToString();
        }
        catch (InvalidOperationException)
        {
            return "indisponivel";
        }
    }

    sealed class Diagnostics
    {
        readonly object gate = new();
        readonly Queue<string> stderrTail = new();
        readonly Stopwatch elapsed = Stopwatch.StartNew();

        public TimeSpan Elapsed => elapsed.Elapsed;

        public void AddStderr(string line)
        {
            lock (gate)
            {
                stderrTail.Enqueue(line);
                while (stderrTail.Count > StderrTailCapacity)
                {
                    stderrTail.Dequeue();
                }
            }
        }

        public IReadOnlyList<string> GetStderrTail()
        {
            lock (gate)
            {
                return stderrTail.ToArray();
            }
        }
    }
}
