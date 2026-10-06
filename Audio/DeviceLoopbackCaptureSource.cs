using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WindowsPcAudioUplink.Audio;

public sealed class DeviceLoopbackCaptureSource : IAudioCaptureSource
{
#pragma warning disable CS0618 // Mantem o caminho antigo para preservar comportamento do modo driver.
    readonly WasapiLoopbackCapture capture;
#pragma warning restore CS0618

    public DeviceLoopbackCaptureSource(MMDevice device)
    {
#pragma warning disable CS0618
        capture = new WasapiLoopbackCapture(device);
#pragma warning restore CS0618
        Description = device.FriendlyName;
        capture.DataAvailable += (_, eventArgs) =>
        {
            DataAvailable?.Invoke(this, new AudioDataAvailableEventArgs(eventArgs.Buffer, eventArgs.BytesRecorded));
        };
        capture.RecordingStopped += (_, eventArgs) => RecordingStopped?.Invoke(this, eventArgs);
    }

    public string Description { get; }
    public WaveFormat WaveFormat => capture.WaveFormat;
    public event EventHandler<AudioDataAvailableEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public void StartRecording() => capture.StartRecording();

    public void StopRecording() => capture.StopRecording();

    public void Dispose() => capture.Dispose();
}
