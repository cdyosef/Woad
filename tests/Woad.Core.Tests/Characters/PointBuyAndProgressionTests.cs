using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Rules;

namespace Woad.Core.Tests.Characters;

public class PointBuyAndProgressionTests
{
    [Fact]
    public void StatsCostDifferentAmounts()
    {
        var allocation = new StatAllocation { Hearts = 2, Speed = 1, AttackDice = 1, DefenseDice = 1, Luck = 1 };

        Assert.Equal(2 * 1 + 1 * 2 + 1 * 3 + 1 * 3 + 1 * 1, PointBuy.Cost(allocation));
    }

    [Theory]
    [InlineData(StatKind.OdHandDice, new[] { 1, 2, 2, 3, 4, 5 })]
    [InlineData(StatKind.AttackDice, new[] { 3, 3, 3, 4, 5, 6 })]
    [InlineData(StatKind.DefenseDice, new[] { 3, 3, 3, 4, 5, 6 })]
    [InlineData(StatKind.MoveDice, new[] { 3, 3, 3, 4, 5, 6 })]
    [InlineData(StatKind.Hearts, new[] { 1, 1, 1, 1, 1, 1 })]
    public void DiceGetDearerAfterTheThird(StatKind stat, int[] costs)
    {
        Assert.Equal(costs, Enumerable.Range(1, costs.Length).Select(n => PointBuy.StepCost(stat, n)));
        Assert.Equal(costs.Sum(), PointBuy.Cost(StatAllocation.None.With(stat, costs.Length)));
    }

    [Fact]
    public void TheFirstOdHandDieBringsASkillFreeAndTheRestCostAPoint()
    {
        var allocation = new StatAllocation { OdHandDice = 1, Skills = Skill.Search };

        Assert.Equal(1, PointBuy.Cost(allocation));
        Assert.Equal(2, PointBuy.Cost(allocation with { Skills = Skill.Search | Skill.Aid }));
        Assert.Contains("first Od Hand die", PointBuy.Check(new StatAllocation { Skills = Skill.Search }, 1, PointBuy.CreationPoints));
    }

    [Fact]
    public void TheFirstRumbleDieBringsShoveOrBullRushIfYouKnowShoveAlready()
    {
        StatAllocation raised = PointBuy.Raise(StatAllocation.None, StatKind.AttackDice);
        Assert.Equal(Skill.Shove, raised.Skills);
        Assert.Equal(3, PointBuy.Cost(raised));

        StatAllocation shover = new() { Skills = Skill.Shove };
        Assert.Equal(1, PointBuy.Cost(shover));
        StatAllocation rusher = PointBuy.Raise(shover, StatKind.AttackDice);
        Assert.Equal(Skill.Shove | Skill.BullRush, rusher.Skills);
        Assert.Equal(4, PointBuy.Cost(rusher));
    }

    [Fact]
    public void BullRushIsLearnedAfterShove()
    {
        Assert.Contains("after Shove", PointBuy.Check(new StatAllocation { Skills = Skill.BullRush }, 1, PointBuy.CreationPoints));
        Assert.False(PointBuy.CanLearn(StatAllocation.None, Skill.BullRush, 1, PointBuy.CreationPoints));
        Assert.True(PointBuy.CanLearn(StatAllocation.None, Skill.Guard, 1, PointBuy.CreationPoints));
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
    [InlineData(0, 2, 0, null)]
    [InlineData(0, 0, 2, null)]
    [InlineData(0, 3, 0, "Not enough points")]
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
        var allocation = new StatAllocation { OdHandDice = 3 };

        Assert.Contains("at most 2", PointBuy.Check(allocation, level: 1, PointBuy.CreationPoints));
        Assert.Null(PointBuy.Check(allocation, level: 2, PointBuy.CreationPoints));
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
