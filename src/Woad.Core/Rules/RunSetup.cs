using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// A hero joining a run: a saved character's name, stats and the cards they bring in.
/// <paramref name="CharacterId"/> links the hero back to the character for saving results.
/// </summary>
public sealed record HeroSetup(string Name, Stats BaseStats, IReadOnlyList<string> CardIds, Guid? CharacterId = null, string Portrait = "");

/// <summary>
/// One room of the dungeon and the rooms its doors lead to. <paramref name="MapPosition"/> is
/// its cell on the floor's map. In a <paramref name="Trap"/> room the doors stay shut while
/// monsters remain. <paramref name="Features"/> adds traps, locked chests and hidden stashes.
/// </summary>
public sealed record RoomSetup(
    string Id,
    RoomTemplate Template,
    IReadOnlyDictionary<Direction, string>? Doors = null,
    int Floor = 1,
    GridPosition MapPosition = default,
    RoomRole Role = RoomRole.Combat,
    bool Trap = false,
    RoomFeatures? Features = null);

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

    /// <summary>The chest holding the Boss Key, if it's in this room.</summary>
    public GridPosition? BossKeyChest { get; init; }

    /// <summary>Where the Boss Key lies in the open, if this room has it but no chest for it.</summary>
    public GridPosition? BossKeyOnFloor { get; init; }

    public IReadOnlyDictionary<GridPosition, CacheSetup> Caches { get; init; } = new Dictionary<GridPosition, CacheSetup>();

    /// <summary>Keys lying on the floor from the start.</summary>
    public IReadOnlyDictionary<GridPosition, int> Keys { get; init; } = new Dictionary<GridPosition, int>();
}

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

    /// <summary>Doors that aren't plain open doors (each listed from one side only).</summary>
    public IReadOnlyList<PassageSetup> Passages { get; init; } = [];
}
