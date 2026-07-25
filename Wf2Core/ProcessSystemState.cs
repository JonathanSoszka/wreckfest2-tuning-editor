using System.Diagnostics;

namespace Wf2Core;

/// <summary>Real <see cref="ISystemState"/>: reports whether Wreckfest 2 is running.</summary>
public sealed class ProcessSystemState : ISystemState
{
    // Process name (without ".exe"). The game executable is "Wreckfest2". Steam running is fine —
    // only the game actively overwrites the live save from memory.
    private const string GameProcessName = "Wreckfest2";

    public bool IsGameRunning()
    {
        Process[] procs = Process.GetProcessesByName(GameProcessName);
        try
        {
            return procs.Length > 0;
        }
        finally
        {
            foreach (var p in procs)
                p.Dispose();
        }
    }
}
