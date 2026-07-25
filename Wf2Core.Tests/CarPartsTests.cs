using Wf2Core;
using Xunit;

namespace Wf2Core.Tests;

public class CarPartsTests
{
    /// <summary>
    /// Parts are read from the decompressed cars payload, where paths are literal. The reader this
    /// replaced scanned the outer tree — mostly the still-compressed cars container — and so only
    /// ever recovered mangled fragments. Every path here must be intact and well-formed.
    /// </summary>
    [Fact]
    public void Parts_AreCompleteAssetPaths()
    {
        var save = SaveFile.Parse(Fixtures.Bytes("BACKUP_20260722_012434.sgfi"));

        var all = save.Cars.SelectMany(c => c.Parts).ToList();
        Assert.NotEmpty(all);
        Assert.All(all, p =>
        {
            Assert.StartsWith("data/vehicle/", p);
            Assert.EndsWith(".upgr", p);
            Assert.DoesNotContain(" ", p);
            Assert.All(p, ch => Assert.InRange(ch, ' ', '~'));
        });
    }

    /// <summary>
    /// Cars stored past the first LZ4 block must get their parts too — the same failure mode that
    /// once hid Jackal entirely.
    /// </summary>
    [Fact]
    public void Parts_AreReadForCarsInContinuationBlocks()
    {
        var save = SaveFile.Parse(Fixtures.Bytes("BACKUP_20260722_012434.sgfi"));

        var jackal = save.Cars.FirstOrDefault(c => c.Name == "Jackal");
        Assert.NotNull(jackal);
        Assert.NotEmpty(jackal!.Parts);
    }
}
