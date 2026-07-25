using Wf2Core;

// Headless harness for reverse-engineering and verification.
//   info <file.sgfi>            container + per-chunk summary (lengths, block count, CRC status)
//   cars <file.sgfi>            list cars -> presets -> stored tuning records
//   parts <file.sgfi>           list the parts fitted to each car
//   preset export|export-all|import  ...   tuning preset export / import (see Preset())
//   settune <in.sgfi> <out.sgfi> <car> <preset> <paramIndex> <aux> <value>   edit one tuning value
//   roundtrip <file.sgfi>       parse -> serialize -> confirm byte-identical
//   hexdiff <a.sgfi> <b.sgfi>   list every byte offset that differs (for in-game diff sessions)
//   decompress <file.sgfi> <out.bin> [chunk]   decode every LZ4 block of a chunk to a plain node tree
//   catalog <vehicleDir> [car]  list installable parts from game data (data\vehicle)
//   tuning <tuningDir>          decode every .ctms — the legal min/max per tunable parameter
//   guides <vehicleDir> <outDir>  export car names/descriptions + part catalog to JSON
//   scrub <in.sgfi> <out.sgfi>  anonymize a save for sharing (blank online/ghost source ids)
//   schema [out.json]  emit the editable-parameter schema (the source of truth for the TS export lib)

const string Usage = "usage: wf2 <info|cars|parts|preset|settune|roundtrip|hexdiff|decompress|catalog|tuning|guides|scrub|schema> <args>";

if (args.Length == 0)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "info" when args.Length == 2:
            return Info(args[1]);
        case "cars" when args.Length == 2:
            return Cars(args[1]);
        case "parts" when args.Length == 2:
            return Parts(args[1]);
        case "preset" when args.Length >= 2:
            return Preset(args[1..]);
        case "settune" when args.Length == 8:
            return SetTune(args[1], args[2], args[3], args[4],
                           uint.Parse(args[5]), uint.Parse(args[6]), ParseFloat(args[7]));
        case "roundtrip" when args.Length == 2:
            return RoundTrip(args[1]);
        case "hexdiff" when args.Length == 3:
            return HexDiff(args[1], args[2]);
        case "decompress" when args.Length is 3 or 4:
            return Decompress(args[1], args[2], args.Length == 4 ? args[3] : "srcc");
        case "catalog" when args.Length is 2 or 3:
            return Catalog(args[1], args.Length == 3 ? args[2] : null);
        case "tuning" when args.Length == 2:
            return Tuning(args[1]);
        case "calibrate" when args.Length >= 2:
            return Calibrate(args[1..]);
        case "bbag" when args.Length is 2 or 3:
            return Bbag(args[1], args.Length == 3 ? args[2] : null);
        case "scrub" when args.Length == 3:
            return Scrub(args[1], args[2]);
        case "schema" when args.Length is 1 or 2:
            return Schema(args.Length == 2 ? args[1] : null);
        case "guides" when args.Length == 3:
            return Guides(args[1], args[2]);
        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

// info <file.sgfi>  — outer container, then one line per chunk
static int Info(string path)
{
    var save = SaveFile.Load(path);
    Console.WriteLine($"file        {path}");
    Console.WriteLine($"size        {new FileInfo(path).Length} bytes");
    Console.WriteLine($"outer       root {save.RootValue}, tag '{save.Tag}', " +
                      $"crc 0x{save.StoredCrc:X8} {(save.StoredCrcValid ? "ok" : "BAD")}");
    Console.WriteLine($"tree        {save.Chunks.Count} chunk(s)");
    Console.WriteLine();
    Console.WriteLine($"  {"chunk",-6} {"block1",8} {"decoded",8} {"blocks",6}  crc");
    foreach (var c in save.Chunks)
        Console.WriteLine($"  {c.Tag,-6} {c.Container.Content.Length,8} {c.DecodedPayload.Length,8} " +
                          $"{c.BlockCount,6}  " +
                          $"chunk {(c.StoredCrcValid ? "ok" : "BAD")}, inner {(c.Container.StoredCrcValid ? "ok" : "BAD")}");
    return 0;
}

