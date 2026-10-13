using System.Runtime.InteropServices;
using ThermalGuard.Perf;

namespace ThermalGuard.UI;

/// <summary>
/// A borderless, click-through, always-on-top window showing live FPS, positioned over a corner of
/// the attached game's window.
///
/// This is NOT a true in-game overlay - it does not hook DirectX/OpenGL or touch the game process at
/// all, so it cannot draw inside the game's own rendered frame the way RTSS or the Steam overlay do.
/// That kind of hook is the same class of risk flagged elsewhere in this project (code injection into
/// a game process - the sort of thing anti-cheat watches for) and isn't worth it for a text counter.
/// Instead this is a second window Windows keeps always-on-top with mouse clicks passed through to
/// whatever is beneath it, repositioned to track the game's window every tick. It will not show over
/// exclusive fullscreen (nothing can draw on top of that except the game's own swap chain), but works
/// for windowed and borderless-fullscreen games, which covers most modern titles.
/// </summary>
public sealed class FpsOverlayForm : Form
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoActivate = 0x0010, SwpNoSize = 0x0001;
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x80000, WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    private readonly IFpsSource _fps;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 300 };
    private readonly Label _label = new()
    {
        AutoSize = true,
        ForeColor = Color.FromArgb(120, 220, 140),
        Font = new Font("Consolas", 15, FontStyle.Bold),
        BackColor = Color.Black,
        Padding = new Padding(10, 5, 10, 5),
        TextAlign = ContentAlignment.MiddleCenter,
    };

    /// <summary>The game's main window handle to track, or IntPtr.Zero to hide the overlay.</summary>
    public IntPtr TargetWindow { get; set; } = IntPtr.Zero;

    public FpsOverlayForm(IFpsSource fps)
    {
        _fps = fps;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;

        // A magic near-black key color (not pure black) makes the form itself invisible while the
        // label's own pure-black backdrop box - a different RGB value - stays opaque and visible.
        var magicKey = Color.FromArgb(1, 1, 1);
        BackColor = magicKey;
        TransparencyKey = magicKey;

        Controls.Add(_label);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        Load += (_, _) => ApplyClickThroughStyle();
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        Hide();
    }

    private void ApplyClickThroughStyle()
    {
        int ex = GetWindowLong(Handle, GwlExStyle);
        SetWindowLong(Handle, GwlExStyle, ex | WsExLayered | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    private void Tick()
    {
        if (TargetWindow == IntPtr.Zero || !IsWindow(TargetWindow) || !GetWindowRect(TargetWindow, out var r))
        {
            if (Visible) Hide();
            return;
        }

        if (_fps.Available && _fps.Fps is int f)
        {
            _label.Text = $"{f} FPS";
            _label.ForeColor = f >= 50 ? Color.FromArgb(100, 220, 120) : f >= 30 ? Color.FromArgb(255, 210, 90) : Color.FromArgb(230, 90, 90);
        }
        else
        {
            _label.Text = "FPS n/a";
            _label.ForeColor = Color.FromArgb(160, 160, 160);
        }

        var target = new Point(r.Left + 16, r.Top + 16);
        if (Location != target) Location = target;
        if (!Visible) Show();

        // Reassert topmost every tick: a fullscreen game can otherwise push itself above this window.
        SetWindowPos(Handle, HwndTopmost, Location.X, Location.Y, 0, 0, SwpNoSize | SwpNoActivate);
    }
}
