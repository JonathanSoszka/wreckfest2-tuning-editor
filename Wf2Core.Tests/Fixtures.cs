namespace Wf2Core.Tests;

/// <summary>Helpers for locating the save-file fixtures copied next to the test assembly.</summary>
public static class Fixtures
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures");

    /// <summary>All <c>*.sgfi</c> fixtures, as xUnit MemberData rows of {fileName}.</summary>
    public static IEnumerable<object[]> AllSgfi()
    {
        foreach (var path in Directory.EnumerateFiles(Dir, "*.sgfi"))
            yield return [Path.GetFileName(path)];
    }

    public static byte[] Bytes(string fileName) => File.ReadAllBytes(Path.Combine(Dir, fileName));
}