// cars <file.sgfi>  — decode the save and list cars -> presets -> stored tuning records
static int Cars(string path)
{
    var save = SaveFile.Load(path);
    Console.WriteLine($"{path}");
    Console.WriteLine($"  outer CRC {(save.StoredCrcValid ? "ok" : "BAD")}   chunks: " +
        string.Join(", ", save.Chunks.Select(c =>
            $"{c.Tag}({c.Container.Content.Length}B, crc {(c.StoredCrcValid && c.Container.StoredCrcValid ? "ok" : "BAD")})")));
    Console.WriteLine($"  {save.Cars.Count} cars\n");

    foreach (var car in save.Cars)
    {
        Console.WriteLine($"[{car.Name}]  {car.Config}   selected: '{car.SelectedPreset}'" +
                          (car.PresetsComplete ? "" : "   (preset list truncated)"));
        foreach (var preset in car.Presets)
        {
            Console.WriteLine($"  {preset.Name,-24} {preset.Tuning.Count,3} value(s)");
            foreach (var t in preset.Tuning)
                Console.WriteLine($"      idx {t.ParamIndex,3}  aux {t.Aux,10}  {t.Value}");
        }
    }
    return 0;
}

// parts <file.sgfi>  — the parts fitted to each car, read from the decompressed cars payload
static int Parts(string path)
{
    var save = SaveFile.Load(path);
    foreach (var car in save.Cars)
    {
        Console.WriteLine($"[{car.Name}]  {car.Config}   {car.Parts.Count} part(s)");
        foreach (var part in car.Parts)
            Console.WriteLine($"    {part}");
    }
    return 0;
}

// preset export     <save> <car> <preset> <out.json>
// preset export-all <save> <outDir>
// preset import     <save> <out.sgfi> <car> <preset> <in.json> [--dry-run] [--allow-grow]
// preset duplicate  <save> <out.sgfi> <car> <preset> <newName>
static int Preset(string[] a)
{
    switch (a[0].ToLowerInvariant())
    {
        case "export" when a.Length == 5:
            return PresetExportOne(a[1], a[2], a[3], a[4]);
        case "export-all" when a.Length == 3:
            return PresetExportAll(a[1], a[2]);
        case "import" when a.Length >= 6:
        {
            var flags = a[6..];
            bool dryRun = flags.Any(f => f.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
            bool allowGrow = flags.Any(f => f.Equals("--allow-grow", StringComparison.OrdinalIgnoreCase));
            var unknown = flags.FirstOrDefault(f =>
                !f.Equals("--dry-run", StringComparison.OrdinalIgnoreCase) &&
                !f.Equals("--allow-grow", StringComparison.OrdinalIgnoreCase));
            if (unknown is not null) { Console.Error.WriteLine($"unknown flag: {unknown}"); return 2; }
            return PresetImport(a[1], a[2], a[3], a[4], a[5], dryRun, allowGrow);
        }
        case "duplicate" when a.Length == 6:
            return PresetDuplicate(a[1], a[2], a[3], a[4], a[5]);
        case "create" when a.Length == 5:
            return PresetCreate(a[1], a[2], a[3], a[4]);
        case "validate" when a.Length is 2 or 4:
            return PresetValidate(a[1], a.Length == 4 ? a[2] : null, a.Length == 4 ? a[3] : null);
        default:
            Console.Error.WriteLine("usage: wf2 preset export <save> <car> <preset> <out.json>");
            Console.Error.WriteLine("       wf2 preset export-all <save> <outDir>");
            Console.Error.WriteLine("       wf2 preset import <save> <out.sgfi> <car> <preset> <in.json> [--dry-run] [--allow-grow]");
            Console.Error.WriteLine("       wf2 preset duplicate <save> <out.sgfi> <car> <preset> <newName>");
            Console.Error.WriteLine("       wf2 preset create <save> <out.sgfi> <car> <newName>");
            Console.Error.WriteLine("       wf2 preset validate <in.json> [<save.sgfi> <car>]");
            return 2;
    }
}

// preset validate <in.json> [<save.sgfi> <car>]  — parse a preset the same way the app's import does
// (FromJson: version, finite values, no duplicate params) and, when a save + car are given, print the
// plan and any warnings PlanNewPreset would raise. The cross-language check for the TS export library.
static int PresetValidate(string jsonPath, string? savePath, string? carName)
{
    PresetExport import;
    try
    {
        import = PresetIo.FromJson(File.ReadAllText(jsonPath));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"REJECTED: {ex.Message}");
        return 1;
    }

    Console.WriteLine($"OK: format v{import.FormatVersion}, {import.Tuning.Count} value(s), " +
                      $"source '{import.Source.Car}' / '{import.Source.Preset}'.");

    if (savePath is not null && carName is not null)
    {
        var save = SaveFile.Load(savePath);
        var plan = PresetIo.PlanNewPreset(save, carName, import);
        Console.WriteLine($"plan onto '{carName}': {plan.Added.Count} value(s) would be added.");
        foreach (var w in plan.Warnings) Console.WriteLine($"  warn: {w}");
        foreach (var r in plan.RangeWarnings)
            Console.WriteLine($"  range: {r.Name} = {r.Value} outside {r.Min}..{r.Max} " +
                              $"({(r.IsExact ? "exact limit" : "observed")})");
    }
    return 0;
}

