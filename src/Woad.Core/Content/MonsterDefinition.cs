namespace Woad.Core.Content;

/// <summary>How Holy Water takes the undead: lesser ones are destroyed outright, greater ones take its Radiate dice.</summary>
public enum UndeadKind
{
    Lesser,
    Greater,
}

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
/// <summary>A monster that hits back whenever it's attacked, hit or miss (the Emerald Knight): its plain dice, its weapon, and typed dice.</summary>
public sealed record CounterAttack
{
    public int Dice { get; init; } = 1;

    /// <summary>The typed dice rolled in (the Emerald Axe's Life die), whose sixes take hold on a hit or a miss.</summary>
    public Model.DieType? DieType { get; init; }

    public int TypedDice { get; init; } = 1;
}

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

    /// <summary>A hit seizes the hero and carries them off toward the nearest open air, dropping them in if it gets there (Baron Herkullos's beetle).</summary>
    public bool CarriesOff { get; init; }

    /// <summary>A hit knocks the hero a tile straight back, into a pit or onto a trap if one's there.</summary>
    public bool KnocksBack { get; init; }

    /// <summary>Every 6 on its attack roll knocks its targets a tile further back, hit or miss.</summary>
    public bool KnockbackOnSix { get; init; }

    /// <summary>What it bellows when heroes first come into its room (shown and heard, no effect).</summary>
    public string? Taunt { get; init; }

    /// <summary>
    /// The weapon it fights with (a card, an ogre's Big Club): its weapon dice, value, reach and ways go into its
    /// attacks; if it breaks, they're gone; and when the monster falls with it whole, it drops it.
    /// </summary>
    public string? Wields { get; init; }

    /// <summary>What it may carry to throw, checked in turn on one d100 as it's placed (an ogre's Boulder, else Rocks).</summary>
    public IReadOnlyList<ThrowableGear> Throws { get; init; } = [];

    /// <summary>
    /// Its off hand's fists: after its blows at a hero right beside it, it punches with this many dice, every 6
    /// knocking them a tile back, a Rock or Boulder it carries adding to the punch (an ogre's 2).
    /// </summary>
    public int Fists { get; init; }

    /// <summary>
    /// It charges (a Bull Rush) at a hero in a clear straight line beyond its reach: a Wend die of tiles (a 6
    /// rolls another), and reaching them, a blow with the tiles to spare added; a hit drives them 2 tiles on,
    /// a miss leaves it prone (an ogre).
    /// </summary>
    public bool BullRushes { get; init; }

    /// <summary>
    /// A 6 on its claws, hitting, snatches the hero (Leviathan Jr.): held, dragged off into deep water and under with
    /// it, till they break free (a Rumble roll beating its claws' total) or it's hurt.
    /// </summary>
    public bool Snatches { get; init; }

    /// <summary>Slain, its body floats belly-up where it fell, eyes X'd: a raft over the water heroes can stand on (Leviathan Jr.).</summary>
    public bool FloatsWhenSlain { get; init; }

    /// <summary>
    /// With nobody in reach of its blows, it lashes its tongue at a hero up to this many tiles off, in sight: its
    /// Attack dice, without its weapon (Leviathan Jr.'s 5). 0 for none.
    /// </summary>
    public int Tongue { get; init; }

    /// <summary>It throws twice as far as anyone else (an ogre).</summary>
    public bool ThrowsFar { get; init; }

    /// <summary>The card it <see cref="Wields"/>, linked up as the content loads.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public CardDefinition? WieldsCard { get; internal set; }

    /// <summary>What it yells at the start of each of its turns (shown and heard, no effect): an ogre's "GRA!!".</summary>
    public string? Yell { get; init; }

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

    /// <summary>Luck it starts with: tried when its attack or defense is about to fail (a Hexer's, at least 1).</summary>
    public int Luck { get; init; }

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

    /// <summary>It hits back at whoever attacks it, hit or miss, unless stunned or frozen past half its Wend (see <c>GameSession.Counter</c>).</summary>
    public CounterAttack? Counter { get; init; }

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

    /// <summary>What its attack dice are made of (fire, poison...): their 6s roll more of them in, and on a hit lay their effect on the target. Null for plain Star dice.</summary>
    public Model.DieType? DieType { get; init; }

    /// <summary>Never catches fire.</summary>
    public bool FireImmune { get; init; }

    /// <summary>Undead, lesser or greater (see <see cref="UndeadKind"/>), or null for the living.</summary>
    public UndeadKind? Undead { get; init; }

    /// <summary>A Ghoul's miasma: every living creature starting its turn beside it is attacked with a Poison die.</summary>
    public bool Miasma { get; init; }

    /// <summary>The disease its hits carry: a 6 among its dice tries to give it to a hero it hits (see <see cref="Infection"/>).</summary>
    public Infection? Infects { get; init; }

    /// <summary>Dry as tinder: catching fire, it takes a fire die more.</summary>
    public bool Flammable { get; init; }

    /// <summary>
    /// Grave dust puffs off it whenever it loses a heart: each hero beside it rolls a d6, and on 3 or under
    /// its disease tries to take them (and they roll to resist). Destroyed, it bursts over 2 tiles, the
    /// dust and a Poison and a Necro die at everyone there. Not when fire or Radiate does it.
    /// </summary>
    public bool GraveDust { get; init; }

    /// <summary>Typed dice it rolls on top of its own with every attack (a Rotten Mummy's Necro die).</summary>
    public IReadOnlyList<TypedDie> ExtraDice { get; init; } = [];

    /// <summary>One of those at the cult's ritual (the cabal and its leader): kill them all and it's broken. They go on the undead's turn.</summary>
    public bool Ritualist { get; init; }

    /// <summary>A spell (by id) it casts at a hero in range when it can't strike anyone (a Rotten Mummy's Swirly Eyes).</summary>
    public string? Spell { get; init; }

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

    /// <summary>Things it carries and drops when defeated, each on its own chance (a grave robber's shovel and oil).</summary>
    public IReadOnlyList<RareDrop> Carries { get; init; } = [];
}

/// <summary>A special card a monster drops on defeat, with a percent chance, besides its usual loot.</summary>
/// <summary>Something a monster may carry to throw: <see cref="Chance"/>% of them have <see cref="Min"/>-<see cref="Max"/> of it.</summary>
public sealed record ThrowableGear
{
    public required string Card { get; init; }

    public int Chance { get; init; }

    public int Min { get; init; } = 1;

    public int Max { get; init; } = 1;
}

public sealed record RareDrop
{
    public required string Card { get; init; }

    public int Chance { get; init; }
}

/// <summary>A disease a monster carries, and the d6 a hero needs to resist catching it (Thieves' Oil makes it 3).</summary>
public sealed record Infection
{
    public required Model.Condition Disease { get; init; }

    public int Resist { get; init; } = 6;
}

/// <summary>Dice of one type.</summary>
public sealed record TypedDie
{
    public required Model.DieType Type { get; init; }

    public int Dice { get; init; } = 1;
}
