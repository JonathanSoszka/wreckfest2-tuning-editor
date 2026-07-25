using System.Buffers.Binary;
using System.Collections;
using System.Text;

namespace Wf2Core;

/// <summary>
/// One stored tuning value inside a preset's <c>atvc</c> node.
/// </summary>
/// <param name="Offset">
/// Byte offset of the 12-byte record inside the cars container's decompressed content — the handle
/// an editor uses to patch the value in place.
/// </param>
/// <param name="ParamIndex">
/// The parameter this value belongs to. See <c>docs/PARAM_MAP.md</c> for the canonical map
/// (0 = Braking Balance, 40..46 = gearbox, 53/54 = ride height, …).
/// </param>
/// <param name="Aux">
/// The <b>slider position</b> — the authoritative field. The stored <paramref name="Value"/> is
/// derived from it: <c>value = min + aux × (max − min) / steps</c>, with min/max/steps from the
/// part's <c>.ctms</c> schema (<see cref="TuningParameter"/>). Verified against every record of every
/// fixture save; see <c>docs/PARAM_MAP.md</c>. An editor should set <c>aux</c> and derive the value,
/// which keeps the result in range and on-step by construction.
/// </param>
/// <param name="Value">
/// The <b>physical</b> value in SI base units — metres, N/m, N·s/m — <em>not</em> a normalized
/// slider position. The UI converts for display and can switch metric/imperial, so a number read
/// off the screen is never the stored number.
/// </param>
public sealed record TuningRecord(int Offset, uint ParamIndex, uint Aux, float Value);

/// <summary>
/// One tuning preset of one car: a name, the <c>stvc</c> marker node and an <c>atvc</c> value node.
/// Only values the player actually moved are stored — sliders left at their default are absent
/// entirely, so an empty <see cref="Tuning"/> list means "everything stock", not "no data".
/// </summary>
public sealed class TuningPreset
{
    internal TuningPreset(string name, int nameOffset, int valuesOffset, uint valuesKind,
                          IReadOnlyList<TuningRecord> tuning)
    {
        Name = name;
        NameOffset = nameOffset;
        ValuesOffset = valuesOffset;
        ValuesKind = valuesKind;
        Tuning = tuning;
    }

    /// <summary>The preset's display name, e.g. <c>"Preset 1"</c> or <c>"CALIB"</c>.</summary>
    public string Name { get; }

    /// <summary>Offset of the name's length prefix inside the cars container content.</summary>
    public int NameOffset { get; }

    /// <summary>Offset of the <c>atvc</c> tag inside the cars container content.</summary>
    public int ValuesOffset { get; }

    /// <summary>The <c>atvc</c> node's kind word. 2 — a list of numeric records — in every save seen.</summary>
    public uint ValuesKind { get; }

    /// <summary>The stored (non-default) tuning values, in file order.</summary>
    public IReadOnlyList<TuningRecord> Tuning { get; }

    /// <summary>Find the stored record for <paramref name="paramIndex"/>, or null if it is at its default.</summary>
    public TuningRecord? Find(uint paramIndex)
    {
        foreach (var r in Tuning)
            if (r.ParamIndex == paramIndex) return r;
        return null;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} ({Tuning.Count} value(s))";
}

/// <summary>
/// One car's record inside the cars container: its localisation key, display name, config string
/// and its tuning presets.
/// </summary>
public sealed class CarRecord
{
    internal CarRecord(int offset, string vehicleKey, string name, string config,
                       IReadOnlyList<TuningPreset> presets, string sourceId, string selectedPreset,
                       bool presetsComplete, IReadOnlyList<string> parts)
    {
        Parts = parts;
        Offset = offset;
        VehicleKey = vehicleKey;
        Name = name;
        Config = config;
        Presets = presets;
        SourceId = sourceId;
        SelectedPreset = selectedPreset;
        PresetsComplete = presetsComplete;
    }

    /// <summary>Offset of this car's <c>nart</c> tag inside the cars container content.</summary>
    public int Offset { get; }

    /// <summary>The localisation key, e.g. <c>"VEHICLE_NAME_3137119279_9"</c>.</summary>
    public string VehicleKey { get; }

    /// <summary>The display name, e.g. <c>"Hurricane"</c>.</summary>
    public string Name { get; }

    /// <summary>The config string, e.g. <c>"car02:default"</c>.</summary>
    public string Config { get; }