static int PresetCreate(string inPath, string outPath, string carName, string newName)
{
    var save = SaveFile.Load(inPath);
    save.Cars.CreatePreset(carName, newName);
    var bytes = save.Serialize();
    File.WriteAllBytes(outPath, bytes);

    Console.WriteLine($"{carName}: new empty preset '{newName}' (all sliders at default)");
    Console.WriteLine($"wrote {outPath}  {bytes.Length} bytes (was {new FileInfo(inPath).Length})");
    return 0;
}

static int PresetDuplicate(string inPath, string outPath, string carName, string presetName, string newName)
{
    var save = SaveFile.Load(inPath);
    var source = save.Cars.Find(carName)?.Find(presetName);
    save.Cars.DuplicatePreset(carName, presetName, newName);
    var bytes = save.Serialize();
    File.WriteAllBytes(outPath, bytes);

    Console.WriteLine($"{carName} / {presetName} ({source?.Tuning.Count ?? 0} value(s))  →  new preset '{newName}'");
    Console.WriteLine($"wrote {outPath}  {bytes.Length} bytes (was {new FileInfo(inPath).Length})");
    return 0;
}

static int PresetExportOne(string savePath, string carName, string presetName, string outPath)
{
    var save = SaveFile.Load(savePath);
    var car = save.Cars.Find(carName)
        ?? throw new InvalidOperationException($"No car named '{carName}' in this save.");
    var preset = car.Find(presetName)
        ?? throw new InvalidOperationException(
            $"Car '{car.Name}' has no preset '{presetName}'. Has: {string.Join(", ", car.Presets.Select(p => $"'{p.Name}'"))}");

    var json = PresetIo.ToJson(PresetIo.Export(car, preset, DateTimeOffset.UtcNow));
    File.WriteAllText(outPath, json);
    Console.WriteLine($"{car.Name} / {preset.Name}: {preset.Tuning.Count} value(s) -> {outPath}");
    if (preset.Tuning.Count == 0)
        Console.WriteLine("NOTE: this preset stores nothing — every slider is at its default.");
    return 0;
}

static int PresetExportAll(string savePath, string outDir)
{
    var save = SaveFile.Load(savePath);
    Directory.CreateDirectory(outDir);
    var stamp = DateTimeOffset.UtcNow;
    int written = 0, empty = 0;

    foreach (var car in save.Cars)
        foreach (var preset in car.Presets)
        {
            var name = Sanitize($"{car.Name}__{preset.Name}") + ".json";
            File.WriteAllText(Path.Combine(outDir, name),
                              PresetIo.ToJson(PresetIo.Export(car, preset, stamp)));
            written++;
            if (preset.Tuning.Count == 0) empty++;
            Console.WriteLine($"  {car.Name,-16} {preset.Name,-40} {preset.Tuning.Count,3} value(s) -> {name}");
        }

    Console.WriteLine($"\nexported {written} preset(s) from {save.Cars.Count} car(s) -> {outDir}");
    if (empty > 0) Console.WriteLine($"({empty} of them store nothing — all sliders at default.)");
    return 0;
}

