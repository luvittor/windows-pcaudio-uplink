using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace WindowsPcAudioUplink;

public sealed class BackgroundServiceState
{
    public int ProcessId { get; set; }
    public int FfmpegProcessId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CaptureChangedAt { get; set; }
    public int CaptureSwitchCount { get; set; }
    public string? UplinkConfigPath { get; set; }
    public string? CaptureConfigPath { get; set; }
    public string CaptureMode { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourceFormat { get; set; } = "";
    public string Target { get; set; } = "";
    public long PcmBytesSent { get; set; }
    public long SilenceBytesSent { get; set; }
    public long DroppedPcmBytes { get; set; }
    public double Level { get; set; }
}

public static class BackgroundService
{
    static readonly object StateLock = new();
    static readonly string StateDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtime");
    static readonly string StatePath = Path.Combine(StateDirectory, "server-state.json");
    static readonly string LogPath = Path.Combine(StateDirectory, "server.log");

    public const string PipeName = "windows-pcaudio-uplink-control";

    public static bool IsStartCommand(string[] args) => CommandLine.Is(args, "start");
    public static bool IsStatusCommand(string[] args) => CommandLine.Is(args, "status");
    public static bool IsStopCommand(string[] args) => CommandLine.Is(args, "stop");
    public static bool IsLogCommand(string[] args) => CommandLine.Is(args, "log");
    public static bool IsSwitchCommand(string[] args) => CommandLine.Is(args, "switch");
    public static bool IsServerChild(string[] args) => args.Contains("--server-child");

    public static bool IsRunning()
    {
        var state = ReadStateOrNull();
        return state is not null && IsProcessRunning(state.ProcessId);
    }

    public static int Start(string[] args)
    {
        var current = ReadStateOrNull();
        if (current is not null && IsProcessRunning(current.ProcessId))
        {
            Console.WriteLine($"ja esta rodando: pid {current.ProcessId}");
            PrintState(current);
            return 0;
        }

        TryDeleteState();
        Directory.CreateDirectory(StateDirectory);
        var dllPath = Assembly.GetEntryAssembly()?.Location
            ?? throw new InvalidOperationException("Nao foi possivel localizar o executavel atual.");

        var childArgs = CommandLine.WithoutCommand(args)
            .Where(arg => !arg.Equals("--server-child", StringComparison.OrdinalIgnoreCase))
            .Append("--server-child")
            .ToList();

        _ = AppSettings.Load(childArgs.ToArray());

        TryDeleteLog();
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Directory.GetCurrentDirectory()
        };
        startInfo.ArgumentList.Add(dllPath);
        foreach (var childArg in childArgs)
        {
            startInfo.ArgumentList.Add(childArg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Nao foi possivel iniciar o servidor em background.");

        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            process.Refresh();
            if (process.HasExited)
            {
                Console.Error.WriteLine("O servidor encerrou durante a inicializacao.");
                PrintLogTail(30);
                return process.ExitCode == 0 ? 1 : process.ExitCode;
            }

            var state = ReadStateOrNull();
            if (state?.ProcessId == process.Id)
            {
                Console.WriteLine($"iniciado em background: pid {process.Id}");
                PrintState(state);
                return 0;
            }

            Thread.Sleep(100);
        }

        Console.Error.WriteLine($"O servidor pid {process.Id} nao ficou pronto em 15 segundos.");
        PrintLogTail(30);
        return 1;
    }

    public static int Status()
    {
        var response = TrySend(new ControlRequest { Command = ControlCommands.Status });
        if (response?.Success == true && response.State is not null)
        {
            PrintState(response.State);
            return 0;
        }

        var state = ReadStateOrNull();
        if (state is null || !IsProcessRunning(state.ProcessId))
        {
            Console.WriteLine("parado");
            TryDeleteState();
            return 1;
        }

        Console.Error.WriteLine("processo existe, mas o canal de controle nao respondeu");
        PrintState(state);
        return 2;
    }

    public static int SwitchCapture(string[] args)
    {
        if (!IsRunning())
        {
            Console.Error.WriteLine("servidor parado; use: dotnet run -- start");
            return 1;
        }

        var commandArgs = IsSwitchCommand(args) ? CommandLine.WithoutCommand(args) : args;
        if (!CommandLine.HasCaptureSelection(commandArgs))
        {
            Console.Error.WriteLine("Informe a captura, por exemplo: dotnet run -- switch --capture spotify");
            return 1;
        }

        var settings = AppSettings.Load(commandArgs);
        var response = TrySend(new ControlRequest
        {
            Command = ControlCommands.SwitchCapture,
            Capture = CaptureSettings.From(settings),
            CaptureConfigPath = settings.CaptureConfigPath ?? settings.ConfigPath
        }, timeoutMs: 30000);

        if (response is null)
        {
            Console.Error.WriteLine("O servidor nao respondeu ao comando de troca.");
            return 2;
        }

        if (!response.Success)
        {
            Console.Error.WriteLine($"troca recusada: {response.Message}");
            return 1;
        }

        Console.WriteLine(response.Message);
        if (response.State is not null)
        {
            PrintState(response.State);
        }

        return 0;
    }

