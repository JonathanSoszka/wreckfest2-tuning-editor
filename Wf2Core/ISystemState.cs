namespace Wf2Core;

/// <summary>
/// Abstracts "is it safe to touch the live save right now?" so the write pipeline can be tested
/// without a running game. The real implementation checks for running processes; tests inject a fake.
/// </summary>
public interface ISystemState
{
    /// <summary>True if Wreckfest 2 is running — writing the live save now risks the game overwriting it.</summary>
    bool IsGameRunning();
}
