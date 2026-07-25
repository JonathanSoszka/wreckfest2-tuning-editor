using System.Diagnostics;

namespace Wf2Core;

/// <summary>Real <see cref="ISystemState"/>: reports whether Wreckfest 2 or Steam is running.</summary>
public sealed class ProcessSystemState : ISystemState
{
    // Process names (without ".exe"). The game executable is "Wreckfest2"; Steam's client is "steam".
    private static readonly string[] BlockingProcessNames = ["Wreckfest2", "steam"];

    public bool IsGameOrSteamRunning()
    {
        foreach (var name in BlockingProcessNames)
        {
            Process[] procs = Process.GetProcessesByName(name);
            try
            {
                if (procs.Length > 0)
                    return true;
            }
            finally
            {
                foreach (var p in procs)
                    p.Dispose();
            }
        }
        return false;
    }
}
