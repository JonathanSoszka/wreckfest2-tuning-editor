using Wf2Core;
using Xunit;

namespace Wf2Core.Tests;

/// <summary>
/// Delete / Rename Preset. Both are variable-size edits to the preset list; rename also has to keep
/// the car's "selected preset" pointer valid. Tests round-trip through a full serialize→parse so they
/// check what the game would load, not just the in-memory edit.
/// </summary>
public class DeleteRenamePresetTests
{
    private const string Backup = "BACKUP_20260722_012434.sgfi";

    private static SaveFile Load() => SaveFile.Parse(Fixtures.Bytes(Backup));

    /// <summary>A car with ≥2 presets and a preset that is not the one selected in-game (safe to delete).</summary>
    private static (string car, string preset) ADeletablePreset(SaveFile save)
    {
        foreach (var c in save.Cars)
        {
            if (c.Presets.Count < 2) continue;
            var p = c.Presets.FirstOrDefault(x => !string.Equals(x.Name, c.SelectedPreset));
            if (p is not null) return (c.Name, p.Name);
        }
        throw new InvalidOperationException("fixture has no deletable preset");
    }

    [Fact]
    public void Delete_RemovesOnlyThatPreset_AndTheSaveStillRoundTrips()
    {
        var save = Load();
        var (carName, presetName) = ADeletablePreset(save);
        var car = save.Cars.Find(carName)!;
        int before = car.Presets.Count;
        var survivors = car.Presets.Select(p => p.Name).Where(n => n != presetName).ToList();

        save.Cars.DeletePreset(carName, presetName);

        var rewritten = SaveFile.Parse(save.Serialize());
        Assert.True(rewritten.AllCrcsValid);
        Assert.Equal(save.Cars.Count, rewritten.Cars.Count);          // no car lost/gained

        var rcar = rewritten.Cars.Find(carName)!;
        Assert.Equal(before - 1, rcar.Presets.Count);
        Assert.Null(rcar.Find(presetName));                            // gone
        Assert.Equal(survivors, rcar.Presets.Select(p => p.Name));    // the rest, in order
    }

    [Fact]
    public void Delete_ShrinksThePayloadByExactlyTheRemovedBlock()
    {
        var save = Load();
        var (carName, presetName) = ADeletablePreset(save);
        var preset = save.Cars.Find(carName)!.Find(presetName)!;
        int before = save.Cars.PayloadLength;

        int block = 4 + presetName.Length + 20 + 12 + preset.Tuning.Count * 12;
        save.Cars.DeletePreset(carName, presetName);

        Assert.Equal(before - block, save.Cars.PayloadLength);
    }

    [Fact]
    public void Delete_RefusesTheSelectedPreset_AndTheLastPreset()
    {
        var save = Load();

        // The selected preset is protected (deleting it would dangle the selection).
        var selCar = save.Cars.First(c => c.Presets.Count > 1 &&
                                          c.Presets.Any(p => string.Equals(p.Name, c.SelectedPreset)));
        Assert.Throws<InvalidOperationException>(
            () => save.Cars.DeletePreset(selCar.Name, selCar.SelectedPreset));

        // A car must keep at least one preset.
        var loneCar = save.Cars.FirstOrDefault(c => c.Presets.Count == 1);
        if (loneCar is not null)
            Assert.Throws<InvalidOperationException>(
                () => save.Cars.DeletePreset(loneCar.Name, loneCar.Presets[0].Name));
    }

    [Fact]
    public void Rename_ChangesTheName_KeepsValues_AndRoundTrips()
    {
        var save = Load();
        var (carName, presetName) = ADeletablePreset(save);   // a non-selected preset
        var before = save.Cars.Find(carName)!.Find(presetName)!
            .Tuning.Select(t => (t.ParamIndex, t.Aux, t.Value)).ToList();

        const string newName = "Renamed Setup";
        save.Cars.RenamePreset(carName, presetName, newName);

        var rewritten = SaveFile.Parse(save.Serialize());
        Assert.True(rewritten.AllCrcsValid);
        var rcar = rewritten.Cars.Find(carName)!;
        Assert.Null(rcar.Find(presetName));                   // old name gone
        var renamed = rcar.Find(newName);
        Assert.NotNull(renamed);
        Assert.Equal(before, renamed!.Tuning.Select(t => (t.ParamIndex, t.Aux, t.Value)));
    }

    [Fact]
    public void Rename_OfTheSelectedPreset_UpdatesTheSelectionPointer()
    {
        var save = Load();
        var car = save.Cars.First(c => c.Presets.Any(p => string.Equals(p.Name, c.SelectedPreset)));
        string carName = car.Name, selected = car.SelectedPreset;

        const string newName = "Active Renamed";
        save.Cars.RenamePreset(carName, selected, newName);

        var rewritten = SaveFile.Parse(save.Serialize());
        var rcar = rewritten.Cars.Find(carName)!;
        Assert.Equal(newName, rcar.SelectedPreset);           // pointer followed the rename
        Assert.NotNull(rcar.Find(newName));
    }

    [Fact]
    public void Rename_RejectsBlankAndDuplicateNames()
    {
        var save = Load();
        var car = save.Cars.First(c => c.Presets.Count > 1);
        string carName = car.Name;
        string a = car.Presets[0].Name, b = car.Presets[1].Name;

        Assert.Throws<InvalidOperationException>(() => save.Cars.RenamePreset(carName, a, "   "));
        Assert.Throws<InvalidOperationException>(() => save.Cars.RenamePreset(carName, a, b));   // collides
        Assert.Throws<InvalidOperationException>(() => save.Cars.RenamePreset("no such car", a, "x"));
    }
}
