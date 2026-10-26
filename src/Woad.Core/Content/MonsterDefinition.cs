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

    /// <summary>A spin on top of its turn (after its own attack), not instead of it.</summary>
    public bool Bonus { get; init; }
}

/// <summary>A chain flung at a hero (the Iron Warden's): a hit drags them beside the monster, shackled.</summary>
public sealed record ChainAttack
{
    /// <summary>How far it reaches (in sight, Manhattan distance).</summary>
    public required int Range { get; init; }

    /// <summary>Its attack dice.</summary>
    public required int Dice { get; init; }

    /// <summary>The Od Hand total that picks the shackles' lock.</summary>
    public required int Lock { get; init; }
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

    /// <summary>How many tiles across it is: 1, or 2 for a huge monster that fills a 2x2 square (the Bridge Troll).</summary>
    public int Size { get; init; } = 1;

    /// <summary>Its weapon's own dice, rolled in with its first attack each turn (a 6 rolls another straight in).</summary>
    public int WeaponDice { get; init; }

    /// <summary>A hit stuns, and each 6 on its weapon dice stuns once more (a Captain's spiked mace).</summary>
    public bool StunOnHit { get; init; }

    /// <summary>Gets a heart back at the end of each turn it didn't start burning (a troll).</summary>
    public bool Regenerates { get; init; }

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

    /// <summary>
    /// Its attack swings through a 3-tile arc toward its target (it can strike diagonally): everyone
    /// there, monsters too, faces the same roll, and each 6 cleaves on into one more creature beside it.
    /// </summary>
    public bool Arc { get; init; }

    /// <summary>A chain it flings each turn before moving, or null.</summary>
    public ChainAttack? Chain { get; init; }

    /// <summary>Extra Defense dice, rolled in one at a time while a defense is losing (the Warden's Plate's).</summary>
    public int ExtraDefenseDice { get; init; }

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

    /// <summary>A special card it may drop besides its loot (the Iron Warden's Warden's Plate), or null.</summary>
    public RareDrop? RareDrop { get; init; }
}

/// <summary>A special card a monster drops on defeat, with a percent chance, besides its usual loot.</summary>
public sealed record RareDrop
{
    public required string Card { get; init; }

    public int Chance { get; init; }
}
