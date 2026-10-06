using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace WindowsPcAudioUplink;

public static class ControlCommands
{
    public const string Status = "status";
    public const string Stop = "stop";
    public const string SwitchCapture = "switch-capture";
}

public sealed class ControlRequest
{
    public string Command { get; set; } = "";
    public CaptureSettings? Capture { get; set; }
    public string? CaptureConfigPath { get; set; }
}

public sealed class ControlResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public BackgroundServiceState? State { get; set; }
}

public static class ControlClient
{
    public static ControlResponse Send(ControlRequest request, int timeoutMs = 5000)
    {
        using var timeout = new CancellationTokenSource(timeoutMs);
        return SendAsync(request, BackgroundService.PipeName, timeout.Token).GetAwaiter().GetResult();
    }

    internal static async Task<ControlResponse> SendAsync(
        ControlRequest request,
        string pipeName,
        CancellationToken cancellationToken)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellationToken);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, AppSettings.JsonOptions).AsMemory(), cancellationToken);
        var responseJson = await reader.ReadLineAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(responseJson)
            ? throw new InvalidOperationException("O servidor retornou uma resposta vazia.")
            : JsonSerializer.Deserialize<ControlResponse>(responseJson, AppSettings.JsonOptions)
                ?? throw new InvalidOperationException("O servidor retornou uma resposta invalida.");
    }
}

public sealed class ControlServer
{
    readonly string pipeName;
    readonly Func<ControlRequest, Task<ControlResponse>> handler;
    readonly Action stopRequested;

    public ControlServer(
        string pipeName,
        Func<ControlRequest, Task<ControlResponse>> handler,
        Action stopRequested)
    {
        this.pipeName = pipeName;
        this.handler = handler;
        this.stopRequested = stopRequested;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, 1024, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                var requestJson = await reader.ReadLineAsync(cancellationToken);
                var request = string.IsNullOrWhiteSpace(requestJson)
                    ? null
                    : JsonSerializer.Deserialize<ControlRequest>(requestJson, AppSettings.JsonOptions);
                var response = request is null
                    ? new ControlResponse { Success = false, Message = "Comando vazio." }
                    : await HandleSafelyAsync(request);

                await writer.WriteLineAsync(JsonSerializer.Serialize(response, AppSettings.JsonOptions).AsMemory(), cancellationToken);

                if (request?.Command == ControlCommands.Stop && response.Success)
                {
                    stopRequested();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException exception)
            {
                Console.Error.WriteLine($"controle local: {exception.Message}");
            }
        }
    }

    async Task<ControlResponse> HandleSafelyAsync(ControlRequest request)
    {
        try
        {
            return await handler(request);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"controle local: {exception.Message}");
            return new ControlResponse { Success = false, Message = exception.Message };
        }
    }
}
