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

    /// <summary>d6 for moving each turn. Each move uses one or more of them.</summary>
    public int MoveDice { get; init; }

    /// <summary>Flat tiles added to one move per turn, like a die that always rolls this.</summary>
    public int Speed { get; init; }

    /// <summary>d6 for attacking each turn. Each attack uses one or more of them.</summary>
    public int AttackDice { get; init; }

    /// <summary>d6 rolled, all of them, whenever attacked.</summary>
    public int DefenseDice { get; init; }

    /// <summary>A weapon's fixed value, added to one attack per turn, like a die that always rolls this.</summary>
    public int Weapon { get; init; }

    /// <summary>Fixed defense added to every defense roll.</summary>
    public int Armor { get; init; }

    /// <summary>Rerolls of a single die, per floor.</summary>
    public int Luck { get; init; }

    public static Stats operator +(Stats left, Stats right) => new()
    {
        Hearts = left.Hearts + right.Hearts,
        MoveDice = left.MoveDice + right.MoveDice,
        Speed = left.Speed + right.Speed,
        AttackDice = left.AttackDice + right.AttackDice,
        DefenseDice = left.DefenseDice + right.DefenseDice,
        Weapon = left.Weapon + right.Weapon,
        Armor = left.Armor + right.Armor,
        Luck = left.Luck + right.Luck,
    };
}
