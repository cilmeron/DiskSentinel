using System.Drawing.Drawing2D;

namespace DiskSentinel;

/// <summary>A simple time-series line chart with grid, axis labels and a hover read-out.</summary>
public sealed class HistoryChart : Control
{
    List<(DateTime T, double V)> _points = new();
    DateTime _from, _to;
    double _yMin, _yMax;
    int _mouseX = -1;

    public string Unit { get; set; } = "";
    public Color LineColor { get; set; } = Color.FromArgb(30, 100, 200);
    public bool FixedScale { get; set; }
    public double FixedMin { get; set; }
    public double FixedMax { get; set; } = 100;

    public HistoryChart()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 8.5f);
    }

    public void SetData(IEnumerable<(DateTime T, double V)> points, DateTime from, DateTime to)
    {
        _points = points.Where(p => p.T >= from && p.T <= to).OrderBy(p => p.T).ToList();
        _from = from;
        _to = to;
        if (_points.Count > 0 && _from == _to) { _from = _from.AddHours(-1); }
        if (FixedScale) { _yMin = FixedMin; _yMax = FixedMax; }
        else if (_points.Count == 0) { _yMin = 20; _yMax = 60; }
        else
        {
            double lo = _points.Min(p => p.V), hi = _points.Max(p => p.V);
            _yMin = Math.Floor(lo - 3);
            _yMax = Math.Ceiling(hi + 3);
            if (_yMax - _yMin < 10) { _yMax = _yMin + 10; }
        }
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e) { _mouseX = e.X; Invalidate(); base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { _mouseX = -1; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int left = 46, right = 14, top = 12, bottom = 28;
        var plot = new Rectangle(left, top, Width - left - right, Height - top - bottom);
        if (plot.Width < 20 || plot.Height < 20) return;

        using var gridPen = new Pen(Color.FromArgb(225, 225, 225));
        using var axisPen = new Pen(Color.Gray);
        using var textBrush = new SolidBrush(Color.FromArgb(70, 70, 70));
        var right_ = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        var center = new StringFormat { Alignment = StringAlignment.Center };

        // Horizontal grid + Y labels
        for (int i = 0; i <= 5; i++)
        {
            double v = _yMin + (_yMax - _yMin) * i / 5;
            float y = plot.Bottom - (float)(plot.Height * i / 5.0);
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            g.DrawString($"{v:0}{Unit}", Font, textBrush, new RectangleF(0, y - 9, left - 4, 18), right_);
        }

        // Vertical grid + X labels
        double spanHours = Math.Max(1.0 / 60, (_to - _from).TotalHours);
        string fmt = spanHours <= 36 ? "HH:mm" : spanHours <= 24 * 60 ? "MM-dd HH:mm" : "yyyy-MM-dd";
        for (int i = 0; i <= 4; i++)
        {
            float x = plot.Left + plot.Width * i / 4f;
            g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            var t = _from + TimeSpan.FromHours(spanHours * i / 4);
            g.DrawString(t.ToString(fmt), Font, textBrush, new RectangleF(x - 45, plot.Bottom + 4, 90, 18), center);
        }
        g.DrawRectangle(axisPen, plot);

        if (_points.Count == 0)
        {
            g.DrawString("No history in this range yet. Samples are recorded while the app runs.",
                Font, textBrush, plot, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
            return;
        }

        PointF Map((DateTime T, double V) p) => new(
            plot.Left + (float)((p.T - _from).TotalSeconds / Math.Max(1, (_to - _from).TotalSeconds) * plot.Width),
            plot.Bottom - (float)((p.V - _yMin) / (_yMax - _yMin) * plot.Height));

        g.SetClip(plot);
        var pts = _points.Select(Map).ToArray();
        using var pen = new Pen(LineColor, 2f) { LineJoin = LineJoin.Round };
        if (pts.Length >= 2) g.DrawLines(pen, pts);
        using var dot = new SolidBrush(LineColor);
        if (pts.Length <= 60) foreach (var p in pts) g.FillEllipse(dot, p.X - 3, p.Y - 3, 6, 6);
        else g.FillEllipse(dot, pts[^1].X - 3, pts[^1].Y - 3, 6, 6);
        g.ResetClip();

        // Hover read-out: nearest sample to the mouse
        if (_mouseX >= plot.Left && _mouseX <= plot.Right)
        {
            int best = 0;
            for (int i = 1; i < pts.Length; i++)
                if (Math.Abs(pts[i].X - _mouseX) < Math.Abs(pts[best].X - _mouseX)) best = i;
            var p = pts[best];
            using var cross = new Pen(Color.FromArgb(120, Color.Black)) { DashStyle = DashStyle.Dash };
            g.DrawLine(cross, p.X, plot.Top, p.X, plot.Bottom);
            string label = $"{_points[best].V:0.#}{Unit}  {_points[best].T:yyyy-MM-dd HH:mm}";
            var size = g.MeasureString(label, Font);
            float lx = Math.Min(p.X + 8, plot.Right - size.Width - 2);
            float ly = Math.Max(plot.Top + 2, p.Y - size.Height - 6);
            g.FillRectangle(Brushes.LightYellow, lx - 2, ly, size.Width + 4, size.Height);
            g.DrawRectangle(Pens.Gray, lx - 2, ly, size.Width + 4, size.Height);
            g.DrawString(label, Font, Brushes.Black, lx, ly);
        }
    }
}

/// <summary>Horizontal red-yellow-green bar with a percentage, like Hard Disk Sentinel's.</summary>
public sealed class HealthBar : Control
{
    int? _value;

    public HealthBar()
    {
        DoubleBuffered = true;
        Height = 26;
        Font = new Font("Segoe UI", 9f);
    }

    public int? Value { get => _value; set { _value = value; Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        g.FillRectangle(Brushes.White, r);
        if (_value is int v)
        {
            using var brush = new LinearGradientBrush(r, Color.Red, Color.LimeGreen, LinearGradientMode.Horizontal)
            {
                InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.FromArgb(235, 40, 40), Color.FromArgb(250, 220, 40), Color.FromArgb(60, 200, 60) },
                    Positions = new[] { 0f, 0.5f, 1f },
                },
            };
            int w = (int)(r.Width * Math.Clamp(v, 0, 100) / 100.0);
            g.FillRectangle(brush, new Rectangle(0, 0, w, r.Height));
        }
        g.DrawRectangle(Pens.Gray, r);
        string text = _value is int x ? $"{x} %" : "n/a";
        var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, Font, Brushes.Black, r, fmt);
    }
}
