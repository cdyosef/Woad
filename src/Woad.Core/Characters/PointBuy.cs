using Woad.Core.Content;
using Woad.Core.Rules;

namespace Woad.Core.Characters;

/// <summary>Classless point-buy: what stats cost and the limits on spending. See docs/MVP-Design.md.</summary>
public static class PointBuy
{
    /// <summary>Points a new character has to spend.</summary>
    public const int CreationPoints = 6;

    /// <summary>Extra Rumble, Wend, Defense or Od Hand dice a level 1 character may buy (each).</summary>
    public const int LevelOneDiceLimit = 2;

    /// <summary>What a skill costs, beyond the free ones (see <see cref="SkillCost"/>).</summary>
    public const int SkillPrice = 1;

    public static IReadOnlyList<StatKind> AllStats { get; } = Enum.GetValues<StatKind>();

    /// <summary>
    /// What the <paramref name="n"/>th step of a stat costs (from 1). Dice get dearer: Od Hand dice cost
    /// 1, then 2, 2, then 3, 4, 5...; Rumble, Wend and Defense dice 3, 3, 3, then 4, 5, 6.... The rest
    /// cost the same each step.
    /// </summary>
    public static int StepCost(StatKind stat, int n) => stat switch
    {
        StatKind.Hearts => 1,
        StatKind.Speed => 2,
        StatKind.AttackDice or StatKind.DefenseDice or StatKind.MoveDice => n <= 3 ? 3 : n,
        StatKind.OdHandDice => n == 1 ? 1 : n <= 3 ? 2 : n - 1,
        StatKind.Luck => 1,
        StatKind.Magic => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };

    /// <summary>What one more step of <paramref name="stat"/> costs now.</summary>
    public static int CostOfNext(StatAllocation allocation, StatKind stat) => StepCost(stat, allocation[stat] + 1);

    /// <summary>
    /// What the skills bought cost: a point each, but the first Od Hand die brings one Od Hand skill free,
    /// and the first Rumble die bought brings Shove free (or Bull Rush, for a hero who has Shove already).
    /// </summary>
    public static int SkillCost(StatAllocation allocation)
    {
        int odHand = Skills.Count(allocation.Skills & Skills.OdHand) - (allocation.OdHandDice >= 1 && (allocation.Skills & Skills.OdHand) != 0 ? 1 : 0);
        int rumble = Skills.Count(allocation.Skills & Skills.Rumble) - (allocation.AttackDice >= 1 && (allocation.Skills & (Skill.Shove | Skill.BullRush)) != 0 ? 1 : 0);
        return SkillPrice * (odHand + rumble);
    }

    /// <summary>What one step of a stat adds to the hero's stats.</summary>
    public static Stats StepOf(StatKind stat) => stat switch
    {
        StatKind.Hearts => new Stats { Hearts = 1 },
        StatKind.Speed => new Stats { Speed = 1 },
        StatKind.AttackDice => new Stats { AttackDice = 1 },
        StatKind.DefenseDice => new Stats { DefenseDice = 1 },
        StatKind.OdHandDice => new Stats { OdHandDice = 1 },
        StatKind.Luck => new Stats { Luck = 1 },
        StatKind.Magic => new Stats { Magic = 1 },
        StatKind.MoveDice => new Stats { MoveDice = 1 },
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };

    public static int Cost(StatAllocation allocation) =>
        AllStats.Sum(stat => Enumerable.Range(1, Math.Max(0, allocation[stat])).Sum(n => StepCost(stat, n))) + SkillCost(allocation);

    /// <summary>The hero's base stats: the starting stats plus everything bought.</summary>
    public static Stats ToStats(StatAllocation allocation) => GameRules.HeroBaseStats + new Stats
    {
        Hearts = allocation.Hearts,
        Speed = allocation.Speed,
        AttackDice = allocation.AttackDice,
        DefenseDice = allocation.DefenseDice,
        OdHandDice = allocation.OdHandDice,
        Luck = allocation.Luck,
        Magic = allocation.Magic,
        MoveDice = allocation.MoveDice,
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

        if (allocation.Magic > GameRules.MaxMagic)
        {
            return $"Magic goes up to rank {GameRules.MaxMagic}.";
        }

        if (level <= 1 && (allocation.AttackDice > LevelOneDiceLimit || allocation.DefenseDice > LevelOneDiceLimit
                           || allocation.OdHandDice > LevelOneDiceLimit || allocation.MoveDice > LevelOneDiceLimit))
        {
            return $"At level 1 you can add at most {LevelOneDiceLimit} Rumble, Wend, Defense and Od Hand dice each.";
        }

        if ((allocation.Skills & Skills.OdHand) != 0 && allocation.OdHandDice < 1)
        {
            return "Od Hand skills come with your first Od Hand die: buy it first.";
        }

        if (allocation.Skills.HasFlag(Skill.BullRush) && !allocation.Skills.HasFlag(Skill.Shove))
        {
            return "Bull Rush is learned after Shove.";
        }

        return null;
    }

    /// <summary>Whether learning <paramref name="skill"/> as well would still be allowed.</summary>
    public static bool CanLearn(StatAllocation allocation, Skill skill, int level, int budget) =>
        !allocation.Skills.HasFlag(skill) && Check(allocation.WithSkill(skill, true), level, budget) is null;

    /// <summary>
    /// One more step of a stat, with what comes free with it: the first Rumble die bought brings Shove
    /// (or Bull Rush, already knowing Shove).
    /// </summary>
    public static StatAllocation Raise(StatAllocation allocation, StatKind stat)
    {
        StatAllocation raised = allocation.With(stat, allocation[stat] + 1);
        if (stat == StatKind.AttackDice && raised.AttackDice == 1)
        {
            raised = raised.WithSkill(allocation.Skills.HasFlag(Skill.Shove) ? Skill.BullRush : Skill.Shove, true);
        }

        return raised;
    }

    /// <summary>Whether one more step of <paramref name="stat"/> would still be allowed.</summary>
    public static bool CanRaise(StatAllocation allocation, StatKind stat, int level, int budget) =>
        Check(Raise(allocation, stat), level, budget) is null;
}
