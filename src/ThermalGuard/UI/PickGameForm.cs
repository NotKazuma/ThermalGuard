using System.Diagnostics;
using ThermalGuard.Detection;

namespace ThermalGuard.UI;

/// <summary>Lets the user manually pick a running process to throttle, bypassing auto-detect.</summary>
public sealed class PickGameForm : Form
{
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
    public RunningProc? Chosen { get; private set; }

    public PickGameForm()
    {
        Text = "Pick a game"; Width = 420; Height = 500; StartPosition = FormStartPosition.CenterScreen;
        _list.Columns.Add("Process", 220); _list.Columns.Add("PID", 80);

        foreach (var p in Process.GetProcesses().Where(HasWindow).OrderBy(p => p.ProcessName))
        {
            try
            {
                var item = new ListViewItem(p.ProcessName) { Tag = new RunningProc(p.Id, p.ProcessName) };
                item.SubItems.Add(p.Id.ToString());
                _list.Items.Add(item);
            }
            catch (InvalidOperationException) { /* exited between enumeration and read */ }
        }
        _list.DoubleClick += (_, _) => Accept();

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Right, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Dock = DockStyle.Right, Width = 80 };
        ok.Click += (_, _) => Accept();
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 36 };
        bottom.Controls.Add(ok); bottom.Controls.Add(cancel);

        Controls.Add(_list); Controls.Add(bottom);
        AcceptButton = ok; CancelButton = cancel;
    }

    private static bool HasWindow(Process p)
    {
        try { return p.MainWindowHandle != IntPtr.Zero; }
        catch (InvalidOperationException) { return false; }
    }

    private void Accept()
    {
        if (_list.SelectedItems.Count > 0)
        {
            Chosen = (RunningProc)_list.SelectedItems[0].Tag!;
            DialogResult = DialogResult.OK;
        }
    }
}
