namespace Woad.Core.Content;

/// <summary>
/// A hero's stats, expressed directly as dice and points. Also used for the bonuses a gear
/// card grants while equipped, so stats and bonuses add together.
/// </summary>
public sealed record Stats
{
    public static Stats None { get; } = new();

    /// <summary>Maximum HP.</summary>
    public int Hearts { get; init; }

    /// <summary>d6 rolled for movement.</summary>
    public int MoveDice { get; init; }

    /// <summary>Flat tiles added to the movement roll.</summary>
    public int Speed { get; init; }

    /// <summary>d6 rolled when attacking.</summary>
    public int AttackDice { get; init; }

    /// <summary>d6 rolled when attacked.</summary>
    public int DefenseDice { get; init; }

    /// <summary>Rerolls of a single die, per floor.</summary>
    public int Luck { get; init; }

    public static Stats operator +(Stats left, Stats right) => new()
    {
        Hearts = left.Hearts + right.Hearts,
        MoveDice = left.MoveDice + right.MoveDice,
        Speed = left.Speed + right.Speed,
        AttackDice = left.AttackDice + right.AttackDice,
        DefenseDice = left.DefenseDice + right.DefenseDice,
        Luck = left.Luck + right.Luck,
    };
}
