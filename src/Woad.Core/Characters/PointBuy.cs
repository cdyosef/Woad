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
    /// and every Rumble die bought brings a Rumble skill of your choice free.
    /// </summary>
    public static int SkillCost(StatAllocation allocation)
    {
        int odHand = Skills.Count(allocation.Skills & Skills.OdHand) - (allocation.OdHandDice >= 1 && (allocation.Skills & Skills.OdHand) != 0 ? 1 : 0);
        return SkillPrice * (odHand + PaidRumbleSkills(allocation));
    }

    /// <summary>The Rumble skills paid for: those beyond one free with each Rumble die bought.</summary>
    public static int PaidRumbleSkills(StatAllocation allocation) => Math.Max(0, Skills.Count(allocation.Skills & Skills.Rumble) - allocation.AttackDice);

    /// <summary>Free Rumble skills still to pick: a Rumble die bought with no skill taken for it yet.</summary>
    public static int FreeRumbleSkillsLeft(StatAllocation allocation) => Math.Max(0, allocation.AttackDice - Skills.Count(allocation.Skills & Skills.Rumble));

    /// <summary>
    /// The Rumble skills paid for under the old rule, when only the first Rumble die brought one (Shove, or Bull Rush):
    /// what a character saved then is refunded the difference of.
    /// </summary>
    public static int LegacyPaidRumbleSkills(StatAllocation allocation) =>
        Skills.Count(allocation.Skills & Skills.Rumble) - (allocation.AttackDice >= 1 && (allocation.Skills & (Skill.Shove | Skill.BullRush)) != 0 ? 1 : 0);

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
    /// One more step of a stat. A Rumble die brings a free Rumble skill, but it's the player's to choose: it isn't
    /// learned here (see <see cref="FreeRumbleSkillsLeft"/>).
    /// </summary>
    public static StatAllocation Raise(StatAllocation allocation, StatKind stat) => allocation.With(stat, allocation[stat] + 1);

    /// <summary>Whether one more step of <paramref name="stat"/> would still be allowed.</summary>
    public static bool CanRaise(StatAllocation allocation, StatKind stat, int level, int budget) =>
        Check(Raise(allocation, stat), level, budget) is null;
}
