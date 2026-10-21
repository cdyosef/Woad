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

    /// <summary>Once it has stolen something, it runs: away from every hero, no more stealing or fighting.</summary>
    public bool Thief { get; init; }

    /// <summary>Goes after whoever carries the most gold, and every hit of its attacks takes 1 gold too.</summary>
    public bool Mugger { get; init; }

    /// <summary>Its Od Hand steals only gold, never items.</summary>
    public bool GoldOnly { get; init; }

    /// <summary>Every one of its Attack dice is an attack of its own.</summary>
    public bool SeparateAttacks { get; init; }

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

    /// <summary>
    /// Swims: shallow and deep water cost it 1 step, like floor. In deep water it's submerged: ranged
    /// attacks, spells and blasts can't reach it, only blows from right beside it (or a trident).
    /// </summary>
    public bool Swimmer { get; init; }

    /// <summary>A swimmer that never leaves the water (shallow or deep).</summary>
    public bool WaterOnly { get; init; }

    /// <summary>A hard shell: a shove that beats its defense flips it onto its back, and while it lies flipped (prone) its armor doesn't count.</summary>
    public bool Shell { get; init; }

    /// <summary>A hit drags the hero in a straight line to the tile beside it (a tongue or a hook).</summary>
    public bool Pulls { get; init; }

    /// <summary>A hit grabs the hero: they can't move while it holds them, until it's hurt, shoved, stunned or frozen.</summary>
    public bool Grabs { get; init; }

    /// <summary>A hit knocks the hero a tile straight back, into a pit or onto a trap if one's there.</summary>
    public bool KnocksBack { get; init; }

    /// <summary>Every 6 on its attack roll knocks its targets a tile further back, hit or miss.</summary>
    public bool KnockbackOnSix { get; init; }

    /// <summary>What it bellows when heroes first come into its room (shown and heard, no effect).</summary>
    public string? Taunt { get; init; }

    /// <summary>Its own song, played while a hero shares its room.</summary>
    public string? Music { get; init; }

    /// <summary>Drawn bigger than a tile (it still fills only one).</summary>
    public bool Big { get; init; }

    /// <summary>A hit also drains one of the hero's Luck rerolls; with none left, the hex stuns them instead.</summary>
    public bool Hexes { get; init; }

    /// <summary>Swallows the loot (cards and gold) on every tile it crawls over, and drops it where it falls.</summary>
    public bool Engulfs { get; init; }

    /// <summary>Raises this monster beside itself every other turn; they crumble when it falls, and are worth nothing.</summary>
    public string? Raises { get; init; }

    /// <summary>How many it keeps raised at once.</summary>
    public int RaiseMax { get; init; } = 2;

    /// <summary>How many it can raise in all before its power runs dry.</summary>
    public int RaiseTotal { get; init; } = 4;

    /// <summary>Every 6 on its attack roll stuns its targets, hit or miss.</summary>
    public bool StunOnSix { get; init; }

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
