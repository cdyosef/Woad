using Woad.Core.Art;
using Xunit;

namespace Woad.Core.Tests.Art;

public class PaintedLookTests
{
    private static PaintedLook Painted()
    {
        List<string> rows = [.. PaintedLook.Blank().Rows];
        rows[4] = "....aHHSSHHa....";
        rows[5] = "....aSeSSeSa....";
        rows[6] = "....atTRrTta....";
        return new PaintedLook("tin_man", "Tin Man", "human", "hair", rows, new Dictionary<char, string> { ['a'] = "#181420" });
    }

    [Fact]
    public void AFigureSurvivesBeingSavedAndOpened()
    {
        PaintedLook look = Painted();

        PaintedLook opened = PaintedLook.FromJson(look.ToJson());

        Assert.Equal(look.Id, opened.Id);
        Assert.Equal(look.Name, opened.Name);
        Assert.Equal(look.SkinSet, opened.SkinSet);
        Assert.Equal(look.HairSet, opened.HairSet);
        Assert.Equal(look.Rows, opened.Rows);
        Assert.Equal(look.Palette, opened.Palette);
    }

    [Fact]
    public void AFigureWithoutSkinOrHairColorsKeepsNone()
    {
        PaintedLook opened = PaintedLook.FromJson((Painted() with { SkinSet = null, HairSet = null }).ToJson());

        Assert.Null(opened.SkinSet);
        Assert.Null(opened.HairSet);
    }

    [Fact]
    public void TheEyesAreFoundLeftToRight()
    {
        Assert.Equal([(6, 5), (9, 5)], Painted().Eyes);
    }

    [Theory]
    [InlineData("Tin Man", "tin_man")]
    [InlineData("  Geraint's   Arm! ", "geraint_s_arm")]
    [InlineData("Ünïcode 7", "n_code_7")]
    [InlineData("---", "")]
    public void ANameMakesAnId(string name, string id)
    {
        Assert.Equal(id, PaintedLook.IdFor(name));
    }

    [Theory]
    [InlineData('T', 1, false)]
    [InlineData('t', 1, true)]
    [InlineData('R', 2, false)]
    [InlineData('s', 3, true)]
    [InlineData('H', 4, false)]
    [InlineData('a', 0, false)]
    [InlineData('e', 0, false)]
    public void ThePlayerColoredPaintsAreKnown(char pixel, int channel, bool shade)
    {
        Assert.Equal(channel, PaintedLook.Channel(pixel));
        Assert.Equal(shade, PaintedLook.IsShade(pixel));
    }

    [Fact]
    public void ProblemsAreNamed()
    {
        PaintedLook look = Painted();

        Assert.Null(look.Problem());
        Assert.NotNull(PaintedLook.Blank().Problem());
        Assert.NotNull((look with { Id = "" }).Problem());
        Assert.NotNull((look with { Rows = [.. look.Rows.Take(15)] }).Problem());
        Assert.NotNull((look with { Palette = new Dictionary<char, string>() }).Problem());
        Assert.NotNull((look with { Palette = new Dictionary<char, string> { ['a'] = "red" } }).Problem());
        Assert.NotNull((look with { Palette = new Dictionary<char, string> { ['a'] = "#181420", ['T'] = "#ffffff" } }).Problem());
        Assert.Throws<FormatException>(() => PaintedLook.FromJson((look with { Id = "" }).ToJson()));
    }

    [Fact]
    public void TheLettersForFixedColorsAreNeverPaintsOfTheirOwn()
    {
        Assert.DoesNotContain(PaintedLook.Letters, letter => letter is PaintedLook.Empty or PaintedLook.Eye || PaintedLook.Channels.Contains(letter));
        Assert.Equal(PaintedLook.Letters.Length, PaintedLook.Letters.Distinct().Count());
    }
}
