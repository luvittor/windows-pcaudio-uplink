using NAudio.Wave;

namespace WindowsPcAudioUplink.Audio;

public static class AudioProcessing
{
    public static byte[] CreateSilenceBuffer(WaveFormat format, TimeSpan duration)
    {
        var frames = Math.Max(1, (int)(format.SampleRate * duration.TotalSeconds));
        var bytes = frames * format.BlockAlign;
        return new byte[bytes];
    }

    public static byte[] ApplyGain(byte[] source, int bytesRecorded, WaveFormat format, double gainMultiplier)
    {
        var output = new byte[bytesRecorded];

        if (Math.Abs(gainMultiplier - 1.0) < 0.0001)
        {
            Buffer.BlockCopy(source, 0, output, 0, bytesRecorded);
            return output;
        }

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            for (var offset = 0; offset + 3 < bytesRecorded; offset += 4)
            {
                var sample = BitConverter.ToSingle(source, offset) * gainMultiplier;
                sample = Math.Clamp(sample, -1.0, 1.0);
                Buffer.BlockCopy(BitConverter.GetBytes((float)sample), 0, output, offset, 4);
            }

            return output;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            for (var offset = 0; offset + 1 < bytesRecorded; offset += 2)
            {
                var sample = (int)Math.Round(BitConverter.ToInt16(source, offset) * gainMultiplier);
                sample = Math.Clamp(sample, short.MinValue, short.MaxValue);
                Buffer.BlockCopy(BitConverter.GetBytes((short)sample), 0, output, offset, 2);
            }

            return output;
        }

        Buffer.BlockCopy(source, 0, output, 0, bytesRecorded);
        return output;
    }

    public static double EstimateLevel(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (bytesRecorded == 0)
        {
            return 0;
        }

        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            var samples = bytesRecorded / 4;
            var sum = 0.0;
            for (var offset = 0; offset + 3 < bytesRecorded; offset += 4)
            {
                var sample = BitConverter.ToSingle(buffer, offset);
                sum += sample * sample;
            }

            return Math.Min(1.0, Math.Sqrt(sum / Math.Max(1, samples)));
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            var samples = bytesRecorded / 2;
            var sum = 0.0;
            for (var offset = 0; offset + 1 < bytesRecorded; offset += 2)
            {
                var sample = BitConverter.ToInt16(buffer, offset) / 32768.0;
                sum += sample * sample;
            }

            return Math.Min(1.0, Math.Sqrt(sum / Math.Max(1, samples)));
        }

        return 0;
    }
}