    /// <summary>The car's tuning presets, in file order.</summary>
    public IReadOnlyList<TuningPreset> Presets { get; }

    /// <summary>
    /// Full asset paths of the parts fitted to this car, e.g.
    /// <c>data/vehicle/car02/part/engine/stock/engine_block_b.upgr</c>.
    ///
    /// <para>Read from the <b>decompressed</b> cars payload, where paths are literal. An earlier
    /// reader scanned the outer tree — which is mostly the still-compressed cars container — and so
    /// only ever recovered mangled fragments.</para>
    /// </summary>
    public IReadOnlyList<string> Parts { get; }

    /// <summary>
    /// The optional text slot that follows the preset list — an online/ghost id such as
    /// <c>"local-0000000000…"</c>. Empty when its length field is zero (see the union note on
    /// <see cref="CarCollection"/>).
    /// </summary>
    public string SourceId { get; }

    /// <summary>The name of the preset the car currently has selected.</summary>
    public string SelectedPreset { get; }

    /// <summary>
    /// False when the preset list could not be decoded to the end. This happens when a car's record
    /// straddles the boundary between LZ4 blocks and only the first block was available — read
    /// <see cref="SaveChunk.DecodedPayload"/> (not the container alone) and it should be complete.
    /// </summary>
    public bool PresetsComplete { get; }

    /// <summary>Find a preset by name (case-insensitive), or null.</summary>
    public TuningPreset? Find(string presetName)
    {
        foreach (var p in Presets)
            if (string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase)) return p;
        return null;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} [{Config}] {Presets.Count} preset(s)";
}

/// <summary>
/// The cars, presets and tuning values held in a save's <c>srcc</c> container, and the editing
/// entry point for changing a stored tuning value.
///
/// <para><b>Grammar.</b> The container's decompressed content opens with a <c>racc</c> node and
/// then holds one record per car:</para>
/// <code>
/// nart [u32 2][u32 1] [str vehicleKey][str displayName][str config]
///   … part paths and stat nodes …
///   pstv [u32 kind=0][u32 presetCount]
///     presetCount × { [str name] stvc [4 × u32] atvc [u32 kind=2][u32 count] count × 12 bytes }
///     [u32 reserved] [str sourceId] [str selectedPreset]
///   stsc …
/// </code>
/// Each 12-byte <c>atvc</c> record is <c>[u32 paramIndex][u32 aux][f32 value]</c>.
///
/// <para><b>The tagged union.</b> The slots after the preset list are length-prefixed: a
/// <em>zero</em> length means the slot is absent, a non-zero length means it carries text (a preset
/// or ghost name) rather than a number. Both forms occur in real saves — most cars store an empty
/// <see cref="CarRecord.SourceId"/>, cars whose tune came from a leaderboard ghost store a
/// <c>"local-…"</c> string there.</para>
///
/// <para><b>Editing.</b> <see cref="SetTuningValue(string,string,uint,uint,float)"/> patches the
/// 12-byte record in place, which is size-neutral: the container content keeps its exact length and
/// only the CRC layers change. Adding or removing records (which would resize the container, and
/// therefore the enclosing chunk) is deliberately not supported — see the caveat on
/// <see cref="SaveFile.Serialize"/>.</para>
/// </summary>
public sealed class CarCollection : IReadOnlyList<CarRecord>
{
    /// <summary>The 4CC of the container that holds the cars (reversed FourCC of <c>ccrs</c>).</summary>
    public const string ContainerTag = "srcc";

    private const string CarTag = "nart";
    private const string PresetListTag = "pstv";
    private const string PresetMarkerTag = "stvc";
    private const string ValuesTag = "atvc";
    private const string VehicleKeyPrefix = "VEHICLE_NAME_";

    /// <summary>Size of the <c>stvc</c> node: the 4CC plus four uint32 words.</summary>
    private const int PresetMarkerSize = 4 + 16;

    /// <summary>Size of one <c>atvc</c> value record.</summary>
    private const int RecordSize = 12;

    private const int MaxStringLength = 4096;
    private const int MaxPresets = 256;
    private const int MaxRecords = 1024;

    private readonly SaveChunk? _chunk;
    private byte[] _payload;
    private List<CarRecord> _cars;

    /// <param name="chunk">The <c>srcc</c> chunk, or null when the save has no cars chunk.</param>
    internal CarCollection(SaveChunk? chunk)
    {
        _chunk = chunk;
        _payload = chunk?.DecodedPayload ?? [];
        _cars = chunk is null ? [] : ParseCars(_payload);
    }

