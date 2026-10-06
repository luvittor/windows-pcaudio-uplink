using WindowsPcAudioUplink;
using WindowsPcAudioUplink.Audio;
using WindowsPcAudioUplink.Tests;

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

await AudioUplink.RunAsync(settings);
