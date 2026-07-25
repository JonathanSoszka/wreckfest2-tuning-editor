namespace Wf2Core.Tests;

/// <summary>Milestone A.2 — the safe-write interlocks: block writes while the game runs, and always back up first.</summary>
public class SafeWriteTests
{
    private sealed class FakeSystemState(bool running) : ISystemState
    {
        public bool IsGameOrSteamRunning() => running;
    }

    [Fact]
    public void A2_Write_Blocked_WhenGameRunning_LeavesTargetUntouched()
    {
        using var tmp = new TempDir();
        var target = tmp.File("profile.sgfi", [1, 2, 3, 4]);
        var before = File.ReadAllBytes(target);

        var writer = new SaveWriter(new FakeSystemState(running: true));

        Assert.Throws<GameRunningException>(() => writer.Write(target, [9, 9, 9], tmp.Path));
        Assert.Equal(before, File.ReadAllBytes(target)); // unchanged
        Assert.Empty(Directory.GetFiles(tmp.Path, "*.bak.sgfi")); // no backup written on a blocked write
    }

    [Fact]
    public void A2_Write_BacksUpExistingFile_ThenWritesNewBytes()
    {
        using var tmp = new TempDir();
        var original = new byte[] { 10, 20, 30 };
        var target = tmp.File("profile.sgfi", original);
        var backupDir = Path.Combine(tmp.Path, "backups");

        var writer = new SaveWriter(new FakeSystemState(running: false));
        var newBytes = new byte[] { 40, 50, 60, 70 };

        var backupPath = writer.Write(target, newBytes, backupDir);

        Assert.NotNull(backupPath);
        Assert.Equal(original, File.ReadAllBytes(backupPath!)); // backup == pre-write file
        Assert.Equal(newBytes, File.ReadAllBytes(target));      // target == new bytes
    }

    [Fact]
    public void A2_Write_FirstTime_NoBackupWhenTargetMissing()
    {
        using var tmp = new TempDir();
        var target = Path.Combine(tmp.Path, "profile.sgfi"); // does not exist yet
        var writer = new SaveWriter(new FakeSystemState(running: false));

        var backupPath = writer.Write(target, [1, 2, 3], Path.Combine(tmp.Path, "backups"));

        Assert.Null(backupPath);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(target));
    }

    [Fact]
    public void WriteAllMirrors_Blocked_WhenGameRunning_LeavesEveryTargetUntouched()
    {
        using var tmp = new TempDir();
        var a = tmp.File("profile.sgfi", [1, 2, 3]);
        var b = tmp.File("mirror.sgfi", [1, 2, 3]);
        var writer = new SaveWriter(new FakeSystemState(running: true));

        Assert.Throws<GameRunningException>(() => writer.WriteAllMirrors([a, b], [9, 9], tmp.Path));

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(a));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(b));
        Assert.Empty(Directory.GetFiles(tmp.Path, "*.bak.sgfi"));
    }

    [Fact]
    public void WriteAllMirrors_Force_WritesEvenWhileGameRunning_AndStillBacksUp()
    {
        using var tmp = new TempDir();
        var original = new byte[] { 1, 2, 3 };
        var primary = tmp.File("profile.sgfi", original);
        var backupDir = Path.Combine(tmp.Path, "backups");
        var writer = new SaveWriter(new FakeSystemState(running: true));
        var newBytes = new byte[] { 7, 8 };

        var backupPath = writer.WriteAllMirrors([primary], newBytes, backupDir, force: true);

        Assert.NotNull(backupPath);
        Assert.Equal(original, File.ReadAllBytes(backupPath!)); // backup still taken
        Assert.Equal(newBytes, File.ReadAllBytes(primary));     // written despite the game running
    }

    [Fact]
    public void WriteAllMirrors_BacksUpPrimaryOnce_ThenWritesAllTargets()
    {
        using var tmp = new TempDir();
        var original = new byte[] { 10, 20, 30 };
        var primary = tmp.File("profile.sgfi", original);
        var mirror = tmp.File("mirror.sgfi", original);
        var backupDir = Path.Combine(tmp.Path, "backups");
        var writer = new SaveWriter(new FakeSystemState(running: false));
        var newBytes = new byte[] { 40, 50, 60, 70 };

        var backupPath = writer.WriteAllMirrors([primary, mirror], newBytes, backupDir);

        Assert.NotNull(backupPath);
        Assert.Equal(original, File.ReadAllBytes(backupPath!));   // backup == the pre-write primary
        Assert.Equal(newBytes, File.ReadAllBytes(primary));       // both targets got the new bytes
        Assert.Equal(newBytes, File.ReadAllBytes(mirror));
        Assert.Single(Directory.GetFiles(backupDir, "*.bak.sgfi")); // exactly one backup, not one per target
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wf2test_" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public string File(string name, byte[] content)
        {
            var p = System.IO.Path.Combine(Path, name);
            System.IO.File.WriteAllBytes(p, content);
            return p;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }
}
