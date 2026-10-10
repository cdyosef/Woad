using Woad.Core.Characters;
using Woad.Core.Rules;

namespace Woad.Core.Tests.Characters;

public class PointBuyAndProgressionTests
{
    [Fact]
    public void StatsCostDifferentAmounts()
    {
        var allocation = new StatAllocation { Hearts = 2, Speed = 1, AttackDice = 1, DefenseDice = 1, Luck = 1 };

        Assert.Equal(2 * 1 + 1 * 2 + 1 * 3 + 1 * 3 + 1 * 2, PointBuy.Cost(allocation));
    }

    [Fact]
    public void AllocationsAddToTheBaseHeroStats()
    {
        var stats = PointBuy.ToStats(new StatAllocation { Hearts = 2, AttackDice = 1, Luck = 1 });

        Assert.Equal(GameRules.HeroBaseStats.Hearts + 2, stats.Hearts);
        Assert.Equal(GameRules.HeroBaseStats.AttackDice + 1, stats.AttackDice);
        Assert.Equal(1, stats.Luck);
        Assert.Equal(GameRules.HeroBaseStats.MoveDice, stats.MoveDice);
    }

    [Theory]
    [InlineData(6, 0, 0, null)]
    [InlineData(7, 0, 0, "Not enough points")]
    [InlineData(0, 2, 0, "at most 1 Attack")]
    [InlineData(0, 0, 2, "at most 1 Attack")]
    [InlineData(-1, 0, 0, "below")]
    public void CreationSpendingIsLimited(int hearts, int attack, int defense, string? problem)
    {
        var allocation = new StatAllocation { Hearts = hearts, AttackDice = attack, DefenseDice = defense };

        string? error = PointBuy.Check(allocation, level: 1, PointBuy.CreationPoints);

        if (problem is null)
        {
            Assert.Null(error);
        }
        else
        {
            Assert.Contains(problem, error);
        }
    }

    [Fact]
    public void TheDiceLimitOnlyAppliesAtLevelOne()
    {
        var allocation = new StatAllocation { AttackDice = 2 };

        Assert.NotNull(PointBuy.Check(allocation, level: 1, budget: 10));
        Assert.Null(PointBuy.Check(allocation, level: 2, budget: 10));
    }

    [Fact]
    public void CanRaiseChecksTheNextStep()
    {
        var allocation = new StatAllocation { Hearts = 3, AttackDice = 1 };

        Assert.False(PointBuy.CanRaise(allocation, StatKind.AttackDice, level: 1, PointBuy.CreationPoints));
        Assert.False(PointBuy.CanRaise(allocation, StatKind.DefenseDice, level: 1, PointBuy.CreationPoints));
        Assert.False(PointBuy.CanRaise(allocation, StatKind.Hearts, level: 1, PointBuy.CreationPoints));
        Assert.True(PointBuy.CanRaise(allocation with { Hearts = 2 }, StatKind.Hearts, level: 1, PointBuy.CreationPoints));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    [InlineData(29, 2)]
    [InlineData(30, 3)]
    [InlineData(60, 4)]
    [InlineData(100, 5)]
    public void LevelsFollowTheXpTable(int xp, int level)
    {
        Assert.Equal(level, Progression.LevelForXp(xp));
    }
}
