namespace Wf2Core.Tests;

/// <summary>
/// Acceptance tests for the full save reader/writer (<see cref="SaveFile"/>).
///
/// Two fixtures drive them:
/// <list type="bullet">
///   <item><c>BACKUP_20260722_012434.sgfi</c> — a real, unmodified game-written save (20 cars).</item>
///   <item><c>TEST_brake44_v2.sgfi</c> — a save we rebuilt that the game <b>accepted and loaded</b>
///   with all data intact. It differs from the backup by exactly one edit: Hurricane → "Preset 1" →
///   parameter 0 (Braking Balance) went from <c>aux=19, value=0.19</c> to <c>aux=44, value=0.44</c>.
///   It is the correctness oracle for the writer.</item>
/// </list>
/// </summary>
public class SaveFileTests
{
    private const string Backup = "BACKUP_20260722_012434.sgfi";
    private const string Brake44 = "TEST_brake44_v2.sgfi";

    private static SaveFile Load(string name) => SaveFile.Parse(Fixtures.Bytes(name));

    // ---------------------------------------------------------------- 1. integrity

    [Theory]
    [InlineData(Backup)]
    [InlineData(Brake44)]
    public void Parse_ValidatesEveryIntegrityLayer(string fileName)
    {
        var save = Load(fileName);

        Assert.True(save.StoredCrcValid, "outer header CRC-32C does not match the decompressed tree");
        Assert.Equal(4, save.Chunks.Count);
        Assert.Equal(["forp", "srcc", "sspu", "sdia"], save.Chunks.Select(c => c.Tag));

        foreach (var chunk in save.Chunks)
        {
            Assert.True(chunk.StoredCrcValid, $"chunk '{chunk.Tag}' CRC-32C does not match its bytes");
            Assert.True(chunk.Container.StoredCrcValid, $"container '{chunk.Tag}' CRC-32C does not match its content");
        }
        Assert.True(save.AllCrcsValid);
    }

    [Fact]
    public void CarsChunk_HasTheExpectedShape()
    {
        var save = Load(Backup);
        var cars = save.Chunks.Single(c => c.Tag == CarCollection.ContainerTag);

        Assert.Equal(65536, cars.Container.Content.Length);   // fixed 64 KiB cars buffer
        Assert.NotEmpty(cars.Trailer);                        // the chunk runs past the container end
    }

    // ---------------------------------------------------------------- 2. cars

    [Fact]
    public void Cars_EnumeratesEveryCarInTheSave()
    {
        var save = Load(Backup);

        Assert.Equal(21, save.Cars.Count);
        var names = save.Cars.Select(c => c.Name).ToList();
        Assert.Contains("Hurricane", names);
        Assert.Contains("RoadSlayer", names);
        Assert.Contains("Nami", names);
        Assert.Contains("Stahlwagen", names);
    }

    /// <summary>
    /// Regression: the cars payload continues into LZ4 continuation blocks held in the chunk
    /// trailer, decoded with the previous block's output as a dictionary. Reading the container
    /// alone silently drops every car past the first ~64 KiB block — it yields a complete-looking
    /// prefix whose CRC even validates, which is exactly how "Jackal" went missing while being
    /// present in the player's garage the whole time.
    /// </summary>
    [Fact]
    public void Cars_IncludesCarsStoredInContinuationBlocks()
    {
        var save = Load(Backup);
        var chunk = save.Chunks.Single(c => c.Tag == CarCollection.ContainerTag);

        // the payload really does extend past the container's own block
        Assert.True(chunk.DecodedPayload.Length > chunk.ContainerPayloadLength,
                    "cars payload should span more than the container block");

        // the car that only exists in the continuation block
        var jackal = save.Cars.Find("Jackal");
        Assert.NotNull(jackal);
        Assert.Equal("car26:default", jackal!.Config);
        Assert.Contains(jackal.Presets, p => p.Name.Contains("Dalsbanen", StringComparison.Ordinal));

        // and it is correctly refused for editing, since continuation blocks cannot be re-encoded
        var record = jackal.Presets.SelectMany(p => p.Tuning).FirstOrDefault();
        if (record is not null && !save.Cars.IsEditable(record))
            Assert.Throws<NotSupportedException>(() => save.Cars.SetTuningValue(record, 0, 0f));
    }

    [Fact]
    public void Cars_CarryTheirKeyAndConfigStrings()
    {
        var hurricane = Load(Backup).Cars.Find("Hurricane");

        Assert.NotNull(hurricane);
        Assert.StartsWith("VEHICLE_NAME_", hurricane.VehicleKey, StringComparison.Ordinal);
        Assert.Equal("car02:default", hurricane.Config);
    }

    // ---------------------------------------------------------------- 3. presets and tuning

