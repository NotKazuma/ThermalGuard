using System.Diagnostics;
using ThermalGuard.Logging;
using ThermalGuard.Safety;
using ThermalGuard.Sensors;

namespace ThermalGuard.Core;

/// <summary>Headless monitoring loop for one game. The tray UI will drive the same class.</summary>
public sealed class Runner
{
    private const int BlindPollLimit = 5; // consecutive polls with no valid temp before we stop throttling

    private readonly ITempSource _temps;
    private readonly ThrottleController _ctl;
    private readonly CsvLogger _log;
    private readonly Action<string> _say;

    public Runner(ITempSource temps, ThrottleController ctl, CsvLogger log, Action<string> say)
    {
        _temps = temps; _ctl = ctl; _log = log; _say = say;
    }

    public void Run(Process game, Profile p, CancellationToken stop)
    {
        var engine = new ThrottleEngine(p);
        _ctl.Attach(game.Id, p);
        int blind = 0;
        try
        {
            while (!game.HasExited && !stop.IsCancellationRequested)
            {
                _temps.Update();
                var gpu = _temps.GpuTemp; var cpu = _temps.CpuTemp;

                Level next;
                if (gpu is null && cpu is null)
                {
                    // Never throttle blind: after a few empty polls, drop to normal and say so.
                    if (++blind == BlindPollLimit) _say("No valid temperature readings - throttling released.");
                    next = blind >= BlindPollLimit ? Level.Ok : engine.Current;
                    if (blind >= BlindPollLimit) engine.Reset();
                }
                else { blind = 0; next = engine.Update(gpu, cpu); }

                if (next != _lastApplied)
                {
                    _say($"{_lastApplied} -> {next}  (GPU {gpu?.ToString() ?? "n/a"}C, CPU {cpu?.ToString() ?? "n/a"}C)");
                    _ctl.Apply(next);
                    _lastApplied = next;
                }
                _log.Write(DateTime.Now, p.DisplayName, gpu, cpu, next, p.CapFor(next));

                if (next == Level.Critical) _ctl.PulseSuspend(ms => Thread.Sleep(ms));
                stop.WaitHandle.WaitOne(TimeSpan.FromSeconds(p.PollSeconds));
            }
        }
        finally
        {
            _ctl.RestoreAll();
            _lastApplied = Level.Ok;
            _say("Stopped. CPU limit and game priority restored.");
        }
    }

    private Level _lastApplied = Level.Ok;
}
