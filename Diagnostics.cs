using System.Diagnostics;
using System.Text;

namespace WindowsPcAudioUplink;

public static class Diagnostics
{
    static readonly object Gate = new();
    static bool writing;

    public static void Write(string category, Exception? exception = null, string? details = null)
    {
        try
        {
            var directory = BackgroundService.RuntimeDirectory;
            Directory.CreateDirectory(directory);
            var timestamp = DateTimeOffset.Now;
            var process = Process.GetCurrentProcess();
            var builder = new StringBuilder()
                .Append('[').Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss zzz")).Append("] ")
                .Append(category)
                .Append(" | pid ").Append(process.Id)
                .Append(" | exe ").Append(Environment.ProcessPath)
                .Append(" | base ").Append(AppContext.BaseDirectory)
                .Append(" | command ").Append(Environment.CommandLine)
                .Append(" | workingDir ").Append(Environment.CurrentDirectory)
                .AppendLine();

            if (!string.IsNullOrWhiteSpace(details))
            {
                builder.Append("details: ").AppendLine(details);
            }

            if (exception is not null)
            {
                builder.Append("exception: ").AppendLine(exception.ToString());
            }

            var message = builder.ToString();
            lock (Gate)
            {
                if (writing)
                {
                    return;
                }

                writing = true;
                try
                {
                    File.AppendAllText(Path.Combine(directory, "diagnostics.log"), message, Encoding.UTF8);
                }
                finally
                {
                    writing = false;
                }
            }
        }
        catch
        {
            // Diagnostico nao pode causar uma segunda falha durante o encerramento.
        }
    }

    public static void WriteCrash(string category, Exception? exception, string? details = null)
    {
        try
        {
            var directory = BackgroundService.RuntimeDirectory;
            Directory.CreateDirectory(directory);
            var timestamp = DateTimeOffset.Now;
            var path = Path.Combine(
                directory,
                $"crash-{timestamp:yyyyMMdd-HHmmss-fff}-pid{Environment.ProcessId}.txt");
            var process = Process.GetCurrentProcess();
            var report = new StringBuilder()
                .AppendLine("windows-pcaudio-uplink crash report")
                .AppendLine($"timestamp: {timestamp:O}")
                .AppendLine($"category: {category}")
                .AppendLine($"pid: {process.Id}")
                .AppendLine($"process: {Environment.ProcessPath}")
                .AppendLine($"command: {Environment.CommandLine}")
                .AppendLine($"base: {AppContext.BaseDirectory}")
                .AppendLine($"workingDir: {Environment.CurrentDirectory}")
                .AppendLine($"os: {Environment.OSVersion}")
                .AppendLine($"runtime: {Environment.Version}")
                .AppendLine($"memoryBytes: {process.WorkingSet64}")
                .AppendLine($"threads: {process.Threads.Count}");

            if (!string.IsNullOrWhiteSpace(details))
            {
                report.AppendLine($"details: {details}");
            }

            if (exception is not null)
            {
                report.AppendLine("exception:");
                report.AppendLine(exception.ToString());
            }

            report.AppendLine("state:");
            report.AppendLine(ReadStateSnapshot());
            File.WriteAllText(path, report.ToString(), Encoding.UTF8);
            Write(category, exception, details + $" | crashReport={path}");
        }
        catch
        {
            Write(category, exception, details);
        }
    }

    static string ReadStateSnapshot()
    {
        try
        {
            var path = Path.Combine(BackgroundService.RuntimeDirectory, "server-state.json");
            if (!File.Exists(path))
            {
                return "(ausente)";
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception exception)
        {
            return $"(indisponivel: {exception.GetType().Name}: {exception.Message})";
        }
    }
}
