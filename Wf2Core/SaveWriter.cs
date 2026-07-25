namespace Wf2Core;

/// <summary>
/// Safe-write pipeline for the live save (Milestone A.2 / AGENTS.md safety rules):
///   1. Refuse to write while the game is running.
///   2. Back up the existing target (timestamped) before overwriting.
///   3. Only then write the new bytes.
/// The backup step is skippable only when there is no existing file to back up.
/// </summary>
public sealed class SaveWriter(ISystemState systemState)
{
    private readonly ISystemState _systemState = systemState;

    /// <summary>The default backups location.</summary>
    public static string DefaultBackupDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Wreckfest2 Backups");

    /// <summary>
    /// Write <paramref name="bytes"/> to <paramref name="targetPath"/> through the safe pipeline.
    /// Throws <see cref="GameRunningException"/> (writing nothing) if the game is running.
    /// Returns the path of the backup that was created, or <c>null</c> if the target did not yet exist.
    /// </summary>
    public string? Write(string targetPath, byte[] bytes, string? backupDir = null, DateTime? timestampUtc = null)
    {
        ArgumentNullException.ThrowIfNull(targetPath);
        ArgumentNullException.ThrowIfNull(bytes);

        GuardGameNotRunning();
        string? backupPath = BackUp(targetPath, backupDir, timestampUtc);
        File.WriteAllBytes(targetPath, bytes);
        return backupPath;
    }

    /// <summary>
    /// Write <paramref name="bytes"/> to every path in <paramref name="targets"/> through the safe
    /// pipeline, backing up only the first (authoritative) target once.
    ///
    /// <para>The live save exists in two places — the real save under <c>My Games</c> and the Steam
    /// <c>userdata\…\remote</c> mirror. Writing only one lets Steam Cloud re-sync the stale copy back
    /// over the edit, which actually happened during development, so both must be written together.
    /// The first target is treated as authoritative for the backup; the rest are its cloud mirror(s)
    /// and are assumed to hold the same bytes.</para>
    ///
    /// <para>By default throws <see cref="GameRunningException"/> — writing nothing — if the game is
    /// running. Pass <paramref name="force"/> to write anyway; the caller is then responsible for having
    /// warned the user, since the game can overwrite the file on its next save. Returns the backup path,
    /// or <c>null</c> when the first target did not yet exist.</para>
    /// </summary>
    public string? WriteAllMirrors(IReadOnlyList<string> targets, byte[] bytes,
                                   string? backupDir = null, DateTime? timestampUtc = null,
                                   bool force = false)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(bytes);
        if (targets.Count == 0)
            throw new ArgumentException("At least one target path is required.", nameof(targets));

        if (!force) GuardGameNotRunning();
        string? backupPath = BackUp(targets[0], backupDir, timestampUtc);
        foreach (var target in targets)
            File.WriteAllBytes(target, bytes);
        return backupPath;
    }

    /// <summary>Whether Wreckfest 2 is currently running — the write-time hazard.</summary>
    public bool IsGameRunning() => _systemState.IsGameRunning();

    private void GuardGameNotRunning()
    {
        if (_systemState.IsGameRunning())
            throw new GameRunningException(
                "Refusing to write the save while Wreckfest 2 is running. Close the game and retry.");
    }

    /// <summary>Copy the target aside to a timestamped backup, or return null if it does not exist.</summary>
    private static string? BackUp(string targetPath, string? backupDir, DateTime? timestampUtc)
    {
        if (!File.Exists(targetPath)) return null;

        backupDir ??= DefaultBackupDir;
        Directory.CreateDirectory(backupDir);
        var stamp = (timestampUtc ?? DateTime.UtcNow).ToString("yyyyMMdd_HHmmss");
        var name = Path.GetFileNameWithoutExtension(targetPath);
        var ext = Path.GetExtension(targetPath);
        var backupPath = Path.Combine(backupDir, $"{name}.{stamp}.bak{ext}");
        // Avoid clobbering a same-second backup.
        var n = 1;
        while (File.Exists(backupPath))
            backupPath = Path.Combine(backupDir, $"{name}.{stamp}_{n++}.bak{ext}");
        File.Copy(targetPath, backupPath);
        return backupPath;
    }
}

/// <summary>Thrown when a write is attempted while the game is running.</summary>
public sealed class GameRunningException(string message) : Exception(message);
