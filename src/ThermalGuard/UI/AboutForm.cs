using System.Diagnostics;
using System.Reflection;

namespace ThermalGuard.UI;

public sealed class AboutForm : Form
{
    public AboutForm()
    {
        Text = "About ThermalGuard";
        Width = 420; Height = 320; StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = Color.White;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(20) };

        var title = new Label { Text = "ThermalGuard", Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true };
        var ver = new Label { Text = $"Version {version}", ForeColor = Color.Gray, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        var desc = new Label
        {
            Text = "Watches CPU/GPU temperature while a game runs and throttles the system - CPU and GPU power caps, game priority, and an optional brief suspend - to stop a laptop overheating. Every change is restored on exit, crash, or a hard kill.",
            AutoSize = false, Width = 360, Height = 80, Margin = new Padding(0, 0, 0, 12)
        };
        var author = new Label { Text = "By Afif Aiman", AutoSize = true };
        var license = new Label { Text = "MIT License", AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        var repo = new LinkLabel { Text = "github.com/NotKazuma/ThermalGuard", AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        repo.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://github.com/NotKazuma/ThermalGuard") { UseShellExecute = true });
        var credits = new Label
        {
            Text = "Uses LibreHardwareMonitorLib (MPL-2.0) and bundles PresentMon (MIT, Intel) for FPS. See THIRD-PARTY-NOTICES.md.",
            AutoSize = false, Width = 360, Height = 40, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8)
        };

        var close = new Button { Text = "Close", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right };
        close.Click += (_, _) => Close();

        layout.Controls.Add(title);
        layout.Controls.Add(ver);
        layout.Controls.Add(desc);
        layout.Controls.Add(author);
        layout.Controls.Add(license);
        layout.Controls.Add(repo);
        layout.Controls.Add(credits);
        layout.Controls.Add(close);

        Controls.Add(layout);
        AcceptButton = close;
    }
}
