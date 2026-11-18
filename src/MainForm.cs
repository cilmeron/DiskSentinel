namespace DiskSentinel;

/// <summary>Main window: drive list on the left, tabs (Overview, Temperature, S.M.A.R.T., Information, Log) on the right.
/// Closing it only hides it; the app keeps running in the tray.</summary>
public sealed class MainForm : Form
{
    static readonly Color Green = Color.FromArgb(200, 255, 200);
    static readonly Color Yellow = Color.FromArgb(255, 245, 190);
    static readonly Color Red = Color.FromArgb(255, 205, 205);
    static readonly Color Grey = Color.FromArgb(230, 230, 230);

    readonly HistoryStore _history;
    IReadOnlyList<DiskInfo> _disks = Array.Empty<DiskInfo>();
    string _status = "";

    readonly ListBox _list = new() { Dock = DockStyle.Top, DrawMode = DrawMode.OwnerDrawFixed, BorderStyle = BorderStyle.FixedSingle };
    readonly FlowLayoutPanel _drives = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    readonly ToolStripStatusLabel _statusLabel = new();

    // Overview
    readonly HealthBar _healthBar = new() { Dock = DockStyle.Top };
    readonly Label _statusText = new() { Dock = DockStyle.Fill, Padding = new Padding(8), AutoSize = false };
    readonly Label _vTemp = new(), _vPower = new(), _vLife = new(), _vWritten = new(), _vCycles = new();
    readonly HistoryChart _healthChart = new() { Dock = DockStyle.Fill, Unit = "%", FixedScale = true, FixedMin = 0, FixedMax = 100 };

    // Temperature
    readonly ComboBox _range = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    readonly Label _tempStats = new() { AutoSize = true, Padding = new Padding(12, 6, 0, 0) };
    readonly HistoryChart _tempChart = new() { Dock = DockStyle.Fill, Unit = DiskInfo.Deg, LineColor = Color.FromArgb(200, 60, 40) };

    readonly ListView _smart = MakeList(("ID", 45), ("Attribute", 210), ("Current", 65), ("Worst", 60), ("Threshold", 75), ("Data", 130), ("Status", 70));
    readonly ListView _info = MakeList(("Property", 180), ("Value", 480));
    readonly ListView _log = MakeList(("Time", 160), ("Event", 560));

    static readonly (string Name, TimeSpan? Span)[] Ranges =
    {
        ("Last hour", TimeSpan.FromHours(1)), ("Last 24 hours", TimeSpan.FromHours(24)),
        ("Last 7 days", TimeSpan.FromDays(7)), ("Last 30 days", TimeSpan.FromDays(30)), ("All history", null),
    };

    public MainForm(HistoryStore history)
    {
        _history = history;
        Text = "DiskSentinel";
        Font = new Font("Segoe UI", 9f);
        Size = new Size(1000, 720);
        MinimumSize = new Size(820, 600);
        StartPosition = FormStartPosition.CenterScreen;

        // ---- left side ----
        _list.ItemHeight = 62;
        _list.DrawItem += DrawDiskItem;
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        var left = new Panel { Dock = DockStyle.Left, Width = 290, Padding = new Padding(6) };
        left.Controls.Add(_drives);
        left.Controls.Add(new Label { Dock = DockStyle.Top, Height = 22, Text = "Volumes", Font = new Font(Font, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft });
        left.Controls.Add(_list);

        // ---- tabs ----
        _tabs.TabPages.Add(BuildOverview());
        _tabs.TabPages.Add(BuildTemperature());
        _tabs.TabPages.Add(new TabPage("S.M.A.R.T.") { Controls = { _smart } });
        _tabs.TabPages.Add(new TabPage("Information") { Controls = { _info } });
        _tabs.TabPages.Add(new TabPage("Log") { Controls = { _log } });
        _tabs.SelectedIndexChanged += (_, _) => ShowSelected();

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_statusLabel);

        Controls.Add(_tabs);
        Controls.Add(left);
        Controls.Add(statusStrip);
        _list.Height = _list.ItemHeight * 3 + 4;
    }