    /// <summary>False when the save has no <c>srcc</c> chunk (then the collection is empty).</summary>
    public bool IsPresent => _chunk is not null;

    /// <summary>Total size of the cars payload, across all LZ4 blocks.</summary>
    public int PayloadLength => _payload.Length;

    /// <summary>
    /// True when <paramref name="record"/> can be written. Every record in the payload is editable —
    /// including cars stored in continuation blocks, which are re-encoded on save.
    /// </summary>
    public bool IsEditable(TuningRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.Offset >= 0 && record.Offset + RecordSize <= _payload.Length;
    }

    /// <inheritdoc/>
    public int Count => _cars.Count;

    /// <inheritdoc/>
    public CarRecord this[int index] => _cars[index];

    /// <summary>Find a car by display name (case-insensitive), or null.</summary>
    public CarRecord? Find(string name)
    {
        foreach (var c in _cars)
            if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
        return null;
    }

    /// <inheritdoc/>
    public IEnumerator<CarRecord> GetEnumerator() => _cars.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Overwrite an existing tuning record in place. The record must already exist: the game omits
    /// every slider left at its default, so a parameter that was never touched has no record to
    /// change and adding one would resize the container.
    /// </summary>
    /// <param name="carName">Car display name, e.g. <c>"Hurricane"</c>.</param>
    /// <param name="presetName">Preset name, e.g. <c>"Preset 1"</c>.</param>
    /// <param name="paramIndex">Parameter index — see <c>docs/PARAM_MAP.md</c>.</param>
    /// <param name="aux">The secondary word to store (usually the slider percent).</param>
    /// <param name="value">The physical value in SI base units.</param>
    /// <exception cref="InvalidOperationException">The car, preset or record does not exist.</exception>
    public void SetTuningValue(string carName, string presetName, uint paramIndex, uint aux, float value)
    {
        var car = Find(carName) ?? throw new InvalidOperationException($"No car named '{carName}' in this save.");
        var preset = car.Find(presetName)
            ?? throw new InvalidOperationException($"Car '{car.Name}' has no preset named '{presetName}'.");
        var record = preset.Find(paramIndex)
            ?? throw new InvalidOperationException(
                $"Preset '{preset.Name}' of '{car.Name}' stores no value for parameter {paramIndex} " +
                "(the slider is at its default, and adding a record is not supported).");
        SetTuningValue(record, aux, value);
    }

    /// <summary>
    /// Overwrite <paramref name="record"/> in place. The record must come from this collection.
    /// </summary>
    /// <exception cref="InvalidOperationException">This save has no cars container.</exception>
    public void SetTuningValue(TuningRecord record, uint aux, float value)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (_chunk is null)
            throw new InvalidOperationException("This save has no cars container to edit.");

        var content = _payload;
        if (record.Offset < 0 || record.Offset + RecordSize > content.Length)
            throw new InvalidOperationException($"Tuning record offset 0x{record.Offset:X} is outside the cars payload.");

