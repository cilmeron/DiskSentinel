namespace DiskSentinel;

/// <summary>Owns one tray icon per drive and the main window; refreshes everything on a timer.
/// The app keeps running in the tray until "Exit" is chosen from a tray icon's menu.</summary>
public sealed class TrayApp : ApplicationContext
{
    readonly List<NotifyIcon> _icons = new();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = Settings.RefreshSeconds * 1000 };
    readonly ContextMenuStrip _menu = new();
    readonly HistoryStore _history = new();
    readonly MainForm _main;
    readonly HashSet<string> _alerted = new();
    List<DiskInfo> _disks = new();
    string _status = "Reading drives...";
    bool _busy;

    public TrayApp(bool showWindow)
    {
        _main = new MainForm(_history);

        _menu.Items.Add("Open DiskSentinel", null, (_, _) => _main.ShowFromTray());
        _menu.Items.Add("Refresh now", null, async (_, _) => await RefreshAsync());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitThread());

        HistoryStore.Log("DiskSentinel started");
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
        if (showWindow) _main.ShowFromTray();
    }

    /// <summary>Called when a second instance is launched: bring the existing window forward.</summary>
    public void ShowWindow() => _main.BeginInvoke(new Action(_main.ShowFromTray));

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
                _status = _disks.Count == 0 ? "No drives found. Run DiskSentinel as administrator." : "";
            }
        }
        catch (Exception ex)
        {
            _disks = new();
            _status = "Error reading drives: " + ex.Message;
        }
        finally { _busy = false; }

        foreach (var d in _disks) _history.Add(d);
        Apply();
        _main.UpdateDisks(_disks, _status);
    }

    void Apply()
    {
        // One icon per drive, or a single placeholder icon when nothing could be read.
        int wanted = Math.Max(1, _disks.Count);
        while (_icons.Count < wanted)
        {
            var ni = new NotifyIcon { ContextMenuStrip = _menu, Visible = true };
            ni.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) _main.ShowFromTray(); };
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
                string text = "DiskSentinel: " + _status;
                ni.Text = text.Length > 127 ? "DiskSentinel: no data" : text;
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
    }

    void CheckAlerts(NotifyIcon ni, DiskInfo d)
    {
        foreach (var p in d.Problems)
        {
            if (!_alerted.Add(d.Serial + p.Kind)) continue;
            string message = $"{d.Name}: {p.Message}";
            HistoryStore.Log("ALERT - " + message);
            ni.ShowBalloonTip(15000, "DiskSentinel", message, ToolTipIcon.Warning);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            foreach (var ni in _icons) { ni.Visible = false; ni.Icon?.Dispose(); ni.Dispose(); }
            _menu.Dispose();
            _main.Dispose();
        }
        base.Dispose(disposing);
    }
}
