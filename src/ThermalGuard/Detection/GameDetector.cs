using ThermalGuard.Config;
using ThermalGuard.Core;

namespace ThermalGuard.Detection;

public sealed record RunningProc(int Pid, string Name);

public sealed record Match(RunningProc Proc, Profile Profile, bool AutoDetected);

/// <summary>
/// Decides which running process to guard. Pure logic: the caller supplies the process list and the
/// foreground/fullscreen facts, so every rule is unit-testable.
/// </summary>
public sealed class GameDetector
{
    // Things that are fullscreen or foreground but are never the game.
    private static readonly HashSet<string> BuiltInExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "chrome", "msedge", "firefox", "brave", "opera", "vlc", "mpv", "wmplayer",
        "steam", "steamwebhelper", "epicgameslauncher", "eadesktop", "origin", "battle.net", "galaxyclient",
        "discord", "obs64", "code", "devenv", "powershell", "windowsterminal", "cmd", "thermalguard",
        "applicationframehost", "shellexperiencehost", "searchhost", "lockapp",
    };

    private readonly AppConfig _cfg;
    private int _candidatePid;
    private DateTime _candidateSince;

    public GameDetector(AppConfig cfg) => _cfg = cfg;

    public static string Normalize(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;

    public bool IsExcluded(string name) =>
        BuiltInExclusions.Contains(Normalize(name)) ||
        _cfg.Ignored.Any(i => string.Equals(Normalize(i), Normalize(name), StringComparison.OrdinalIgnoreCase));

    /// <param name="foreground">The foreground process when it is fullscreen, else null.</param>
    public Match? Choose(IReadOnlyList<RunningProc> running, RunningProc? foreground, DateTime now)
    {
        // 1. Explicit profiles always win. If several are running, prefer the foreground one.
        var hits = running
            .Select(r => (Proc: r, Profile: _cfg.Profiles.FirstOrDefault(p =>
                p.Enabled && p.ExeName.Length > 0 &&
                string.Equals(Normalize(p.ExeName), Normalize(r.Name), StringComparison.OrdinalIgnoreCase))))
            .Where(h => h.Profile is not null)
            .ToList();
        if (hits.Count > 0)
        {
            var pick = hits.FirstOrDefault(h => h.Proc.Pid == foreground?.Pid);
            if (pick.Profile is null) pick = hits[0];
            return new Match(pick.Proc, pick.Profile!, false);
        }

        // 2. Auto-detect: a non-excluded fullscreen app that stays foreground for AutoDetectSeconds.
        if (!_cfg.AutoDetect || foreground is null || IsExcluded(foreground.Name))
        {
            _candidatePid = 0;
            return null;
        }
        if (_candidatePid != foreground.Pid) { _candidatePid = foreground.Pid; _candidateSince = now; }
        if ((now - _candidateSince).TotalSeconds < _cfg.AutoDetectSeconds) return null;

        var p = JsonClone(_cfg.Default);
        p.DisplayName = Normalize(foreground.Name);
        p.ExeName = Normalize(foreground.Name);
        return new Match(foreground, p, true);
    }

    private static Profile JsonClone(Profile src) =>
        System.Text.Json.JsonSerializer.Deserialize<Profile>(System.Text.Json.JsonSerializer.Serialize(src))!;
}