        uint stored = BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(record.Offset, 4));
        if (stored != record.ParamIndex)
            throw new InvalidOperationException(
                $"Stale tuning record: offset 0x{record.Offset:X} now holds parameter {stored}, expected {record.ParamIndex}.");

        BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(record.Offset + 4, 4), aux);
        BinaryPrimitives.WriteSingleLittleEndian(content.AsSpan(record.Offset + 8, 4), value);
        // Push the whole payload back so it is re-split across blocks and every block CRC recomputed.
        _chunk.SetDecodedPayload(content);
        _cars = ParseCars(content);
    }

    /// <summary>
    /// Add a tuning record for a parameter the preset does not yet store, growing the payload by one
    /// 12-byte record. The game omits sliders left at their default, so this is how an imported tune
    /// brings a car off a default it never touched.
    ///
    /// <para>The record is inserted in ascending <c>paramIndex</c> order — the order the game itself
    /// writes them — and the node's count word is bumped, so the result is indistinguishable from a
    /// preset the game produced. This resizes the container; every downstream length and CRC is
    /// recomputed on <see cref="SaveFile.Serialize"/> (the verified variable-size write path).</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The car or preset does not exist, the parameter is already stored (use
    /// <see cref="SetTuningValue(string,string,uint,uint,float)"/> to overwrite it), or the preset's
    /// value node has moved since it was parsed.
    /// </exception>
    public void AddTuningValue(string carName, string presetName, uint paramIndex, uint aux, float value)
    {
        if (_chunk is null)
            throw new InvalidOperationException("This save has no cars container to edit.");
        var car = Find(carName) ?? throw new InvalidOperationException($"No car named '{carName}' in this save.");
        var preset = car.Find(presetName)
            ?? throw new InvalidOperationException($"Car '{car.Name}' has no preset named '{presetName}'.");
        if (preset.Find(paramIndex) is not null)
            throw new InvalidOperationException(
                $"Preset '{preset.Name}' of '{car.Name}' already stores parameter {paramIndex}; " +
                "use SetTuningValue to overwrite it.");

        int atvc = preset.ValuesOffset;
        if (!TagAt(_payload, atvc, ValuesTag))
            throw new InvalidOperationException(
                $"Stale preset: offset 0x{atvc:X} no longer holds an '{ValuesTag}' node.");

        uint count = ReadU32(_payload, atvc + 8);
        int recordsStart = atvc + 12;

        // Insert in ascending paramIndex order (the game's own ordering): before the first existing
        // record with a larger index, else at the end of this preset's record list.
        int insertAt = recordsStart + (int)count * RecordSize;
        for (int i = 0; i < count; i++)
        {
            int off = recordsStart + i * RecordSize;
            if (paramIndex < ReadU32(_payload, off)) { insertAt = off; break; }
        }

        var updated = new byte[_payload.Length + RecordSize];
        _payload.AsSpan(0, insertAt).CopyTo(updated);
        var rec = updated.AsSpan(insertAt, RecordSize);
        BinaryPrimitives.WriteUInt32LittleEndian(rec[..4], paramIndex);
        BinaryPrimitives.WriteUInt32LittleEndian(rec[4..8], aux);
        BinaryPrimitives.WriteSingleLittleEndian(rec[8..12], value);
        _payload.AsSpan(insertAt).CopyTo(updated.AsSpan(insertAt + RecordSize));
        BinaryPrimitives.WriteUInt32LittleEndian(updated.AsSpan(atvc + 8, 4), count + 1);

        _payload = updated;
        _chunk.SetDecodedPayload(_payload);
        _cars = ParseCars(_payload);
    }

    /// <summary>
    /// Create a new preset that is a copy of <paramref name="sourcePresetName"/> under
    /// <paramref name="newName"/>. Unlike editing a value, this <b>adds a whole preset</b> to the
    /// car's <c>pstv</c> node and bumps its preset count — the first write of a preset that did not
    /// previously exist.
    ///
    /// <para>The new block is a fresh name string followed by a verbatim copy of the source's
    /// <c>stvc</c> and <c>atvc</c> nodes and records, appended after the car's last preset (just
    /// before the trailing source/selected slots). It grows the payload; the verified variable-size
    /// write path recomputes every length and CRC on save.</para>
    ///
    /// <para><b>Caveat:</b> the four <c>stvc</c> words are undecoded and copied as-is. That is
    /// correct if they carry no per-preset identity, which is expected but unproven — see
    /// <c>docs/PLAN_gui.md</c> §8a. Duplication must be confirmed in-game before it is trusted.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The car or source preset does not exist, <paramref name="newName"/> is blank or already used by
    /// the car, the preset cap is reached, or the source preset's nodes have moved since parsing.
    /// </exception>
    public void DuplicatePreset(string carName, string sourcePresetName, string newName)
    {
        var (car, list, name) = BeginPresetInsert(carName, newName);
        var source = car.Find(sourcePresetName)
            ?? throw new InvalidOperationException($"Car '{car.Name}' has no preset named '{sourcePresetName}'.");

        // The clonable tail of the source preset: stvc (20) + atvc header (12) + its records.
        int stvcStart = source.ValuesOffset - PresetMarkerSize;
        if (stvcStart < 0 || !TagAt(_payload, stvcStart, PresetMarkerTag) || !TagAt(_payload, source.ValuesOffset, ValuesTag))
            throw new InvalidOperationException(
                $"Stale preset '{source.Name}': its nodes are no longer where they were parsed.");
        int tailEnd = source.ValuesOffset + 12 + source.Tuning.Count * RecordSize;

        InsertPreset(car, list, name, _payload.AsSpan(stvcStart, tailEnd - stvcStart));
    }

    /// <summary>
    /// Create a new, empty preset under <paramref name="newName"/> — every slider at its default,
    /// exactly like the game's own new presets (which store no records). Adds a preset to the car's
    /// <c>pstv</c> node and bumps its count.
    ///
    /// <para>The empty preset needs a valid <c>stvc</c> node; its four words are undecoded, so they
    /// are copied from an existing preset of the same car (an empty one when available). We proved
    /// those words carry no per-preset identity (see <c>docs/PLAN_gui.md</c> §8a), so this is safe.
    /// The new <c>atvc</c> node is written fresh with a zero record count.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The car does not exist, has no preset to template the <c>stvc</c> from, <paramref name="newName"/>
    /// is blank or already used, or the preset cap is reached.
    /// </exception>
    public void CreatePreset(string carName, string newName)
    {
        var (car, list, name) = BeginPresetInsert(carName, newName);
        if (car.Presets.Count == 0)
            throw new InvalidOperationException(
                $"Car '{car.Name}' has no existing preset to model the new one on.");

        // Prefer an existing empty preset as the stvc template — the closest structural match.
        var template = car.Presets.FirstOrDefault(p => p.Tuning.Count == 0) ?? car.Presets[0];
        int stvcStart = template.ValuesOffset - PresetMarkerSize;
        if (stvcStart < 0 || !TagAt(_payload, stvcStart, PresetMarkerTag))
            throw new InvalidOperationException(
                $"Stale preset '{template.Name}': its stvc node is no longer where it was parsed.");

        // tail = stvc (20, copied) + a fresh empty atvc node (12: tag + kind 2 + count 0).
        var tail = new byte[PresetMarkerSize + 12];
        _payload.AsSpan(stvcStart, PresetMarkerSize).CopyTo(tail);
        Encoding.Latin1.GetBytes(ValuesTag, tail.AsSpan(PresetMarkerSize, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(PresetMarkerSize + 4, 4), 2); // kind
        BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(PresetMarkerSize + 8, 4), 0); // count

        InsertPreset(car, list, name, tail);
    }

    /// <summary>
    /// Delete <paramref name="presetName"/> from <paramref name="carName"/>: remove its whole block
    /// (name + <c>stvc</c> + <c>atvc</c> + records) from the car's <c>pstv</c> node and decrement the
    /// preset count. Shrinks the payload; the verified variable-size write path recomputes every length
    /// and CRC on <see cref="SaveFile.Serialize"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The car or preset does not exist, it is the car's only preset, it is the preset currently
    /// selected in-game (deleting it would leave the selection dangling), or its nodes have moved since
    /// they were parsed.
    /// </exception>
    public void DeletePreset(string carName, string presetName)
    {
        if (_chunk is null)
            throw new InvalidOperationException("This save has no cars container to edit.");
        var car = Find(carName) ?? throw new InvalidOperationException($"No car named '{carName}' in this save.");
        var preset = car.Find(presetName)
            ?? throw new InvalidOperationException($"Car '{car.Name}' has no preset named '{presetName}'.");

        if (car.Presets.Count <= 1)
            throw new InvalidOperationException($"'{car.Name}' has only one preset; a car must keep at least one.");
        if (string.Equals(car.SelectedPreset, preset.Name, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"'{preset.Name}' is the preset currently equipped on '{car.Name}'. Select a different preset " +
                "in-game first, then delete this one.");

        int nameLen = Encoding.Latin1.GetByteCount(preset.Name);
        int blockStart = preset.NameOffset;
        int stvcStart = preset.ValuesOffset - PresetMarkerSize;
        int blockEnd = preset.ValuesOffset + 12 + preset.Tuning.Count * RecordSize;
        if (blockStart < 0 || blockEnd > _payload.Length
            || ReadU32(_payload, blockStart) != (uint)nameLen
            || !TagAt(_payload, stvcStart, PresetMarkerTag)
            || !TagAt(_payload, preset.ValuesOffset, ValuesTag))
            throw new InvalidOperationException(
                $"Stale preset '{preset.Name}': its nodes are no longer where they were parsed.");

        int list = FindPresetList(_payload, car.Offset, _payload.Length);
        if (list < 0)
            throw new InvalidOperationException($"Could not locate the preset list for '{car.Name}'.");
        uint count = ReadU32(_payload, list + 8);

        var updated = new byte[_payload.Length - (blockEnd - blockStart)];
        _payload.AsSpan(0, blockStart).CopyTo(updated);
        _payload.AsSpan(blockEnd).CopyTo(updated.AsSpan(blockStart));
        BinaryPrimitives.WriteUInt32LittleEndian(updated.AsSpan(list + 8, 4), count - 1);

        _payload = updated;
        _chunk.SetDecodedPayload(_payload);
        _cars = ParseCars(_payload);
    }

    /// <summary>
    /// Rename <paramref name="presetName"/> of <paramref name="carName"/> to <paramref name="newName"/>.
    /// Rewrites the preset's name string in place; if the preset is the one selected in-game, the car's
    /// trailing "selected" text slot is rewritten too so the selection keeps pointing at it. Both edits
    /// resize the payload; the verified variable-size write path fixes every length and CRC on save.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The car or preset does not exist, <paramref name="newName"/> is blank / too long / already used by
    /// another preset of the car, or the name slots have moved since they were parsed.
    /// </exception>
    public void RenamePreset(string carName, string presetName, string newName)
    {
        if (_chunk is null)
            throw new InvalidOperationException("This save has no cars container to edit.");
        var car = Find(carName) ?? throw new InvalidOperationException($"No car named '{carName}' in this save.");
        var preset = car.Find(presetName)
            ?? throw new InvalidOperationException($"Car '{car.Name}' has no preset named '{presetName}'.");

        newName = (newName ?? "").Trim();
        if (newName.Length == 0)
            throw new InvalidOperationException("The new preset name cannot be empty.");
        if (newName.Length > MaxStringLength)
            throw new InvalidOperationException("The new preset name is too long.");
        if (string.Equals(newName, preset.Name, StringComparison.Ordinal))
            return;   // no change
        if (car.Presets.Any(p => !ReferenceEquals(p, preset) && string.Equals(p.Name, newName, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Car '{car.Name}' already has a preset named '{newName}'.");

        int oldLen = Encoding.Latin1.GetByteCount(preset.Name);
        int nameStart = preset.NameOffset;
        if (nameStart < 0 || nameStart + 4 + oldLen > _payload.Length
            || ReadU32(_payload, nameStart) != (uint)oldLen)
            throw new InvalidOperationException(
                $"Stale preset '{preset.Name}': its name is no longer where it was parsed.");

        byte[] field = MakeStringField(newName);
        byte[] work = _payload;

        // If this preset is selected, its name is also stored in the car's trailing "selected" slot.
        // Rewrite that slot first (it sits after the name), so the lower name offset stays valid.
        if (string.Equals(car.SelectedPreset, preset.Name, StringComparison.Ordinal))
        {
            int selStart = LocateSelectedSlot(car);
            int selOld = Encoding.Latin1.GetByteCount(car.SelectedPreset);
            if (selStart < 0 || selStart + 4 + selOld > work.Length || ReadU32(work, selStart) != (uint)selOld)
                throw new InvalidOperationException($"Stale selection slot for '{car.Name}'.");
            work = ReplaceRange(work, selStart, 4 + selOld, field);
        }
        work = ReplaceRange(work, nameStart, 4 + oldLen, field);

        _payload = work;
        _chunk.SetDecodedPayload(_payload);
        _cars = ParseCars(_payload);
    }

    /// <summary>A length-prefixed Latin-1 text field: <c>[u32 len][bytes]</c>.</summary>
    private static byte[] MakeStringField(string s)
    {
        byte[] b = Encoding.Latin1.GetBytes(s);
        var field = new byte[4 + b.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(field.AsSpan(0, 4), (uint)b.Length);
        b.CopyTo(field.AsSpan(4));
        return field;
    }

    /// <summary>Return a copy of <paramref name="src"/> with <paramref name="oldLen"/> bytes at
    /// <paramref name="start"/> replaced by <paramref name="repl"/>.</summary>
    private static byte[] ReplaceRange(byte[] src, int start, int oldLen, byte[] repl)
    {
        var dst = new byte[src.Length - oldLen + repl.Length];
        src.AsSpan(0, start).CopyTo(dst);
        repl.CopyTo(dst, start);
        src.AsSpan(start + oldLen).CopyTo(dst.AsSpan(start + repl.Length));
        return dst;
    }

    /// <summary>
    /// Offset of the length prefix of the car's trailing "selected preset" text slot — after the last
    /// preset block, the reserved word, and the sourceId string (matching <see cref="ParseCar"/>).
    /// </summary>
    private int LocateSelectedSlot(CarRecord car)
    {
        int afterLast = car.Presets.Max(p => p.ValuesOffset + 12 + p.Tuning.Count * RecordSize);
        int q = afterLast + 4;   // reserved word
        if (q + 4 > _payload.Length) return -1;
        uint idLen = ReadU32(_payload, q);
        int sel = q + 4 + (int)idLen;
        return sel + 4 <= _payload.Length ? sel : -1;
    }

    /// <summary>
    /// Validate a preset-insert request (car exists, name is non-blank/unique, cap not hit) and locate
    /// the car's <c>pstv</c> node. Returns the car, the node offset, and the trimmed name.
    /// </summary>
    private (CarRecord car, int list, string name) BeginPresetInsert(string carName, string newName)
    {
        if (_chunk is null)
            throw new InvalidOperationException("This save has no cars container to edit.");
        var car = Find(carName) ?? throw new InvalidOperationException($"No car named '{carName}' in this save.");

        newName = (newName ?? "").Trim();
        if (newName.Length == 0)
            throw new InvalidOperationException("The new preset name cannot be empty.");
        if (newName.Length > MaxStringLength)
            throw new InvalidOperationException("The new preset name is too long.");
        if (car.Find(newName) is not null)
            throw new InvalidOperationException($"Car '{car.Name}' already has a preset named '{newName}'.");
        if (car.Presets.Count >= MaxPresets)
            throw new InvalidOperationException($"Car '{car.Name}' already has the maximum {MaxPresets} presets.");

        int list = FindPresetList(_payload, car.Offset, _payload.Length);
        if (list < 0)
            throw new InvalidOperationException($"Could not locate the preset list for '{car.Name}'.");
        return (car, list, newName);
    }

    /// <summary>
    /// Insert a new preset block (<paramref name="tail"/> = its stvc + atvc nodes, name prepended)
    /// after the car's last preset and bump the <c>pstv</c> count. Grows the payload; the verified
    /// variable-size write path recomputes every length and CRC on save.
    /// </summary>
    private void InsertPreset(CarRecord car, int list, string newName, ReadOnlySpan<byte> tail)
    {
        uint count = ReadU32(_payload, list + 8);
        int insertAt = car.Presets.Max(p => p.ValuesOffset + 12 + p.Tuning.Count * RecordSize);

        byte[] nameBytes = Encoding.Latin1.GetBytes(newName);
        var block = new byte[4 + nameBytes.Length + tail.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(0, 4), (uint)nameBytes.Length);
        nameBytes.CopyTo(block.AsSpan(4));
        tail.CopyTo(block.AsSpan(4 + nameBytes.Length));

        var updated = new byte[_payload.Length + block.Length];
        _payload.AsSpan(0, insertAt).CopyTo(updated);
        block.CopyTo(updated, insertAt);
        _payload.AsSpan(insertAt).CopyTo(updated.AsSpan(insertAt + block.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(updated.AsSpan(list + 8, 4), count + 1);

        _payload = updated;
        _chunk!.SetDecodedPayload(_payload);   // non-null: BeginPresetInsert guarded it
        _cars = ParseCars(_payload);
    }

    // ---------------------------------------------------------------- parsing

    /// <summary>Literal <c>data/vehicle/....upgr</c> paths inside one car's byte range.</summary>
    private static List<string> ParseParts(byte[] d, int start, int end)
    {
        var prefix = "data/vehicle/"u8;
        var suffix = ".upgr"u8;
        var found = new List<string>();
        for (int i = start; i + prefix.Length < end; i++)
        {
            if (!d.AsSpan(i).StartsWith(prefix)) continue;
            int stop = -1;
            for (int j = i; j + suffix.Length <= end && j - i < 200; j++)
            {
                if (d[j] < 0x20 || d[j] > 0x7E) break;                 // path ended
                if (d.AsSpan(j).StartsWith(suffix)) { stop = j + suffix.Length; break; }
            }
            if (stop < 0) continue;
            found.Add(Encoding.ASCII.GetString(d, i, stop - i));
            i = stop - 1;
        }
        return found;
    }

    private static List<CarRecord> ParseCars(byte[] d)
    {
        var starts = new List<int>();
        for (int i = 0; i + 12 <= d.Length; i++)
        {
            if (!TagAt(d, i, CarTag)) continue;
            if (ReadU32(d, i + 4) != 2 || ReadU32(d, i + 8) != 1) continue;
            if (!TryReadString(d, i + 12, d.Length, out string key, out _)) continue;
            if (!key.StartsWith(VehicleKeyPrefix, StringComparison.Ordinal)) continue;
            starts.Add(i);
        }

        var cars = new List<CarRecord>(starts.Count);
        for (int k = 0; k < starts.Count; k++)
        {
            int end = k + 1 < starts.Count ? starts[k + 1] : d.Length;
            cars.Add(ParseCar(d, starts[k], end));
        }
        return cars;
    }

    private static CarRecord ParseCar(byte[] d, int start, int end)
    {
        int p = start + 12;
        TryReadString(d, p, end, out string vehicleKey, out p);
        TryReadString(d, p, end, out string name, out p);
        TryReadString(d, p, end, out string config, out p);

        var presets = new List<TuningPreset>();
        string sourceId = string.Empty, selected = string.Empty;
        bool complete = false;

        int list = FindPresetList(d, p, end);
        if (list >= 0)
        {
            int count = (int)ReadU32(d, list + 8);
            int q = list + 12;
            complete = true;
            for (int i = 0; i < count; i++)
            {
                if (!TryReadPreset(d, q, end, out TuningPreset? preset, out q))
                {
                    complete = false;
                    break;
                }
                presets.Add(preset);
            }

            // Trailer: a reserved word, then two length-prefixed text slots (the tagged union —
            // length 0 means "no text here").
            if (complete)
            {
                if (q + 4 <= end
                    && TryReadString(d, q + 4, end, out string id, out int r)
                    && TryReadString(d, r, end, out string sel, out _))
                {
                    sourceId = id;
                    selected = sel;
                }
                else
                {
                    complete = false;
                }
            }
        }

        return new CarRecord(start, vehicleKey, name, config, presets, sourceId, selected, complete,
                             ParseParts(d, start, end));
    }

    /// <summary>Locate the car's <c>pstv</c> node: kind 0 and a plausible preset count.</summary>
    private static int FindPresetList(byte[] d, int from, int end)
    {
        for (int i = from; i + 12 <= end; i++)
        {
            if (!TagAt(d, i, PresetListTag)) continue;
            if (ReadU32(d, i + 4) != 0) continue;
            uint count = ReadU32(d, i + 8);
            if (count is 0 or > MaxPresets) continue;
            return i;
        }
        return -1;
    }

    private static bool TryReadPreset(byte[] d, int p, int end, out TuningPreset preset, out int next)
    {
        preset = null!;
        next = p;

        int nameOffset = p;
        if (!TryReadString(d, p, end, out string name, out int q)) return false;
        if (!TagAt(d, q, PresetMarkerTag) || q + PresetMarkerSize > end) return false;
        q += PresetMarkerSize;

        int valuesOffset = q;
        if (!TagAt(d, q, ValuesTag) || q + 12 > end) return false;
        uint kind = ReadU32(d, q + 4);
        uint count = ReadU32(d, q + 8);
        q += 12;

        // kind 2 — the only form seen — is a flat list of 12-byte numeric records.
        if (kind != 2 || count > MaxRecords || q + (long)count * RecordSize > end) return false;

        var records = new List<TuningRecord>((int)count);
        for (int i = 0; i < count; i++, q += RecordSize)
            records.Add(new TuningRecord(q, ReadU32(d, q), ReadU32(d, q + 4),
                                         BinaryPrimitives.ReadSingleLittleEndian(d.AsSpan(q + 8, 4))));

        preset = new TuningPreset(name, nameOffset, valuesOffset, kind, records);
        next = q;
        return true;
    }

    // ---------------------------------------------------------------- primitives

    private static bool TagAt(byte[] d, int p, string tag)
    {
        if (p < 0 || p + 4 > d.Length) return false;
        for (int i = 0; i < 4; i++)
            if (d[p + i] != (byte)tag[i]) return false;
        return true;
    }

    private static uint ReadU32(byte[] d, int p) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(p, 4));

    /// <summary>Read a <c>[u32 length][bytes]</c> string. A zero length yields an empty string.</summary>
    private static bool TryReadString(byte[] d, int p, int end, out string value, out int next)
    {
        value = string.Empty;
        next = p;
        if (p < 0 || p + 4 > end) return false;
        uint length = ReadU32(d, p);
        if (length > MaxStringLength || p + 4 + length > end) return false;
        value = Encoding.Latin1.GetString(d, p + 4, (int)length);
        next = p + 4 + (int)length;
        return true;
    }
}
