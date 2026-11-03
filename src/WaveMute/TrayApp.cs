using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using WaveMute.Sync;

namespace WaveMute;

internal sealed class TrayApp : ApplicationContext
{
    private readonly SyncService _service = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 350 };
    private readonly ToolStripMenuItem _state = new("Connecting…") { Enabled = false };
    private readonly ToolStripMenuItem _wave = new("Wave: —") { Enabled = false };
    private readonly ToolStripMenuItem _discord = new("Discord: —") { Enabled = false };
    private readonly ToolStripMenuItem _detail = new("Connecting…") { Enabled = false };
    private readonly ToolStripMenuItem _pause = new("Pause sync");
    private readonly Icon _muted = CreateIcon(Color.Firebrick, true);
    private readonly Icon _live = CreateIcon(Color.SeaGreen, false);
    private readonly Icon _waiting = CreateIcon(Color.DimGray, false);
    private readonly Control _dispatcher = new();
    private readonly Task _pipe;

    public TrayApp()
    {
        _dispatcher.CreateControl();
        var menu = new ContextMenuStrip();
        menu.Items.AddRange([_state, _wave, _discord, _detail, new ToolStripSeparator(), _pause]);
        _pause.Click += (_, _) => _service.SetPaused(!_service.Paused);
        menu.Items.Add("Reconnect", null, (_, _) => _service.Reconnect());
        menu.Items.Add("Open log", null, (_, _) => OpenLog());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        _tray = new NotifyIcon { Icon = _waiting, Text = "Wave Mute — Connecting", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => ShowStatus();
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
        _pipe = CommandPipe.ServeAsync(_service, () => _dispatcher.BeginInvoke(ExitThread), _stop.Token);
        AppLog.Write("Wave Mute started");
    }

    private void RefreshStatus()
    {
        var s = _service.Status;
        _state.Text = $"Wave Mute · {s.State}";
        _wave.Text = $"Wave: {FormatMute(s.WaveMuted)}";
        _discord.Text = $"Discord: {FormatMute(s.DiscordMuted)}{(s.DiscordDeafened == true ? " · Deafened" : "")}";
        _detail.Text = s.Detail.Length > 115 ? s.Detail[..112] + "…" : s.Detail;
        _pause.Text = _service.Paused ? "Resume sync" : "Pause sync";
        _tray.Icon = s.State == "Connected" ? s.WaveMuted == true ? _muted : _live : _waiting;
        var text = $"Wave Mute · {s.State} · {FormatMute(s.WaveMuted)}";
        _tray.Text = text[..Math.Min(63, text.Length)];
    }

    private void ShowStatus()
    {
        var s = _service.Status;
        MessageBox.Show($"{s.State}\n\nWave: {FormatMute(s.WaveMuted)}\nDiscord: {FormatMute(s.DiscordMuted)}\n\n{s.Detail}\n\nRight-click the tray icon to pause, reconnect, or exit.", "Wave Mute", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string FormatMute(bool? muted) => muted switch { true => "Muted", false => "Unmuted", _ => "Unavailable" };
    private static void OpenLog()
    {
        AppLog.Write("Log opened");
        Process.Start(new ProcessStartInfo("notepad.exe", AppLog.FilePath) { UseShellExecute = true });
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _stop.Cancel();
        _service.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _timer.Dispose();
        _dispatcher.Dispose();
        _muted.Dispose(); _live.Dispose(); _waiting.Dispose();
        AppLog.Write("Wave Mute stopped; mute states were left as they were");
        base.ExitThreadCore();
    }

    private static Icon CreateIcon(Color color, bool muted)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        graphics.FillEllipse(brush, 1, 1, 30, 30);
        using var pen = new Pen(Color.White, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(pen, 9, 9, 14, 14, 0, 180);
        graphics.DrawLine(pen, 16, 23, 16, 26);
        graphics.DrawLine(pen, 12, 26, 20, 26);
        graphics.FillRoundedRectangleCompat(Brushes.White, 13, 6, 6, 14, 3);
        if (muted) graphics.DrawLine(pen, 6, 6, 26, 26);
        var handle = bitmap.GetHicon();
        try { using var original = Icon.FromHandle(handle); return (Icon)original.Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
}

internal static class DrawingExtensions
{
    public static void FillRoundedRectangleCompat(this Graphics graphics, Brush brush, int x, int y, int width, int height, int radius)
    {
        using var path = new GraphicsPath();
        path.AddArc(x, y, radius * 2, radius * 2, 180, 180);
        path.AddArc(x, y + height - radius * 2, radius * 2, radius * 2, 0, 180);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }
}
