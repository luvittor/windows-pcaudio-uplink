using System.Windows.Forms;

namespace WindowsPcAudioUplink.Tray;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var instanceMutex = new Mutex(true, "Local\\WindowsPcAudioUplinkTray", out var firstInstance);
        if (!firstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
