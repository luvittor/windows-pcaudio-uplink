namespace WindowsPcAudioUplink;

public static class CommandLine
{
    static readonly HashSet<string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        "start", "status", "stop", "log", "switch", "mock-server"
    };

    static readonly string[] CaptureOptions =
    [
        "--capture",
        "--capture-mode",
        "--device-index",
        "--process-id",
        "--process-name",
        "--capture-buffer-ms",
        "--gain-db",
        "--silence-after-ms",
        "--silence-chunk-ms",
        "--status-interval-seconds"
    ];

    public static string? Command(string[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        var isLongOption = args[0].StartsWith("--", StringComparison.Ordinal);
        var candidate = isLongOption ? args[0][2..] : args[0];
        return Commands.Contains(candidate) ? candidate.ToLowerInvariant() : null;
    }

    public static bool Is(string[] args, string command)
    {
        return string.Equals(Command(args), command, StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasCaptureSelection(string[] args)
    {
        return CaptureOptions.Any(args.Contains);
    }

    public static string[] WithoutCommand(string[] args)
    {
        return Command(args) is null ? args : args[1..];
    }
}
