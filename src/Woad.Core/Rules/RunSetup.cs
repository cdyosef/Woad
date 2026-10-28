using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// A hero joining a run: a saved character's name, stats and the cards they bring in.
/// <paramref name="CharacterId"/> links the hero back to the character for saving results. A
/// companion names the seat of the player it belongs to in <paramref name="OwnerSeat"/>; the game
/// plays its turns, following that player's orders. <paramref name="Hearts"/> starts the hero
/// wounded (a raid carries hearts from one dungeon to the next); null starts them at full.
/// <paramref name="GuildStanding"/> is their name with the Merchant Guild, which sets a merchant's prices.
/// </summary>
public sealed record HeroSetup(
    string Name,
    Stats BaseStats,
    IReadOnlyList<string> CardIds,
    Guid? CharacterId = null,
    string Portrait = "",
    int? OwnerSeat = null,
    Appearance? Appearance = null,
    int? Hearts = null,
    int GuildStanding = 0,
    int PowerDice = 0,
    StatAllocation? Allocation = null,
    int Xp = 0,
    int UnspentPoints = 0,
    CompanionPolicy? Policy = null,
    IReadOnlyList<string>? Spells = null);

/// <summary>A merchant chained up in a room, the Od Hand needed to pick the lock of their chains, and their name.</summary>
public sealed record MerchantSetup(GridPosition Position, int LockDifficulty, string Name);

/// <summary>
/// One room of the dungeon and the rooms its doors lead to. <paramref name="MapPosition"/> is
/// its cell on the floor's map. In a <paramref name="Trap"/> room the doors stay shut while
/// monsters remain. <paramref name="Features"/> adds traps, locked chests and hidden stashes.
/// <paramref name="DoorTiles"/> places doors along their edges (by default, the middle of each).
/// </summary>
public sealed record RoomSetup(
    string Id,
    RoomTemplate Template,
    IReadOnlyDictionary<Direction, string>? Doors = null,
    int Floor = 1,
    GridPosition MapPosition = default,
    RoomRole Role = RoomRole.Combat,
    bool Trap = false,
    RoomFeatures? Features = null,
    IReadOnlyDictionary<Direction, GridPosition>? DoorTiles = null,
    bool Dark = false);

public sealed record TrapSetup(TrapKind Kind, int Difficulty);

/// <summary>A hidden stash: <paramref name="Keys"/> keys and <paramref name="Cards"/> cards from the loot deck.</summary>
public sealed record CacheSetup(int Difficulty, int Keys = 0, int Cards = 0, bool BehindWall = false);

/// <summary>What a room hides or guards, besides its monsters.</summary>
public sealed record RoomFeatures
{
    public static RoomFeatures None { get; } = new();

    /// <summary>Traps on floor tiles, hidden at the start.</summary>
    public IReadOnlyDictionary<GridPosition, TrapSetup> Traps { get; init; } = new Dictionary<GridPosition, TrapSetup>();

    /// <summary>Locked chests and the Od Hand needed to pick each. They hold better loot.</summary>
    public IReadOnlyDictionary<GridPosition, int> LockedChests { get; init; } = new Dictionary<GridPosition, int>();

    /// <summary>Hidden needle traps in chests.</summary>
    public IReadOnlyDictionary<GridPosition, TrapSetup> ChestTraps { get; init; } = new Dictionary<GridPosition, TrapSetup>();

    /// <summary>Grand chests (a den's): three cards dealt as if from two floors deeper, and gold.</summary>
    public IReadOnlyCollection<GridPosition> GrandChests { get; init; } = [];

    /// <summary>Chests that are mimics, and the search needed to see through each.</summary>
    public IReadOnlyDictionary<GridPosition, int> Mimics { get; init; } = new Dictionary<GridPosition, int>();

    /// <summary>A traveling merchant chained up here, if any.</summary>
    public MerchantSetup? Merchant { get; init; }

    /// <summary>The chest holding the Boss Key, if it's in this room.</summary>
    public GridPosition? BossKeyChest { get; init; }

    /// <summary>Where the Boss Key lies in the open, if this room has it but no chest for it.</summary>
    public GridPosition? BossKeyOnFloor { get; init; }

    public IReadOnlyDictionary<GridPosition, CacheSetup> Caches { get; init; } = new Dictionary<GridPosition, CacheSetup>();

    /// <summary>Keys lying on the floor from the start.</summary>
    public IReadOnlyDictionary<GridPosition, int> Keys { get; init; } = new Dictionary<GridPosition, int>();

    /// <summary>Locked gates in a wall dividing the room, and the Od Hand needed to pick each.</summary>
    public IReadOnlyDictionary<GridPosition, int> Gates { get; init; } = new Dictionary<GridPosition, int>();

    /// <summary>Secret ways through a wall dividing the room, and the search needed to find each.</summary>
    public IReadOnlyDictionary<GridPosition, int> HiddenGates { get; init; } = new Dictionary<GridPosition, int>();
}

/// <summary>
/// A breach: the cracked edge tiles <paramref name="TileA"/> of room <paramref name="RoomA"/> and
/// <paramref name="TileB"/> of its neighbor <paramref name="RoomB"/>, facing each other. Breaking either
/// breaks both, opening a way between the rooms.
/// </summary>
public sealed record BreachSetup(string RoomA, GridPosition TileA, string RoomB, GridPosition TileB);

/// <summary>
/// How the door on <paramref name="Side"/> of room <paramref name="RoomId"/> starts: locked,
/// the Boss Door, secret or a cracked wall, perhaps with a dart trap. Doors without one are open.
/// </summary>
public sealed record PassageSetup(string RoomId, Direction Side, PassageKind Kind, int Difficulty = 0, TrapSetup? Trap = null);

public sealed record RunSetup
{
    /// <summary>Decides every die, loot draw and monster choice; the same seed and commands replay the same run.</summary>
    public required ulong Seed { get; init; }

    public required string ThemeId { get; init; }

    /// <summary>Heroes in seat (turn) order.</summary>
    public required IReadOnlyList<HeroSetup> Heroes { get; init; }

    public required IReadOnlyList<RoomSetup> Rooms { get; init; }

    /// <summary>Where the heroes begin, on the template's <c>1</c>-<c>4</c> marks or near the center.</summary>
    public required string StartRoomId { get; init; }

    /// <summary>A fight out on the Woarld Map (see <c>Skirmish</c>): it ends, and the party walks on, once the room's monsters are beaten.</summary>
    public bool Skirmish { get; init; }

    /// <summary>The monsters act before any hero: an ambush nobody saw coming, or a failed escape.</summary>
    public bool MonstersFirst { get; init; }

    /// <summary>The Merchant Guild's level: the better the guild, the better a dungeon merchant's wares.</summary>
    public int MerchantGuildLevel { get; init; } = 1;

    /// <summary>Doors that aren't plain open doors (each listed from one side only).</summary>
    public IReadOnlyList<PassageSetup> Passages { get; init; } = [];

    /// <summary>Cracked edge walls between neighboring rooms that a bomb can open.</summary>
    public IReadOnlyList<BreachSetup> Breaches { get; init; } = [];

    /// <summary>Heroes can hurt each other: blasts catch them, and they can attack and shove one another.</summary>
    public bool FriendlyFire { get; init; }
}
