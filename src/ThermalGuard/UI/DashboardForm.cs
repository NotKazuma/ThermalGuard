using ThermalGuard.Core;

namespace ThermalGuard.UI;

/// <summary>Live read-only view of what TrayForm is doing: temps, load, clocks, power, FPS, cap levels.</summary>
public sealed class DashboardForm : Form
{
    private readonly Label _game = Big();
    private readonly Label _level = Big();
    private readonly Label _cpuLine = Row();
    private readonly Label _gpuLine = Row();
    private readonly Label _fpsLine = Row();
    private readonly Label _capLine = Row();
    private readonly Label _hwLine = Row();

    public DashboardForm()
    {
        Text = "ThermalGuard Dashboard";
        Width = 480; Height = 320; StartPosition = FormStartPosition.CenterScreen; MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(16), AutoSize = true };
        foreach (var c in new Control[] { _game, _level, _cpuLine, _gpuLine, _fpsLine, _capLine, _hwLine })
            layout.Controls.Add(c);
        Controls.Add(layout);
        Show(_game, "Waiting for a game...");
    }

    private static Label Big() => new() { AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold), Margin = new Padding(0, 0, 0, 8) };
    private static Label Row() => new() { AutoSize = true, Font = new Font("Segoe UI", 10), Margin = new Padding(0, 2, 0, 2) };
    private static void Show(Label l, string text) => l.Text = text;

    /// <summary>Call from the UI thread (TrayForm's timer already runs there) with the latest snapshot.</summary>
    public void Apply(MonitorSnapshot s)
    {
        if (IsDisposed) return;
        _game.Text = s.Attached ? s.Game : "Waiting for a game...";
        _level.Text = s.Attached ? $"State: {s.Level}" : "";
        _level.ForeColor = s.Level switch
        {
            Level.Warm => Color.DarkGoldenrod,
            Level.Hot => Color.DarkOrange,
            Level.Critical => Color.Crimson,
            _ => Color.SeaGreen,
        };
        _cpuLine.Text = $"CPU   {Fmt(s.CpuTemp, "C")}   load {Fmt(s.CpuLoad, "%")}   {Fmt(s.CpuClockMHz, "MHz")}   cap {s.CpuCapPercent}%";
        _gpuLine.Text = $"GPU   {Fmt(s.GpuTemp, "C")}   load {Fmt(s.GpuLoad, "%")}   {Fmt(s.GpuClockMHz, "MHz")}   {Fmt(s.GpuPowerW, "W")}" +
            (s.GpuCapSupported ? $"   cap {s.GpuCapPercent}%" : "   (power-limit control not supported on this GPU)");
        _fpsLine.Text = s.FpsAvailable ? $"FPS   {Fmt(s.Fps, "")}" : "FPS   n/a (PresentMon.exe not found next to ThermalGuard.exe)";
        _hwLine.Text = $"{s.CpuName}\n{s.GpuName}";
    }

    private static string Fmt(int? v, string unit) => v is int n ? $"{n}{unit}" : "n/a";
}
