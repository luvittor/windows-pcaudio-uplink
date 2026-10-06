namespace WindowsPcAudioUplink;

public static class FfmpegLocator
{
    public static string Find(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        if (CommandExists("ffmpeg"))
        {
            return "ffmpeg";
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var wingetPackages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(wingetPackages))
        {
            var ffmpeg = EnumerateFilesSafe(wingetPackages, "ffmpeg.exe")
                .Where(path => path.Contains("Gyan.FFmpeg", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path =>
                {
                    try
                    {
                        return File.GetLastWriteTimeUtc(path);
                    }
                    catch
                    {
                        return DateTime.MinValue;
                    }
                })
                .FirstOrDefault();

            if (ffmpeg is not null)
            {
                return ffmpeg;
            }
        }

        throw new FileNotFoundException("FFmpeg nao encontrado no PATH nem nos pacotes do Winget.");
    }

    static IEnumerable<string> EnumerateFilesSafe(string root, string pattern)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, pattern);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(directory);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in directories)
            {
                pending.Push(child);
            }
        }
    }

    static bool CommandExists(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE").Split(';', StringSplitOptions.RemoveEmptyEntries);

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                if (File.Exists(Path.Combine(directory, command + extension)))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
