using WindowsPcAudioUplink;
using WindowsPcAudioUplink.Audio;
using WindowsPcAudioUplink.Tests;

try
{
    Environment.ExitCode = await RunAsync(args);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"erro: {exception.Message}");
    Environment.ExitCode = 1;
}

static async Task<int> RunAsync(string[] args)
{
    if (CommandLine.Is(args, "mock-server"))
    {
        return await MockAudioServer.RunAsync(args);
    }

    if (BackgroundService.IsStatusCommand(args))
    {
        return BackgroundService.Status();
    }

    if (BackgroundService.IsStopCommand(args))
    {
        return BackgroundService.Stop();
    }

    if (BackgroundService.IsLogCommand(args))
    {
        return BackgroundService.Log();
    }

    if (BackgroundService.IsSwitchCommand(args))
    {
        return BackgroundService.SwitchCapture(args);
    }

    if (BackgroundService.IsStartCommand(args))
    {
        return BackgroundService.Start(args);
    }

    var isServerChild = BackgroundService.IsServerChild(args);
    if (isServerChild)
    {
        BackgroundService.InstallServerLogging();
    }

    var settings = AppSettings.Load(args);

    if (settings.RunTests)
    {
        TestRunner.Run();
        return Environment.ExitCode;
    }

    if (settings.ListProcesses)
    {
        ProcessCatalog.PrintAudioCandidates();
        return 0;
    }

    if (settings.PrintResolvedConfig)
    {
        ResolvedConfigPrinter.Print(settings);
        return 0;
    }

    if (!isServerChild && BackgroundService.IsRunning())
    {
        return CommandLine.HasCaptureSelection(args)
            ? BackgroundService.SwitchCapture(args)
            : BackgroundService.Status();
    }

    try
    {
        await AudioUplink.RunAsync(settings, isServerChild);
        return 0;
    }
    finally
    {
        if (isServerChild)
        {
            BackgroundService.ClearStateForCurrentProcess();
        }
    }
}
