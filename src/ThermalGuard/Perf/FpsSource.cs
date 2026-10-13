using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace ThermalGuard.Perf;

public interface IFpsSource : IDisposable
{
    /// <summary>True only if a working PresentMon.exe was actually found - never assumed.</summary>
    bool Available { get; }
    void Attach(int pid);
    void Detach();
    int? Fps { get; }
}

/// <summary>
/// FPS via Intel's PresentMon (github.com/GameTechDev/PresentMon, MIT licensed) - the same ETW-based
/// present-tracking tool RTSS-style overlays are built on; reimplementing that ourselves would risk
/// silently-wrong numbers across the many D3D/DXGI presentation paths PresentMon already handles
/// correctly. The verified official v2.6.0 release exe (SHA256 checked against GitHub's published
/// digest at download time) is bundled in tools/PresentMon and published alongside ThermalGuard.exe.
/// Looked up by filename pattern so a newer verified version can replace it without a code change.
/// </summary>
public sealed class PresentMonFpsSource : IFpsSource
{
    private readonly string? _exe;
    private Process? _proc;
    private readonly ConcurrentQueue<double> _recentMs = new();
    public bool Available { get; }
    public int? Fps { get; private set; }

    public PresentMonFpsSource(string? explicitPath = null)
    {
        _exe = explicitPath is not null && File.Exists(explicitPath) ? explicitPath : Find();
        Available = _exe is not null;
    }

    private static string? Find()
    {
        var dir = AppContext.BaseDirectory;
        try
        {
            return Directory.EnumerateFiles(dir, "PresentMon*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }
        catch (IOException) { return null; }
    }

    public void Attach(int pid)
    {
        Detach();
        if (!Available) return;
        try
        {
            var psi = new ProcessStartInfo(_exe!,
                $"--process_id {pid} --output_stdout --no_console_stats --stop_existing_session --terminate_on_proc_exit")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            _proc = Process.Start(psi);
            if (_proc is null) return;
            _proc.OutputDataReceived += OnLine;
            _proc.BeginOutputReadLine();
        }
        catch (Exception) { _proc = null; } // PresentMon missing a dependency, wrong version, etc: fail quiet, FPS stays n/a
    }

    private int _msColumn = -1;

    private void OnLine(object? sender, DataReceivedEventArgs e)
    {
        var line = e.Data;
        if (string.IsNullOrEmpty(line)) return;

        // Header row (varies slightly by PresentMon version): locate the frame-time column once.
        if (_msColumn < 0)
        {
            var header = line.Split(',');
            _msColumn = Array.FindIndex(header, h => h.Contains("MsBetweenPresents", StringComparison.OrdinalIgnoreCase));
            return;
        }
        if (_msColumn < 0) return;

        var cols = line.Split(',');
        if (_msColumn >= cols.Length) return;
        if (!double.TryParse(cols[_msColumn], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms) || ms <= 0) return;

        _recentMs.Enqueue(ms);
        while (_recentMs.Count > 60) _recentMs.TryDequeue(out _); // ~1s of history at 60fps

        var avgMs = _recentMs.Average();
        Fps = avgMs > 0 ? (int)Math.Round(1000.0 / avgMs) : null;
    }

    public void Detach()
    {
        if (_proc is null) return;
        try { if (!_proc.HasExited) _proc.Kill(entireProcessTree: true); } catch (Exception) { }
        _proc.Dispose();
        _proc = null;
        _msColumn = -1;
        _recentMs.Clear();
        Fps = null;
    }

    public void Dispose() => Detach();
}

public sealed class NullFpsSource : IFpsSource
{
    public bool Available => false;
    public int? Fps => null;
    public void Attach(int pid) { }
    public void Detach() { }
    public void Dispose() { }
}
