using System.Diagnostics;

namespace WindowsPcAudioUplink;

public sealed class FfmpegSession : IDisposable
{
    readonly string ffmpeg;
    readonly string inputFormat;
    readonly int sampleRate;
    readonly int channels;
    readonly AppSettings settings;
    readonly CancellationTokenSource reconnectCancellation;
    readonly object gate = new();
    readonly SemaphoreSlim reconnectLock = new(1, 1);
    Process process;
    bool disposed;

    public FfmpegSession(
        string ffmpeg,
        string inputFormat,
        int sampleRate,
        int channels,
        AppSettings settings,
        CancellationToken lifetimeCancellation)
    {
        this.ffmpeg = ffmpeg;
        this.inputFormat = inputFormat;
        this.sampleRate = sampleRate;
        this.channels = channels;
        this.settings = settings;
        reconnectCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation);
        process = StartProcess();
    }

    public int ProcessId
    {
        get
        {
            lock (gate)
            {
                return process.Id;
            }
        }
    }

    public async Task WriteAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Process current;
            lock (gate)
            {
                current = process;
            }

            try
            {
                if (current.HasExited)
                {
                    RequestReconnect(current, "processo ja encerrado antes da escrita");
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                await current.StandardInput.BaseStream.WriteAsync(buffer, cancellationToken);
                return;
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                RequestReconnect(current, $"falha ao escrever PCM: {exception.Message}");
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    Process StartProcess()
    {
        var started = FfmpegProcess.Start(ffmpeg, inputFormat, sampleRate, channels, settings);
        started.Exited += HandleExited;
        if (started.HasExited)
        {
            RequestReconnect(started, "processo encerrou durante a inicializacao");
        }

        return started;
    }

    void HandleExited(object? sender, EventArgs eventArgs)
    {
        if (sender is Process exited)
        {
            RequestReconnect(exited, "evento Exited");
        }
    }

    void RequestReconnect(Process exited, string reason)
    {
        _ = ReconnectSafelyAsync(exited, reason);
    }

    async Task ReconnectSafelyAsync(Process exited, string reason)
    {
        try
        {
            await reconnectLock.WaitAsync(reconnectCancellation.Token);
            try
            {
                Process current;
                lock (gate)
                {
                    if (disposed || !ReferenceEquals(process, exited))
                    {
                        return;
                    }

                    current = process;
                }

                string exitSummary;
                try
                {
                    exitSummary = FfmpegProcess.DescribeUnexpectedExit(current);
                }
                catch (Exception exception)
                {
                    exitSummary = $"pid {SafeProcessId(current)}; resumo indisponivel: {exception.Message}";
                }

                LogReconnect($"ffmpeg desconectado: {exitSummary}; motivo={reason}");

                for (var attempt = 1; ; attempt++)
                {
                    reconnectCancellation.Token.ThrowIfCancellationRequested();
                    var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(attempt - 1, 5))));
                    LogReconnect($"ffmpeg reconexao tentativa {attempt} em {delay.TotalSeconds:0}s");
                    await Task.Delay(delay, reconnectCancellation.Token);

                    try
                    {
                        var next = StartProcess();
                        lock (gate)
                        {
                            if (disposed || !ReferenceEquals(process, current))
                            {
                                next.Exited -= HandleExited;
                                next.Dispose();
                                return;
                            }

                            process = next;
                        }

                        var previousPid = SafeProcessId(current);
                        var nextPid = SafeProcessId(next);
                        current.Exited -= HandleExited;
                        current.Dispose();
                        LogReconnect($"ffmpeg reconectado com sucesso: pid anterior {previousPid}, pid atual {nextPid}");
                        return;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        Diagnostics.Write("ffmpeg-reconnect-failed", exception, $"attempt={attempt}");
                        Console.Error.WriteLine($"ffmpeg reconexao falhou na tentativa {attempt}: {exception.Message}");
                    }
                }
            }
            finally
            {
                reconnectLock.Release();
            }
        }
        catch (OperationCanceledException) when (reconnectCancellation.IsCancellationRequested)
        {
            // Encerramento normal solicitado pelo uplink.
        }
        catch (Exception exception)
        {
            Diagnostics.Write("ffmpeg-reconnect-loop-failed", exception, $"reason={reason}");
            Console.Error.WriteLine($"loop de reconexao do ffmpeg falhou: {exception.Message}");
        }
    }

    void LogReconnect(string message)
    {
        Diagnostics.Write("ffmpeg-reconnect", details: message);
        Console.Error.WriteLine(message);
    }

    static string SafeProcessId(Process process)
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

    public void Dispose()
    {
        Process current;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            current = process;
        }

        reconnectCancellation.Cancel();

        current.Exited -= HandleExited;
        try
        {
            current.StandardInput.Close();
        }
        catch
        {
            // O FFmpeg pode ja ter encerrado durante a parada.
        }

        try
        {
            if (!current.WaitForExit(settings.FfmpegExitTimeoutMs))
            {
                Console.Error.WriteLine($"ffmpeg nao encerrou em {settings.FfmpegExitTimeoutMs} ms; finalizando a arvore do processo pid {SafeProcessId(current)}.");
                current.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            current.Dispose();
        }
    }
}
