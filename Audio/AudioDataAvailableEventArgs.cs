namespace WindowsPcAudioUplink.Audio;

public sealed class AudioDataAvailableEventArgs(byte[] buffer, int bytesRecorded) : EventArgs
{
    public byte[] Buffer { get; } = buffer;
    public int BytesRecorded { get; } = bytesRecorded;
}
