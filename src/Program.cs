using System.Diagnostics;
using System.Security.Principal;

namespace DiskSentinel;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Diagnostic helpers (no tray icon, no elevation):
        //   DiskSentinel.exe --dump <file>                  write drive details to a text file
        //   DiskSentinel.exe --render-icon <temp> <file>    write a tray icon preview PNG
        if (args.Length >= 2 && args[0] == "--dump")
        {
            File.WriteAllText(args[1], string.Join(Environment.NewLine + Environment.NewLine,
                SmartService.ReadAll().Select(d => d.DetailText())));
            return;
        }
        if (args.Length >= 3 && args[0] == "--render-icon")
        {
            using var bmp = TrayIconRenderer.RenderBitmap(int.Parse(args[1]), 64);
            bmp.Save(args[2], System.Drawing.Imaging.ImageFormat.Png);
            return;
        }

        // Reading SATA drives needs administrator rights. The installer registers a
        // scheduled task that starts elevated at logon, so this only prompts on manual launch.
        if (!IsAdmin() && !args.Contains("--no-elevate"))
        {
            try
            {
                Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { Verb = "runas", UseShellExecute = true });
            }
            catch (System.ComponentModel.Win32Exception) { /* user declined UAC */ }
            return;
        }

        using var mutex = new Mutex(true, @"Global\DiskSentinel.SingleInstance", out bool first);
        if (!first) return;

        Application.Run(new TrayApp());
    }

    static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