static string Sanitize(string s)
{
    var invalid = Path.GetInvalidFileNameChars();
    var chars = s.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray();
    return new string(chars);
}

static int PresetImport(string inPath, string outPath, string carName, string presetName,
                        string jsonPath, bool dryRun, bool allowGrow)
{
    var save = SaveFile.Load(inPath);
    var import = PresetIo.FromJson(File.ReadAllText(jsonPath));
    var plan = PresetIo.Plan(save, carName, presetName, import,
                             new PresetIo.ImportOptions(AllowAdd: allowGrow));

    Console.WriteLine($"{import.Source.Car} / {import.Source.Preset}  ->  {plan.Car} / {plan.Preset}" +
                      (allowGrow ? "   [grow]" : ""));
    Console.WriteLine();

    foreach (var w in plan.Warnings)
        Console.WriteLine($"  warning: {w}\n");

    if (plan.Applied.Count > 0)
    {
        Console.WriteLine($"  {plan.Applied.Count} value(s) to overwrite:");
        foreach (var c in plan.Applied)
            Console.WriteLine($"    idx {c.ParamIndex,3}  {c.Name,-28} {c.FromValue,12} -> {c.ToValue,-12}" +
                              (c.IsNoOp ? "  (unchanged)" : ""));
    }
    if (plan.Added.Count > 0)
    {
        Console.WriteLine($"\n  {plan.Added.Count} value(s) to add (grows the preset):");
        foreach (var c in plan.Added)
            Console.WriteLine($"    idx {c.ParamIndex,3}  {c.Name,-28} {"(default)",12} -> {c.Value,-12}");
    }
    if (plan.Skipped.Count > 0)
    {
        Console.WriteLine($"\n  {plan.Skipped.Count} value(s) skipped:");
        foreach (var s in plan.Skipped)
            Console.WriteLine($"    idx {s.ParamIndex,3}  {s.Name,-28} {s.Reason}");
    }
    if (plan.RangeWarnings.Count > 0)
    {
        Console.WriteLine($"\n  {plan.RangeWarnings.Count} value(s) out of range:");
        foreach (var r in plan.RangeWarnings)
        {
            string kind = r.IsExact ? "exceeds the game limit" : "outside observed";
            Console.WriteLine($"    idx {r.ParamIndex,3}  {r.Name,-28} {r.Value:0.####} {kind} {r.Min:0.####}..{r.Max:0.####}");
        }
    }

    if (dryRun)
    {
        Console.WriteLine("\n--dry-run: nothing written.");
        return 0;
    }
    if (plan.IsEmpty)
    {
        Console.WriteLine("\nNothing to change; no file written.");
        return 0;
    }

    PresetIo.Apply(save, plan);
    var bytes = save.Serialize();
    File.WriteAllBytes(outPath, bytes);
    Console.WriteLine($"\nwrote {outPath}  {bytes.Length} bytes (was {new FileInfo(inPath).Length})");
    return 0;
}

// settune <in> <out> <car> <preset> <paramIndex> <aux> <value>  — overwrite one stored tuning value
static int SetTune(string inPath, string outPath, string car, string preset,
                   uint paramIndex, uint aux, float value)
{
    var save = SaveFile.Load(inPath);
    var before = save.Cars.Find(car)?.Find(preset)?.Find(paramIndex);
    save.Cars.SetTuningValue(car, preset, paramIndex, aux, value);
    var bytes = save.Serialize();
    File.WriteAllBytes(outPath, bytes);

    Console.WriteLine($"{car} / {preset} / idx {paramIndex}: " +
                      $"(aux {before?.Aux}, {before?.Value}) -> (aux {aux}, {value})");
    Console.WriteLine($"wrote {outPath}  {bytes.Length} bytes (was {new FileInfo(inPath).Length})");
    if (bytes.Length != new FileInfo(inPath).Length)
        Console.WriteLine("NOTE: file size changed (chunk lengths shifted). This path is verified in-game, but always keep a backup.");
    return 0;
}

