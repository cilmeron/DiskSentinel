namespace DiskSentinel;

/// <summary>Owns one tray icon per drive and refreshes them on a timer.</summary>
public sealed class TrayApp : ApplicationContext
{
    readonly List<NotifyIcon> _icons = new();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = Settings.RefreshSeconds * 1000 };
    readonly ContextMenuStrip _menu = new();
    readonly DetailsForm _details = new();
    readonly HashSet<string> _alerted = new();
    List<DiskInfo> _disks = new();
    string _status = "Reading drives...";
    bool _busy;

    public TrayApp()
    {
        _menu.Items.Add("Show details", null, (_, _) => ShowDetails());
        _menu.Items.Add("Refresh now", null, async (_, _) => await RefreshAsync());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    void ShowDetails()
    {
        _details.ShowDisks(_disks, _status);
        if (!_details.Visible) _details.Show();
        _details.WindowState = FormWindowState.Normal;
        _details.Activate();
    }

    async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (!SmartService.Available)
            {
                _disks = new();
                _status = "smartctl.exe not found. Install smartmontools: winget install smartmontools.smartmontools";
            }
            else
            {
                _disks = await Task.Run(SmartService.ReadAll);
                _status = _disks.Count == 0
                    ? "No drives found. Run DiskSentinel as administrator."
                    : "";
            }
        }
        catch (Exception ex)
        {
            _disks = new();
            _status = "Error reading drives: " + ex.Message;
        }
        finally { _busy = false; }

        Apply();
    }

    void Apply()
    {
        // One icon per drive, or a single placeholder icon when nothing could be read.
        int wanted = Math.Max(1, _disks.Count);
        while (_icons.Count < wanted)
        {
            var ni = new NotifyIcon { ContextMenuStrip = _menu, Visible = true };
            ni.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowDetails(); };
            _icons.Add(ni);
        }
        while (_icons.Count > wanted)
        {
            var ni = _icons[^1];
            ni.Visible = false;
            ni.Icon?.Dispose();
            ni.Dispose();
            _icons.RemoveAt(_icons.Count - 1);
        }

        for (int i = 0; i < _icons.Count; i++)
        {
            var ni = _icons[i];
            var old = ni.Icon;
            if (_disks.Count == 0)
            {
                ni.Icon = TrayIconRenderer.Render(null);
                ni.Text = ("DiskSentinel: " + _status).Length > 127 ? "DiskSentinel: no data" : "DiskSentinel: " + _status;
            }
            else
            {
                var d = _disks[i];
                ni.Icon = TrayIconRenderer.Render(d.Temp);
                ni.Text = d.ToolTip();
                CheckAlerts(ni, d);
            }
            old?.Dispose();
        }

        if (_details.Visible) _details.ShowDisks(_disks, _status);
    }

    void CheckAlerts(NotifyIcon ni, DiskInfo d)
    {
        void Alert(string kind, string message)
        {
            if (_alerted.Add(d.Serial + kind))
                ni.ShowBalloonTip(15000, "DiskSentinel", message, ToolTipIcon.Warning);
        }
        if (d.Health < Settings.HealthWarning) Alert("health", $"{d.Name} health is {d.Health}%. Back up your data.");
        if (d.Temp >= Settings.TempCritical) Alert("temp", $"{d.Name} is at {d.Temp}°C.");
        if (d.SmartPassed == false) Alert("smart", $"{d.Name} failed its S.M.A.R.T. self-assessment.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            foreach (var ni in _icons) { ni.Visible = false; ni.Icon?.Dispose(); ni.Dispose(); }
            _menu.Dispose();
            _details.Dispose();
        }
        base.Dispose(disposing);
    }
}