    [Fact]
    public void Hurricane_HasTheExpectedPresetsAndTuning()
    {
        var hurricane = Load(Backup).Cars.Find("Hurricane");
        Assert.NotNull(hurricane);

        var presetNames = hurricane.Presets.Select(p => p.Name).ToList();
        Assert.Contains("Preset 1", presetNames);
        Assert.Contains("CALIB", presetNames);
        Assert.Contains("Hybrid_", presetNames);

        var preset1 = hurricane.Find("Preset 1");
        Assert.NotNull(preset1);
        var record = Assert.Single(preset1.Tuning);
        Assert.Equal(0u, record.ParamIndex);          // Braking Balance
        Assert.Equal(19u, record.Aux);
        Assert.Equal(0.19f, record.Value, 5);

        var calib = hurricane.Find("CALIB");
        Assert.NotNull(calib);
        Assert.Equal(31, calib.Tuning.Count);
    }

    [Fact]
    public void Presets_OnlyStoreNonDefaultValues()
    {
        // The CALIB run left Rear Camber (34) and Front Balancer (2) at their defaults, so the game
        // stores no record for them at all.
        var calib = Load(Backup).Cars.Find("Hurricane")!.Find("CALIB");
        Assert.NotNull(calib);
        Assert.Null(calib.Find(34));
        Assert.Null(calib.Find(2));
        Assert.Equal(140f, calib.Find(1)!.Value, 3);       // Braking Pressure
        Assert.Equal(0.2035f, calib.Find(53)!.Value, 5);   // Ride height front, metres
    }

    // ---------------------------------------------------------------- 4. round-trip

    [Theory]
    [InlineData(Backup)]
    [InlineData(Brake44)]
    public void Serialize_WithNoChanges_RoundTripsAllContent(string fileName)
    {
        var original = Load(fileName);
        var reparsed = SaveFile.Parse(original.Serialize());

        Assert.True(reparsed.AllCrcsValid, "a re-serialized save must validate at every layer");
        Assert.True(original.ContentEquals(reparsed), "decoded content changed across a no-op round-trip");
        Assert.Equal(original.Cars.Count, reparsed.Cars.Count);
    }

    [Fact]
    public void Serialize_WithNoChanges_IsByteIdentical()
    {
        // Nothing was modified, so every container re-emits the compressed bytes it was parsed from.
        var bytes = Fixtures.Bytes(Backup);
        Assert.Equal(bytes, SaveFile.Parse(bytes).Serialize());
    }

    // ---------------------------------------------------------------- 5. the key test

    [Fact]
    public void EditingBrakeBalance_ReproducesTheGameAcceptedSave()
    {
        var save = Load(Backup);
        save.Cars.SetTuningValue("Hurricane", "Preset 1", paramIndex: 0, aux: 44, value: 0.44f);

        var rebuilt = SaveFile.Parse(save.Serialize());
        var oracle = Load(Brake44);

        Assert.True(rebuilt.AllCrcsValid, "every CRC in our output must validate");
        Assert.True(rebuilt.ContentEquals(oracle),
            "the rebuilt save's decoded content must match the save the game accepted");

        var edited = rebuilt.Cars.Find("Hurricane")!.Find("Preset 1")!;
        var record = Assert.Single(edited.Tuning);
        Assert.Equal(0u, record.ParamIndex);
        Assert.Equal(44u, record.Aux);
        Assert.Equal(0.44f, record.Value, 5);
    }

    [Fact]
    public void SetTuningValue_LeavesEverythingElseUntouched()
    {
        var before = Load(Backup);
        var after = Load(Backup);
        after.Cars.SetTuningValue("Hurricane", "Preset 1", 0, 44, 0.44f);

        var a = before.Chunks.Single(c => c.Tag == CarCollection.ContainerTag).Container.Content;
        var b = after.Chunks.Single(c => c.Tag == CarCollection.ContainerTag).Container.Content;

        Assert.Equal(a.Length, b.Length);                      // the edit is size-neutral
        var changed = Enumerable.Range(0, a.Length).Where(i => a[i] != b[i]).ToList();
        Assert.All(changed, i => Assert.InRange(i, 0x40C7, 0x40D2));   // inside the one 12-byte record
    }

    [Fact]
    public void SetTuningValue_Throws_ForUnknownTargets()
    {
        var save = Load(Backup);
        Assert.Throws<InvalidOperationException>(() => save.Cars.SetTuningValue("Nope", "Preset 1", 0, 1, 1f));
        Assert.Throws<InvalidOperationException>(() => save.Cars.SetTuningValue("Hurricane", "Nope", 0, 1, 1f));
        // Parameter 99 is at its default and therefore has no record to overwrite.
        Assert.Throws<InvalidOperationException>(() => save.Cars.SetTuningValue("Hurricane", "Preset 1", 99, 1, 1f));
    }

    // ---------------------------------------------------------------- regression guards

    [Theory]
    [MemberData(nameof(AllSaves))]
    public void EverySaveFixture_ParsesWithValidCrcs(string fileName)
    {
        var save = Load(fileName);
        Assert.True(save.AllCrcsValid);
        Assert.NotEmpty(save.Cars);
    }

    public static IEnumerable<object[]> AllSaves() => Fixtures.AllSgfi();

    [Fact]
    public void Parse_RejectsANonSaveFile()
    {
        Assert.Throws<SgfiFormatException>(() => SaveFile.Parse(new byte[64]));
    }
}
