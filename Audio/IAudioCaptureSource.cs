using NAudio.Wave;

namespace WindowsPcAudioUplink.Audio;

public interface IAudioCaptureSource : IDisposable
{
    string Description { get; }
    WaveFormat WaveFormat { get; }
    event EventHandler<AudioDataAvailableEventArgs>? DataAvailable;
    event EventHandler<StoppedEventArgs>? RecordingStopped;
    void StartRecording();
    void StopRecording();
}
