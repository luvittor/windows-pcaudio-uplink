using NAudio.Wave;

namespace WindowsPcAudioUplink.Audio;

public static class WaveFormatUtilities
{
    public static string GetFfmpegInputFormat(WaveFormat inputFormat)
    {
        return inputFormat.Encoding switch
        {
            WaveFormatEncoding.IeeeFloat when inputFormat.BitsPerSample == 32 => "f32le",
            WaveFormatEncoding.Pcm when inputFormat.BitsPerSample == 16 => "s16le",
            WaveFormatEncoding.Pcm when inputFormat.BitsPerSample == 24 => "s24le",
            WaveFormatEncoding.Pcm when inputFormat.BitsPerSample == 32 => "s32le",
            _ => throw new NotSupportedException($"Formato WASAPI nao suportado ainda: {inputFormat.Encoding}, {inputFormat.BitsPerSample} bits")
        };
    }
}
