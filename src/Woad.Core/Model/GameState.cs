namespace Woad.Core.Model;

public enum RunPhase
{
    NotStarted,
    InProgress,
    Ended,
}

/// <summary>Everything about a run in progress. Read it freely; change it only through GameSession commands.</summary>
public sealed class GameState
{
    private readonly List<Hero> _heroes = [];
    private readonly Dictionary<string, Room> _rooms = new(StringComparer.Ordinal);
    private int _nextId = 1;

    internal GameState(ulong seed, string themeId, LootDeck lootDeck)
    {
        Seed = seed;
        ThemeId = themeId;
        LootDeck = lootDeck;
    }

    public ulong Seed { get; }

    public string ThemeId { get; }

    public int Round { get; internal set; }

    public RunPhase Phase { get; internal set; }

    /// <summary>Heroes in seat order.</summary>
    public IReadOnlyList<Hero> Heroes => _heroes;

    public IReadOnlyDictionary<string, Room> Rooms => _rooms;

    /// <summary>The active hero's turn, or null when the run isn't in progress.</summary>
    public TurnState? Turn { get; internal set; }

    public Hero? ActiveHero => Turn is null ? null : GetHero(Turn.HeroId);

    /// <summary>Set when the run ends: the escaped heroes with the most treasure (empty if nobody escaped).</summary>
    public IReadOnlyList<int> WinnerIds { get; internal set; } = [];

    internal LootDeck LootDeck { get; }

    public Hero GetHero(int heroId) =>
        _heroes.FirstOrDefault(hero => hero.Id == heroId) ?? throw new KeyNotFoundException($"No hero with id {heroId}.");

    public Room RoomOf(Hero hero) => _rooms[hero.RoomId];

    public IEnumerable<Hero> ActiveHeroesIn(Room room) =>
        _heroes.Where(hero => hero.IsActive && hero.RoomId == room.Id);

    /// <summary>Ids are shared by heroes, monsters and cards, so every id in a run is unique.</summary>
    internal int NextId() => _nextId++;

    internal void AddHero(Hero hero) => _heroes.Add(hero);

    internal void AddRoom(Room room) => _rooms.Add(room.Id, room);
}
