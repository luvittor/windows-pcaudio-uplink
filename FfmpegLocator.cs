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
            var ffmpeg = Directory
                .EnumerateFiles(wingetPackages, "ffmpeg.exe", SearchOption.AllDirectories)
                .Where(path => path.Contains("Gyan.FFmpeg", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (ffmpeg is not null)
            {
                return ffmpeg;
            }
        }

        throw new FileNotFoundException("FFmpeg nao encontrado no PATH nem nos pacotes do Winget.");
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
