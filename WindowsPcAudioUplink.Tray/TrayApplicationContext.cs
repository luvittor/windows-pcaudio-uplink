using System.Drawing;
using System.Windows.Forms;
using WindowsPcAudioUplink;

namespace WindowsPcAudioUplink.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    readonly NotifyIcon notifyIcon;
    readonly ToolStripMenuItem statusItem;
    readonly System.Windows.Forms.Timer statusTimer;
    readonly System.Windows.Forms.Timer animationTimer;
    readonly ServerProcessLaunch serverLaunch;
    readonly Icon blankIcon;
    readonly Icon noWavesIcon;
    readonly Icon oneWaveIcon;
    readonly Icon twoWavesIcon;
    readonly Icon redNoWavesIcon;
    readonly Icon redOneWaveIcon;
    readonly Icon redTwoWavesIcon;
    bool operationInProgress;
    bool statusRequestInProgress;
    bool isRunning;
    string visualState = "stopped";
    int animationFrame;
    string? lastCommandError;

    public TrayApplicationContext()
    {
        serverLaunch = CreateServerLaunch();
        blankIcon = TrayIconFactory.Create(TrayIconFrame.NoWaves, Color.Transparent);
        noWavesIcon = TrayIconFactory.Create(TrayIconFrame.NoWaves);
        oneWaveIcon = TrayIconFactory.Create(TrayIconFrame.OneWave);
        twoWavesIcon = TrayIconFactory.Create(TrayIconFrame.TwoWaves);
        redNoWavesIcon = TrayIconFactory.Create(TrayIconFrame.NoWaves, Color.Red);
        redOneWaveIcon = TrayIconFactory.Create(TrayIconFrame.OneWave, Color.Red);
        redTwoWavesIcon = TrayIconFactory.Create(TrayIconFrame.TwoWaves, Color.Red);
        statusItem = new ToolStripMenuItem("Status: consultando...") { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateAction("Iniciar", StartBackendAsync));

        var captureMenu = new ToolStripMenuItem("Trocar captura");
        captureMenu.DropDownItems.Add(CreateCaptureAction("Dispositivo", "device"));
        captureMenu.DropDownItems.Add(CreateCaptureAction("Spotify", "spotify"));
        captureMenu.DropDownItems.Add(CreateCaptureAction("Chrome", "chrome"));
        captureMenu.DropDownItems.Add(CreateCaptureAction("Reprodutor de Midia", "media-player"));
        menu.Items.Add(captureMenu);
        menu.Items.Add(CreateAction("Parar", StopBackendAsync));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateAction("Atualizar status", RefreshStatusAsync));
        menu.Items.Add(CreateAction("Abrir log", OpenLogAsync));
        menu.Items.Add(CreateAction("Abrir configuracoes", OpenConfigsAsync));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(CreateAction("Sair", () =>
        {
            ExitTray();
            return Task.CompletedTask;
        }));

        notifyIcon = new NotifyIcon
        {
            Text = "Windows PC Audio Uplink",
            Icon = noWavesIcon,
            Visible = true,
            ContextMenuStrip = menu
        };
        notifyIcon.MouseClick += NotifyIconMouseClick;

        animationTimer = new System.Windows.Forms.Timer { Interval = 400 };
        animationTimer.Tick += (_, _) => AdvanceAnimation();

        statusTimer = new System.Windows.Forms.Timer { Interval = 500 };
        statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        statusTimer.Start();
        _ = RefreshStatusAsync();
    }

    ToolStripMenuItem CreateAction(string text, Func<Task> action)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += async (_, _) => await ExecuteAsync(action);
        return item;
    }

    ToolStripMenuItem CreateCaptureAction(string text, string profile)
    {
        return CreateAction(text, () => SwitchCaptureAsync(profile));
    }

    void NotifyIconMouseClick(object? sender, MouseEventArgs args)
    {
        if (args.Button == MouseButtons.Left && notifyIcon.ContextMenuStrip is not null)
        {
            notifyIcon.ContextMenuStrip.Show(Cursor.Position);
        }
    }

    async Task RefreshStatusAsync()
    {
        if (operationInProgress || statusRequestInProgress)
        {
            return;
        }

        statusRequestInProgress = true;
        try
        {
            ControlResponse? response = null;
            try
            {
                response = await Task.Run(() =>
                    ControlClient.Send(new ControlRequest { Command = ControlCommands.Status }, 250));
            }
            catch (Exception)
            {
                // Sem servidor, o estado normal do tray e parado.
            }

            if (response?.Success == true && response.State is not null)
            {
                var state = response.State;
                var now = DateTimeOffset.UtcNow;
                var disconnectedFor = state.DisconnectedAt.HasValue
                    ? now - state.DisconnectedAt.Value
                    : TimeSpan.Zero;
                var isReconnecting = state.ConnectionState is "reconnecting" or "degraded";
                var isDegraded = isReconnecting && disconnectedFor >= ReconnectPolicy.DegradedAfter;
                var isAttempting = isReconnecting &&
                    state.NextReconnectAt.HasValue &&
                    now >= state.NextReconnectAt.Value;

                statusItem.Text = state.ConnectionState switch
                {
                    "degraded" => $"Status: conexao degradada | tentativa {state.ReconnectAttempt}",
                    "reconnecting" => $"Status: reconectando | tentativa {state.ReconnectAttempt}",
                    _ => $"Status: transmitindo | {state.Source}"
                };
                notifyIcon.Text = FitNotifyText(state.ConnectionState switch
                {
                    "degraded" => $"Uplink degradado ha {disconnectedFor:mm\\:ss}",
                    "reconnecting" => $"Reconectando: tentativa {state.ReconnectAttempt}",
                    _ => "Uplink: Mimic confirmado"
                });

                var nextVisualState = !isReconnecting
                    ? "connected"
                    : isAttempting
                        ? (isDegraded ? "reconnect-attempt-red" : "reconnect-attempt")
                        : (isDegraded ? "reconnect-waiting-red" : "reconnect-waiting");
                SetConnectionVisual(nextVisualState);
                return;
            }
        }
        finally
        {
            statusRequestInProgress = false;
        }

        statusItem.Text = lastCommandError is null ? "Status: parado" : $"Erro: {lastCommandError}";
        notifyIcon.Text = "Uplink: parado";
        SetConnectionVisual("stopped");
    }

    async Task StartBackendAsync()
    {
        statusItem.Text = "Status: iniciando...";
        notifyIcon.Text = "Uplink: iniciando";
        SetConnectionVisual("connecting");
        var exitCode = await Task.Run(() => BackgroundService.Start([], serverLaunch));
        EnsureSuccess(exitCode, "iniciar");
        lastCommandError = null;
        notifyIcon.ShowBalloonTip(2000, "Windows PC Audio Uplink", "Servidor iniciado.", ToolTipIcon.Info);
    }

    async Task StopBackendAsync()
    {
        var exitCode = await Task.Run(BackgroundService.Stop);
        EnsureSuccess(exitCode, "parar");
        lastCommandError = null;
        statusItem.Text = "Status: parado";
        notifyIcon.Text = "Uplink: parado";
        SetConnectionVisual("stopped");
    }

    async Task SwitchCaptureAsync(string profile)
    {
        var exitCode = await Task.Run(() =>
            BackgroundService.SwitchCapture(["switch", "--capture", profile]));
        EnsureSuccess(exitCode, "trocar a captura");
        lastCommandError = null;
    }

    static ServerProcessLaunch CreateServerLaunch()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "windows-pcaudio-uplink.exe");
        if (File.Exists(executable))
        {
            return new ServerProcessLaunch
            {
                FileName = executable,
                WorkingDirectory = AppContext.BaseDirectory
            };
        }

        var assembly = Path.Combine(AppContext.BaseDirectory, "windows-pcaudio-uplink.dll");
        if (!File.Exists(assembly))
        {
            throw new FileNotFoundException("Backend windows-pcaudio-uplink.exe nao encontrado.");
        }

        return new ServerProcessLaunch
        {
            FileName = "dotnet",
            AssemblyPath = assembly,
            WorkingDirectory = AppContext.BaseDirectory
        };
    }

    static void EnsureSuccess(int exitCode, string action)
    {
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Nao foi possivel {action}. Abra o log para ver o motivo.");
        }
    }

    Task OpenLogAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "runtime", "server.log");
        if (!File.Exists(path))
        {
            MessageBox.Show("Ainda nao existe um log.", "Windows PC Audio Uplink", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Task.CompletedTask;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
        return Task.CompletedTask;
    }

    Task OpenConfigsAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "configs");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
        return Task.CompletedTask;
    }

    async Task ExecuteAsync(Func<Task> action)
    {
        if (operationInProgress)
        {
            return;
        }

        operationInProgress = true;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            lastCommandError = exception.Message.Replace(Environment.NewLine, " ");
            statusItem.Text = $"Erro: {lastCommandError}";
            notifyIcon.ShowBalloonTip(4000, "Windows PC Audio Uplink", lastCommandError, ToolTipIcon.Error);
        }
        finally
        {
            operationInProgress = false;
            await RefreshStatusAsync();
        }
    }

    void SetConnectionVisual(string state)
    {
        if (visualState == state)
        {
            return;
        }

        visualState = state;
        animationFrame = 0;
        animationTimer.Stop();
        isRunning = state != "stopped";

        switch (state)
        {
            case "connecting":
            case "reconnect-attempt":
                notifyIcon.Icon = noWavesIcon;
                animationTimer.Start();
                return;
            case "reconnect-attempt-red":
                notifyIcon.Icon = redNoWavesIcon;
                animationTimer.Start();
                return;
            case "connected":
                notifyIcon.Icon = noWavesIcon;
                animationTimer.Start();
                return;
            case "reconnect-waiting":
                notifyIcon.Icon = noWavesIcon;
                animationTimer.Start();
                return;
            case "reconnect-waiting-red":
                notifyIcon.Icon = redNoWavesIcon;
                animationTimer.Start();
                return;
            default:
                isRunning = false;
                notifyIcon.Icon = noWavesIcon;
                return;
        }
    }

    void AdvanceAnimation()
    {
        if (!isRunning)
        {
            return;
        }

        switch (visualState)
        {
            case "connected":
                animationFrame = (animationFrame + 1) % 3;
                notifyIcon.Icon = GetWaveIcon(animationFrame, red: false);
                return;

            case "connecting":
            case "reconnect-attempt":
            case "reconnect-attempt-red":
                animationFrame = (animationFrame + 1) % 6;
                if (animationFrame % 2 == 1)
                {
                    notifyIcon.Icon = blankIcon;
                    return;
                }

                notifyIcon.Icon = GetWaveIcon(
                    animationFrame / 2,
                    red: visualState == "reconnect-attempt-red");
                return;

            case "reconnect-waiting":
            case "reconnect-waiting-red":
                animationFrame = (animationFrame + 1) % 2;
                if (animationFrame == 1)
                {
                    notifyIcon.Icon = blankIcon;
                    return;
                }

                notifyIcon.Icon = visualState == "reconnect-waiting-red"
                    ? redNoWavesIcon
                    : noWavesIcon;
                return;
        }
    }

    Icon GetWaveIcon(int frame, bool red)
    {
        if (red)
        {
            return frame switch
            {
                1 => redOneWaveIcon,
                2 => redTwoWavesIcon,
                _ => redNoWavesIcon
            };
        }

        return frame switch
        {
            1 => oneWaveIcon,
            2 => twoWavesIcon,
            _ => noWavesIcon
        };
    }

    void ExitTray()
    {
        statusTimer.Stop();
        animationTimer.Stop();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        blankIcon.Dispose();
        noWavesIcon.Dispose();
        oneWaveIcon.Dispose();
        twoWavesIcon.Dispose();
        redNoWavesIcon.Dispose();
        redOneWaveIcon.Dispose();
        redTwoWavesIcon.Dispose();
        ExitThread();
    }

    static string FitNotifyText(string value)
    {
        return value.Length <= 63 ? value : value[..60] + "...";
    }
}
