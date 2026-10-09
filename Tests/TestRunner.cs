using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NAudio.Wave;
using WindowsPcAudioUplink.Audio;

namespace WindowsPcAudioUplink.Tests;

public static class TestRunner
{
    static int passed;
    static int failed;

    public static void Run()
    {
        Test("perfil device usa captura por driver", DeviceProfileUsesDeviceMode);
        Test("perfil FLAC 48000 preserva parametros", Flac48000ProfileKeepsOutput);
        Test("aliases de captureMode normalizam", CaptureModeAliasesNormalize);
        Test("formatos PCM/WASAPI viram formato ffmpeg correto", FfmpegInputFormatMapping);
        Test("argumentos ffmpeg preservam saida FLAC sem bitrate", FfmpegArgumentsForFlac);
        Test("diagnostico de ffmpeg informa processo ainda ativo", FfmpegDiagnosticsForRunningProcess);
        Test("sessao ffmpeg reconecta depois de reset TCP", FfmpegSessionReconnectsAfterTcpReset);
        Test("sessao ffmpeg sobrevive a dois resets TCP", FfmpegSessionReconnectsAfterTwoTcpResets);
        Test("sessao ffmpeg reconecta quando servidor volta", FfmpegSessionReconnectsWhenServerReturns);
        Test("sessao ffmpeg nao reconecta depois do dispose", FfmpegSessionStopsDuringReconnect);
        Test("perfil Reprodutor de Midia captura por processo", MediaPlayerProfileUsesProcessMode);
        Test("perfis separados montam uplink e captura por driver", SplitProfilesBuildDeviceConfiguration);
        Test("defaults apontam perfis validos", DefaultsProfileBuildsConfiguration);
        Test("defaults sem campos obrigatorios falha", InvalidDefaultsFail);
        Test("porta de uplink invalida impede inicializacao", InvalidUplinkPortFails);
        Test("captura process sem alvo impede inicializacao", ProcessCaptureWithoutTargetFails);
        Test("perfil inexistente falha", MissingProfileFails);
        Test("atalho de perfil resolve pasta configs", ProfileShortcutResolvesConfigFolder);
        Test("perfil Spotify usa captura por processo", SpotifyProfileUsesProcessMode);
        Test("perfil Chrome usa captura por processo", ChromeProfileUsesProcessMode);
        Test("override --capture spotify resolve processo", CaptureOverrideResolvesSpotify);
        Test("override --capture chrome resolve processo", CaptureOverrideResolvesChrome);
        Test("flag --print-config nao inicia audio", PrintConfigFlagIsParsed);
        Test("comandos background sao detectados", BackgroundCommandsAreDetected);
        Test("runtime fica junto ao executavel", RuntimeDirectoryUsesApplicationBase);
        Test("comandos nao confundem valores com lifecycle", CommandsDoNotMatchOptionValues);
        Test("conversor normaliza taxa e canais para transporte fixo", PcmConverterNormalizesFormat);
        Test("hot swap troca fonte sem aceitar frames antigos", CaptureRouterHotSwapsSources);
        Test("hot swap falho preserva fonte atual", FailedCaptureSwitchKeepsCurrentSource);
        Test("cliente e servidor de controle conversam pelo pipe", ControlPipeRoundTrip);
        Test("pipe transporta hot swap e stop", ControlPipeCarriesSwitchAndStop);
        Test("perfil de uplink mock aponta para loopback", MockUplinkProfileUsesLoopback);
        Test("perfil de uplink habilita confirmacao do receptor", ProfileEnablesReceiverConfirmation);
        Test("destinos de uplink preservam ordem e removem duplicados", UplinkDestinationsPreserveOrder);
        Test("log rotativo limita mil linhas", RollingLogKeepsLastThousandLines);
        Test("log e exibido do mais novo para o mais antigo", LogIsDisplayedNewestFirst);
        Test("logger usa fallback quando o arquivo esta bloqueado", RollingLogFallsBackWhenLocked);
        Test("arquivamento preserva o log anterior", LogArchivePreservesPreviousLog);
        Test("crash report preserva contexto da excecao", CrashDiagnosticsWritesReport);
        Test("ganho PCM16 satura sem estourar", GainClampsPcm16);
        Test("match de processo aceita nome e titulo", ProcessMatchingUsesNameAndTitle);

        Console.WriteLine($"tests: {passed} passed, {failed} failed");
        if (failed > 0)
        {
            Environment.ExitCode = 1;
        }
    }