static int RoundTrip(string path)
{
    var original = File.ReadAllBytes(path);
    var reserialized = SaveFile.Load(path).Serialize();
    if (original.AsSpan().SequenceEqual(reserialized))
    {
        Console.WriteLine($"OK  byte-identical round-trip ({original.Length} bytes)");
        return 0;
    }
    var min = Math.Min(original.Length, reserialized.Length);
    for (var i = 0; i < min; i++)
        if (original[i] != reserialized[i])
        {
            Console.WriteLine($"DIFF at 0x{i:X4}: {original[i]:X2} -> {reserialized[i]:X2}");
            return 1;
        }
    Console.WriteLine($"DIFF length: {original.Length} vs {reserialized.Length}");
    return 1;
}

// decompress <file.sgfi> <out.bin> [chunk]  — every LZ4 block of the chunk, not just the first
static int Decompress(string path, string outPath, string tag)
{
    var save = SaveFile.Load(path);
    var chunk = save.Chunks.FirstOrDefault(c => string.Equals(c.Tag, tag, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(
            $"no chunk '{tag}' (have: {string.Join(", ", save.Chunks.Select(c => c.Tag))})");
    var payload = chunk.DecodedPayload;
    File.WriteAllBytes(outPath, payload);
    Console.WriteLine($"{tag}: {chunk.BlockCount} LZ4 block(s) " +
                      $"-> {payload.Length} bytes -> {outPath}");
    return 0;
}

static int HexDiff(string aPath, string bPath)
{
    var a = File.ReadAllBytes(aPath);
    var b = File.ReadAllBytes(bPath);
    var min = Math.Min(a.Length, b.Length);
    var diffs = 0;
    for (var i = 0; i < min; i++)
        if (a[i] != b[i])
        {
            Console.WriteLine($"0x{i:X4}: {a[i]:X2} -> {b[i]:X2}");
            diffs++;
        }
    if (a.Length != b.Length)
        Console.WriteLine($"(length differs: {a.Length} vs {b.Length})");
    Console.WriteLine($"{diffs} differing byte(s) in the overlapping region.");
    return 0;
}

static int Catalog(string vehicleDir, string? car)
{
    var cat = PartCatalog.Load(vehicleDir);
    Console.WriteLine($"{cat.Parts.Count} parts across {cat.Cars.Count} cars: {string.Join(", ", cat.Cars)}\n");
    var cars = car is null ? cat.Cars : [car];
    foreach (var c in cars)
    {
        var cats = cat.Parts.Where(p => p.Car == c).Select(p => p.Category).Distinct().OrderBy(x => x);
        Console.WriteLine($"[{c}]");
        foreach (var category in cats)
        {
            var variants = cat.Variants(c, category).Select(p => p.Variant);
            Console.WriteLine($"  {category,-32} {string.Join(", ", variants)}");
        }
    }
    return 0;
}

// bbag <file> [out.bin]  — decode ANY bbag container (.vecs/.vesg/.vbpr/.upgr/.ctms/…) and dump its
//   decompressed payload. Every one of the game's vehicle property files uses the same container as
//   the save, so this is the general-purpose way to look inside them.
static int Bbag(string path, string? outPath)
{
    var bytes = File.ReadAllBytes(path);
    var c = BbagContainer.Parse(bytes);
    var content = c.Content;

    Console.WriteLine($"file        {path}");
    Console.WriteLine($"tag         '{c.Tag}'   (reversed: {new string(c.Tag.Reverse().ToArray())})");
    Console.WriteLine($"size        {bytes.Length} bytes → {content.Length} decompressed");
    Console.WriteLine($"crc         {(c.StoredCrcValid ? "ok" : "BAD")}");

    if (outPath is not null)
    {
        File.WriteAllBytes(outPath, content);
        Console.WriteLine($"wrote       {outPath}");
        return 0;
    }

    // Inline view: 4CC-ish tags, printable strings, and plausible floats.
    Console.WriteLine("\nstrings:");
    foreach (var s in PrintableRuns(content, 4).Take(30)) Console.WriteLine($"  {s}");

    Console.WriteLine("\nfloats (finite, |v| in 1e-3..1e7):");
    for (int i = 0; i + 4 <= content.Length; i += 4)
    {
        float f = BitConverter.ToSingle(content, i);
        if (float.IsFinite(f) && f != 0 && Math.Abs(f) >= 1e-3 && Math.Abs(f) <= 1e7)
            Console.WriteLine($"  @{i,5}  {f:0.######}");
    }
    return 0;
}

// Anonymize a save for sharing: blank out the online/ghost source ids the game stamps onto cars and
// leaderboard-seeded presets (e.g. "local-0000…", a Steam-derived handle). The replacement is the
// SAME length as what it overwrites, so every node offset, count and tuning value is untouched.
static int Scrub(string inPath, string outPath)
{
    var save = SaveFile.Load(inPath);
    int hits = 0;

    foreach (var chunk in save.Chunks)
    {
        var payload = chunk.DecodedPayload;
        // Latin-1 is a lossless 1:1 byte<->char map, so same-length string edits round-trip exactly.
        var text = System.Text.Encoding.Latin1.GetString(payload);

        // Online/ghost source ids: "local-<digits>" -> "local-000...".
        var scrubbed = System.Text.RegularExpressions.Regex.Replace(
            text, @"local-\d+", m => "local-" + new string('0', m.Value.Length - 6));

        if (scrubbed != text)
        {
            hits++;
            chunk.SetDecodedPayload(System.Text.Encoding.Latin1.GetBytes(scrubbed));
        }
    }

    save.Save(outPath);
    Console.WriteLine($"scrubbed {hits} chunk(s) -> {outPath}");
    return 0;
}

// schema [out.json]  — emit the editable-parameter schema as JSON: the single source of truth the
// TypeScript export library generates from. Combines TuningSchema (min/max/steps, the aux→value
// arithmetic) with ParamMap (name + display units) for every editable parameter, plus the current
// preset format version. Prints to stdout, or writes to out.json when a path is given.
static int Schema(string? outPath)
{
    var opts = new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    var parameters = TuningSchema.EditableIndices.Select(i =>
    {
        var s = TuningSchema.For(i)!;              // present by definition of EditableIndices
        var info = ParamMap.Lookup(i);
        return new
        {
            index = i,
            name = info?.Name ?? $"parameter {i}",
            min = s.Min,
            max = s.Max,
            steps = s.Steps,
            storedUnit = info?.StoredUnit ?? "",
            displayUnit = info?.DisplayUnit ?? "",
            displayFactor = info?.DisplayFactor ?? 1.0,
            confirmed = info?.Confirmed ?? false,
        };
    }).ToList();

    var doc = new
    {
        formatVersion = PresetIo.CurrentFormatVersion,
        generatedBy = "wf2 schema",
        note = "value = min + aux * (max - min) / steps, computed in float32. aux is an integer 0..steps.",
        @params = parameters,
    };

    string json = System.Text.Json.JsonSerializer.Serialize(doc, opts);
    if (outPath is not null)
    {
        File.WriteAllText(outPath, json);
        Console.WriteLine($"wrote {parameters.Count} parameters -> {outPath}");
    }
    else
    {
        Console.WriteLine(json);
    }
    return 0;
}

static IEnumerable<string> PrintableRuns(byte[] d, int min)
{
    var sb = new System.Text.StringBuilder();
    foreach (var b in d)
    {
        if (b >= 0x20 && b <= 0x7e) sb.Append((char)b);
        else { if (sb.Length >= min) yield return sb.ToString(); sb.Clear(); }
    }
    if (sb.Length >= min) yield return sb.ToString();
}

// calibrate <save> [more saves...]  — aggregate every stored value per paramIndex across all presets
//   of all the given saves, to see the empirical range (in stored SI units) we already have and which
//   indices are still unnamed. Range validation (M5) needs per-index min/max in *stored* units.
static int Calibrate(string[] paths)
{
    // paramIndex -> (values seen, aux values seen)
    var byIndex = new SortedDictionary<uint, (List<float> vals, HashSet<uint> auxes, int count)>();
    void Add(uint idx, uint aux, float v)
    {
        if (!byIndex.TryGetValue(idx, out var e)) e = (new List<float>(), new HashSet<uint>(), 0);
        e.vals.Add(v); e.auxes.Add(aux); e.count++;
        byIndex[idx] = e;
    }

    // A few records store float.MinValue (~-3.4e38) as "unset / inherit the default" rather than a
    // real tuned value. It must be excluded from min/max or it swamps every range.
    static bool IsSentinel(float v) => !float.IsFinite(v) || v <= -3.0e38f;

    int presetCount = 0, sentinels = 0;
    foreach (var path in paths)
    {
        var save = SaveFile.Load(path);
        foreach (var car in save.Cars)
            foreach (var preset in car.Presets)
            {
                presetCount++;
                foreach (var t in preset.Tuning)
                    if (IsSentinel(t.Value)) sentinels++;
                    else Add(t.ParamIndex, t.Aux, t.Value);
            }
    }

    Console.WriteLine($"aggregated {presetCount} preset(s) from {paths.Length} save(s)  ({sentinels} unset-sentinel records skipped)\n");
    Console.WriteLine($"{"idx",4} {"name",-28} {"n",4} {"min",12} {"max",12}  distinct (first few)");
    Console.WriteLine(new string('-', 108));
    foreach (var (idx, e) in byIndex)
    {
        var finite = e.vals.Where(float.IsFinite).ToList();
        float min = finite.Min(), max = finite.Max();
        var distinct = finite.Distinct().OrderBy(v => v).ToList();
        var shown = string.Join(", ", distinct.Take(8).Select(v => v.ToString("0.####")));
        if (distinct.Count > 8) shown += $", … ({distinct.Count} distinct)";
        var info = ParamMap.Lookup(idx);
        string name = info?.Name ?? "??? UNMAPPED";
        Console.WriteLine($"{idx,4} {name,-28} {e.count,4} {min,12:0.####} {max,12:0.####}  {shown}");
    }

    var unmapped = byIndex.Keys.Where(i => ParamMap.Lookup(i) is null).ToList();
    Console.WriteLine($"\n{byIndex.Count} indices in use; {unmapped.Count} unmapped: {string.Join(", ", unmapped)}");
    return 0;
}

// tuning <tuningDir>  — decode every .ctms tuning definition (the game's tunable-parameter schema)
static int Tuning(string tuningDir)
{
    var files = Directory.EnumerateFiles(tuningDir, "*.ctms", SearchOption.AllDirectories)
                         .OrderBy(f => f, StringComparer.Ordinal).ToList();
    if (files.Count == 0) { Console.Error.WriteLine($"no .ctms files under {tuningDir}"); return 1; }

    Console.WriteLine($"{"tuning definition",-30} {"decl",4} {"found",5} {"crc",4}  parameters (type: min -> max)");
    Console.WriteLine(new string('-', 104));
    int bad = 0;
    foreach (var f in files)
    {
        var c = CtmsFile.Load(f);
        bool ok = c.DeclaredParameterCount == c.Parameters.Count && c.CrcValid;
        if (!ok) bad++;
        // collapse identical consecutive parameter definitions for readability
        var grouped = c.Parameters
            .GroupBy(p => (p.Type, p.Min, p.Max, p.Steps))
            .Select(g => $"{g.Key.Type}: {g.Key.Min:0.####}..{g.Key.Max:0.####} /{g.Key.Steps} (step {g.First().StepSize:0.####})"
                         + (g.Count() > 1 ? $" x{g.Count()}" : ""));
        Console.WriteLine($"{Path.GetFileName(f),-30} {c.DeclaredParameterCount,4} {c.Parameters.Count,5} " +
                          $"{(c.CrcValid ? "ok" : "BAD"),4}  {string.Join("; ", grouped)}");
    }
    Console.WriteLine($"\n{files.Count - bad}/{files.Count} decoded cleanly (declared count == found, CRC verified)");
    return bad == 0 ? 0 : 1;
}

static int Guides(string vehicleDir, string outDir)
{
    var cars = GuideExporter.Build(vehicleDir);
    GuideExporter.Write(vehicleDir, outDir);
    Console.WriteLine($"exported {cars.Count} cars -> {outDir}\\cars.json + parts\\<id>.json\n");
    foreach (var c in cars)
        Console.WriteLine($"  {c.Id,-8} {c.Name,-16} {c.Categories.Count} categories" +
            (c.Description is null ? "" : $"   \"{Truncate(c.Description, 48)}\""));
    return 0;
}

static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

static float ParseFloat(string s) =>
    float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
