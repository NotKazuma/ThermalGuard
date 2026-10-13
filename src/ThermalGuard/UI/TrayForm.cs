using System.Diagnostics;
using ThermalGuard.Config;
using ThermalGuard.Core;
using ThermalGuard.Detection;
using ThermalGuard.Hardware;
using ThermalGuard.Logging;
using ThermalGuard.Perf;
using ThermalGuard.Safety;
using ThermalGuard.Sensors;

namespace ThermalGuard.UI;

/// <summary>
/// Hidden host form for the NotifyIcon. Runs the detect-and-throttle loop on a UI timer (the actions
/// per tick are all fast; the one exception, suspend, is documented below) and always restores on exit.
/// </summary>
public sealed class TrayForm : Form
{
    private readonly AppConfig _cfg;
    private readonly ProfileStore _store;
    private readonly GameDetector _detector;
    private readonly ITempSource _temps;
    private readonly ThrottleController _ctl;
    private readonly CsvLogger _log;
    private readonly RestoreJournal _journal;
    private readonly HardwareInfo _hw;
    private readonly IGpuPower _gpu;
    private readonly IFpsSource _fps;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };

    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _overlayItem;
    private readonly FpsOverlayForm _overlay;
    private DashboardForm? _dashboard;

    /// <summary>Fires once per tick with the latest state, for the Dashboard (and anything else) to display.</summary>
    public event Action<MonitorSnapshot>? Updated;

    private readonly Dictionary<Level, Icon> _icons = new()
    {
        [Level.Ok] = IconFactory.Dot(Color.MediumSeaGreen),
        [Level.Warm] = IconFactory.Dot(Color.Gold),
        [Level.Hot] = IconFactory.Dot(Color.DarkOrange),
        [Level.Critical] = IconFactory.Dot(Color.Crimson),
    };
    private readonly Icon _idleIcon = IconFactory.Dot(Color.Silver);
    private readonly Icon _pausedIcon = IconFactory.Dot(Color.Gray);

    private const int BlindPollLimit = 5; // consecutive polls with no valid temp before we stop throttling

    private int _attachedPid;
    private Profile? _attachedProfile;
    private ThrottleEngine? _engine;
    private bool _paused;
    private int _blindPolls;
    private bool _blindWarned;
    private Level _lastApplied = Level.Ok;

    public TrayForm(AppConfig cfg, ProfileStore store, GameDetector detector, ITempSource temps,
        ThrottleController ctl, CsvLogger log, RestoreJournal journal, HardwareInfo hw, IGpuPower gpu, IFpsSource? fps = null)
    {
        _cfg = cfg; _store = store; _detector = detector; _temps = temps; _ctl = ctl; _log = log; _journal = journal;
        _hw = hw; _gpu = gpu; _fps = fps ?? new PresentMonFpsSource();
        _overlay = new FpsOverlayForm(_fps);

        // Invisible host window: only the tray icon is meant to be seen.
        ShowInTaskbar = false; WindowState = FormWindowState.Minimized; FormBorderStyle = FormBorderStyle.FixedToolWindow;
        Opacity = 0; Load += (_, _) => Hide();

        var menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("ThermalGuard - waiting for a game") { Enabled = false };
        _pauseItem = new ToolStripMenuItem("Pause", null, (_, _) => TogglePause());
        var dashboard = new ToolStripMenuItem("Dashboard...", null, (_, _) => OpenDashboard());
        _overlayItem = new ToolStripMenuItem("FPS overlay", null, (_, _) => ToggleOverlay()) { CheckOnClick = false, Checked = _cfg.ShowFpsOverlay };
        var pick = new ToolStripMenuItem("Pick game...", null, (_, _) => PickGame());
        var settings = new ToolStripMenuItem("Settings...", null, (_, _) => OpenSettings());
        var openLog = new ToolStripMenuItem("Open log folder", null, (_, _) => Process.Start(new ProcessStartInfo(CsvLogger.DefaultDir) { UseShellExecute = true }));
        var about = new ToolStripMenuItem("About ThermalGuard...", null, (_, _) => new AboutForm().ShowDialog(this));
        var exit = new ToolStripMenuItem("Exit", null, (_, _) => Close());
        menu.Items.AddRange(new ToolStripItem[] { _statusItem, new ToolStripSeparator(), _pauseItem, dashboard, _overlayItem, pick, settings, openLog, about, new ToolStripSeparator(), exit });

        _tray = new NotifyIcon { Icon = _idleIcon, Visible = true, Text = "ThermalGuard", ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => OpenDashboard();

        if (_journal.Exists)
        {
            var power = new SystemPowerApi(); var proc = new SystemProcessOps();
            if (_journal.Recover(power, proc))
                _tray.ShowBalloonTip(4000, "ThermalGuard", "Restored settings left over from a previous run.", ToolTipIcon.Info);
        }

        Watchdog.Start(RestoreJournal.DefaultPath);

        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        FormClosing += (_, _) => Shutdown();
    }

    private void TogglePause()
    {
        _paused = !_paused;
        _pauseItem.Text = _paused ? "Resume" : "Pause";
        if (_paused) Detach();
    }

    private void PickGame()
    {
        using var dlg = new PickGameForm();
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Chosen is null) return;
        var name = GameDetector.Normalize(dlg.Chosen.Name);
        var profile = _cfg.Profiles.FirstOrDefault(p => string.Equals(GameDetector.Normalize(p.ExeName), name, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            profile = new Profile { DisplayName = name, ExeName = name };
            _cfg.Profiles.Add(profile);
            _store.Save(_cfg);
        }
        Attach(dlg.Chosen.Pid, profile);
    }

    private void OpenSettings()
    {
        using var f = new SettingsForm(_cfg, _store);
        f.ShowDialog(this);
        _store.Save(_cfg);
    }

    private void OpenDashboard()
    {
        if (_dashboard is null || _dashboard.IsDisposed)
        {
            _dashboard = new DashboardForm();
            var handler = (Action<MonitorSnapshot>)(s => _dashboard?.Apply(s));
            Updated += handler;
            _dashboard.FormClosed += (_, _) => { Updated -= handler; _dashboard = null; };
        }
        _dashboard.Show();
        _dashboard.Activate();
    }

    private void Tick()
    {
        if (_paused) return;

        if (_attachedPid != 0 && !IsAlive(_attachedPid))
        {
            Detach();
        }

        if (_attachedPid == 0)
        {
            var running = WindowProbe.Running();
            var fg = WindowProbe.FullscreenForeground();
            var match = _detector.Choose(running, fg, DateTime.Now);
            if (match is not null) Attach(match.Proc.Pid, match.Profile);
            else
            {
                _tray.Icon = _idleIcon; _tray.Text = "ThermalGuard - waiting for a game"; _statusItem.Text = _tray.Text;
                Updated?.Invoke(new MonitorSnapshot(false, "", Level.Ok, null, null, null, 100, null, null, null, null, 100,
                    _gpu.Supported, null, _fps.Available, _hw.CpuName, _hw.GpuName, false, false));
            }
            return;
        }

        // Re-derive the game's main window each tick: some games show a launcher/splash window first
        // and only create their real window a moment later, and it can change (e.g. a restart).
        _overlay.TargetWindow = _cfg.ShowFpsOverlay ? GetMainWindowHandle(_attachedPid) : IntPtr.Zero;

        _temps.Update();
        var gpu = _temps.GpuTemp; var cpu = _temps.CpuTemp;

        Level next;
        if (gpu is null && cpu is null)
        {
            // Never throttle blind: after a few empty polls in a row, drop to normal and say so once.
            _blindPolls++;
            next = _blindPolls >= BlindPollLimit ? Level.Ok : _engine!.Current;
            if (_blindPolls >= BlindPollLimit)
            {
                _engine!.Reset();
                if (!_blindWarned)
                {
                    _blindWarned = true;
                    _tray.ShowBalloonTip(5000, "ThermalGuard",
                        "No temperature readings - throttling released until sensors recover.", ToolTipIcon.Warning);
                }
            }
        }
        else { _blindPolls = 0; _blindWarned = false; next = _engine!.Update(gpu, cpu); }

        // Only touch the power scheme / priority when the level actually changes. Writing it every
        // poll (as this did before) hammers PowerWriteACValueIndex/PowerSetActiveScheme ~30x/minute
        // even while holding steady, which showed up as a stall in the power subsystem under load.
        if (next != _lastApplied) { _ctl.Apply(next); _lastApplied = next; }

        var p = _attachedProfile!;
        _log.Write(DateTime.Now, p.DisplayName, gpu, cpu, next, p.CapFor(next));

        _tray.Icon = _icons[next];
        var fpsPart = _fps.Available && _fps.Fps is int f ? $"  {f}fps" : "";
        var text = $"{p.DisplayName}: {next}  GPU {gpu?.ToString() ?? "n/a"}C  CPU {cpu?.ToString() ?? "n/a"}C{fpsPart}";
        _tray.Text = text.Length > 63 ? text[..63] : text; // NotifyIcon.Text is capped at 63 chars
        _statusItem.Text = text;

        Updated?.Invoke(new MonitorSnapshot(
            true, p.DisplayName, next,
            cpu, _temps.CpuLoad, _temps.CpuClockMHz, p.CapFor(next),
            gpu, _temps.GpuLoad, _temps.GpuClockMHz, _temps.GpuPowerW, p.GpuPowerCapFor(next), _gpu.Supported,
            _fps.Fps, _fps.Available, _hw.CpuName, _hw.GpuName, p.ProtectedGame, p.SuspendAtCritical));

        // Suspend briefly freezes the game; it is opt-in (off by default) and rate-limited, so a short
        // UI pause here is an acceptable trade-off against the races a background thread would add.
        if (next == Level.Critical && p.SuspendAtCritical)
            _ctl.PulseSuspend(ms => Thread.Sleep(ms));
    }

    private void Attach(int pid, Profile profile)
    {
        if (!IsAlive(pid)) return;
        _ctl.Attach(pid, profile);
        _attachedPid = pid; _attachedProfile = profile; _engine = new ThrottleEngine(profile);
        _blindPolls = 0; _blindWarned = false; _lastApplied = Level.Ok;
        _fps.Attach(pid);
    }

    private void Detach()
    {
        if (_attachedPid == 0) return;
        _ctl.RestoreAll();
        _fps.Detach();
        _overlay.TargetWindow = IntPtr.Zero;
        _attachedPid = 0; _attachedProfile = null; _engine = null; _lastApplied = Level.Ok;
        Updated?.Invoke(new MonitorSnapshot(false, "", Level.Ok, null, null, null, 100, null, null, null, null, 100,
            _gpu.Supported, null, _fps.Available, _hw.CpuName, _hw.GpuName, false, false));
    }

    private void ToggleOverlay()
    {
        _cfg.ShowFpsOverlay = !_cfg.ShowFpsOverlay;
        _overlayItem.Checked = _cfg.ShowFpsOverlay;
        _store.Save(_cfg);
        if (!_cfg.ShowFpsOverlay) _overlay.TargetWindow = IntPtr.Zero;
    }

    private static bool IsAlive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static IntPtr GetMainWindowHandle(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.MainWindowHandle; }
        catch (ArgumentException) { return IntPtr.Zero; }
        catch (InvalidOperationException) { return IntPtr.Zero; }
    }

    private void Shutdown()
    {
        _timer.Stop();
        Detach();
        _fps.Dispose();
        _overlay.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
    }
}
