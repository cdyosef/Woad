using Woad.Core.Content;
using Woad.Core.Grid;

namespace Woad.Core.Model;

public sealed class Room
{
    private readonly List<Monster> _monsters = [];
    private readonly Dictionary<GridPosition, List<CardInstance>> _loot = [];
    private readonly HashSet<GridPosition> _openedChests = [];

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

    public IEnumerable<GridPosition> TilesOfKind(TileKind kind) => RoomGrid.AllPositions().Where(position => Layout[position] == kind);

    public Monster? MonsterAt(GridPosition position) => _monsters.FirstOrDefault(monster => monster.Position == position);

    public Monster? FindMonster(int monsterId) => _monsters.FirstOrDefault(monster => monster.Id == monsterId);

    internal void AddMonster(Monster monster) => _monsters.Add(monster);

    internal void RemoveMonster(Monster monster) => _monsters.Remove(monster);

    internal void OpenChest(GridPosition position) => _openedChests.Add(position);

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
