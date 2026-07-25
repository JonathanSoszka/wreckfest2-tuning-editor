namespace Wf2Core;

/// <summary>
/// Abstracts "is it safe to touch the live save right now?" so the write pipeline can be tested
/// without a running game. The real implementation checks for running processes; tests inject a fake.
/// </summary>
public interface ISystemState
{
    /// <summary>True if Wreckfest 2 or Steam is running — writing the live save now risks a cloud conflict.</summary>
    bool IsGameOrSteamRunning();
}
