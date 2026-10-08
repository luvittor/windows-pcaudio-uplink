using System.Diagnostics;
using System.Threading.Channels;
using NAudio.Wave;
using WindowsPcAudioUplink.Audio;

namespace WindowsPcAudioUplink;

public static class AudioUplink
{
    public static async Task RunAsync(AppSettings initialSettings, bool runControlServer = false)
    {
        if (initialSettings.ListDevices)
        {
            AudioCaptureFactory.PrintDevices();
            return;
        }

        var ffmpeg = FfmpegLocator.Find(initialSettings.FfmpegPath);
        var transportFormat = WaveFormat.CreateIeeeFloatWaveFormat(
            initialSettings.OutputSampleRate,
            initialSettings.OutputChannels);

        using var ffmpegProcess = FfmpegProcess.Start(
            ffmpeg,
            "f32le",
            transportFormat.SampleRate,
            transportFormat.Channels,
            initialSettings);
        using var stopped = new CancellationTokenSource();
        using var durationCancellation = new CancellationTokenSource();

        var settings = initialSettings.Clone();
        var stateLock = new object();
        var bytesSent = 0L;
        var silenceBytesSent = 0L;
        var droppedPcmBytes = 0L;
        var lastLevel = 0.0;
        var lastAudioFrame = Stopwatch.StartNew();
        var lastStatus = Stopwatch.StartNew();
        BackgroundServiceState? state = null;

        void Stop(Exception? exception = null)
        {
            if (exception is not null)
            {
                Console.Error.WriteLine(exception.Message);
            }

            stopped.Cancel();
        }

        var ffmpegExitHandled = 0;
        void HandleFfmpegExited(object? sender, EventArgs eventArgs)
        {
            if (Interlocked.Exchange(ref ffmpegExitHandled, 1) != 0)
            {
                return;
            }

            var transportState =
                $"transporte pcm {Interlocked.Read(ref bytesSent)} bytes, " +
                $"silencio {Interlocked.Read(ref silenceBytesSent)} bytes, " +
                $"descartado {Interlocked.Read(ref droppedPcmBytes)} bytes, " +
                $"ultimo frame ha {lastAudioFrame.Elapsed.TotalSeconds:F1}s";
            Stop(new InvalidOperationException(
                $"{FfmpegProcess.DescribeUnexpectedExit(ffmpegProcess)} Estado no encerramento: {transportState}."));
        }

        ffmpegProcess.Exited += HandleFfmpegExited;
        if (ffmpegProcess.HasExited)
        {
            HandleFfmpegExited(ffmpegProcess, EventArgs.Empty);
        }

        var audioQueue = Channel.CreateBounded<(byte[] Buffer, bool Silence)>(new BoundedChannelOptions(8)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        using var transportCancellation = new CancellationTokenSource();
        var transportTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var packet in audioQueue.Reader.ReadAllAsync(transportCancellation.Token))
                {
                    await ffmpegProcess.StandardInput.BaseStream.WriteAsync(packet.Buffer, transportCancellation.Token);
                    if (packet.Silence)
                    {
                        Interlocked.Add(ref silenceBytesSent, packet.Buffer.Length);
                    }
                    else
                    {
                        Interlocked.Add(ref bytesSent, packet.Buffer.Length);
                    }
                }
            }
            catch (OperationCanceledException) when (transportCancellation.IsCancellationRequested)
            {
                // Encerramento normal do transporte.
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                if (!stopped.IsCancellationRequested)
                {
                    Stop(exception);
                }
            }
        });

        void WritePcm(byte[] buffer, double level)
        {
            if (ffmpegProcess.HasExited)
            {
                HandleFfmpegExited(ffmpegProcess, EventArgs.Empty);
                return;
            }

            if (!audioQueue.Writer.TryWrite((buffer, false)))
            {
                Interlocked.Add(ref droppedPcmBytes, buffer.Length);
            }

            lastAudioFrame.Restart();
            lastLevel = level;
        }

        using var router = new AudioCaptureRouter(
            transportFormat.SampleRate,
            transportFormat.Channels,
            WritePcm,
            Stop);

        await router.SwitchAsync(settings);

        string SourceFormat()
        {
            var format = router.SourceFormat;
            return format is null
                ? "desconhecido"
                : $"{format.Encoding} {format.SampleRate} Hz {format.Channels} ch {format.BitsPerSample} bit";
        }

        BackgroundServiceState SnapshotState()
        {
            lock (stateLock)
            {
                state ??= BackgroundService.CreateState(
                    settings,
                    router.Description,
                    SourceFormat(),
                    ffmpegProcess.Id);
                state.PcmBytesSent = Interlocked.Read(ref bytesSent);
                state.SilenceBytesSent = Interlocked.Read(ref silenceBytesSent);
                state.DroppedPcmBytes = Interlocked.Read(ref droppedPcmBytes);
                state.Level = lastLevel;
                return CloneState(state);
            }
        }

        async Task<ControlResponse> HandleControlAsync(ControlRequest request)
        {
            switch (request.Command)
            {
                case ControlCommands.Status:
                    return new ControlResponse
                    {
                        Success = true,
                        Message = "rodando",
                        State = SnapshotState()
                    };

                case ControlCommands.Stop:
                    return new ControlResponse
                    {
                        Success = true,
                        Message = "parando",
                        State = SnapshotState()
                    };

                case ControlCommands.SwitchCapture:
                    if (request.Capture is null)
                    {
                        return new ControlResponse { Success = false, Message = "Configuracao de captura ausente." };
                    }

                    var next = settings.Clone();
                    request.Capture.ApplyTo(next);
                    next.CaptureConfigPath = request.CaptureConfigPath;
                    next.CaptureMode = CaptureModes.Normalize(next.CaptureMode);

                    await router.SwitchAsync(next);
                    settings = next;
                    lastLevel = 0;
                    lastAudioFrame.Restart();
                    lock (stateLock)
                    {
                        state ??= BackgroundService.CreateState(
                            settings,
                            router.Description,
                            SourceFormat(),
                            ffmpegProcess.Id);
                        state.CaptureConfigPath = settings.CaptureConfigPath;
                        state.CaptureMode = settings.CaptureMode;
                        state.Source = router.Description;
                        state.SourceFormat = SourceFormat();
                        state.CaptureChangedAt = DateTimeOffset.Now;
                        state.CaptureSwitchCount++;
                    }

                    var switchedState = SnapshotState();
                    BackgroundService.WriteState(switchedState);
                    Console.WriteLine($"captura trocada para {switchedState.Source} | conexao ffmpeg pid {switchedState.FfmpegProcessId} preservada");
                    return new ControlResponse
                    {
                        Success = true,
                        Message = $"captura trocada para {switchedState.Source}",
                        State = switchedState
                    };

                default:
                    return new ControlResponse { Success = false, Message = $"Comando desconhecido: {request.Command}" };
            }
        }

        var serverState = SnapshotState();
        if (runControlServer)
        {
            BackgroundService.WriteState(serverState);
        }

        using var controlCancellation = new CancellationTokenSource();
        Task? controlTask = null;
        if (runControlServer)
        {
            var controlServer = new ControlServer(
                BackgroundService.PipeName,
                HandleControlAsync,
                () => Stop());
            controlTask = controlServer.RunAsync(controlCancellation.Token);
        }

        Console.CancelKeyPress += HandleCancelKeyPress;
        void HandleCancelKeyPress(object? sender, ConsoleCancelEventArgs eventArgs)
        {
            eventArgs.Cancel = true;
            Stop();
        }

        Console.WriteLine("windows-pcaudio-uplink");
        Console.WriteLine($"capture: {settings.CaptureMode}");
        Console.WriteLine($"source: {router.Description}");
        Console.WriteLine($"format: {SourceFormat()}");
        Console.WriteLine($"transport: IeeeFloat {transportFormat.SampleRate} Hz {transportFormat.Channels} ch 32 bit");
        Console.WriteLine($"ffmpeg: {ffmpeg} (pid {ffmpegProcess.Id})");
        Console.WriteLine($"target: tcp://{settings.Host}:{settings.Port} {settings.OutputFormat} {settings.AudioCodec} {settings.OutputSampleRate} Hz {settings.OutputChannels} ch{(string.IsNullOrWhiteSpace(settings.OutputSampleFormat) ? "" : $" {settings.OutputSampleFormat}")}{(string.IsNullOrWhiteSpace(settings.Bitrate) ? "" : $" {settings.Bitrate}")}");
        Console.WriteLine($"gain: {settings.GainDb:+0.0;-0.0;0.0} dB");
        Console.WriteLine(runControlServer ? "controle local pronto." : settings.DurationSeconds > 0 ? $"parada automatica em {settings.DurationSeconds}s." : "Ctrl+C para parar.");

        if (settings.DurationSeconds > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(settings.DurationSeconds), durationCancellation.Token);
                    Stop();
                }
                catch (OperationCanceledException)
                {
                    // Encerramento normal antes da duracao configurada.
                }
            });
        }

        var silenceBuffer = AudioProcessing.CreateSilenceBuffer(
            transportFormat,
            TimeSpan.FromMilliseconds(settings.SilenceChunkMs));
        using var silenceTimer = new Timer(_ =>
        {
            if (stopped.IsCancellationRequested || ffmpegProcess.HasExited)
            {
                return;
            }

            if (lastAudioFrame.Elapsed >= TimeSpan.FromMilliseconds(settings.SilenceAfterMs))
            {
                if (!audioQueue.Writer.TryWrite((silenceBuffer, true)))
                {
                    Interlocked.Add(ref droppedPcmBytes, silenceBuffer.Length);
                }
            }

            if (lastStatus.Elapsed >= TimeSpan.FromSeconds(settings.StatusIntervalSeconds))
            {
                var snapshot = SnapshotState();
                Console.WriteLine($"capturando {snapshot.Source} | nivel {snapshot.Level:P0} | ganho {settings.GainDb:+0.0;-0.0;0.0} dB | pcm {snapshot.PcmBytesSent / 1024 / 1024} MiB | silencio {snapshot.SilenceBytesSent / 1024 / 1024} MiB | descartado {snapshot.DroppedPcmBytes / 1024 / 1024} MiB");
                if (runControlServer)
                {
                    try
                    {
                        BackgroundService.WriteState(snapshot);
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine($"falha ao atualizar estado; transmissao continua: {exception.GetType().Name}: {exception.Message}");
                    }
                }

                lastStatus.Restart();
            }
        }, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(Math.Max(25, settings.SilenceChunkMs)));

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stopped.Token);
        }
        catch (OperationCanceledException)
        {
            // Encerramento solicitado pelo terminal ou canal de controle.
        }
        finally
        {
            durationCancellation.Cancel();
            controlCancellation.Cancel();
            Console.CancelKeyPress -= HandleCancelKeyPress;
            ffmpegProcess.Exited -= HandleFfmpegExited;
            router.Dispose();
            audioQueue.Writer.TryComplete();
            transportCancellation.Cancel();

            try
            {
                ffmpegProcess.StandardInput.Close();
            }
            catch
            {
                // O FFmpeg pode ja ter encerrado quando o servidor fecha a conexao.
            }

            try
            {
                await transportTask;
            }
            catch (OperationCanceledException)
            {
                // Encerramento normal do transporte.
            }

            if (!ffmpegProcess.WaitForExit(settings.FfmpegExitTimeoutMs))
            {
                Console.Error.WriteLine($"ffmpeg nao encerrou em {settings.FfmpegExitTimeoutMs} ms; finalizando a arvore do processo pid {ffmpegProcess.Id}.");
                ffmpegProcess.Kill(entireProcessTree: true);
            }

            if (controlTask is not null)
            {
                try
                {
                    await controlTask;
                }
                catch (OperationCanceledException)
                {
                    // Encerramento normal do pipe local.
                }
            }
        }
    }

    static BackgroundServiceState CloneState(BackgroundServiceState state)
    {
        return new BackgroundServiceState
        {
            ProcessId = state.ProcessId,
            FfmpegProcessId = state.FfmpegProcessId,
            StartedAt = state.StartedAt,
            CaptureChangedAt = state.CaptureChangedAt,
            CaptureSwitchCount = state.CaptureSwitchCount,
            UplinkConfigPath = state.UplinkConfigPath,
            CaptureConfigPath = state.CaptureConfigPath,
            CaptureMode = state.CaptureMode,
            Source = state.Source,
            SourceFormat = state.SourceFormat,
            Target = state.Target,
            PcmBytesSent = state.PcmBytesSent,
            SilenceBytesSent = state.SilenceBytesSent,
            DroppedPcmBytes = state.DroppedPcmBytes,
            Level = state.Level
        };
    }
}
