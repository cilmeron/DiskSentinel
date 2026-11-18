namespace DiskSentinel;

/// <summary>Simple window listing the full details of every drive. Hides instead of closing.</summary>
public sealed class DetailsForm : Form
{
    readonly TextBox _text = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        Font = new Font("Consolas", 10f),
        BackColor = Color.White,
    };

    public DetailsForm()
    {
        Text = "DiskSentinel";
        Size = new Size(640, 560);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_text);
    }

    public void ShowDisks(IReadOnlyList<DiskInfo> disks, string status)
    {
        string body = disks.Count == 0
            ? status
            : string.Join(Environment.NewLine + new string('-', 60) + Environment.NewLine,
                disks.Select(d => d.DetailText()));
        _text.Text = body.Replace("\r\n", "\n").Replace("\n", "\r\n");
        _text.Select(0, 0);
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
