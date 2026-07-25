using Wf2Core;
using Xunit;

namespace Wf2Core.Tests;

/// <summary>
/// The editor now shows every editable parameter, not just the ones a preset already stores. Setting a
/// parameter the preset left at its default must <b>add</b> a record (the game omits defaults). This
/// covers that path with the exact schema the UI uses (<see cref="TuningSchema"/>).
/// </summary>
public class EditDefaultParameterTests
{
    private static SaveFile Load() => SaveFile.Parse(Fixtures.Bytes("BACKUP_20260722_012434.sgfi"));

    [Fact]
    public void SettingAnEditableParameterThatWasAtDefault_AddsAValidRecord()
    {
        var save = Load();

        // A rich preset, and an editable parameter it does NOT currently store (i.e. left at default).
        var (carName, presetName, schemaIndex) = FindDefaultEditable(save);
        var schema = TuningSchema.For(schemaIndex)!;
        var preset = save.Cars.Find(carName)!.Find(presetName)!;
        int before = preset.Tuning.Count;

        // Mirror the editor: pick a slider position, derive the on-step value from the schema.
        uint aux = (uint)Math.Max(1, schema.Steps / 2);
        float value = schema.ValueAt(aux);
        save.Cars.AddTuningValue(carName, presetName, schemaIndex, aux, value);

        var rewritten = SaveFile.Parse(save.Serialize());
        Assert.True(rewritten.AllCrcsValid);

        var rp = rewritten.Cars.Find(carName)!.Find(presetName)!;
        Assert.Equal(before + 1, rp.Tuning.Count);
        var rec = rp.Find(schemaIndex);
        Assert.NotNull(rec);
        Assert.Equal(aux, rec!.Aux);
        Assert.Equal(value, rec.Value);
        Assert.False(TuningSchema.IsOutsideExact(schemaIndex, rec.Value, out _, out _));   // legal, on range
    }

    private static (string car, string preset, uint index) FindDefaultEditable(SaveFile save)
    {
        foreach (var c in save.Cars)
        foreach (var p in c.Presets)
        {
            var missing = TuningSchema.EditableIndices.FirstOrDefault(
                i => p.Find(i) is null, uint.MaxValue);
            if (p.Tuning.Count > 0 && missing != uint.MaxValue)
                return (c.Name, p.Name, missing);
        }
        throw new InvalidOperationException("fixture has no preset with an unstored editable parameter");
    }
}