    static void DeviceProfileUsesDeviceMode()
    {
        var settings = CaptureSettings.LoadFromJson(File.ReadAllText("configs/capture/device.json"));

        AssertEqual(CaptureModes.Device, CaptureModes.Normalize(settings.CaptureMode));
        AssertEqual(-1, settings.DeviceIndex);
    }

    static void Flac48000ProfileKeepsOutput()
    {
        var settings = UplinkSettings.LoadFromJson(File.ReadAllText("configs/uplink/flac-48000-stereo16.json"));

        AssertEqual("flac", settings.AudioCodec);
        AssertEqual("flac", settings.OutputFormat);
        AssertEqual(48000, settings.OutputSampleRate);
        AssertEqual(2, settings.OutputChannels);
        AssertEqual("s16", settings.OutputSampleFormat);
        AssertEqual(null, settings.Bitrate);
    }

    static void CaptureModeAliasesNormalize()
    {
        AssertEqual(CaptureModes.Device, CaptureModes.Normalize("driver"));
        AssertEqual(CaptureModes.Process, CaptureModes.Normalize("app"));
    }

    static void FfmpegInputFormatMapping()
    {
        AssertEqual("f32le", WaveFormatUtilities.GetFfmpegInputFormat(WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)));
        AssertEqual("s16le", WaveFormatUtilities.GetFfmpegInputFormat(new WaveFormat(44100, 16, 2)));
    }

    static void FfmpegArgumentsForFlac()
    {
        var settings = new AppSettings
        {
            Host = "127.0.0.1",
            Port = 18080,
            Bitrate = null,
            AudioCodec = "flac",
            OutputFormat = "flac",
            OutputSampleRate = 48000,
            OutputChannels = 2,
            OutputSampleFormat = "s16"
        };

        var args = FfmpegProcess.BuildArguments("f32le", 48000, 2, settings);

        AssertContains("-codec:a flac", args);
        AssertContains("-sample_fmt s16", args);
        AssertContains("-f flac", args);
        AssertContains("tcp://127.0.0.1:18080", args);
        AssertFalse(args.Any(arg => arg.StartsWith("-b:a", StringComparison.Ordinal)));
    }

    static void FfmpegDiagnosticsForRunningProcess()
    {
        using var process = Process.GetCurrentProcess();

        var message = FfmpegProcess.DescribeUnexpectedExit(process);

        AssertTrue(message.Contains($"pid {process.Id}", StringComparison.Ordinal));
        AssertTrue(message.Contains("processo ainda ativo", StringComparison.Ordinal));
    }

    static void FfmpegSessionReconnectsAfterTcpReset()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var firstConnection = new ManualResetEventSlim();
        var secondConnection = new ManualResetEventSlim();
        var firstPid = 0;
        var serverTask = Task.Run(async () =>
        {
            using var first = await listener.AcceptTcpClientAsync(cancellation.Token);
            firstConnection.Set();
            using var firstStream = first.GetStream();
            var buffer = new byte[65536];
            _ = await firstStream.ReadAsync(buffer, cancellation.Token);
            first.Close();

            using var second = await listener.AcceptTcpClientAsync(cancellation.Token);
            secondConnection.Set();
            using var secondStream = second.GetStream();
            _ = await secondStream.ReadAsync(buffer, cancellation.Token);
        }, cancellation.Token);

        var settings = new AppSettings
        {
            Host = "127.0.0.1",
            Port = port,
            AudioCodec = "flac",
            OutputFormat = "flac",
            OutputSampleRate = 48000,
            OutputChannels = 2,
            OutputSampleFormat = "s16",
            FfmpegLogLevel = "error",
            FfmpegExitTimeoutMs = 1000
        };

        using (var session = new FfmpegSession(
            FfmpegLocator.Find(null),
            "f32le",
            48000,
            2,
            settings,
            cancellation.Token))
        {
            AssertTrue(firstConnection.Wait(TimeSpan.FromSeconds(5)));
            firstPid = session.ProcessId;
            var pcm = new byte[38400];
            for (var i = 0; i < 80 && !secondConnection.IsSet; i++)
            {
                session.WriteAsync(pcm, cancellation.Token).GetAwaiter().GetResult();
                Thread.Sleep(25);
            }

            AssertTrue(secondConnection.Wait(TimeSpan.FromSeconds(5)));
            AssertTrue(session.ProcessId != firstPid);
        }

        cancellation.Cancel();
        listener.Stop();
        try
        {
            serverTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal do servidor de teste.
        }
    }

    static void FfmpegSessionReconnectsAfterTwoTcpResets()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var thirdConnection = new ManualResetEventSlim();
        var serverTask = Task.Run(async () =>
        {
            for (var connectionNumber = 1; connectionNumber <= 3; connectionNumber++)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
                using var stream = client.GetStream();
                var buffer = new byte[65536];
                _ = await stream.ReadAsync(buffer, cancellation.Token);
                if (connectionNumber < 3)
                {
                    client.Close();
                    continue;
                }

                thirdConnection.Set();
                await Task.Delay(200, cancellation.Token);
            }
        }, cancellation.Token);

        using (var session = new FfmpegSession(
            FfmpegLocator.Find(null),
            "f32le",
            48000,
            2,
            ReconnectSettings(port),
            cancellation.Token))
        {
            var pcm = new byte[38400];
            for (var i = 0; i < 180 && !thirdConnection.IsSet; i++)
            {
                session.WriteAsync(pcm, cancellation.Token).GetAwaiter().GetResult();
                Thread.Sleep(25);
            }

            AssertTrue(thirdConnection.Wait(TimeSpan.FromSeconds(5)));
        }

        cancellation.Cancel();
        listener.Stop();
        try
        {
            serverTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal do servidor de teste.
        }
    }

    static void FfmpegSessionStopsDuringReconnect()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var firstConnection = new ManualResetEventSlim();
        var firstClosed = new ManualResetEventSlim();
        var secondConnection = new ManualResetEventSlim();
        var serverTask = Task.Run(async () =>
        {
            using var first = await listener.AcceptTcpClientAsync(cancellation.Token);
            firstConnection.Set();
            using var stream = first.GetStream();
            var buffer = new byte[65536];
            _ = await stream.ReadAsync(buffer, cancellation.Token);
            first.Close();
            firstClosed.Set();
            try
            {
                using var second = await listener.AcceptTcpClientAsync(cancellation.Token);
                secondConnection.Set();
            }
            catch (OperationCanceledException)
            {
                // A reconexao nao deve ocorrer depois do dispose.
            }
        }, cancellation.Token);

        using (var session = new FfmpegSession(
            FfmpegLocator.Find(null),
            "f32le",
            48000,
            2,
            ReconnectSettings(port),
            cancellation.Token))
        {
            AssertTrue(firstConnection.Wait(TimeSpan.FromSeconds(5)));
            session.WriteAsync(new byte[38400], cancellation.Token).GetAwaiter().GetResult();
            AssertTrue(firstClosed.Wait(TimeSpan.FromSeconds(5)));
        }

        Thread.Sleep(1500);
        AssertFalse(secondConnection.IsSet);
        cancellation.Cancel();
        listener.Stop();
        try
        {
            serverTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal do servidor de teste.
        }
    }

    static void FfmpegSessionReconnectsWhenServerReturns()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var session = new FfmpegSession(
            FfmpegLocator.Find(null),
            "f32le",
            48000,
            2,
            ReconnectSettings(port),
            cancellation.Token);

        // Deixa a tentativa inicial e a primeira tentativa do backoff falharem.
        Thread.Sleep(1200);
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var connected = new ManualResetEventSlim();
        var serverTask = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
            connected.Set();
            using var stream = client.GetStream();
            var buffer = new byte[65536];
            _ = await stream.ReadAsync(buffer, cancellation.Token);
        }, cancellation.Token);

        var pcm = new byte[38400];
        for (var i = 0; i < 160 && !connected.IsSet; i++)
        {
            session.WriteAsync(pcm, cancellation.Token).GetAwaiter().GetResult();
            Thread.Sleep(25);
        }

        AssertTrue(connected.Wait(TimeSpan.FromSeconds(6)));
        cancellation.Cancel();
        listener.Stop();
        try
        {
            serverTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Encerramento normal do servidor de teste.
        }
    }

    static AppSettings ReconnectSettings(int port)
    {
        return new AppSettings
        {
            Host = "127.0.0.1",
            Port = port,
            AudioCodec = "flac",
            OutputFormat = "flac",
            OutputSampleRate = 48000,
            OutputChannels = 2,
            OutputSampleFormat = "s16",
            FfmpegLogLevel = "error",
            FfmpegExitTimeoutMs = 1000
        };
    }

    static void MediaPlayerProfileUsesProcessMode()
    {
        var settings = CaptureSettings.LoadFromJson(File.ReadAllText("configs/capture/media-player.json"));

        AssertEqual(CaptureModes.Process, CaptureModes.Normalize(settings.CaptureMode));
        AssertEqual("Microsoft.Media.Player", settings.ProcessName);
    }

    static void SplitProfilesBuildDeviceConfiguration()
    {
        var settings = AppSettings.LoadFromProfileJson(
            File.ReadAllText("configs/uplink/flac-48000-stereo16.json"),
            File.ReadAllText("configs/capture/device.json"));

        AssertEqual("flac", settings.AudioCodec);
        AssertEqual(48000, settings.OutputSampleRate);
        AssertEqual(CaptureModes.Device, settings.CaptureMode);
        AssertEqual(-1, settings.DeviceIndex);
    }

    static void DefaultsProfileBuildsConfiguration()
    {
        var defaults = DefaultsSettings.LoadFromJson(File.ReadAllText("configs/defaults.json"));
        defaults.Validate("configs/defaults.json");

        var expectedUplinkPath = AppSettings.ResolveProfilePath(defaults.Uplink!, "configs", "uplink");
        var expectedCapturePath = AppSettings.ResolveProfilePath(defaults.Capture!, "configs", "capture");
        var expected = AppSettings.LoadFromProfileJson(
            File.ReadAllText(expectedUplinkPath),
            File.ReadAllText(expectedCapturePath));

        var settings = AppSettings.Load(["--duration", "1"]);

        AssertEqual(expected.AudioCodec, settings.AudioCodec);
        AssertEqual(expected.OutputSampleRate, settings.OutputSampleRate);
        AssertEqual(expected.CaptureMode, settings.CaptureMode);
        AssertEqual(Path.GetFileName(expectedUplinkPath), Path.GetFileName(settings.UplinkConfigPath ?? ""));
        AssertEqual(Path.GetFileName(expectedCapturePath), Path.GetFileName(settings.CaptureConfigPath ?? ""));
    }

    static void InvalidDefaultsFail()
    {
        var defaults = DefaultsSettings.LoadFromJson("""{"uplink":"flac-48000-stereo16"}""");

        AssertThrows<InvalidOperationException>(() => defaults.Validate("configs/defaults.json"));
    }

    static void InvalidUplinkPortFails()
    {
        var settings = new AppSettings { Port = 70000 };

        AssertThrows<InvalidOperationException>(settings.Validate);
    }

    static void ProcessCaptureWithoutTargetFails()
    {
        var settings = new AppSettings
        {
            CaptureMode = CaptureModes.Process,
            ProcessId = null,
            ProcessName = null
        };

        AssertThrows<InvalidOperationException>(settings.Validate);
    }

    static void MissingProfileFails()
    {
        AssertThrows<FileNotFoundException>(() => AppSettings.ResolveProfilePath("nao-existe", "configs", "capture"));
    }

    static void ProfileShortcutResolvesConfigFolder()
    {
        var uplink = AppSettings.ResolveProfilePath("flac-48000-stereo16", "configs", "uplink");
        var capture = AppSettings.ResolveProfilePath("device", "configs", "capture");

        AssertTrue(uplink.EndsWith(Path.Combine("configs", "uplink", "flac-48000-stereo16.json"), StringComparison.OrdinalIgnoreCase));
        AssertTrue(capture.EndsWith(Path.Combine("configs", "capture", "device.json"), StringComparison.OrdinalIgnoreCase));
    }

    static void SpotifyProfileUsesProcessMode()
    {
        var settings = CaptureSettings.LoadFromJson(File.ReadAllText("configs/capture/spotify.json"));

        AssertEqual(CaptureModes.Process, CaptureModes.Normalize(settings.CaptureMode));
        AssertEqual("Spotify", settings.ProcessName);
    }

    static void ChromeProfileUsesProcessMode()
    {
        var settings = CaptureSettings.LoadFromJson(File.ReadAllText("configs/capture/chrome.json"));

        AssertEqual(CaptureModes.Process, CaptureModes.Normalize(settings.CaptureMode));
        AssertEqual("chrome", settings.ProcessName);
    }

    static void CaptureOverrideResolvesSpotify()
    {
        var settings = AppSettings.Load(["--capture", "spotify", "--print-config"]);

        AssertEqual(CaptureModes.Process, settings.CaptureMode);
        AssertEqual("Spotify", settings.ProcessName);
        AssertEqual("spotify.json", Path.GetFileName(settings.CaptureConfigPath ?? ""));
    }

    static void CaptureOverrideResolvesChrome()
    {
        var settings = AppSettings.Load(["--capture", "chrome", "--print-config"]);

        AssertEqual(CaptureModes.Process, settings.CaptureMode);
        AssertEqual("chrome", settings.ProcessName);
        AssertEqual("chrome.json", Path.GetFileName(settings.CaptureConfigPath ?? ""));
    }

    static void PrintConfigFlagIsParsed()
    {
        var defaults = DefaultsSettings.LoadFromJson(File.ReadAllText("configs/defaults.json"));
        defaults.Validate("configs/defaults.json");
        var settings = AppSettings.Load(["--print-config"]);

        AssertTrue(settings.PrintResolvedConfig);
        AssertEqual(
            Path.GetFileName(AppSettings.ResolveProfilePath(defaults.Capture!, "configs", "capture")),
            Path.GetFileName(settings.CaptureConfigPath ?? ""));
    }

    static void BackgroundCommandsAreDetected()
    {
        AssertTrue(BackgroundService.IsStartCommand(["start"]));
        AssertTrue(BackgroundService.IsStatusCommand(["status"]));
        AssertTrue(BackgroundService.IsStopCommand(["stop"]));
        AssertTrue(BackgroundService.IsLogCommand(["log"]));
        AssertTrue(BackgroundService.IsServerChild(["--server-child"]));
    }

    static void CommandsDoNotMatchOptionValues()
    {
        AssertFalse(BackgroundService.IsStartCommand(["--process-name", "start"]));
        AssertFalse(BackgroundService.IsStopCommand(["--capture", "stop"]));
        AssertFalse(BackgroundService.IsSwitchCommand(["--capture", "spotify"]));
        AssertTrue(CommandLine.HasCaptureSelection(["--capture", "spotify"]));
        AssertTrue(BackgroundService.IsSwitchCommand(["switch", "--capture", "spotify"]));
    }

    static void RuntimeDirectoryUsesApplicationBase()
    {
        AssertEqual(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "runtime")),
            Path.GetFullPath(BackgroundService.RuntimeDirectory));
    }

    static void PcmConverterNormalizesFormat()
    {
        var inputFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 1);
        var converter = new PcmFrameConverter(inputFormat, 48000, 2);
        var input = FloatBytes(Enumerable.Repeat(0.25f, 4410).ToArray());

        var output = converter.Convert(input, input.Length);

        AssertTrue(output.Length > 0);
        AssertEqual(0, output.Length % (sizeof(float) * 2));
        var outputFrames = output.Length / (sizeof(float) * 2);
        AssertTrue(outputFrames >= 4700 && outputFrames <= 4820);
        AssertTrue(AudioProcessing.EstimateLevel(
            output,
            output.Length,
            WaveFormat.CreateIeeeFloatWaveFormat(48000, 2)) > 0.1);
    }

    static void CaptureRouterHotSwapsSources()
    {
        var first = new FakeCaptureSource("first", WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        var second = new FakeCaptureSource("second", WaveFormat.CreateIeeeFloatWaveFormat(44100, 1));
        var sources = new Queue<FakeCaptureSource>([first, second]);
        var bytesReceived = 0;

        using var router = new AudioCaptureRouter(
            48000,
            2,
            (buffer, _) => bytesReceived += buffer.Length,
            _ => throw new InvalidOperationException("captura ativa parou"),
            _ => Task.FromResult<IAudioCaptureSource>(sources.Dequeue()));

        router.SwitchAsync(new AppSettings()).GetAwaiter().GetResult();
        first.Emit(FloatBytes(Enumerable.Repeat(0.2f, 960).ToArray()));
        var afterFirst = bytesReceived;
        AssertTrue(afterFirst > 0);

        router.SwitchAsync(new AppSettings()).GetAwaiter().GetResult();
        AssertTrue(first.Stopped);
        AssertTrue(first.Disposed);
        first.Emit(FloatBytes(Enumerable.Repeat(0.8f, 960).ToArray()));
        AssertEqual(afterFirst, bytesReceived);

        second.Emit(FloatBytes(Enumerable.Repeat(0.3f, 441).ToArray()));
        AssertTrue(bytesReceived > afterFirst);
        AssertEqual("second", router.Description);
    }

    static void FailedCaptureSwitchKeepsCurrentSource()
    {
        var current = new FakeCaptureSource("current", WaveFormat.CreateIeeeFloatWaveFormat(48000, 2));
        var attempts = 0;
        var bytesReceived = 0;

        using var router = new AudioCaptureRouter(
            48000,
            2,
            (buffer, _) => bytesReceived += buffer.Length,
            _ => { },
            _ => attempts++ == 0
                ? Task.FromResult<IAudioCaptureSource>(current)
                : throw new InvalidOperationException("fonte indisponivel"));

        router.SwitchAsync(new AppSettings()).GetAwaiter().GetResult();
        AssertThrows<InvalidOperationException>(() =>
            router.SwitchAsync(new AppSettings()).GetAwaiter().GetResult());

        current.Emit(FloatBytes(Enumerable.Repeat(0.2f, 960).ToArray()));
        AssertTrue(bytesReceived > 0);
        AssertFalse(current.Disposed);
        AssertEqual("current", router.Description);
    }

    static void ControlPipeRoundTrip()
    {
        var pipeName = $"windows-pcaudio-uplink-test-{Guid.NewGuid():N}";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = new ControlServer(
            pipeName,
            request => Task.FromResult(new ControlResponse
            {
                Success = request.Command == ControlCommands.Status,
                Message = request.Command,
                State = new BackgroundServiceState { ProcessId = 123, Source = "fake" }
            }),
            () => { });
        var serverTask = server.RunAsync(cancellation.Token);

        var response = ControlClient.SendAsync(
            new ControlRequest { Command = ControlCommands.Status },
            pipeName,
            cancellation.Token).GetAwaiter().GetResult();

        AssertTrue(response.Success);
        AssertEqual("status", response.Message);
        AssertEqual("fake", response.State?.Source);
        cancellation.Cancel();
        serverTask.GetAwaiter().GetResult();
    }

    static void ControlPipeCarriesSwitchAndStop()
    {
        var pipeName = $"windows-pcaudio-uplink-test-{Guid.NewGuid():N}";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var clientTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        CaptureSettings? receivedCapture = null;
        var stopRequested = false;
        var server = new ControlServer(
            pipeName,
            request =>
            {
                receivedCapture = request.Capture;
                return Task.FromResult(new ControlResponse { Success = true, Message = request.Command });
            },
            () =>
            {
                stopRequested = true;
                cancellation.Cancel();
            });
        var serverTask = server.RunAsync(cancellation.Token);

        var switched = ControlClient.SendAsync(
            new ControlRequest
            {
                Command = ControlCommands.SwitchCapture,
                Capture = new CaptureSettings { CaptureMode = CaptureModes.Process, ProcessName = "Spotify" }
            },
            pipeName,
            clientTimeout.Token).GetAwaiter().GetResult();
        AssertTrue(switched.Success);
        AssertEqual("Spotify", receivedCapture?.ProcessName);

        var stopped = ControlClient.SendAsync(
            new ControlRequest { Command = ControlCommands.Stop },
            pipeName,
            clientTimeout.Token).GetAwaiter().GetResult();
        AssertTrue(stopped.Success);
        serverTask.GetAwaiter().GetResult();
        AssertTrue(stopRequested);
    }

    static void MockUplinkProfileUsesLoopback()
    {
        var settings = UplinkSettings.LoadFromJson(File.ReadAllText("configs/uplink/mock-flac-48000-stereo16.json"));

        AssertEqual("127.0.0.1", settings.Host);
        AssertEqual(18081, settings.Port);
        AssertEqual("flac", settings.AudioCodec);
        AssertEqual(48000, settings.OutputSampleRate);
    }

    static void ProfileEnablesReceiverConfirmation()
    {
        var settings = UplinkSettings.LoadFromJson(File.ReadAllText("configs/uplink/flac-48000-stereo16.json"));
        var app = new AppSettings();
        settings.ApplyTo(app);

        AssertTrue(app.ConfirmReceiverIngest);
        AssertEqual(18081, app.ReceiverStatusPort);
        AssertEqual(10000, app.ReceiverConfirmationTimeoutMs);
    }

    static void UplinkDestinationsPreserveOrder()
    {
        var resolved = UplinkDestinationSelector.Resolve(
            "192.168.15.14",
            18080,
            [
                new UplinkDestination("192.168.15.18", 18080),
                new UplinkDestination("192.168.15.14", 18080),
                new UplinkDestination("192.168.15.18", 18080)
            ]);

        AssertEqual(2, resolved.Count);
        AssertEqual("192.168.15.14:18080", resolved[0].ToString());
        AssertEqual("192.168.15.18:18080", resolved[1].ToString());
    }

    static void RollingLogKeepsLastThousandLines()
    {
        var path = Path.Combine(Path.GetTempPath(), "windows-pcaudio-uplink-tests", $"{Guid.NewGuid():N}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            using (var writer = new RollingFileTextWriter(path, 1000))
            {
                for (var i = 0; i < 1005; i++)
                {
                    writer.WriteLine($"line-{i}");
                }
            }

            var lines = File.ReadAllLines(path);
            AssertEqual(1000, lines.Length);
            AssertTrue(lines[0].Contains("line-5", StringComparison.Ordinal));
            AssertTrue(lines[^1].Contains("line-1004", StringComparison.Ordinal));
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    static void LogIsDisplayedNewestFirst()
    {
        var ordered = BackgroundService.OrderLogLinesDescending(["oldest", "middle", "newest"]).ToArray();

        AssertEqual("newest", ordered[0]);
        AssertEqual("oldest", ordered[^1]);
    }

    static void RollingLogFallsBackWhenLocked()
    {
        var path = Path.Combine(Path.GetTempPath(), "windows-pcaudio-uplink-tests", $"{Guid.NewGuid():N}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            using (var locked = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            using (var writer = new RollingFileTextWriter(path, 1000))
            {
                writer.WriteLine("fallback-line");
            }

            AssertTrue(File.Exists(path + ".fallback"));
            AssertTrue(File.ReadAllText(path + ".fallback").Contains("fallback-line", StringComparison.Ordinal));
        }
        finally
        {
            foreach (var candidate in new[] { path, path + ".fallback" })
            {
                try
                {
                    File.Delete(candidate);
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }
    }

    static void LogArchivePreservesPreviousLog()
    {
        var root = Path.Combine(Path.GetTempPath(), "windows-pcaudio-uplink-tests", Guid.NewGuid().ToString("N"));
        var logPath = Path.Combine(root, "server.log");
        var archive = Path.Combine(root, "log-archive");
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(logPath, "previous log line");
            var archivedPath = BackgroundService.ArchiveLogFile(logPath, archive, 123);

            AssertTrue(archivedPath is not null);
            AssertFalse(File.Exists(logPath));
            AssertTrue(File.ReadAllText(archivedPath!).Contains("previous log line", StringComparison.Ordinal));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    static void CrashDiagnosticsWritesReport()
    {
        var directory = BackgroundService.RuntimeDirectory;
        Directory.CreateDirectory(directory);
        var before = Directory.GetFiles(directory, "crash-*.txt").ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            Diagnostics.WriteCrash(
                "test-crash-report",
                new InvalidOperationException("diagnostic marker"),
                "test details");
            var report = Directory.GetFiles(directory, "crash-*.txt")
                .Where(path => !before.Contains(path, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            AssertTrue(report is not null);
            var text = File.ReadAllText(report!);
            AssertTrue(text.Contains("test-crash-report", StringComparison.Ordinal));
            AssertTrue(text.Contains("diagnostic marker", StringComparison.Ordinal));
            AssertTrue(text.Contains("test details", StringComparison.Ordinal));
        }
        finally
        {
            foreach (var report in Directory.GetFiles(directory, "crash-*.txt")
                         .Where(path => !before.Contains(path, StringComparer.OrdinalIgnoreCase)))
            {
                try
                {
                    File.Delete(report);
                }
                catch
                {
                    // Best-effort cleanup.
                }
            }
        }
    }

    static void GainClampsPcm16()
    {
        var input = BitConverter.GetBytes((short)30000);
        var output = AudioProcessing.ApplyGain(input, input.Length, new WaveFormat(48000, 16, 1), 2.0);

        AssertEqual(short.MaxValue, BitConverter.ToInt16(output, 0));
    }

    static void ProcessMatchingUsesNameAndTitle()
    {
        var candidate = new ProcessTarget(123, "Microsoft.Media.Player", "Reprodutor de Midia");

        AssertTrue(ProcessCatalog.IsMatch(candidate, "Media.Player"));
        AssertTrue(ProcessCatalog.IsMatch(candidate, "Reprodutor"));
    }

    static void Test(string name, Action action)
    {
        try
        {
            action();
            passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
            failed++;
        }
    }

    static void AssertEqual<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"expected '{expected}', actual '{actual}'");
        }
    }

    static void AssertContains(string expected, IReadOnlyList<string> actual)
    {
        if (!actual.Contains(expected))
        {
            throw new InvalidOperationException($"expected item '{expected}'");
        }
    }

    static void AssertTrue(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("expected true");
        }
    }

    static void AssertFalse(bool condition)
    {
        if (condition)
        {
            throw new InvalidOperationException("expected false");
        }
    }

    static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"expected exception {typeof(TException).Name}");
    }

    static byte[] FloatBytes(float[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    sealed class FakeCaptureSource : IAudioCaptureSource
    {
        public FakeCaptureSource(string description, WaveFormat waveFormat)
        {
            Description = description;
            WaveFormat = waveFormat;
        }

        public string Description { get; }
        public WaveFormat WaveFormat { get; }
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }
        public event EventHandler<AudioDataAvailableEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public void Emit(byte[] buffer)
        {
            DataAvailable?.Invoke(this, new AudioDataAvailableEventArgs(buffer, buffer.Length));
        }

        public void StartRecording() => Started = true;
        public void StopRecording() => Stopped = true;

        public void Dispose()
        {
            Disposed = true;
            GC.KeepAlive(RecordingStopped);
        }
    }
}