    public static int Stop()
    {
        var state = ReadStateOrNull();
        if (state is null || !IsProcessRunning(state.ProcessId))
        {
            Console.WriteLine("ja esta parado");
            TryDeleteState();
            return 0;
        }

        var response = TrySend(new ControlRequest { Command = ControlCommands.Stop });
        if (response?.Success == true && WaitUntilStopped(state.ProcessId, TimeSpan.FromSeconds(5)))
        {
            Console.WriteLine($"parado: pid {state.ProcessId}");
            TryDeleteState();
            return 0;
        }

        try
        {
            using var process = Process.GetProcessById(state.ProcessId);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
            Console.WriteLine($"parado a forca: pid {state.ProcessId}");
        }
        catch (ArgumentException)
        {
            Console.WriteLine($"ja estava parado: pid {state.ProcessId}");
        }

        TryDeleteState();
        return 0;
    }

    public static int Log()
    {
        if (!File.Exists(LogPath))
        {
            Console.WriteLine("log vazio");
            return 1;
        }

        Console.Write(File.ReadAllText(LogPath));
        return 0;
    }

    public static void InstallServerLogging()
    {
        Directory.CreateDirectory(StateDirectory);
        var writer = TextWriter.Synchronized(new RollingFileTextWriter(LogPath, 1000));
        Console.SetOut(writer);
        Console.SetError(writer);
        Console.WriteLine("log iniciado");
    }

    public static BackgroundServiceState CreateState(
        AppSettings settings,
        string source,
        string sourceFormat,
        int ffmpegProcessId)
    {
        var now = DateTimeOffset.Now;
        return new BackgroundServiceState
        {
            ProcessId = Environment.ProcessId,
            FfmpegProcessId = ffmpegProcessId,
            StartedAt = now,
            CaptureChangedAt = now,
            UplinkConfigPath = settings.UplinkConfigPath ?? settings.ConfigPath,
            CaptureConfigPath = settings.CaptureConfigPath ?? settings.ConfigPath,
            CaptureMode = settings.CaptureMode,
            Source = source,
            SourceFormat = sourceFormat,
            Target = $"tcp://{settings.Host}:{settings.Port} {settings.OutputFormat} {settings.AudioCodec} {settings.OutputSampleRate} Hz {settings.OutputChannels} ch"
        };
    }

    public static void WriteState(BackgroundServiceState state)
    {
        lock (StateLock)
        {
            Directory.CreateDirectory(StateDirectory);
            var temporaryPath = StatePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, AppSettings.JsonOptions));
            File.Move(temporaryPath, StatePath, overwrite: true);
        }
    }

    public static void ClearStateForCurrentProcess()
    {
        var state = ReadStateOrNull();
        if (state?.ProcessId == Environment.ProcessId)
        {
            TryDeleteState();
        }
    }

    static ControlResponse? TrySend(ControlRequest request, int timeoutMs = 5000)
    {
        try
        {
            return ControlClient.Send(request, timeoutMs);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or OperationCanceledException)
        {
            return null;
        }
    }

    static void PrintState(BackgroundServiceState state)
    {
        Console.WriteLine($"rodando: pid {state.ProcessId} | ffmpeg {state.FfmpegProcessId}");
        Console.WriteLine($"inicio: {state.StartedAt:yyyy-MM-dd HH:mm:ss zzz}");
        Console.WriteLine($"uplink: {state.UplinkConfigPath}");
        Console.WriteLine($"capture: {state.CaptureConfigPath}");
        Console.WriteLine($"captureMode: {state.CaptureMode}");
        Console.WriteLine($"source: {state.Source}");
        Console.WriteLine($"format: {state.SourceFormat}");
        Console.WriteLine($"target: {state.Target}");
        Console.WriteLine($"trocas: {state.CaptureSwitchCount}");
        Console.WriteLine($"nivel: {state.Level:P0} | pcm {state.PcmBytesSent / 1024 / 1024} MiB | silencio {state.SilenceBytesSent / 1024 / 1024} MiB | descartado {state.DroppedPcmBytes / 1024 / 1024} MiB");
        Console.WriteLine($"log: {LogPath}");
    }

    static void PrintLogTail(int lineCount)
    {
        if (!File.Exists(LogPath))
        {
            return;
        }

        foreach (var line in File.ReadLines(LogPath).TakeLast(lineCount))
        {
            Console.Error.WriteLine(line);
        }
    }

    static BackgroundServiceState? ReadStateOrNull()
    {
        lock (StateLock)
        {
            try
            {
                return File.Exists(StatePath)
                    ? JsonSerializer.Deserialize<BackgroundServiceState>(File.ReadAllText(StatePath), AppSettings.JsonOptions)
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }

    static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    static bool WaitUntilStopped(int processId, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (!IsProcessRunning(processId))
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }

    static void TryDeleteState()
    {
        lock (StateLock)
        {
            try
            {
                File.Delete(StatePath);
                File.Delete(StatePath + ".tmp");
            }
            catch
            {
                // Estado local best-effort.
            }
        }
    }

    static void TryDeleteLog()
    {
        try
        {
            File.Delete(LogPath);
        }
        catch
        {
            // Log local best-effort.
        }
    }
}
