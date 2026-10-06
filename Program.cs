using WindowsPcAudioUplink;
using WindowsPcAudioUplink.Audio;
using WindowsPcAudioUplink.Tests;

if (CommandLine.Is(args, "mock-server"))
{
    Environment.ExitCode = await MockAudioServer.RunAsync(args);
    return;
}

if (BackgroundService.IsStatusCommand(args))
{
    Environment.ExitCode = BackgroundService.Status();
    return;
}

if (BackgroundService.IsStopCommand(args))
{
    Environment.ExitCode = BackgroundService.Stop();
    return;
}

if (BackgroundService.IsLogCommand(args))
{
    Environment.ExitCode = BackgroundService.Log();
    return;
}

if (BackgroundService.IsSwitchCommand(args))
{
    Environment.ExitCode = BackgroundService.SwitchCapture(args);
    return;
}

if (BackgroundService.IsStartCommand(args))
{
    Environment.ExitCode = BackgroundService.Start(args);
    return;
}

if (BackgroundService.IsServerChild(args))
{
    BackgroundService.InstallServerLogging();
}

var settings = AppSettings.Load(args);

if (settings.RunTests)
{
    TestRunner.Run();
    return;
}

if (settings.ListProcesses)
{
    ProcessCatalog.PrintAudioCandidates();
    return;
}

if (settings.PrintResolvedConfig)
{
    ResolvedConfigPrinter.Print(settings);
    return;
}

if (!BackgroundService.IsServerChild(args) && BackgroundService.IsRunning())
{
    Environment.ExitCode = CommandLine.HasCaptureSelection(args)
        ? BackgroundService.SwitchCapture(args)
        : BackgroundService.Status();
    return;
}

try
{
    await AudioUplink.RunAsync(settings, BackgroundService.IsServerChild(args));
}
finally
{
    if (BackgroundService.IsServerChild(args))
    {
        BackgroundService.ClearStateForCurrentProcess();
    }
}
