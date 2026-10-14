using Woad.Core.Content;
using Woad.Core.Grid;

namespace Woad.Core.Model;

public sealed class Room
{
    private readonly List<Monster> _monsters = [];
    private readonly Dictionary<GridPosition, List<CardInstance>> _loot = [];
    private readonly HashSet<GridPosition> _openedChests = [];
    private readonly Dictionary<Direction, Passage> _passages = [];
    private readonly Dictionary<GridPosition, Trap> _traps = [];
    private readonly Dictionary<GridPosition, Trap> _chestTraps = [];
    private readonly Dictionary<GridPosition, int> _chestLocks = [];
    private readonly HashSet<GridPosition> _superiorChests = [];
    private readonly HashSet<GridPosition> _bossKeyChests = [];
    private readonly Dictionary<GridPosition, Cache> _caches = [];
    private readonly Dictionary<GridPosition, int> _keys = [];

    internal Room(
        string id,
        RoomLayout layout,
        IReadOnlyDictionary<Direction, string> doors,
        int floor = 1,
        GridPosition mapPosition = default,
        RoomRole role = RoomRole.Combat,
        bool trap = false,
        IReadOnlyList<GridPosition>? arrivalTiles = null)
    {
        Id = id;
        Layout = layout;
        Doors = doors;
        Floor = floor;
        MapPosition = mapPosition;
        Role = role;
        Trap = trap;
        ArrivalTiles = arrivalTiles ?? [];
    }

    public string Id { get; }

    public int Floor { get; }

    /// <summary>The room's cell on its floor's map, for drawing the minimap.</summary>
    public GridPosition MapPosition { get; }

    public RoomRole Role { get; }

    /// <summary>
    /// The room right underneath, on the next floor at the same map cell (never the boss room): where
    /// its pits and trap doors lead. Null when nothing is below, and its pits are bottomless.
    /// </summary>
    public string? BelowRoomId { get; internal set; }

    /// <summary>A trap room's doors stay shut while any monster is left in it.</summary>
    public bool Trap { get; }

    /// <summary>Where heroes appear when they come down the stairs into this room, in order of preference.</summary>
    public IReadOnlyList<GridPosition> ArrivalTiles { get; }

    /// <summary>True while a trap room still holds monsters: nobody can leave through its doors.</summary>
    public bool IsSealed => Trap && _monsters.Count > 0;

    public RoomLayout Layout { get; }

    /// <summary>The room each door leads to, by the side of this room it is on.</summary>
    public IReadOnlyDictionary<Direction, string> Doors { get; }

    /// <summary>Whether any hero has been inside. Unexplored rooms are hidden on the map.</summary>
    public bool Explored { get; internal set; }

    public IReadOnlyList<Monster> Monsters => _monsters;

    /// <summary>Tiles holding dropped cards, in the order the piles appeared.</summary>
    public IEnumerable<GridPosition> LootPositions => _loot.Keys;

    public IReadOnlyList<CardInstance> LootAt(GridPosition position) =>
        _loot.TryGetValue(position, out List<CardInstance>? pile) ? pile : [];

    public bool IsChestOpened(GridPosition position) => _openedChests.Contains(position);

    /// <summary>The door on each side that has one (secret ones too, found or not).</summary>
    public IReadOnlyDictionary<Direction, Passage> Passages => _passages;

    public Passage? PassageOn(Direction side) => _passages.GetValueOrDefault(side);

    /// <summary>Traps on floor tiles, hidden or revealed.</summary>
    public IReadOnlyDictionary<GridPosition, Trap> Traps => _traps;

    public Trap? TrapAt(GridPosition position) => _traps.GetValueOrDefault(position);

    public Trap? ChestTrapAt(GridPosition position) => _chestTraps.GetValueOrDefault(position);

    /// <summary>The Od Hand needed to pick a chest's lock, or null when it isn't locked.</summary>
    public int? ChestLockAt(GridPosition position) => _chestLocks.TryGetValue(position, out int difficulty) ? difficulty : null;

