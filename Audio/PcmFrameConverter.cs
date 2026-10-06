using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WindowsPcAudioUplink.Audio;

public sealed class PcmFrameConverter
{
    readonly object sync = new();
    readonly BufferedWaveProvider input;
    readonly ISampleProvider output;
    readonly int outputChannels;
    readonly double outputToInputRatio;

    public PcmFrameConverter(WaveFormat inputFormat, int outputSampleRate, int outputChannels)
    {
        if (outputSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputSampleRate));
        }

        if (outputChannels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputChannels));
        }

        WaveFormatUtilities.GetFfmpegInputFormat(inputFormat);
        this.outputChannels = outputChannels;
        outputToInputRatio = (double)outputSampleRate / inputFormat.SampleRate;

        input = new BufferedWaveProvider(inputFormat, TimeSpan.FromSeconds(2))
        {
            ReadFully = false,
            DiscardOnBufferOverflow = true
        };

        ISampleProvider provider = input.ToSampleProvider();
        if (provider.WaveFormat.Channels != outputChannels)
        {
            provider = new ChannelMappingSampleProvider(provider, outputChannels);
        }

        output = provider.WaveFormat.SampleRate == outputSampleRate
            ? provider
            : new WdlResamplingSampleProvider(provider, outputSampleRate);
    }

    public byte[] Convert(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded <= 0)
        {
            return [];
        }

        lock (sync)
        {
            input.AddSamples(buffer, 0, bytesRecorded);
            var inputFrames = bytesRecorded / input.WaveFormat.BlockAlign;
            var requestedFrames = Math.Max(1, (int)Math.Ceiling(inputFrames * outputToInputRatio) + 8);
            var samples = new float[requestedFrames * outputChannels];
            var samplesRead = output.Read(samples.AsSpan());
            if (samplesRead == 0)
            {
                return [];
            }

            var converted = new byte[samplesRead * sizeof(float)];
            Buffer.BlockCopy(samples, 0, converted, 0, converted.Length);
            return converted;
        }
    }
}

sealed class ChannelMappingSampleProvider : ISampleProvider
{
    readonly ISampleProvider source;
    readonly int inputChannels;
    readonly int outputChannels;
    float[] sourceBuffer = [];

    public ChannelMappingSampleProvider(ISampleProvider source, int outputChannels)
    {
        this.source = source;
        inputChannels = source.WaveFormat.Channels;
        this.outputChannels = outputChannels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, outputChannels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(Span<float> buffer)
    {
        var outputFrames = buffer.Length / outputChannels;
        var required = outputFrames * inputChannels;
        if (sourceBuffer.Length < required)
        {
            sourceBuffer = new float[required];
        }

        var sourceSamples = source.Read(sourceBuffer.AsSpan(0, required));
        var framesRead = sourceSamples / inputChannels;
        for (var frame = 0; frame < framesRead; frame++)
        {
            var inputOffset = frame * inputChannels;
            var outputOffset = frame * outputChannels;

            if (outputChannels == 1)
            {
                var sum = 0f;
                for (var channel = 0; channel < inputChannels; channel++)
                {
                    sum += sourceBuffer[inputOffset + channel];
                }

                buffer[outputOffset] = sum / inputChannels;
                continue;
            }

            for (var channel = 0; channel < outputChannels; channel++)
            {
                var sourceChannel = inputChannels == 1 ? 0 : Math.Min(channel, inputChannels - 1);
                buffer[outputOffset + channel] = sourceBuffer[inputOffset + sourceChannel];
            }
        }

        return framesRead * outputChannels;
    }
}
