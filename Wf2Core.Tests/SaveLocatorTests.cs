using Wf2Core;
using Xunit;

namespace Wf2Core.Tests;

/// <summary>
/// G4 — `SaveLocator` against a faked filesystem tree, so discovery is testable without a real
/// Steam install. Each test builds the directory layout it needs under a temp root.
/// </summary>
public class SaveLocatorTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "wf2loc_" + Guid.NewGuid().ToString("N"));

    private string Make(string relative, string content = "x")
    {
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private string Dir(string relative)
    {
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(full);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void FindSaves_ReturnsEveryProfilesSave()
    {
        var a = Make("docs/My Games/Wreckfest 2/111/savegame/profile.sgfi");
        var b = Make("docs/My Games/Wreckfest 2/222/savegame/profile.sgfi");
        Dir("docs/My Games/Wreckfest 2/333/savegame"); // a profile dir with no save file

        var locator = new SaveLocator(Path.Combine(_root, "docs"), [Path.Combine(_root, "steam")]);

        Assert.Equal(new[] { a, b }.OrderBy(x => x), locator.FindSaves().OrderBy(x => x));
        Assert.Null(locator.FindSingleSave()); // two saves → caller must choose
    }

    [Fact]
    public void FindSingleSave_ReturnsTheOneSave()
    {
        var save = Make("docs/My Games/Wreckfest 2/111/savegame/profile.sgfi");
        var locator = new SaveLocator(Path.Combine(_root, "docs"), [Path.Combine(_root, "steam")]);
        Assert.Equal(save, locator.FindSingleSave());
    }

    [Fact]
    public void WriteTargets_ForLiveSave_IncludeEveryUserdataMirror()
    {
        var save = Make("docs/My Games/Wreckfest 2/111/savegame/profile.sgfi");
        var mirror1 = Make($"steam/userdata/4444/{SaveLocator.AppId}/remote/profile.sgfi");
        var mirror2 = Make($"steam/userdata/5555/{SaveLocator.AppId}/remote/profile.sgfi");
        Make("steam/userdata/6666/999/remote/profile.sgfi"); // a different app — must be ignored

        var locator = new SaveLocator(Path.Combine(_root, "docs"), [Path.Combine(_root, "steam")]);

        var targets = locator.WriteTargetsFor(save);
        Assert.Equal(save, targets[0]);                 // the save is first (authoritative for backup)
        Assert.Contains(mirror1, targets);
        Assert.Contains(mirror2, targets);
        Assert.Equal(3, targets.Count);                 // save + two mirrors, not the other app
    }

    [Fact]
    public void WriteTargets_ForAnArbitraryFile_AreJustThatFile()
    {
        Make($"steam/userdata/4444/{SaveLocator.AppId}/remote/profile.sgfi");
        var loose = Make("elsewhere/backup.sgfi");
        var locator = new SaveLocator(Path.Combine(_root, "docs"), [Path.Combine(_root, "steam")]);

        var targets = locator.WriteTargetsFor(loose);
        Assert.Equal([loose], targets); // not a live save → no mirrors appended
    }

    [Fact]
    public void FindGameInstall_FollowsLibraryFoldersToAnotherDrive()
    {
        // The game is installed in a second library, declared in the primary root's libraryfolders.vdf.
        var libRoot = Dir("D_drive/SteamLibrary");
        var install = Dir("D_drive/SteamLibrary/steamapps/common/Wreckfest 2");
        Dir($"D_drive/SteamLibrary/steamapps/common/Wreckfest 2/{"data/vehicle/shared/part/tuning"}");

        var escaped = libRoot.Replace(@"\", @"\\");
        Make("steam/steamapps/libraryfolders.vdf", $$"""
            "libraryfolders"
            {
                "0" { "path" "{{Path.Combine(_root, "steam").Replace(@"\", @"\\")}}" }
                "1" { "path" "{{escaped}}" }
            }
            """);

        var locator = new SaveLocator(Path.Combine(_root, "docs"), [Path.Combine(_root, "steam")]);

        Assert.Equal(Path.GetFullPath(install), Path.GetFullPath(locator.FindGameInstall()!));
        Assert.NotNull(locator.FindTuningDir());
    }

    [Fact]
    public void FindSaves_IsEmpty_WhenNothingIsThere()
    {
        var locator = new SaveLocator(Path.Combine(_root, "docs"), [Path.Combine(_root, "steam")]);
        Assert.Empty(locator.FindSaves());
        Assert.Null(locator.FindSingleSave());
        Assert.Null(locator.FindGameInstall());
    }
}
