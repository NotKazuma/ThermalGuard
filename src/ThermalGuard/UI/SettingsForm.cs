using ThermalGuard.Config;
using ThermalGuard.Core;
using ThermalGuard.Startup;

namespace ThermalGuard.UI;

/// <summary>Edits the profile list and global settings in place, saving to the same AppConfig instance.</summary>
public sealed class SettingsForm : Form
{
    private readonly AppConfig _cfg;
    private readonly ProfileStore _store;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill };
    private readonly TextBox _displayName = new() { Dock = DockStyle.Fill };
    private readonly TextBox _exeName = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _enabled = new() { Text = "Enabled", AutoSize = true };
    private readonly CheckBox _lowerPriority = new() { Text = "Lower priority at Hot/Critical", AutoSize = true };
    private readonly CheckBox _suspend = new() { Text = "Briefly suspend at Critical (can disconnect online games)", AutoSize = true };
    private readonly CheckBox _protectedGame = new() { Text = "Protected game (anti-cheat): power cap only, no priority/suspend", AutoSize = true };
    private readonly NumericUpDown _gpuWarm = Num(), _gpuHot = Num(), _gpuCritical = Num();
    private readonly NumericUpDown _cpuWarm = Num(), _cpuHot = Num(), _cpuCritical = Num();
    private readonly NumericUpDown _capWarm = Num(), _capHot = Num(), _capCritical = Num();
    private readonly NumericUpDown _gpuCapWarm = Num(10, 100), _gpuCapHot = Num(10, 100), _gpuCapCritical = Num(10, 100);
    private readonly NumericUpDown _poll = Num(1, 30), _cooldown = Num(0, 20), _suspendMs = Num(200, 5000);
    private readonly CheckBox _autoDetect = new() { Text = "Auto-detect fullscreen games", AutoSize = true };
    private readonly NumericUpDown _autoDetectSeconds = Num(1, 120);
    private readonly CheckBox _startWithWindows = new() { Text = "Start with Windows", AutoSize = true };
    private bool _loading;

    public SettingsForm(AppConfig cfg, ProfileStore store)
    {
        _cfg = cfg; _store = store;
        Text = "ThermalGuard Settings";
        Width = 760; Height = 560; StartPosition = FormStartPosition.CenterScreen; MinimizeBox = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(8) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var leftPanel = new Panel { Dock = DockStyle.Fill };
        leftPanel.Controls.Add(_list);
        var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32 };
        var add = new Button { Text = "Add" }; add.Click += (_, _) => AddProfile();
        var remove = new Button { Text = "Remove" }; remove.Click += (_, _) => RemoveProfile();
        leftButtons.Controls.Add(add); leftButtons.Controls.Add(remove);
        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(leftPanel); left.Controls.Add(leftButtons);

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(BuildDetailPanel(), 1, 0);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        var save = new Button { Text = "Save", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => Save();
        bottom.Controls.Add(cancel); bottom.Controls.Add(save);
        AcceptButton = save; CancelButton = cancel;

        Controls.Add(root);
        Controls.Add(bottom);

        _list.SelectedIndexChanged += (_, _) => LoadSelected();
        RefreshList();
        _startWithWindows.Checked = AutostartManager.IsEnabled();
        _autoDetect.Checked = _cfg.AutoDetect;
        _autoDetectSeconds.Value = _cfg.AutoDetectSeconds;
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    private Control BuildDetailPanel()
    {
        var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(8) };
        void Row(string label, Control c)
        {
            p.RowCount++;
            p.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) });
            p.Controls.Add(c);
        }
        Row("Display name", _displayName);
        Row("Process (.exe)", _exeName);
        Row("", _enabled);
        Row("", _protectedGame);
        Row("", _lowerPriority);
        Row("", _suspend);
        Row("Suspend duration (ms)", _suspendMs);
        Row("GPU warm / hot / critical (°C)", Triple(_gpuWarm, _gpuHot, _gpuCritical));
        Row("CPU warm / hot / critical (°C)", Triple(_cpuWarm, _cpuHot, _cpuCritical));
        Row("CPU cap at warm / hot / critical (%)", Triple(_capWarm, _capHot, _capCritical));
        Row("GPU power cap at warm / hot / critical (% of default)", Triple(_gpuCapWarm, _gpuCapHot, _gpuCapCritical));
        Row("Poll interval (s)", _poll);
        Row("Cooldown margin (°C)", _cooldown);
        Row("—", new Label { Text = "Global settings", Font = new Font(Font, FontStyle.Bold), AutoSize = true });
        Row("", _autoDetect);
        Row("Auto-detect hold time (s)", _autoDetectSeconds);
        Row("", _startWithWindows);
        return p;
    }

    private static Control Triple(NumericUpDown a, NumericUpDown b, NumericUpDown c)
    {
        var f = new FlowLayoutPanel { AutoSize = true };
        f.Controls.Add(a); f.Controls.Add(b); f.Controls.Add(c);
        return f;
    }

    private static NumericUpDown Num(int min = 0, int max = 150) => new() { Minimum = min, Maximum = max, Width = 70 };

    private void RefreshList()
    {
        _list.Items.Clear();
        foreach (var p in _cfg.Profiles) _list.Items.Add(p.DisplayName);
    }

    private Profile? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < _cfg.Profiles.Count
        ? _cfg.Profiles[_list.SelectedIndex] : null;

    private void LoadSelected()
    {
        var pr = Selected;
        if (pr is null) return;
        _loading = true;
        _displayName.Text = pr.DisplayName; _exeName.Text = pr.ExeName;
        _enabled.Checked = pr.Enabled; _lowerPriority.Checked = pr.LowerPriority;
        _suspend.Checked = pr.SuspendAtCritical; _protectedGame.Checked = pr.ProtectedGame;
        _gpuWarm.Value = pr.GpuWarm; _gpuHot.Value = pr.GpuHot; _gpuCritical.Value = pr.GpuCritical;
        _cpuWarm.Value = pr.CpuWarm; _cpuHot.Value = pr.CpuHot; _cpuCritical.Value = pr.CpuCritical;
        _capWarm.Value = pr.CapWarm; _capHot.Value = pr.CapHot; _capCritical.Value = pr.CapCritical;
        _gpuCapWarm.Value = pr.GpuPowerCapWarm; _gpuCapHot.Value = pr.GpuPowerCapHot; _gpuCapCritical.Value = pr.GpuPowerCapCritical;
        _poll.Value = pr.PollSeconds; _cooldown.Value = pr.Cooldown; _suspendMs.Value = pr.SuspendMs;
        _loading = false;
    }

    private void StoreSelected()
    {
        var pr = Selected;
        if (pr is null || _loading) return;
        pr.DisplayName = _displayName.Text; pr.ExeName = _exeName.Text;
        pr.Enabled = _enabled.Checked; pr.LowerPriority = _lowerPriority.Checked;
        pr.SuspendAtCritical = _suspend.Checked; pr.ProtectedGame = _protectedGame.Checked;
        pr.GpuWarm = (int)_gpuWarm.Value; pr.GpuHot = (int)_gpuHot.Value; pr.GpuCritical = (int)_gpuCritical.Value;
        pr.CpuWarm = (int)_cpuWarm.Value; pr.CpuHot = (int)_cpuHot.Value; pr.CpuCritical = (int)_cpuCritical.Value;
        pr.CapWarm = (int)_capWarm.Value; pr.CapHot = (int)_capHot.Value; pr.CapCritical = (int)_capCritical.Value;
        pr.GpuPowerCapWarm = (int)_gpuCapWarm.Value; pr.GpuPowerCapHot = (int)_gpuCapHot.Value; pr.GpuPowerCapCritical = (int)_gpuCapCritical.Value;
        pr.PollSeconds = (int)_poll.Value; pr.Cooldown = (int)_cooldown.Value; pr.SuspendMs = (int)_suspendMs.Value;
    }

    private void AddProfile()
    {
        StoreSelected();
        _cfg.Profiles.Add(new Profile { DisplayName = "New profile", ExeName = "" });
        RefreshList();
        _list.SelectedIndex = _cfg.Profiles.Count - 1;
    }

    private void RemoveProfile()
    {
        var i = _list.SelectedIndex;
        if (i < 0) return;
        _cfg.Profiles.RemoveAt(i);
        RefreshList();
        if (_list.Items.Count > 0) _list.SelectedIndex = Math.Min(i, _list.Items.Count - 1);
    }

    private void Save()
    {
        StoreSelected();
        _cfg.AutoDetect = _autoDetect.Checked;
        _cfg.AutoDetectSeconds = (int)_autoDetectSeconds.Value;
        _store.Save(_cfg);

        bool wantAuto = _startWithWindows.Checked;
        if (wantAuto != AutostartManager.IsEnabled())
        {
            bool ok = wantAuto ? AutostartManager.Enable() : AutostartManager.Disable();
            if (!ok) MessageBox.Show(this, "Could not change the Windows startup task.", "ThermalGuard",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
