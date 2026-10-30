using Woad.Core.Content;

namespace Woad.Core.Characters;

public enum StatKind
{
    Hearts,
    Speed,
    AttackDice,
    DefenseDice,
    OdHandDice,
    Luck,
    Magic,

    /// <summary>Wend dice (Speed is the Wend bonus).</summary>
    MoveDice,
}

/// <summary>Points a character has spent on top of the base hero stats.</summary>
public sealed record StatAllocation
{
    public static StatAllocation None { get; } = new();

    public int Hearts { get; init; }

    public int Speed { get; init; }

    public int AttackDice { get; init; }

    public int DefenseDice { get; init; }

    public int OdHandDice { get; init; }

    public int Luck { get; init; }

    public int Magic { get; init; }

    public int MoveDice { get; init; }

    /// <summary>The skills bought (see <see cref="PointBuy.SkillCost"/>).</summary>
    public Skill Skills { get; init; }

    public StatAllocation WithSkill(Skill skill, bool learned) => this with { Skills = learned ? Skills | skill : Skills & ~skill };

    public int this[StatKind stat] => stat switch
    {
        StatKind.Hearts => Hearts,
        StatKind.Speed => Speed,
        StatKind.AttackDice => AttackDice,
        StatKind.DefenseDice => DefenseDice,
        StatKind.OdHandDice => OdHandDice,
        StatKind.Luck => Luck,
        StatKind.Magic => Magic,
        StatKind.MoveDice => MoveDice,
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };

    public StatAllocation With(StatKind stat, int value) => stat switch
    {
        StatKind.Hearts => this with { Hearts = value },
        StatKind.Speed => this with { Speed = value },
        StatKind.AttackDice => this with { AttackDice = value },
        StatKind.DefenseDice => this with { DefenseDice = value },
        StatKind.OdHandDice => this with { OdHandDice = value },
        StatKind.Luck => this with { Luck = value },
        StatKind.Magic => this with { Magic = value },
        StatKind.MoveDice => this with { MoveDice = value },
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };
}
