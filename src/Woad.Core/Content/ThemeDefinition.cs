namespace Woad.Core.Content;

public sealed record LootDeckEntry
{
    public required string Card { get; init; }

    public int Copies { get; init; } = 1;

    /// <summary>The first floor this card can be found on; deeper floors hide better loot.</summary>
    public int MinFloor { get; init; } = 1;
}

/// <summary>What smashing one of the theme's pots can turn up. Chances are percentages.</summary>
public sealed record PotDefinition
{
    /// <summary>The treasure card a pot can hold.</summary>
    public required string Treasure { get; init; }

    public int TreasureChance { get; init; }

    /// <summary>Chance the smasher finds a heart and recovers 1 heart.</summary>
    public int HeartChance { get; init; }
}

/// <summary>Chests that are really monsters lying in wait.</summary>
public sealed record MimicDefinition
{
    /// <summary>The monster a mimic chest turns into.</summary>
    public required string Monster { get; init; }

    /// <summary>Percent chance an ordinary chest (not the Boss Key's) is a mimic.</summary>
    public int Chance { get; init; }

    public int MinFloor { get; init; } = 1;
}

/// <summary>Percent chances of finding a small key.</summary>
public sealed record KeyChances
{
    /// <summary>A defeated monster leaves a key where it fell.</summary>
    public int Monster { get; init; } = 15;

    /// <summary>A smashed pot holds a key (checked before its other contents).</summary>
    public int Pot { get; init; } = 10;

    /// <summary>A key appears somewhere in a room when its last monster falls.</summary>
    public int RoomCleared { get; init; } = 35;

    /// <summary>A successful steal from a monster takes a key rather than a card.</summary>
    public int Steal { get; init; } = 40;
}

/// <summary>What a room is for in a generated floor.</summary>
public enum RoomRole
{
    /// <summary>Where heroes start the floor (the first floor's entrance, or the bottom of the stairs). No monsters.</summary>
    Arrival,

    Combat,

    /// <summary>Chests and few guards.</summary>
    Treasure,

    /// <summary>Holds the stairs down to the next floor.</summary>
    Stairs,

    /// <summary>The last floor's boss, guarding the exit.</summary>
    Boss,

    /// <summary>The last floor's vault: the Boss Key's chest, guarded by the theme's miniboss.</summary>
    Vault,

    /// <summary>A hidden room behind a secret door or cracked wall, with extra treasure and no other way in.</summary>
    Secret,

    /// <summary>
    /// A monster's lair off one ordinary room, its only way in: a platform ringed by pits, its keeper in
    /// the middle, and a grand chest beyond (see <see cref="ThemeDefinition.Den"/>).
    /// </summary>
    Den,
}

/// <summary>One floor always has a den: a big monster on a platform ringed by pits, guarding a grand chest.</summary>
public sealed record DenDefinition
{
    public required string Monster { get; init; }

    public int Floor { get; init; } = 1;
}

/// <summary>A monster the generator may place on this theme's floors.</summary>
public sealed record RosterEntry
{
    public required string Monster { get; init; }

    /// <summary>Relative chance of being picked among the monsters that fit.</summary>
    public int Weight { get; init; } = 1;

    /// <summary>The first floor this monster can appear on.</summary>
    public int MinFloor { get; init; } = 1;
}

/// <summary>A handcrafted room layout and the roles it can be used for.</summary>
public sealed record ThemeRoomDefinition
{
    public required string Id { get; init; }

    public required IReadOnlyList<RoomRole> Roles { get; init; }

    /// <summary>Nine rows of nine characters; see <see cref="Dungeon.RoomTemplate"/> for the legend.</summary>
    public required IReadOnlyList<string> Layout { get; init; }
}

/// <summary>A dungeon's identity: its name, loot deck, monsters, boss and room layouts.</summary>
public sealed record ThemeDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public int Floors { get; init; } = 3;

    /// <summary>A flooded dungeon: built rooms get pools, the boss arena a great pool, and streams are never chasms.</summary>
    public bool Flooded { get; init; }

    public IReadOnlyList<LootDeckEntry> LootDeck { get; init; } = [];

    public PotDefinition? Pots { get; init; }

    public KeyChances Keys { get; init; } = new();

    public IReadOnlyList<RosterEntry> Roster { get; init; } = [];

    /// <summary>The floor's den and its keeper; null for none.</summary>
    public DenDefinition? Den { get; init; }

    /// <summary>Chests that may be mimics; null for none.</summary>
    public MimicDefinition? Mimics { get; init; }

    public string? Boss { get; init; }

    /// <summary>The miniboss guarding the Boss Key in the last floor's vault (placed on its '&').</summary>
    public string? Miniboss { get; init; }

    public IReadOnlyList<ThemeRoomDefinition> Rooms { get; init; } = [];
}
