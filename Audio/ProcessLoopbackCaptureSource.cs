using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WindowsPcAudioUplink.Audio;

public sealed class ProcessLoopbackCaptureSource : IAudioCaptureSource
{
    readonly WasapiRecorder capture;

    ProcessLoopbackCaptureSource(WasapiRecorder capture, ProcessTarget target)
    {
        this.capture = capture;
        Target = target;
        Description = $"{target.ProcessName} ({target.ProcessId})";
        capture.DataAvailable += (buffer, _, _, _) =>
        {
            DataAvailable?.Invoke(this, new AudioDataAvailableEventArgs(buffer.ToArray(), buffer.Length));
        };
        capture.RecordingStopped += (_, eventArgs) => RecordingStopped?.Invoke(this, eventArgs);
    }

    public ProcessTarget Target { get; }
    public string Description { get; }
    public WaveFormat WaveFormat => capture.WaveFormat;
    public event EventHandler<AudioDataAvailableEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public static async Task<ProcessLoopbackCaptureSource> CreateAsync(AppSettings settings)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            throw new PlatformNotSupportedException("captureMode=process requer Windows 10 2004 build 19041 ou superior.");
        }

        var target = ProcessCatalog.Resolve(settings.ProcessId, settings.ProcessName);
        var recorder = await new WasapiRecorderBuilder()
            .WithProcessLoopback((uint)target.ProcessId, ProcessLoopbackMode.IncludeTargetProcessTree)
            .WithBufferLength(settings.CaptureBufferMs)
            .BuildAsync();

        return new ProcessLoopbackCaptureSource(recorder, target);
    }

    public void StartRecording() => capture.StartRecording();

    public void StopRecording() => capture.StopRecording();

    public void Dispose() => capture.Dispose();
}