    /// <summary>A chest that was locked holds better loot.</summary>
    public bool IsSuperiorChest(GridPosition position) => _superiorChests.Contains(position);

    public bool HoldsBossKey(GridPosition position) => _bossKeyChests.Contains(position);

    /// <summary>Hidden stashes not yet found.</summary>
    public IReadOnlyDictionary<GridPosition, Cache> Caches => _caches;

    /// <summary>Keys lying on the floor, by tile.</summary>
    public IReadOnlyDictionary<GridPosition, int> Keys => _keys;

    public int KeysAt(GridPosition position) => _keys.GetValueOrDefault(position);

    /// <summary>Where the Boss Key lies on the floor, if it does.</summary>
    public GridPosition? BossKeyAt { get; internal set; }

    /// <summary>Anything on the tile to pick up: cards, keys or the Boss Key.</summary>
    public bool HasLootAt(GridPosition position) => LootAt(position).Count > 0 || KeysAt(position) > 0 || BossKeyAt == position;

    public IEnumerable<GridPosition> TilesOfKind(TileKind kind) => RoomGrid.AllPositions().Where(position => Layout[position] == kind);

    public Monster? MonsterAt(GridPosition position) => _monsters.FirstOrDefault(monster => monster.Position == position);

    public Monster? FindMonster(int monsterId) => _monsters.FirstOrDefault(monster => monster.Id == monsterId);

    internal void AddMonster(Monster monster) => _monsters.Add(monster);

    internal void RemoveMonster(Monster monster) => _monsters.Remove(monster);

    internal void OpenChest(GridPosition position)
    {
        _openedChests.Add(position);
        _bossKeyChests.Remove(position);
    }

    internal void AddPassage(Direction side, Passage passage) => _passages[side] = passage;

    internal void SetTrap(GridPosition position, Trap? trap)
    {
        if (trap is null)
        {
            _traps.Remove(position);
        }
        else
        {
            _traps[position] = trap;
        }
    }

    internal void SetChestTrap(GridPosition position, Trap? trap)
    {
        if (trap is null)
        {
            _chestTraps.Remove(position);
        }
        else
        {
            _chestTraps[position] = trap;
        }
    }

    internal void LockChest(GridPosition position, int difficulty)
    {
        _chestLocks[position] = difficulty;
        _superiorChests.Add(position);
    }

    internal void UnlockChest(GridPosition position) => _chestLocks.Remove(position);

    internal void PutBossKeyInChest(GridPosition position) => _bossKeyChests.Add(position);

    internal void HideCache(GridPosition position, Cache cache) => _caches[position] = cache;

    internal void RemoveCache(GridPosition position) => _caches.Remove(position);

    internal void AddKeys(GridPosition position, int count)
    {
        if (count > 0)
        {
            _keys[position] = KeysAt(position) + count;
        }
    }

    /// <summary>Takes every key on the tile.</summary>
    internal int TakeKeys(GridPosition position)
    {
        int count = KeysAt(position);
        _keys.Remove(position);
        return count;
    }

    internal void AddLoot(GridPosition position, IEnumerable<CardInstance> cards)
    {
        if (!_loot.TryGetValue(position, out List<CardInstance>? pile))
        {
            pile = [];
            _loot[position] = pile;
        }

        pile.AddRange(cards);
        if (pile.Count == 0)
        {
            _loot.Remove(position);
        }
    }

    /// <summary>Takes up to <paramref name="count"/> cards from the top of the pile on a tile.</summary>
    internal IReadOnlyList<CardInstance> TakeLoot(GridPosition position, int count)
    {
        if (!_loot.TryGetValue(position, out List<CardInstance>? pile))
        {
            return [];
        }

        List<CardInstance> taken = pile.Take(count).ToList();
        pile.RemoveRange(0, taken.Count);
        if (pile.Count == 0)
        {
            _loot.Remove(position);
        }

        return taken;
    }
}
