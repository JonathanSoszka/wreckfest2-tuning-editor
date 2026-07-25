using Wf2Core;
using Xunit;

namespace Wf2Core.Tests;

public class PresetIoTests
{
    private const string Backup = "BACKUP_20260722_012434.sgfi";
    private static readonly DateTimeOffset Stamp = new(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);

    private static SaveFile Load() => SaveFile.Parse(Fixtures.Bytes(Backup));

    private static PresetExport ExportCalib(SaveFile save)
    {
        var car = save.Cars.Find("Hurricane")!;
        return PresetIo.Export(car, car.Find("CALIB")!, Stamp);
    }

    // ------------------------------------------------------------------ export

    [Fact]
    public void Export_CapturesTheStoredValuesVerbatim()
    {
        var save = Load();
        var car = save.Cars.Find("Hurricane")!;
        var preset = car.Find("CALIB")!;

        var export = PresetIo.Export(car, preset, Stamp);

        Assert.Equal(PresetIo.CurrentFormatVersion, export.FormatVersion);
        Assert.Equal("Hurricane", export.Source.Car);
        Assert.Equal("CALIB", export.Source.Preset);
        Assert.Equal(preset.Tuning.Count, export.Tuning.Count);

        // paramIndex / value / aux are authoritative and must survive untouched.
        foreach (var stored in preset.Tuning)
        {
            var exported = Assert.Single(export.Tuning, e => e.ParamIndex == stored.ParamIndex);
            Assert.Equal(stored.Value, exported.Value);
            Assert.Equal(stored.Aux, exported.Aux);
        }
    }

    [Fact]
    public void Export_UsesCarIndependentPartRoles()
    {
        var save = Load();
        var export = ExportCalib(save);

        Assert.NotEmpty(export.RequiredParts);
        // "car02/part/..." would make every cross-car comparison report a false mismatch.
        Assert.All(export.RequiredParts, p =>
        {
            Assert.DoesNotContain("data/vehicle/", p);
            Assert.StartsWith("part/", p);
        });
    }

    [Fact]
    public void Json_RoundTripsWithoutLoss()
    {
        var save = Load();
        var original = ExportCalib(save);

        var parsed = PresetIo.FromJson(PresetIo.ToJson(original));

        Assert.Equal(original.FormatVersion, parsed.FormatVersion);
        Assert.Equal(original.ExportedUtc, parsed.ExportedUtc);
        Assert.Equal(original.Source, parsed.Source);
        Assert.Equal(original.RequiredParts, parsed.RequiredParts);
        Assert.Equal(original.Tuning.Count, parsed.Tuning.Count);
        for (int i = 0; i < original.Tuning.Count; i++)
        {
            Assert.Equal(original.Tuning[i].ParamIndex, parsed.Tuning[i].ParamIndex);
            Assert.Equal(original.Tuning[i].Value, parsed.Tuning[i].Value);   // exact float equality
            Assert.Equal(original.Tuning[i].Aux, parsed.Tuning[i].Aux);
        }
    }

    // ------------------------------------------------------------------ validation

