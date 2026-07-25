using System.Text.RegularExpressions;

namespace Wf2Core;

/// <summary>
/// Discovers where the player's save, its Steam Cloud mirror(s), and the game install live. All
/// inputs (the Documents folder, the Steam roots) are injected, so it is testable against a faked
/// filesystem tree; <see cref="ForCurrentMachine"/> wires the real ones. Pure path logic — no format
/// parsing — which is why it can live here yet stay out of the format library's concerns.
///
/// <para>Registry lookup of the Steam path is Windows-only, so it is <b>not</b> done here (this
/// library is cross-platform). A caller that has it — e.g. the WPF app — passes it as an extra Steam
/// root.</para>
/// </summary>
public sealed partial class SaveLocator
{
    /// <summary>The Wreckfest 2 Steam app id.</summary>
    public const string AppId = "1203190";

    /// <summary>Path, relative to the game install, of the tuning <c>.ctms</c> schemas (for M5).</summary>
    private const string TuningRelative = @"data/vehicle/shared/part/tuning";

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly string? _documentsPath;
    private readonly IReadOnlyList<string> _steamRoots;

    /// <param name="documentsPath">The user's Documents folder, or null if unknown.</param>
    /// <param name="steamRoots">Candidate Steam install roots (the ones that hold <c>userdata</c>).</param>
    public SaveLocator(string? documentsPath, IEnumerable<string> steamRoots)
    {
        ArgumentNullException.ThrowIfNull(steamRoots);
        _documentsPath = documentsPath;
        _steamRoots = steamRoots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(Path.GetFullPath)
            .Distinct(PathComparer)
            .ToList();
    }

    /// <summary>
    /// A locator for this machine: Documents from the OS, plus the default Steam locations and any
    /// <paramref name="extraSteamRoots"/> the caller resolved (e.g. from the Windows registry).
    /// </summary>
    public static SaveLocator ForCurrentMachine(IEnumerable<string>? extraSteamRoots = null)
    {
        var docs = SafeFolder(Environment.SpecialFolder.MyDocuments);
        var roots = new List<string>();
        if (extraSteamRoots is not null) roots.AddRange(extraSteamRoots);
        foreach (var pf in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            var f = SafeFolder(pf);
            if (f is not null) roots.Add(Path.Combine(f, "Steam"));
        }
        return new SaveLocator(docs, roots);
    }

    /// <summary>Every live save under <c>Documents\My Games\Wreckfest 2\&lt;profile&gt;\savegame</c>.</summary>
    public IReadOnlyList<string> FindSaves()
    {
        var root = MyGamesRoot();
        if (root is null || !Directory.Exists(root)) return [];
        return Directory.EnumerateDirectories(root)
            .Select(profile => Path.Combine(profile, "savegame", "profile.sgfi"))
            .Where(File.Exists)
            .ToList();
    }

    /// <summary>The single live save, or null when there are zero or several (the caller should ask).</summary>
    public string? FindSingleSave()
    {
        var saves = FindSaves();
        return saves.Count == 1 ? saves[0] : null;
    }

    /// <summary>Every Steam <c>userdata\&lt;account&gt;\1203190\remote\profile.sgfi</c> that exists.</summary>
    public IReadOnlyList<string> FindUserdataMirrors()
    {
        var mirrors = new List<string>();
        foreach (var steam in _steamRoots)
        {
            var userdata = Path.Combine(steam, "userdata");
            if (!Directory.Exists(userdata)) continue;
            foreach (var account in Directory.EnumerateDirectories(userdata))
            {
                var mirror = Path.Combine(account, AppId, "remote", "profile.sgfi");
                if (File.Exists(mirror)) mirrors.Add(mirror);
            }
        }
        return mirrors;
    }

    /// <summary>True when <paramref name="path"/> is a discovered live save, not an arbitrary file.</summary>
    public bool IsLiveSave(string path) => FindSaves().Any(s => SamePath(s, path));

    /// <summary>
    /// Every file a write should touch for <paramref name="savePath"/>. For a live save that is the
    /// save plus every userdata mirror — writing only one lets Steam Cloud re-sync the stale copy back
    /// over the edit. For any other file it is just that file.
    /// </summary>
    public IReadOnlyList<string> WriteTargetsFor(string savePath)
    {
        var targets = new List<string> { savePath };
        if (IsLiveSave(savePath))
            foreach (var mirror in FindUserdataMirrors())
                if (!targets.Any(t => SamePath(t, mirror)))
                    targets.Add(mirror);
        return targets;
    }

    /// <summary>The Wreckfest 2 install directory (searching all Steam libraries), or null.</summary>
    public string? FindGameInstall()
    {
        foreach (var library in AllLibraries())
        {
            var install = Path.Combine(library, "steamapps", "common", "Wreckfest 2");
            if (Directory.Exists(install)) return install;
        }
        return null;
    }

    /// <summary>The installed tuning <c>.ctms</c> directory, or null when the game is not found.</summary>
    public string? FindTuningDir()
    {
        var install = FindGameInstall();
        if (install is null) return null;
        var dir = Path.Combine(install, TuningRelative.Replace('/', Path.DirectorySeparatorChar));
        return Directory.Exists(dir) ? dir : null;
    }

    // ---------------------------------------------------------------- internals

    private string? MyGamesRoot() =>
        _documentsPath is null ? null : Path.Combine(_documentsPath, "My Games", "Wreckfest 2");

    /// <summary>Steam roots plus every library path declared in their <c>libraryfolders.vdf</c>.</summary>
    private IEnumerable<string> AllLibraries()
    {
        var seen = new HashSet<string>(PathComparer);
        foreach (var root in _steamRoots)
            if (seen.Add(root)) yield return root;

        foreach (var root in _steamRoots)
        {
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            string text;
            try { text = File.ReadAllText(vdf); }
            catch (IOException) { continue; }
            foreach (Match m in PathEntry().Matches(text))
            {
                var libPath = m.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(libPath) && seen.Add(Path.GetFullPath(libPath)))
                    yield return libPath;
            }
        }
    }

    private static string? SafeFolder(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder);
        return string.IsNullOrEmpty(path) ? null : path;
    }

    private static bool SamePath(string a, string b)
    {
        try { return PathComparer.Equals(Path.GetFullPath(a), Path.GetFullPath(b)); }
        catch (ArgumentException) { return false; }
    }

    // A libraryfolders.vdf "path" entry: "path"  "D:\\SteamLibrary"
    [GeneratedRegex("\"path\"\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PathEntry();
}
