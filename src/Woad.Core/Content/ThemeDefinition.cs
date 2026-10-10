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

    public IReadOnlyList<LootDeckEntry> LootDeck { get; init; } = [];

    public PotDefinition? Pots { get; init; }

    public IReadOnlyList<RosterEntry> Roster { get; init; } = [];

    public string? Boss { get; init; }

    public IReadOnlyList<ThemeRoomDefinition> Rooms { get; init; } = [];
}
