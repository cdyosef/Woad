using Woad.Core.Art;

namespace Woad.Core.Tests.Art;

/// <summary>Figures that ship locked: a code kept only as a hash, typed any which way, travelling with the figure's file.</summary>
public sealed class FigureLockTests
{
    [Fact]
    public void ACodeIsComparedWithoutCaseOrSpaces()
    {
        Assert.Equal("stonegate", FigureLock.Normalize(" Stone Gate! "));
        Assert.Equal(FigureLock.HashOf("stonegate"), FigureLock.HashOf("STONE-gate"));
        Assert.NotEqual(FigureLock.HashOf("stonegate"), FigureLock.HashOf("stonegat"));
        Assert.Null(FigureLock.For("  !! ", secret: false));
    }

    [Fact]
    public void TheCodeItselfIsNeverKept()
    {
        FigureLock locked = FigureLock.For("stonegate", secret: true)!;
        PaintedLook figure = PaintedLook.Blank("Footman") with { Rows = [.. Enumerable.Repeat("..TTTT..TTTT....", 16)], Lock = locked };

        string json = figure.ToJson();
        PaintedLook read = PaintedLook.FromJson(json);

        Assert.DoesNotContain("stonegate", json);
        Assert.Equal(locked, read.Lock);
        Assert.Null(PaintedLook.FromJson((figure with { Lock = null }).ToJson()).Lock);
        Assert.DoesNotContain("lockHash", (figure with { Lock = null }).ToJson());
    }
}
