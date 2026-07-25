using Wf2Core;
using Xunit;

namespace Wf2Core.Tests;

/// <summary>
/// G5 — Duplicate Preset. This is the first write that creates a preset that did not exist, so the
/// tests check the structure hard: the new preset is present and value-identical, the source is
/// untouched, the payload grew by exactly the new block, and the whole save still round-trips.
/// </summary>
public class DuplicatePresetTests
{
    private const string Backup = "BACKUP_20260722_012434.sgfi";

    private static SaveFile Load() => SaveFile.Parse(Fixtures.Bytes(Backup));

    /// <summary>Pick a car whose selected/first non-empty preset we can clone.</summary>
    private static (string car, string preset) ARichPreset(SaveFile save)
    {
        foreach (var c in save.Cars)
        {
            var p = c.Presets.FirstOrDefault(x => x.Tuning.Count > 0);
            if (p is not null) return (c.Name, p.Name);
        }
        throw new InvalidOperationException("fixture has no non-empty preset");
    }

    [Fact]
    public void Duplicate_AddsAPresetThatMatchesTheSource_AndLeavesTheSourceUntouched()
    {
        var save = Load();
        var (carName, presetName) = ARichPreset(save);
        var car = save.Cars.Find(carName)!;
        var source = car.Find(presetName)!;
        int presetsBefore = car.Presets.Count;
        var sourceValues = source.Tuning.Select(t => (t.ParamIndex, t.Aux, t.Value)).ToList();

        save.Cars.DuplicatePreset(carName, presetName, presetName + " copy");

        // Re-read through a full serialize→parse so we test what the game would load, not the in-memory edit.
        var rewritten = SaveFile.Parse(save.Serialize());
        Assert.True(rewritten.AllCrcsValid);
        Assert.Equal(save.Cars.Count, rewritten.Cars.Count);       // no car lost/gained

        var rcar = rewritten.Cars.Find(carName)!;
        Assert.Equal(presetsBefore + 1, rcar.Presets.Count);       // exactly one new preset

        var copy = rcar.Find(presetName + " copy");
        Assert.NotNull(copy);
        var original = rcar.Find(presetName)!;

        // Copy is value-for-value identical to the (unchanged) source.
        Assert.Equal(sourceValues,
            copy!.Tuning.Select(t => (t.ParamIndex, t.Aux, t.Value)));
        Assert.Equal(sourceValues,
            original.Tuning.Select(t => (t.ParamIndex, t.Aux, t.Value)));
    }

    [Fact]
    public void Duplicate_GrowsThePayloadByExactlyTheNewBlock()
    {
        var save = Load();
        var (carName, presetName) = ARichPreset(save);
        var source = save.Cars.Find(carName)!.Find(presetName)!;
        int before = save.Cars.PayloadLength;

        const string newName = "Clone";
        save.Cars.DuplicatePreset(carName, presetName, newName);

        // New block = [u32 len][name] + stvc(20) + atvc header(12) + records(n×12).
        int expected = 4 + newName.Length + 20 + 12 + source.Tuning.Count * 12;
        Assert.Equal(before + expected, save.Cars.PayloadLength);
    }

    [Fact]
    public void Create_AddsAnEmptyPreset_ThatRoundTrips()
    {
        var save = Load();
        var carName = save.Cars.First(c => c.Presets.Count > 0).Name;
        var car = save.Cars.Find(carName)!;
        int presetsBefore = car.Presets.Count;
        int payloadBefore = save.Cars.PayloadLength;

        const string name = "Fresh Setup";
        save.Cars.CreatePreset(carName, name);

        // The new block is just [len][name] + stvc(20) + empty atvc(12) — no records.
        Assert.Equal(payloadBefore + 4 + name.Length + 20 + 12, save.Cars.PayloadLength);

        var rewritten = SaveFile.Parse(save.Serialize());
        Assert.True(rewritten.AllCrcsValid);
        Assert.Equal(save.Cars.Count, rewritten.Cars.Count);

        var rcar = rewritten.Cars.Find(carName)!;
        Assert.Equal(presetsBefore + 1, rcar.Presets.Count);
        var created = rcar.Find(name);
        Assert.NotNull(created);
        Assert.Empty(created!.Tuning);                 // every slider at default → no records
        Assert.True(rcar.PresetsComplete);             // the preset list still parses to the end
    }

    [Fact]
    public void Create_ThenSetAValue_AddsToTheNewPresetOnly()
    {
        var save = Load();
        var carName = save.Cars.First(c => c.Presets.Count > 0).Name;

        save.Cars.CreatePreset(carName, "Blank");
        save.Cars.AddTuningValue(carName, "Blank", 0, 50, 0.5f);   // Braking Balance

        var rewritten = SaveFile.Parse(save.Serialize());
        var blank = rewritten.Cars.Find(carName)!.Find("Blank")!;
        Assert.Equal(0.5f, blank.Find(0)!.Value);
        Assert.Single(blank.Tuning);
    }

    [Fact]
    public void Create_RejectsBlankOrDuplicateNames()
    {
        var save = Load();
        var car = save.Cars.First(c => c.Presets.Count > 0);
        Assert.Throws<InvalidOperationException>(() => save.Cars.CreatePreset(car.Name, "   "));
        Assert.Throws<InvalidOperationException>(() => save.Cars.CreatePreset(car.Name, car.Presets[0].Name));
        Assert.Throws<InvalidOperationException>(() => save.Cars.CreatePreset("no such car", "x"));
    }

    [Fact]
    public void Duplicate_RejectsBlankOrDuplicateNames()
    {
        var save = Load();
        var (carName, presetName) = ARichPreset(save);

        Assert.Throws<InvalidOperationException>(() => save.Cars.DuplicatePreset(carName, presetName, "  "));
        Assert.Throws<InvalidOperationException>(() => save.Cars.DuplicatePreset(carName, presetName, presetName));
        Assert.Throws<InvalidOperationException>(() => save.Cars.DuplicatePreset(carName, "no such preset", "x"));
    }

    [Fact]
    public void Duplicate_ThenEditTheCopy_DoesNotTouchTheSource()
    {
        var save = Load();
        var (carName, presetName) = ARichPreset(save);
        var source = save.Cars.Find(carName)!.Find(presetName)!;
        var firstParam = source.Tuning[0];

        save.Cars.DuplicatePreset(carName, presetName, "Edited copy");
        // Change a value in the copy; the source's stored value for the same parameter must not move.
        save.Cars.SetTuningValue(carName, "Edited copy", firstParam.ParamIndex, firstParam.Aux, firstParam.Value + 1f);

        var rewritten = SaveFile.Parse(save.Serialize());
        var rcar = rewritten.Cars.Find(carName)!;
        Assert.Equal(firstParam.Value, rcar.Find(presetName)!.Find(firstParam.ParamIndex)!.Value);
        Assert.Equal(firstParam.Value + 1f, rcar.Find("Edited copy")!.Find(firstParam.ParamIndex)!.Value);
    }
}
