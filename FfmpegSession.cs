using System.Diagnostics;
using System.Text.Json;

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
    readonly object connectionStateGate = new();
    readonly SemaphoreSlim reconnectLock = new(1, 1);
    static readonly HttpClient ReceiverClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    Process process;
    bool disposed;
    string connectionState = "connected";
    int reconnectAttempt;
    DateTimeOffset? disconnectedAt;
    DateTimeOffset? nextReconnectAt;

    public string ConnectionState { get { lock (connectionStateGate) return connectionState; } }
    public int ReconnectAttempt { get { lock (connectionStateGate) return reconnectAttempt; } }
    public DateTimeOffset? DisconnectedAt { get { lock (connectionStateGate) return disconnectedAt; } }
    public DateTimeOffset? NextReconnectAt { get { lock (connectionStateGate) return nextReconnectAt; } }

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
                SetConnectionState("reconnecting", 0, DateTimeOffset.UtcNow, null);

                for (var attempt = 1; ; attempt++)
                {
                    reconnectCancellation.Token.ThrowIfCancellationRequested();
                    var disconnectedSince = DisconnectedAt ?? DateTimeOffset.UtcNow;
                    var disconnectedFor = DateTimeOffset.UtcNow - disconnectedSince;
                    var degraded = disconnectedFor >= ReconnectPolicy.DegradedAfter;
                    var delay = ReconnectPolicy.GetDelay(attempt, disconnectedFor);
                    var nextAttemptAt = DateTimeOffset.UtcNow + delay;
                    SetConnectionState(degraded ? "degraded" : "reconnecting", attempt, disconnectedSince, nextAttemptAt);
                    LogReconnect($"ffmpeg reconexao tentativa {attempt} em {delay.TotalSeconds:0}s");
                    await Task.Delay(delay, reconnectCancellation.Token);

                    try
                    {
                        var baselineBytes = await ReadReceiverBytesAsync(reconnectCancellation.Token);
                        var next = StartProcess();
                        // O novo processo ainda nao recebe frames do loop de captura enquanto
                        // a confirmacao esta pendente. Um frame curto de silencio permite que
                        // o Mimic confirme a ingestao sem depender de um deadlock de estado.
                        var confirmationFrame = new byte[Math.Max(4096, sampleRate * channels * 4 / 10)];
                        await next.StandardInput.BaseStream.WriteAsync(confirmationFrame, reconnectCancellation.Token);
                        if (settings.ConfirmReceiverIngest &&
                            !await WaitForReceiverConfirmationAsync(baselineBytes, reconnectCancellation.Token))
                        {
                            TryTerminate(next);
                            throw new IOException("o Mimic nao confirmou ingestao apos a reconexao");
                        }
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
                        SetConnectionState("connected", 0, null, null);
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

    void SetConnectionState(string value, int attempt, DateTimeOffset? disconnected, DateTimeOffset? nextAttempt)
    {
        lock (connectionStateGate)
        {
            connectionState = value;
            reconnectAttempt = attempt;
            disconnectedAt = disconnected;
            nextReconnectAt = nextAttempt;
        }
    }

    async Task<long?> ReadReceiverBytesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var uri = new Uri($"http://{settings.Host}:{settings.ReceiverStatusPort}/status");
            using var response = await ReceiverClient.GetAsync(uri, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("live_total_bytes", out var bytes) && bytes.TryGetInt64(out var value) ? value : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            Diagnostics.Write("receiver-status-read-failed", exception);
            return null;
        }
    }

    async Task<bool> WaitForReceiverConfirmationAsync(long? baselineBytes, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(settings.ReceiverConfirmationTimeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var uri = new Uri($"http://{settings.Host}:{settings.ReceiverStatusPort}/status");
                using var response = await ReceiverClient.GetAsync(uri, cancellationToken);
                response.EnsureSuccessStatusCode();
                using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
                var root = document.RootElement;
                var connected = root.TryGetProperty("live_connected", out var live) && live.GetBoolean();
                var hasBytes = root.TryGetProperty("live_last_bytes_at", out var lastBytes) && lastBytes.ValueKind != JsonValueKind.Null;
                var total = root.TryGetProperty("live_total_bytes", out var totalElement) && totalElement.TryGetInt64(out var value) ? value : 0;
                if (connected && hasBytes && (!baselineBytes.HasValue || total > baselineBytes.Value))
                {
                    return true;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                // O Mimic pode estar trocando de sessao; continuar sondando ate o prazo.
            }

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }

    static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch
        {
            // A tentativa de reconexao ja falhou; nao esconder o erro original.
        }
        finally
        {
            process.Dispose();
        }
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
