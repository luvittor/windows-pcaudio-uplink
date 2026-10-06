using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace WindowsPcAudioUplink;

public static class MockAudioServer
{
    public static async Task<int> RunAsync(string[] args)
    {
        var commandArgs = CommandLine.WithoutCommand(args);
        var port = ReadInt(commandArgs, "--port", 18081);
        var durationSeconds = ReadInt(commandArgs, "--duration", 0);
        var inputFormat = ReadString(commandArgs, "--input-format") ?? "flac";
        var ffmpeg = FfmpegLocator.Find(ReadString(commandArgs, "--ffmpeg-path"));
        var outputPath = ReadString(commandArgs, "--output")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "runtime", $"mock-received.{inputFormat}");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using var cancellation = new CancellationTokenSource();
        if (durationSeconds > 0)
        {
            cancellation.CancelAfter(TimeSpan.FromSeconds(durationSeconds));
        }

        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start(1);
        Console.WriteLine($"mock aguardando uma conexao em tcp://127.0.0.1:{port}");
        Console.WriteLine($"arquivo: {outputPath}");

        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
            Console.WriteLine($"conexao 1 aceita de {client.Client.RemoteEndPoint}");
            listener.Stop();

            await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await using var network = client.GetStream();
            var buffer = new byte[64 * 1024];
            long bytesReceived = 0;
            var lastStatus = Stopwatch.StartNew();

            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var read = await network.ReadAsync(buffer, cancellation.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation.Token);
                    bytesReceived += read;
                    if (lastStatus.Elapsed >= TimeSpan.FromSeconds(1))
                    {
                        await output.FlushAsync(cancellation.Token);
                        Console.WriteLine($"mock recebeu {bytesReceived / 1024.0:F1} KiB | conexoes 1");
                        lastStatus.Restart();
                    }
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // Duracao concluida ou Ctrl+C.
            }
            await output.FlushAsync();
            Console.WriteLine($"mock finalizado | conexoes 1 | recebido {bytesReceived / 1024.0 / 1024.0:F2} MiB");
            AnalyzeReceivedAudio(ffmpeg, outputPath);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("mock finalizado sem conexao");
            return 0;
        }
        finally
        {
            listener.Stop();
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    static void AnalyzeReceivedAudio(string ffmpeg, string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            RedirectStandardInput = false,
            RedirectStandardOutput = false,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-hide_banner");
        startInfo.ArgumentList.Add("-loglevel");
        startInfo.ArgumentList.Add("info");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(path);
        startInfo.ArgumentList.Add("-af");
        startInfo.ArgumentList.Add("volumedetect");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("null");
        startInfo.ArgumentList.Add("NUL");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Nao foi possivel iniciar o decoder FFmpeg.");
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit(10000);
        foreach (var line in errors.Split(Environment.NewLine))
        {
            if (line.Contains("mean_volume", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("max_volume", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"mock analise: {line.Trim()}");
            }
        }
    }

    static int ReadInt(string[] args, string name, int defaultValue)
    {
        var value = ReadString(args, name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : defaultValue;
    }

    static string? ReadString(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
