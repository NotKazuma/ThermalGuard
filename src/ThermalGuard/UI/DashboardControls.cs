using System.Drawing.Drawing2D;

namespace ThermalGuard.UI;

/// <summary>Dark-themed horizontal progress bar with a label, used for load %/cap % readouts.</summary>
public sealed class StatBar : Panel
{
    public string Label { get; set; } = "";
    public int Percent { get; set; } // 0-100, clamped
    public Color BarColor { get; set; } = Color.FromArgb(90, 170, 255);
    public string? OverrideText { get; set; } // shown instead of "Label  N%", e.g. "not supported"

    public StatBar() { DoubleBuffered = true; Height = 34; BackColor = Color.Transparent; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var track = new Rectangle(0, 18, Width - 1, 10);
        using (var trackBrush = new SolidBrush(Color.FromArgb(50, 50, 56)))
            g.FillRoundedRect(trackBrush, track, 5);

        if (OverrideText is null)
        {
            int pct = Math.Clamp(Percent, 0, 100);
            int fillW = (int)(track.Width * (pct / 100.0));
            if (fillW > 2)
            {
                var fill = new Rectangle(track.X, track.Y, fillW, track.Height);
                using var fillBrush = new SolidBrush(BarColor);
                g.FillRoundedRect(fillBrush, fill, 5);
            }
        }

        using var textBrush = new SolidBrush(Color.FromArgb(225, 225, 230));
        using var font = new Font("Segoe UI", 8.5f);
        var text = OverrideText ?? $"{Label}  {Math.Clamp(Percent, 0, 100)}%";
        g.DrawString(text, font, textBrush, new PointF(0, 0));
    }
}

/// <summary>Minimal rolling line-graph for a dark card background - no chart library needed.</summary>
public sealed class Sparkline : Panel
{
    private readonly Queue<float> _points = new();
    private readonly int _capacity;
    public Color LineColor { get; set; } = Color.FromArgb(90, 170, 255);
    public float Min { get; set; } = 0;
    public float Max { get; set; } = 100;

    public Sparkline(int capacity = 60)
    {
        _capacity = capacity;
        DoubleBuffered = true; Height = 36; BackColor = Color.Transparent;
    }

    public void Add(float? v)
    {
        if (v is not float f) return;
        _points.Enqueue(f);
        while (_points.Count > _capacity) _points.Dequeue();
        Invalidate();
    }

    public void Clear() { _points.Clear(); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_points.Count < 2) return;

        var arr = _points.ToArray();
        float range = Math.Max(1, Max - Min);
        using var pen = new Pen(LineColor, 1.6f);
        PointF? prev = null;
        for (int i = 0; i < arr.Length; i++)
        {
            float x = Width * (i / (float)(_capacity - 1));
            float norm = Math.Clamp((arr[i] - Min) / range, 0, 1);
            float y = Height - norm * Height;
            var pt = new PointF(x, y);
            if (prev is PointF p) g.DrawLine(pen, p, pt);
            prev = pt;
        }
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRect(this Graphics g, Brush brush, Rectangle r, int radius)
    {
        using var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}

/// <summary>Dark card container with a title, used to group a hardware section on the Dashboard.</summary>
public sealed class Card : Panel
{
    public Card(string title)
    {
        BackColor = Color.FromArgb(30, 30, 34);
        Padding = new Padding(14);
        var t = new Label
        {
            Text = title, Dock = DockStyle.Top, Height = 22,
            Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Color.FromArgb(150, 180, 255),
        };
        Controls.Add(t);
    }
}
