using System.Drawing.Drawing2D;

namespace ThermalGuard.UI;

/// <summary>Draws the tray icon dots in-process, so no .ico assets are needed in the build.</summary>
public static class IconFactory
{
    public static Icon Dot(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            using var pen = new Pen(Color.FromArgb(160, Color.Black), 1.5f);
            g.FillEllipse(brush, 3, 3, 26, 26);
            g.DrawEllipse(pen, 3, 3, 26, 26);
        }
        IntPtr hIcon = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(hIcon).Clone(); } // clone owns its own handle; safe to DestroyIcon the original
        finally { NativeDestroyIcon(hIcon); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr h);
    private static void NativeDestroyIcon(IntPtr h) => DestroyIcon(h);
}