    static ListView MakeList(params (string Name, int Width)[] cols)
    {
        var lv = new ListView
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true,
            HideSelection = false, MultiSelect = true,
        };
        foreach (var (name, width) in cols) lv.Columns.Add(name, width);

        // Copy support: Ctrl+C, Ctrl+A and a right-click menu (tab-separated, pastes cleanly into Excel).
        var menu = new ContextMenuStrip();
        menu.Items.Add("Copy", null, (_, _) => CopyRows(lv, selectedOnly: true));
        menu.Items.Add("Select all", null, (_, _) => SelectAll(lv));
        menu.Items.Add("Copy all", null, (_, _) => CopyRows(lv, selectedOnly: false));
        lv.ContextMenuStrip = menu;
        lv.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.C) { CopyRows(lv, selectedOnly: true); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.A) { SelectAll(lv); e.Handled = true; }
        };
        return lv;
    }

    static void SelectAll(ListView lv)
    {
        foreach (ListViewItem i in lv.Items) i.Selected = true;
    }

    static void CopyRows(ListView lv, bool selectedOnly)
    {
        // Nothing selected means "copy everything".
        var rows = selectedOnly && lv.SelectedItems.Count > 0
            ? lv.SelectedItems.Cast<ListViewItem>()
            : lv.Items.Cast<ListViewItem>();
        string text = string.Join("\r\n", rows.Select(r =>
            string.Join("\t", r.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text))));
        if (text.Length > 0) Clipboard.SetText(text);
    }

    TabPage BuildOverview()
    {
        var page = new TabPage("Overview") { Padding = new Padding(8) };

        var stats = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(0, 10, 0, 6) };
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(string label, Label value)
        {
            value.AutoSize = true;
            value.Font = new Font(Font, FontStyle.Bold);
            value.Margin = new Padding(3, 3, 3, 3);
            stats.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3) });
            stats.Controls.Add(value);
        }
        Row("Temperature", _vTemp);
        Row("Power on time", _vPower);
        Row("Estimated remaining lifetime", _vLife);
        Row("Lifetime writes", _vWritten);
        Row("Power cycles", _vCycles);

        var chartBox = new Panel { Dock = DockStyle.Fill };
        chartBox.Controls.Add(_healthChart);
        chartBox.Controls.Add(new Label { Dock = DockStyle.Top, Text = "Health history (%)", Height = 22, Font = new Font(Font, FontStyle.Bold) });

        var statusBox = new Panel { Dock = DockStyle.Top, Height = 110, Padding = new Padding(0, 8, 0, 0) };
        statusBox.Controls.Add(_statusText);

        // Docking order: last added is laid out first, so add bottom-most first.
        page.Controls.Add(chartBox);
        page.Controls.Add(stats);
        page.Controls.Add(statusBox);
        page.Controls.Add(_healthBar);
        return page;
    }

    TabPage BuildTemperature()
    {
        var page = new TabPage("Temperature") { Padding = new Padding(8) };
        foreach (var r in Ranges) _range.Items.Add(r.Name);
        _range.SelectedIndex = 1;
        _range.SelectedIndexChanged += (_, _) => ShowSelected();
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, FlowDirection = FlowDirection.LeftToRight };
        top.Controls.Add(new Label { Text = "Range:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        top.Controls.Add(_range);
        top.Controls.Add(_tempStats);
        page.Controls.Add(_tempChart);
        page.Controls.Add(top);
        return page;
    }

    // ---------- public API ----------

    public void UpdateDisks(IReadOnlyList<DiskInfo> disks, string status)
    {
        string? selected = (_list.SelectedItem as DiskInfo)?.Serial;
        _disks = disks;
        _status = status;

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var d in disks) _list.Items.Add(d);
        _list.Height = Math.Max(1, disks.Count) * _list.ItemHeight + 4;
        int idx = selected is null ? 0 : Math.Max(0, disks.ToList().FindIndex(d => d.Serial == selected));
        _list.EndUpdate();
        if (disks.Count > 0) _list.SelectedIndex = idx;

        RefreshDrives();
        _statusLabel.Text = "Status last updated: " + DateTime.Now.ToString("g");
        ShowSelected();
    }

    public void ShowFromTray()
    {
        if (!Visible) Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    /// <summary>Diagnostic: saves one PNG per tab into the given directory.</summary>
    public void SaveScreenshots(string dir)
    {
        Directory.CreateDirectory(dir);
        Show();
        for (int i = 0; i < _tabs.TabCount; i++)
        {
            _tabs.SelectedIndex = i;
            Application.DoEvents();
            using var bmp = new Bitmap(Width, Height);
            DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
            bmp.Save(Path.Combine(dir, $"tab{i}_{_tabs.TabPages[i].Text.Replace(".", "")}.png"));
        }
    }

    // ---------- rendering ----------

    DiskInfo? Current => _list.SelectedItem as DiskInfo;

    static Color BarColor(DiskInfo d) =>
        d.Health is null ? Color.Gray :
        d.Problems.Count > 0 ? Color.FromArgb(220, 60, 50) :
        d.Health < 70 ? Color.FromArgb(235, 200, 40) : Color.FromArgb(60, 190, 60);

    void DrawDiskItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _list.Items.Count) return;
        var d = (DiskInfo)_list.Items[e.Index];
        var g = e.Graphics;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var bg = new SolidBrush(sel ? Color.FromArgb(200, 225, 250) : Color.White)) g.FillRectangle(bg, e.Bounds);
        using (var line = new Pen(Color.FromArgb(220, 220, 220))) g.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

        using var bold = new Font(Font, FontStyle.Bold);
        g.DrawString(d.Name, bold, Brushes.Black, e.Bounds.Left + 8, e.Bounds.Top + 4);
        g.DrawString("Health:", Font, Brushes.Black, e.Bounds.Left + 8, e.Bounds.Top + 24);
        var bar = new Rectangle(e.Bounds.Left + 62, e.Bounds.Top + 25, 130, 14);
        g.FillRectangle(Brushes.White, bar);
        if (d.Health is int h)
        {
            using var fill = new SolidBrush(BarColor(d));
            g.FillRectangle(fill, bar.Left, bar.Top, bar.Width * h / 100, bar.Height);
        }
        g.DrawRectangle(Pens.Gray, bar);
        g.DrawString(d.Health is int hh ? $"{hh} %" : "n/a", Font, Brushes.Black, bar.Right + 6, e.Bounds.Top + 24);
        g.DrawString("Temp:", Font, Brushes.Black, e.Bounds.Left + 8, e.Bounds.Top + 42);
        g.DrawString(d.Temp is int t ? $"{t} {DiskInfo.Deg}" : "n/a", Font, Brushes.Black, e.Bounds.Left + 62, e.Bounds.Top + 42);
    }

    void RefreshDrives()
    {
        _drives.SuspendLayout();
        _drives.Controls.Clear();
        foreach (var di in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.Fixed && x.IsReady))
        {
            long used = di.TotalSize - di.TotalFreeSpace;
            int pct = di.TotalSize == 0 ? 0 : (int)(used * 100 / di.TotalSize);
            string label = string.IsNullOrEmpty(di.VolumeLabel) ? "" : $" [{di.VolumeLabel}]";
            var box = new Panel { Width = 255, Height = 48 };
            box.Controls.Add(new Label { Text = $"{di.Name.TrimEnd('\\')}{label}   ({di.TotalSize / 1e9:N1} GB)", AutoSize = true, Left = 0, Top = 2 });
            box.Controls.Add(new Label { Text = $"Free: {di.TotalFreeSpace / 1e9:N1} GB", AutoSize = true, Left = 0, Top = 28 });
            box.Controls.Add(new ProgressBar { Left = 120, Top = 28, Width = 130, Height = 14, Value = Math.Clamp(pct, 0, 100) });
            _drives.Controls.Add(box);
        }
        _drives.ResumeLayout();
    }

    void ShowSelected()
    {
        var d = Current;
        if (d is null)
        {
            _healthBar.Value = null;
            _statusText.BackColor = Grey;
            _statusText.Text = string.IsNullOrEmpty(_status) ? "No drives found." : _status;
            _vTemp.Text = _vPower.Text = _vLife.Text = _vWritten.Text = _vCycles.Text = "";
            return;
        }

        // Overview
        _healthBar.Value = d.Health;
        _statusText.BackColor = d.Health is null ? Grey : d.Problems.Count > 0 ? Red : d.Health < 70 ? Yellow : Green;
        _statusText.Text = d.StatusText;
        _vTemp.Text = d.Temp is int t ? $"{t} {DiskInfo.Deg}" : "n/a";
        _vPower.Text = d.PowerOnText;
        _vLife.Text = d.RemainingText;
        _vWritten.Text = d.WrittenBytes is double w ? $"{w / 1e12:N2} TB" : "n/a";
        _vCycles.Text = d.PowerCycles is long c
            ? $"{c}" + (d.UnsafeShutdowns is long u ? $"  (unsafe shutdowns: {u})" : "") : "n/a";

        var samples = _history.For(d.Serial).ToList();
        var now = DateTime.Now;
        var healthPts = samples.Where(s => s.Health.HasValue).Select(s => (s.Time, (double)s.Health!.Value)).ToList();
        DateTime hFrom = healthPts.Count > 0 ? healthPts.Min(p => p.Time) : now.AddHours(-1);
        _healthChart.SetData(healthPts, hFrom, now.AddMinutes(1));

        // Temperature tab
        if (_tabs.SelectedIndex == 1 || _tempChart.Tag is null)
        {
            var span = Ranges[_range.SelectedIndex].Span;
            var tempPts = samples.Where(s => s.Temp.HasValue).Select(s => (s.Time, (double)s.Temp!.Value)).ToList();
            DateTime from = span is TimeSpan ts ? now - ts : (tempPts.Count > 0 ? tempPts.Min(p => p.Time) : now.AddHours(-1));
            _tempChart.SetData(tempPts, from, now.AddMinutes(1));
            var inRange = tempPts.Where(p => p.Time >= from).Select(p => p.Item2).ToList();
            _tempStats.Text = inRange.Count == 0
                ? "No samples in this range yet."
                : $"Current {d.Temp} {DiskInfo.Deg}    Min {inRange.Min():0} {DiskInfo.Deg}    Avg {inRange.Average():0.0} {DiskInfo.Deg}    Max {inRange.Max():0} {DiskInfo.Deg}    ({inRange.Count} samples)";
            _tempChart.Tag = true;
        }

        // S.M.A.R.T.
        // NVMe drives have a plain list of counters; SATA drives have ID/value/worst/threshold columns.
        bool plain = d.Attributes.Count > 0 && d.Attributes.All(a => a.Id == "");
        int[] full = { 45, 210, 65, 60, 75, 130, 70 };
        for (int col = 0; col < full.Length; col++)
            _smart.Columns[col].Width = plain ? col switch { 1 => 280, 5 => 180, 6 => 70, _ => 0 } : full[col];
        Fill(_smart, d.Attributes.Select(a =>
        {
            var item = new ListViewItem(new[] { a.Id, a.Name, a.Value, a.Worst, a.Threshold, a.Raw, a.Failed ? "FAILING" : "OK" });
            if (a.Failed) item.BackColor = Red;
            return item;
        }));

        Fill(_info, d.Info.Select(kv => new ListViewItem(new[] { kv.Key, kv.Value })));
        Fill(_log, HistoryStore.ReadLog().Select(e => new ListViewItem(new[] { e.Time, e.Message })));
    }

    /// <summary>Replaces a list's rows only if they changed, keeping the user's selection,
    /// so copying isn't interrupted by the periodic refresh.</summary>
    static void Fill(ListView lv, IEnumerable<ListViewItem> rows)
    {
        var items = rows.ToList();
        string signature = string.Join("\n", items.Select(i => string.Join("\t", i.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text))));
        if (lv.Tag as string == signature) return;

        var selected = lv.SelectedIndices.Cast<int>().ToList();
        lv.BeginUpdate();
        lv.Items.Clear();
        lv.Items.AddRange(items.ToArray());
        foreach (int i in selected) if (i < lv.Items.Count) lv.Items[i].Selected = true;
        lv.EndUpdate();
        lv.Tag = signature;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
