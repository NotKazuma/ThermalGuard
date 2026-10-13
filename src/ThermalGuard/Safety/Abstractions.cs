using System.Diagnostics;

namespace ThermalGuard.Safety;

/// <summary>Power-scheme access, abstracted so the restore logic can be tested without touching the system.</summary>
public interface IPowerApi
{
    Guid GetActiveScheme();
    int GetCap(Guid scheme, bool ac);
    void SetCap(Guid scheme, bool ac, int percent);
    void ApplyScheme(Guid scheme);
}

/// <summary>Process operations used by the actuators and by recovery.</summary>
public interface IProcessOps
{
    /// <summary>Start time in UTC ticks, or null if the pid is not running. Used to guard against pid reuse.</summary>
    long? GetStartTicks(int pid);
    ProcessPriorityClass GetPriority(int pid);
    void SetPriority(int pid, ProcessPriorityClass priority);
    void Suspend(int pid);
    void Resume(int pid);
}
