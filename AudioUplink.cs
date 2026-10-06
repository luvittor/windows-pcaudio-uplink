using System.Diagnostics;
using NAudio.Wave;
using WindowsPcAudioUplink.Audio;

namespace WindowsPcAudioUplink;

public static class AudioUplink
{
    public static async Task RunAsync(AppSettings settings)
    {
        if (settings.ListDevices)
        {
            AudioCaptureFactory.PrintDevices();
            return;
        }

        using var capture = await AudioCaptureFactory.CreateAsync(settings);
        var inputFormat = capture.WaveFormat;
        var ffmpeg = FfmpegLocator.Find(settings.FfmpegPath);
        var ffmpegFormat = WaveFormatUtilities.GetFfmpegInputFormat(inputFormat);

        using var ffmpegProcess = FfmpegProcess.Start(ffmpeg, ffmpegFormat, inputFormat.SampleRate, inputFormat.Channels, settings);
        using var stopEvent = new ManualResetEventSlim(false);

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopEvent.Set();
        };

        var bytesSent = 0L;
        var silenceBytesSent = 0L;
        var lastLevel = 0.0;
        var lastStatus = Stopwatch.StartNew();
        var lastAudioFrame = Stopwatch.StartNew();
        var writeLock = new object();
        var silenceBuffer = AudioProcessing.CreateSilenceBuffer(inputFormat, TimeSpan.FromMilliseconds(settings.SilenceChunkMs));
        var gainMultiplier = Math.Pow(10, settings.GainDb / 20.0);

        capture.DataAvailable += (_, eventArgs) =>
        {
            if (ffmpegProcess.HasExited)
            {
                stopEvent.Set();
                return;
            }

            var buffer = AudioProcessing.ApplyGain(eventArgs.Buffer, eventArgs.BytesRecorded, inputFormat, gainMultiplier);

            lock (writeLock)
            {
                ffmpegProcess.StandardInput.BaseStream.Write(buffer, 0, eventArgs.BytesRecorded);
            }

            bytesSent += eventArgs.BytesRecorded;
            lastAudioFrame.Restart();
            lastLevel = AudioProcessing.EstimateLevel(buffer, eventArgs.BytesRecorded, inputFormat);

            if (lastStatus.Elapsed >= TimeSpan.FromSeconds(settings.StatusIntervalSeconds))
            {
                Console.WriteLine($"capturando {capture.Description} | nivel {lastLevel:P0} | ganho {settings.GainDb:+0.0;-0.0;0.0} dB | pcm {bytesSent / 1024 / 1024} MiB | silencio {silenceBytesSent / 1024 / 1024} MiB");
                lastStatus.Restart();
            }
        };

        capture.RecordingStopped += (_, eventArgs) =>
        {
            if (eventArgs.Exception is not null)
            {
                Console.Error.WriteLine(eventArgs.Exception.Message);
            }

            stopEvent.Set();
        };

        Console.WriteLine("windows-pcaudio-uplink");
        Console.WriteLine($"capture: {settings.CaptureMode}");
        Console.WriteLine($"source: {capture.Description}");
        Console.WriteLine($"format: {inputFormat.Encoding} {inputFormat.SampleRate} Hz {inputFormat.Channels} ch {inputFormat.BitsPerSample} bit");
        Console.WriteLine($"ffmpeg: {ffmpeg}");
        Console.WriteLine($"target: tcp://{settings.Host}:{settings.Port} {settings.OutputFormat} {settings.AudioCodec} {settings.OutputSampleRate} Hz {settings.OutputChannels} ch{(string.IsNullOrWhiteSpace(settings.OutputSampleFormat) ? "" : $" {settings.OutputSampleFormat}")}{(string.IsNullOrWhiteSpace(settings.Bitrate) ? "" : $" {settings.Bitrate}")}");
        Console.WriteLine($"gain: {settings.GainDb:+0.0;-0.0;0.0} dB");
        Console.WriteLine(settings.DurationSeconds > 0 ? $"parada automatica em {settings.DurationSeconds}s." : "Ctrl+C para parar.");

        if (settings.DurationSeconds > 0)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(settings.DurationSeconds));
                stopEvent.Set();
            });
        }

        using var silenceTimer = new Timer(_ =>
        {
            if (ffmpegProcess.HasExited || lastAudioFrame.Elapsed < TimeSpan.FromMilliseconds(settings.SilenceAfterMs))
            {
                return;
            }

            lock (writeLock)
            {
                if (!ffmpegProcess.HasExited)
                {
                    ffmpegProcess.StandardInput.BaseStream.Write(silenceBuffer, 0, silenceBuffer.Length);
                    silenceBytesSent += silenceBuffer.Length;
                }
            }
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(settings.SilenceChunkMs));

        capture.StartRecording();
        stopEvent.Wait();
        capture.StopRecording();

        try
        {
            ffmpegProcess.StandardInput.Close();
        }
        catch
        {
            // O ffmpeg pode ja ter encerrado quando o servidor fecha a conexao.
        }

        if (!ffmpegProcess.WaitForExit(settings.FfmpegExitTimeoutMs))
        {
            ffmpegProcess.Kill(entireProcessTree: true);
        }
    }
}
