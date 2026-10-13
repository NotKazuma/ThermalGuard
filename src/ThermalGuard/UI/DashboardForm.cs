using System.Reflection;
using ThermalGuard.Core;

namespace ThermalGuard.UI;

/// <summary>
/// Live read-only view of what TrayForm is doing. Dark theme, per-hardware cards with load/cap bars
/// and short history sparklines, session stats, and FPS. Minimizing hides it to the tray instead of
/// the taskbar - the tray icon (double-click, or the Dashboard menu item) brings it back.
/// </summary>
public sealed class DashboardForm : Form
{
    private static readonly Color Bg = Color.FromArgb(20, 20, 23);
    private static readonly Color Fg = Color.FromArgb(225, 225, 230);
    private static readonly Color Muted = Color.FromArgb(150, 150, 158);

    private readonly Label _game = new() { AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = Fg };
    private readonly Label _statePill = new() { AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.White, Padding = new Padding(10, 4, 10, 4) };
    private readonly Label _elapsed = new() { AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 9) };

    private readonly Label _cpuTemp = BigValue();
    private readonly StatBar _cpuLoad = new() { BarColor = Color.FromArgb(90, 170, 255) };
    private readonly StatBar _cpuCap = new() { BarColor = Color.FromArgb(255, 170, 60) };
    private readonly Label _cpuClock = SmallMuted();
    private readonly Sparkline _cpuHistory = new() { LineColor = Color.FromArgb(90, 170, 255), Min = 30, Max = 105 };

    private readonly Label _gpuTemp = BigValue();
    private readonly StatBar _gpuLoad = new() { BarColor = Color.FromArgb(120, 220, 140) };
    private readonly StatBar _gpuCap = new() { BarColor = Color.FromArgb(255, 170, 60) };
    private readonly Label _gpuClock = SmallMuted();
    private readonly Sparkline _gpuHistory = new() { LineColor = Color.FromArgb(120, 220, 140), Min = 30, Max = 105 };

    private readonly Label _fpsValue = BigValue();
    private readonly Label _fpsStats = SmallMuted();
    private readonly Sparkline _fpsHistory = new() { LineColor = Color.FromArgb(255, 210, 90), Min = 0, Max = 144 };

    private readonly Label _cpuName = SmallMuted();
    private readonly Label _gpuName = SmallMuted();
    private readonly Label _flags = SmallMuted();

    private readonly LinkLabel _about = new() { Text = "About", ForeColor = Color.FromArgb(120, 160, 255), LinkColor = Color.FromArgb(120, 160, 255), AutoSize = true };
    private readonly Label _footer = new()
    {
        Text = $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?"}  •  minimizing sends ThermalGuard to the tray",
        ForeColor = Muted, Font = new Font("Segoe UI", 8), AutoSize = true,
    };

    private DateTime? _sessionStart;
    private int? _fpsMin, _fpsMax;
    private readonly List<int> _fpsSamples = new();

    public DashboardForm()
    {
        Text = "ThermalGuard Dashboard";
        Width = 740; Height = 560; MinimumSize = new Size(640, 480);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Bg; ForeColor = Fg;
        Icon = IconFactory.Dot(Color.MediumSeaGreen);

        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized) Hide(); };

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(20), BackColor = Bg };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, BackColor = Bg, WrapContents = false };
        var headerLeft = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, BackColor = Bg, Margin = new Padding(0, 0, 16, 0) };
        headerLeft.Controls.Add(_game);
        headerLeft.Controls.Add(_elapsed);
        header.Controls.Add(headerLeft);
        header.Controls.Add(_statePill);
        root.Controls.Add(header, 0, 0);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Bg, Margin = new Padding(0, 16, 0, 16) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        grid.Controls.Add(BuildCpuCard(), 0, 0);
        grid.Controls.Add(BuildGpuCard(), 1, 0);
        grid.Controls.Add(BuildFpsCard(), 0, 1);
        grid.Controls.Add(BuildSystemCard(), 1, 1);
        root.Controls.Add(grid, 0, 1);

        var footer = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, BackColor = Bg };
        _about.LinkClicked += (_, _) => new AboutForm().ShowDialog(this);
        footer.Controls.Add(_footer);
        footer.Controls.Add(new Label { Text = "   ", AutoSize = true });
        footer.Controls.Add(_about);
        root.Controls.Add(footer, 0, 2);

        Controls.Add(root);
        ShowIdle();
    }

    private static Label BigValue() => new() { AutoSize = true, Font = new Font("Segoe UI", 28, FontStyle.Bold), ForeColor = Fg, Margin = new Padding(0, 4, 0, 4) };
    private static Label SmallMuted() => new() { AutoSize = true, Font = new Font("Segoe UI", 9), ForeColor = Muted };

    private Control BuildCpuCard()
    {
        var c = new Card("CPU") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 8) };
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, BackColor = c.BackColor, WrapContents = false, AutoSize = false };
        stack.Controls.Add(_cpuTemp);
        stack.Controls.Add(_cpuClock);
        _cpuLoad.Width = 280; _cpuLoad.Margin = new Padding(0, 8, 0, 4);
        _cpuCap.Width = 280; _cpuCap.Margin = new Padding(0, 0, 0, 8);
        _cpuHistory.Width = 280;
        stack.Controls.Add(_cpuLoad);
        stack.Controls.Add(_cpuCap);
        stack.Controls.Add(_cpuHistory);
        c.Controls.Add(stack);
        return c;
    }

    private Control BuildGpuCard()
    {
        var c = new Card("GPU") { Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 8) };
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, BackColor = c.BackColor, WrapContents = false, AutoSize = false };
        stack.Controls.Add(_gpuTemp);
        stack.Controls.Add(_gpuClock);
        _gpuLoad.Width = 280; _gpuLoad.Margin = new Padding(0, 8, 0, 4);
        _gpuCap.Width = 280; _gpuCap.Margin = new Padding(0, 0, 0, 8);
        _gpuHistory.Width = 280;
        stack.Controls.Add(_gpuLoad);
        stack.Controls.Add(_gpuCap);
        stack.Controls.Add(_gpuHistory);
        c.Controls.Add(stack);
        return c;
    }

    private Control BuildFpsCard()
    {
        var c = new Card("FPS") { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 8, 0) };
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, BackColor = c.BackColor, WrapContents = false, AutoSize = false };
        stack.Controls.Add(_fpsValue);
        stack.Controls.Add(_fpsStats);
        _fpsHistory.Width = 280; _fpsHistory.Margin = new Padding(0, 8, 0, 0);
        stack.Controls.Add(_fpsHistory);
        c.Controls.Add(stack);
        return c;
    }

    private Control BuildSystemCard()
    {
        var c = new Card("System") { Dock = DockStyle.Fill, Margin = new Padding(8, 8, 0, 0) };
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, BackColor = c.BackColor, WrapContents = false, AutoSize = false };
        _cpuName.Margin = new Padding(0, 4, 0, 2);
        _gpuName.Margin = new Padding(0, 0, 0, 10);
        _flags.MaximumSize = new Size(280, 0);
        stack.Controls.Add(_cpuName);
        stack.Controls.Add(_gpuName);
        stack.Controls.Add(_flags);
        c.Controls.Add(stack);
        return c;
    }

    private void ShowIdle()
    {
        _game.Text = "Waiting for a game...";
        _statePill.Text = "IDLE"; _statePill.BackColor = Color.FromArgb(70, 70, 76);
        _elapsed.Text = "";
        _cpuTemp.Text = "--°C"; _gpuTemp.Text = "--°C"; _fpsValue.Text = "--";
    }

    /// <summary>Call from the UI thread (TrayForm's timer already runs there) with the latest snapshot.</summary>
    public void Apply(MonitorSnapshot s)
    {
        if (IsDisposed) return;

        if (!s.Attached)
        {
            if (_sessionStart is not null) { _sessionStart = null; _cpuHistory.Clear(); _gpuHistory.Clear(); _fpsHistory.Clear(); _fpsSamples.Clear(); _fpsMin = _fpsMax = null; }
            ShowIdle();
            return;
        }
        _sessionStart ??= DateTime.Now;

        _game.Text = s.Game;
        _elapsed.Text = $"Attached {FormatSpan(DateTime.Now - _sessionStart.Value)}";

        (_statePill.Text, _statePill.BackColor) = s.Level switch
        {
            Level.Warm => ("WARM", Color.FromArgb(190, 150, 40)),
            Level.Hot => ("HOT", Color.FromArgb(210, 110, 30)),
            Level.Critical => ("CRITICAL", Color.FromArgb(200, 50, 60)),
            _ => ("OK", Color.FromArgb(50, 150, 90)),
        };

        _cpuTemp.Text = Fmt(s.CpuTemp, "°C");
        _cpuClock.Text = $"{Fmt(s.CpuClockMHz, " MHz")}";
        _cpuLoad.Label = "Load"; _cpuLoad.Percent = s.CpuLoad ?? 0; _cpuLoad.OverrideText = s.CpuLoad is null ? "Load  n/a" : null; _cpuLoad.Invalidate();
        _cpuCap.Label = "Cap"; _cpuCap.Percent = s.CpuCapPercent; _cpuCap.OverrideText = null; _cpuCap.Invalidate();
        _cpuHistory.Add(s.CpuTemp);

        _gpuTemp.Text = Fmt(s.GpuTemp, "°C");
        _gpuClock.Text = $"{Fmt(s.GpuClockMHz, " MHz")}   {Fmt(s.GpuPowerW, " W")}";
        _gpuLoad.Label = "Load"; _gpuLoad.Percent = s.GpuLoad ?? 0; _gpuLoad.OverrideText = s.GpuLoad is null ? "Load  n/a" : null; _gpuLoad.Invalidate();
        if (s.GpuCapSupported) { _gpuCap.Label = "Cap"; _gpuCap.Percent = s.GpuCapPercent; _gpuCap.OverrideText = null; }
        else { _gpuCap.OverrideText = "Power-limit control not supported on this GPU"; }
        _gpuCap.Invalidate();
        _gpuHistory.Add(s.GpuTemp);

        if (s.FpsAvailable && s.Fps is int fps)
        {
            _fpsValue.Text = fps.ToString();
            _fpsSamples.Add(fps);
            _fpsMin = _fpsMin is null ? fps : Math.Min(_fpsMin.Value, fps);
            _fpsMax = _fpsMax is null ? fps : Math.Max(_fpsMax.Value, fps);
            var avg = _fpsSamples.Count > 0 ? _fpsSamples.Average() : 0;
            _fpsStats.Text = $"min {_fpsMin}  avg {avg:F0}  max {_fpsMax}";
            _fpsHistory.Add(fps);
        }
        else
        {
            _fpsValue.Text = "n/a";
            _fpsStats.Text = "PresentMon.exe not found next to ThermalGuard.exe";
        }

        _cpuName.Text = s.CpuName;
        _gpuName.Text = s.GpuName;
        var flagBits = new List<string>();
        if (s.ProtectedGame) flagBits.Add("Protected game (CPU+GPU cap only, no priority/suspend)");
        if (s.SuspendEnabled) flagBits.Add("Suspend-at-Critical enabled");
        _flags.Text = flagBits.Count > 0 ? string.Join("\n", flagBits) : "";
    }

    private static string Fmt(int? v, string unit) => v is int n ? $"{n}{unit}" : "n/a";

    private static string FormatSpan(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";
}
