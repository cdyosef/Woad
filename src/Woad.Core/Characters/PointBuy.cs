using Woad.Core.Content;
using Woad.Core.Rules;

namespace Woad.Core.Characters;

/// <summary>Classless point-buy: what stats cost and the limits on spending. See docs/MVP-Design.md.</summary>
public static class PointBuy
{
    /// <summary>Points a new character has to spend.</summary>
    public const int CreationPoints = 6;

    /// <summary>Extra Attack or Defense dice a level 1 character may buy.</summary>
    public const int LevelOneDiceLimit = 1;

    public static IReadOnlyList<StatKind> AllStats { get; } = Enum.GetValues<StatKind>();

    public static int CostPerStep(StatKind stat) => stat switch
    {
        StatKind.Hearts => 1,
        StatKind.Speed => 2,
        StatKind.AttackDice => 3,
        StatKind.DefenseDice => 3,
        StatKind.Luck => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };

    public static int Cost(StatAllocation allocation) => AllStats.Sum(stat => allocation[stat] * CostPerStep(stat));

    /// <summary>The hero's base stats: the starting stats plus everything bought.</summary>
    public static Stats ToStats(StatAllocation allocation) => GameRules.HeroBaseStats + new Stats
    {
        Hearts = allocation.Hearts,
        Speed = allocation.Speed,
        AttackDice = allocation.AttackDice,
        DefenseDice = allocation.DefenseDice,
        Luck = allocation.Luck,
    };

    /// <summary>Why the allocation isn't allowed, or null if it is.</summary>
    /// <param name="budget">Total points the allocation may cost.</param>
    public static string? Check(StatAllocation allocation, int level, int budget)
    {
        if (AllStats.Any(stat => allocation[stat] < 0))
        {
            return "Stats can't go below their starting values.";
        }

        if (Cost(allocation) > budget)
        {
            return "Not enough points.";
        }

        if (level <= 1 && (allocation.AttackDice > LevelOneDiceLimit || allocation.DefenseDice > LevelOneDiceLimit))
        {
            return $"At level 1 you can add at most {LevelOneDiceLimit} Attack and {LevelOneDiceLimit} Defense die.";
        }

        return null;
    }

    /// <summary>Whether one more step of <paramref name="stat"/> would still be allowed.</summary>
    public static bool CanRaise(StatAllocation allocation, StatKind stat, int level, int budget) =>
        Check(allocation.With(stat, allocation[stat] + 1), level, budget) is null;
}
