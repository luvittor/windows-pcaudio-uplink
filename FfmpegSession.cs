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
    readonly SemaphoreSlim reconnectLock = new(1, 1);
    readonly IReadOnlyList<UplinkDestination> destinations;
    UplinkDestination activeDestination;
    static readonly HttpClient ReceiverClient = new() { Timeout = TimeSpan.FromSeconds(2) };
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
        destinations = UplinkDestinationSelector.Resolve(settings.Host, settings.Port, settings.Destinations.Skip(1));
        activeDestination = destinations[0];
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

    Process StartProcess(UplinkDestination? destination = null)
    {
        var target = destination ?? activeDestination;
        var processSettings = settings.Clone();
        processSettings.Host = target.Host;
        processSettings.Port = target.Port;
        var started = FfmpegProcess.Start(ffmpeg, inputFormat, sampleRate, channels, processSettings);
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

                    var activeIndex = destinations
                        .Select((destination, index) => (destination, index))
                        .FirstOrDefault(item => item.destination == activeDestination)
                        .index;
                    if (activeIndex < 0)
                    {
                        activeIndex = 0;
                    }

                    foreach (var destination in destinations
                        .Skip(activeIndex)
                        .Concat(destinations.Take(activeIndex)))
                    {
                        try
                        {
                        LogReconnect($"ffmpeg destino de reconexao: {destination}");
                        var baselineBytes = await ReadReceiverBytesAsync(destination, reconnectCancellation.Token);
                        var next = StartProcess(destination);
                        if (settings.ConfirmReceiverIngest &&
                            !await WaitForReceiverConfirmationAsync(destination, baselineBytes, reconnectCancellation.Token))
                        {
                            TryTerminate(next);
                            throw new IOException("o Mimic nao confirmou ingestao apos a reconexao");
                        }
                        activeDestination = destination;
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
                            Diagnostics.Write("ffmpeg-reconnect-failed", exception, $"attempt={attempt}; destination={destination}");
                            Console.Error.WriteLine($"ffmpeg reconexao falhou no destino {destination}: {exception.Message}");
                        }
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

    async Task<long?> ReadReceiverBytesAsync(UplinkDestination destination, CancellationToken cancellationToken)
    {
        try
        {
            var uri = new Uri($"http://{destination.Host}:{settings.ReceiverStatusPort}/status");
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

    async Task<bool> WaitForReceiverConfirmationAsync(UplinkDestination destination, long? baselineBytes, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(settings.ReceiverConfirmationTimeoutMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var uri = new Uri($"http://{destination.Host}:{settings.ReceiverStatusPort}/status");
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
