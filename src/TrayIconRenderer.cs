using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace DiskSentinel;

/// <summary>Draws a small tray icon containing the drive temperature as a number.</summary>
public static class TrayIconRenderer
{
    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    public static Bitmap RenderBitmap(int? temp, int size)
    {
        Color back, fore = Color.Black;
        if (temp is null) back = Color.LightGray;
        else if (temp >= Settings.TempCritical) { back = Color.FromArgb(220, 50, 47); fore = Color.White; }
        else if (temp >= Settings.TempWarning) back = Color.FromArgb(255, 160, 40);
        else back = Color.FromArgb(255, 240, 77);

        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        using (var fill = new SolidBrush(back)) g.FillRectangle(fill, 0, 0, size, size);
        using (var pen = new Pen(Color.FromArgb(120, 0, 0, 0))) g.DrawRectangle(pen, 0, 0, size - 1, size - 1);

        string text = temp?.ToString() ?? "?";
        // Largest bold font whose text fits inside the icon with a 1px margin.
        var fmt = new StringFormat(StringFormat.GenericTypographic)
        { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        Font font = new("Arial", 1f, FontStyle.Bold, GraphicsUnit.Pixel);
        for (float px = size * 0.9f; px >= 6; px -= 0.5f)
        {
            font.Dispose();
            font = new Font("Arial", px, FontStyle.Bold, GraphicsUnit.Pixel);
            var m = g.MeasureString(text, font, int.MaxValue, fmt);
            if (m.Width <= size - 2 && m.Height <= size - 1) break;
        }
        using (font)
        using (var brush = new SolidBrush(fore))
            g.DrawString(text, font, brush, new RectangleF(0, 0, size, size), fmt);
        return bmp;
    }

    public static Icon Render(int? temp)
    {
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using var bmp = RenderBitmap(temp, size);
        IntPtr h = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(h).Clone(); }
        finally { DestroyIcon(h); }
    }
}