    [Theory]
    [InlineData("{ \"formatVersion\": 99, \"tuning\": [] }", "version")]
    [InlineData("{ \"formatVersion\": 1 }", "tuning")]
    [InlineData("not json at all", "valid preset file")]
    public void FromJson_RejectsBadInput(string json, string expectedInMessage)
    {
        var ex = Assert.Throws<InvalidDataException>(() => PresetIo.FromJson(json));
        Assert.Contains(expectedInMessage, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromJson_RejectsDuplicateParameters()
    {
        const string json = """
        { "formatVersion": 1, "exportedUtc": "", "source": { "car": "", "carConfig": "", "preset": "" },
          "requiredParts": [],
          "tuning": [ { "paramIndex": 0, "value": 0.1, "aux": 1 },
                      { "paramIndex": 0, "value": 0.2, "aux": 2 } ] }
        """;
        var ex = Assert.Throws<InvalidDataException>(() => PresetIo.FromJson(json));
        Assert.Contains("more than once", ex.Message);
    }

    // ------------------------------------------------------------------ import

    [Fact]
    public void Import_OntoItsOwnPreset_ChangesNothing()
    {
        var save = Load();
        var export = ExportCalib(save);

        var plan = PresetIo.Plan(save, "Hurricane", "CALIB", export);

        Assert.Empty(plan.Skipped);
        Assert.All(plan.Applied, c => Assert.True(c.IsNoOp));
        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void Import_AppliesValuesAndKeepsTheSaveValid()
    {
        var save = Load();
        var export = ExportCalib(save);

        var plan = PresetIo.Plan(save, "Crusader", "Dirt", export);
        PresetIo.Apply(save, plan);
        var rewritten = SaveFile.Parse(save.Serialize());

        Assert.True(rewritten.AllCrcsValid);
        Assert.Equal(21, rewritten.Cars.Count);          // nothing lost across the block boundary

        var dirt = rewritten.Cars.Find("Crusader")!.Find("Dirt")!;
        foreach (var applied in plan.Applied)
            Assert.Equal(applied.ToValue, dirt.Find(applied.ParamIndex)!.Value);
    }

    /// <summary>
    /// Tier 1 only: a parameter the target preset leaves at its default stores no record, so there
    /// is nothing to overwrite. It must be reported, never silently dropped.
    /// </summary>
    [Fact]
    public void Import_ReportsEveryValueAsEitherAppliedOrSkipped()
    {
        var save = Load();
        var export = ExportCalib(save);

        var plan = PresetIo.Plan(save, "Crusader", "Dirt", export);

        Assert.NotEmpty(plan.Skipped);
        Assert.Equal(export.Tuning.Count, plan.Applied.Count + plan.Skipped.Count);
        Assert.All(plan.Skipped, s => Assert.NotEmpty(s.Reason));
    }

    [Fact]
    public void Import_WarnsOnlyAboutPartsTheTargetGenuinelyLacks()
    {
        var save = Load();
        var export = ExportCalib(save);

        var plan = PresetIo.Plan(save, "Crusader", "Dirt", export);
        var partWarning = plan.Warnings.FirstOrDefault(w => w.Contains("adjustable parts"));

        Assert.NotNull(partWarning);
        // Crusader has the same gearbox / suspension / front ARB roles as the Hurricane; only the
        // rear ARB and the drivetrain differ. Naming the shared ones would be noise.
        Assert.DoesNotContain("gearbox", partWarning);
        Assert.DoesNotContain("suspension", partWarning);
        Assert.Contains("rear_antiroll_bar", partWarning);
    }

    // ------------------------------------------------------------------ import: Tier 2 (grow)

    [Fact]
    public void GrowImport_AddsMissingRecordsInsteadOfSkippingThem()
    {
        var save = Load();
        var export = ExportCalib(save);

        var tier1 = PresetIo.Plan(save, "Crusader", "Dirt", export);
        var tier2 = PresetIo.Plan(save, "Crusader", "Dirt", export, new PresetIo.ImportOptions(AllowAdd: true));

        Assert.NotEmpty(tier2.Added);
        Assert.Empty(tier2.Skipped);
        // Every value Tier 1 skipped is exactly what Tier 2 adds.
        Assert.Equal(
            tier1.Skipped.Select(s => s.ParamIndex).OrderBy(i => i),
            tier2.Added.Select(a => a.ParamIndex).OrderBy(i => i));
        // Nothing falls through the cracks either way.
        Assert.Equal(export.Tuning.Count, tier2.Applied.Count + tier2.Added.Count);
    }

    [Fact]
    public void GrowImport_ProducesAPresetHoldingEveryImportedValue_SortedAndValid()
    {
        var save = Load();
        var export = ExportCalib(save);
        int payloadBefore = save.Cars.PayloadLength;
        int addedCount = PresetIo.Plan(save, "Crusader", "Dirt", export,
                                       new PresetIo.ImportOptions(AllowAdd: true)).Added.Count;

        var plan = PresetIo.Plan(save, "Crusader", "Dirt", export, new PresetIo.ImportOptions(AllowAdd: true));
        PresetIo.Apply(save, plan);
        var rewritten = SaveFile.Parse(save.Serialize());

        Assert.True(rewritten.AllCrcsValid);
        Assert.Equal(21, rewritten.Cars.Count);
        // The payload grew by exactly one record per added value — nothing else moved.
        Assert.Equal(payloadBefore + addedCount * 12, rewritten.Cars.PayloadLength);

        var dirt = rewritten.Cars.Find("Crusader")!.Find("Dirt")!;
        // Every imported parameter is now present with its imported value.
        foreach (var v in export.Tuning)
            Assert.Equal(v.Value, dirt.Find(v.ParamIndex)!.Value);
        // Records stay in ascending paramIndex order, the way the game writes them.
        var indices = dirt.Tuning.Select(t => t.ParamIndex).ToList();
        Assert.Equal(indices.OrderBy(i => i), indices);
    }

    [Fact]
    public void AddTuningValue_RefusesToDuplicateAnExistingParameter()
    {
        var save = Load();
        // Crusader / Dirt stores Braking Balance (idx 0) already.
        Assert.Throws<InvalidOperationException>(
            () => save.Cars.AddTuningValue("Crusader", "Dirt", 0, 0, 0.5f));
    }

    [Fact]
    public void Plan_ThrowsForAnUnknownCarOrPreset()
    {
        var save = Load();
        var export = ExportCalib(save);

        Assert.Throws<InvalidOperationException>(() => PresetIo.Plan(save, "NoSuchCar", "Dirt", export));
        Assert.Throws<InvalidOperationException>(() => PresetIo.Plan(save, "Crusader", "NoSuchPreset", export));
    }
}
