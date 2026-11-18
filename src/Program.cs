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
        //   DiskSentinel.exe --screenshot <dir>             save a PNG of every tab of the main window
        if (args.Length >= 2 && args[0] == "--dump")
        {
            File.WriteAllText(args[1], string.Join(Environment.NewLine,
                SmartService.ReadAll().Select(d => d.DetailText())));
            return;
        }
        if (args.Length >= 3 && args[0] == "--render-icon")
        {
            using var bmp = TrayIconRenderer.RenderBitmap(int.Parse(args[1]), 64);
            bmp.Save(args[2], System.Drawing.Imaging.ImageFormat.Png);
            return;
        }
        if (args.Length >= 2 && args[0] == "--screenshot")
        {
            using var form = new MainForm(new HistoryStore());
            form.UpdateDisks(SmartService.ReadAll(), "");
            form.SaveScreenshots(args[1]);
            return;
        }

        // Reading SATA drives needs administrator rights. The installer registers a
        // scheduled task that starts elevated at logon, so this only prompts on manual launch.
        if (!IsAdmin() && !args.Contains("--no-elevate"))
        {
            try
            {
                var psi = new ProcessStartInfo(Environment.ProcessPath!) { Verb = "runas", UseShellExecute = true };
                foreach (var a in args) psi.ArgumentList.Add(a);
                Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception) { /* user declined UAC */ }
            return;
        }

        // Single instance: a second launch just asks the running one to show its window.
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Global\DiskSentinel.Show");
        using var mutex = new Mutex(true, @"Global\DiskSentinel.SingleInstance", out bool first);
        if (!first)
        {
            showEvent.Set();
            return;
        }

        var app = new TrayApp(showWindow: args.Contains("--show"));
        var registered = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => app.ShowWindow(), null, -1, false);
        Application.Run(app);
        registered.Unregister(null);
    }

    static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
