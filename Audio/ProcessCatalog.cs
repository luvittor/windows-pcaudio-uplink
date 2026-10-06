using System.Diagnostics;

namespace WindowsPcAudioUplink.Audio;

public static class ProcessCatalog
{
    public static ProcessTarget Resolve(int? processId, string? processName)
    {
        if (processId is > 0)
        {
            var process = Process.GetProcessById(processId.Value);
            return new ProcessTarget(process.Id, SafeProcessName(process), SafeMainWindowTitle(process));
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            throw new InvalidOperationException("Informe --process-id ou --process-name para captureMode=process.");
        }

        var matches = Process.GetProcesses()
            .Select(ToCandidate)
            .Where(candidate => candidate is not null && IsMatch(candidate.Value, processName))
            .Select(candidate => candidate!.Value)
            .OrderByDescending(candidate => !string.IsNullOrWhiteSpace(candidate.MainWindowTitle))
            .ThenBy(candidate => candidate.ProcessName)
            .ThenBy(candidate => candidate.ProcessId)
            .ToList();

        if (matches.Count == 0)
        {
            throw new InvalidOperationException($"Processo nao encontrado: {processName}. Use --list-processes para ver candidatos.");
        }

        if (matches.Count > 1)
        {
            Console.WriteLine($"Mais de um processo combina com '{processName}'. Usando {matches[0].ProcessName} ({matches[0].ProcessId}).");
        }

        return matches[0];
    }

    public static void PrintAudioCandidates()
    {
        var candidates = Process.GetProcesses()
            .Select(ToCandidate)
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!.Value)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.MainWindowTitle) || LooksLikeMediaProcess(candidate.ProcessName))
            .OrderBy(candidate => candidate.ProcessName)
            .ThenBy(candidate => candidate.ProcessId);

        foreach (var candidate in candidates)
        {
            var title = string.IsNullOrWhiteSpace(candidate.MainWindowTitle) ? "" : $" | {candidate.MainWindowTitle}";
            Console.WriteLine($"{candidate.ProcessId}: {candidate.ProcessName}{title}");
        }
    }

    internal static bool IsMatch(ProcessTarget candidate, string requested)
    {
        var normalized = requested.Trim();
        return candidate.ProcessName.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || candidate.ProcessName.Contains(normalized, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(candidate.MainWindowTitle)
                && candidate.MainWindowTitle.Contains(normalized, StringComparison.OrdinalIgnoreCase));
    }

    static ProcessTarget? ToCandidate(Process process)
    {
        try
        {
            return new ProcessTarget(process.Id, SafeProcessName(process), SafeMainWindowTitle(process));
        }
        catch
        {
            return null;
        }
        finally
        {
            process.Dispose();
        }
    }

    static bool LooksLikeMediaProcess(string processName)
    {
        return processName.Contains("media", StringComparison.OrdinalIgnoreCase)
            || processName.Contains("player", StringComparison.OrdinalIgnoreCase)
            || processName.Contains("spotify", StringComparison.OrdinalIgnoreCase)
            || processName.Contains("chrome", StringComparison.OrdinalIgnoreCase)
            || processName.Contains("firefox", StringComparison.OrdinalIgnoreCase)
            || processName.Contains("msedge", StringComparison.OrdinalIgnoreCase);
    }

    static string SafeProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    static string SafeMainWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch
        {
            return "";
        }
    }
}
