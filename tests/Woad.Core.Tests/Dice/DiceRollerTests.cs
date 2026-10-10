using Woad.Core.Dice;
using Woad.Core.Randomness;

namespace Woad.Core.Tests.Dice;

public class DiceRollerTests
{
    [Fact]
    public void RollsRequestedNumberOfD6()
    {
        var roller = new DiceRoller(new Pcg32(99));

        var roll = roller.Roll(1_000);

        Assert.Equal(1_000, roll.Faces.Count);
        Assert.All(roll.Faces, face => Assert.InRange(face, 1, DiceRoller.Sides));
        Assert.Equal(Enumerable.Range(1, 6), roll.Faces.Distinct().Order());
    }

    [Fact]
    public void SameSeedRollsSameDice()
    {
        var a = new DiceRoller(new Pcg32(2026));
        var b = new DiceRoller(new Pcg32(2026));

        Assert.Equal(a.Roll(10).Faces, b.Roll(10).Faces);
    }

    [Fact]
    public void RollingZeroDiceIsEmpty()
    {
        var roll = new DiceRoller(new Pcg32(1)).Roll(0);

        Assert.Empty(roll.Faces);
        Assert.Equal(0, roll.Total);
    }

    [Fact]
    public void NegativeCountIsRejected()
    {
        var roller = new DiceRoller(new Pcg32(1));

        Assert.Throws<ArgumentOutOfRangeException>(() => roller.Roll(-1));
    }

    [Fact]
    public void CountsFivesAndSixesAsSuccessesByDefault()
    {
        var roll = new DiceRoll([1, 4, 5, 6, 6]);

        Assert.Equal(3, roll.CountSuccesses());
        Assert.Equal(2, roll.CountSuccesses(minimumFace: 6));
        Assert.Equal(22, roll.Total);
    }
}
