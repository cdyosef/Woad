namespace Woad.Core.Content;

public enum AttackPattern
{
    /// <summary>An orthogonally adjacent tile.</summary>
    Melee,

    /// <summary>A straight line up and down or left and right, up to the attack range, not through walls.</summary>
    Reach,

    /// <summary>Any tile within the attack range (Manhattan distance) with line of sight.</summary>
    Ranged,
}

/// <summary>An attack that hits every hero near the monster, used every few turns instead of its normal attack.</summary>
public sealed record SweepAttack
{
    /// <summary>Heroes within this many tiles (diagonals count as 1) are hit.</summary>
    public required int Radius { get; init; }

    /// <summary>The sweep is used on every Nth turn the monster takes.</summary>
    public required int Every { get; init; }
}

public sealed record MonsterDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The character that places this monster in a room template.</summary>
    public required char Glyph { get; init; }

    public required int Hearts { get; init; }

    /// <summary>Tiles moved per turn. Monsters don't roll for movement.</summary>
    public required int Move { get; init; }

    /// <summary>d6 for attacking each turn, split into one or more attacks.</summary>
    public required int AttackDice { get; init; }

    /// <summary>d6 rolled, all of them, whenever attacked.</summary>
    public required int DefenseDice { get; init; }

    /// <summary>d6 for Od Hand: stealing. Most monsters have none.</summary>
    public int OdHandDice { get; init; }

    /// <summary>
    /// Its <see cref="Move"/> in Wend dice, each worth a fixed 2 moves (the last 1 when the move is odd).
    /// A stun that takes a Wend die takes 2 moves.
    /// </summary>
    public int WendDice => (Move + 1) / 2;

    /// <summary>Fixed value added to one attack per turn, like a hero's weapon.</summary>
    public int Weapon { get; init; }

    /// <summary>Fixed defense added to every defense roll.</summary>
    public int Armor { get; init; }

    public AttackPattern AttackPattern { get; init; } = AttackPattern.Melee;

    public int AttackRange { get; init; } = 1;

    /// <summary>Can cross and stop on pits.</summary>
    public bool Flying { get; init; }

    /// <summary>Each step has an even chance of going in a random direction.</summary>
    public bool Erratic { get; init; }

    /// <summary>Backs away from adjacent heroes before attacking.</summary>
    public bool KeepsDistance { get; init; }

    /// <summary>The first time a weapon defeats it, it collapses and rises again next enemy phase. Bombs and spells destroy it outright.</summary>
    public bool Reassembles { get; init; }

    /// <summary>On defeat it splits into <see cref="SplitCount"/> of this monster.</summary>
    public string? SplitsInto { get; init; }

    public int SplitCount { get; init; }

    public SweepAttack? Sweep { get; init; }

    public bool StunImmune { get; init; }

    /// <summary>Its attacks are fire: any 6 on its attack roll sets the target alight, hit or miss.</summary>
    public bool FireAttack { get; init; }

    /// <summary>Never catches fire.</summary>
    public bool FireImmune { get; init; }

    public bool Boss { get; init; }

    /// <summary>A vault guardian: tougher than most, can't be shoved, but doesn't seal the exit.</summary>
    public bool Miniboss { get; init; }

    public int Xp { get; init; }

    /// <summary>How much of a room's difficulty budget this monster uses up when generated.</summary>
    public int Threat { get; init; } = 1;

    /// <summary>Percent chance of dropping loot from the theme's deck when defeated.</summary>
    public int DropChance { get; init; }

    /// <summary>Cards dropped when the drop chance comes up.</summary>
    public int Drops { get; init; } = 1;
}
