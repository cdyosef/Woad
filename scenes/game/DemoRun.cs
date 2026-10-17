using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Rules;

namespace Woad.Scenes.Game;

/// <summary>Fixed setups for the debug modes: a single hand-made room and a two-hero party.</summary>
public static class DemoRun
{
    // C chest, # wall, ~ pit; s slime, b bat, a archer guard, k skeleton; 1-4 hero starts.
    private const string EntryHall = """
        C...a....
        .s.....b.
        .........
        ..#~~~#..
        ....k....
        .........
        .~.....~.
        ........C
        ..31.24..
        """;

    /// <summary>Aria (with a bomb and Luck) and Bran (with a boomerang).</summary>
    public static IReadOnlyList<HeroSetup> DebugHeroes { get; } =
    [
        new HeroSetup("Aria", GameRules.HeroBaseStats with { Luck = 1 }, [.. GameRules.StarterKit, "bomb"], Portrait: "ember", Appearance: new("princess", Hair: "ginger")),
        new HeroSetup("Bran", GameRules.HeroBaseStats with { Hearts = 5 }, [.. GameRules.StarterKit, "boomerang"], Portrait: "woad", Appearance: new("knight", Trim: "blood")),
    ];

    /// <summary>The debug heroes, then plain heroes up to <paramref name="size"/> (for --autoplay --party N).</summary>
    public static IReadOnlyList<HeroSetup> DebugParty(int size)
    {
        string[] names = ["Cael", "Dara", "Edda", "Finn", "Gwen", "Hugo"];
        string[] portraits = ["moss", "gilt", "violet", "bone", "blood", "frost"];
        string[] looks = ["druid", "dwarf", "mage", "lizard_man", "raider", "cat_lady"];
        return [.. DebugHeroes.Take(size), .. names.Take(size - DebugHeroes.Count).Select((name, i) => new HeroSetup(name, GameRules.HeroBaseStats, GameRules.StarterKit, Portrait: portraits[i], Appearance: new(looks[i])))];
    }

    /// <summary>
    /// Three empty rooms for --floortest: Aria starts in the east doorway of "a", which leads to "b",
    /// whose stairs (one step from the doorway) go down to floor 2's arrival room "c".
    /// </summary>
    public static RunSetup DoorsAndStairs(ulong seed)
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        RoomTemplate Room(string text) => RoomTemplate.Parse(text, content);
        return new RunSetup
        {
            Seed = seed,
            ThemeId = "emerald_keep",
            Heroes = DebugHeroes,
            Rooms =
            [
                new RoomSetup("a", Room(".........\n.........\n.........\n.........\n......2.1\n.........\n.........\n.........\n........."),
                    new Dictionary<Woad.Core.Grid.Direction, string> { [Woad.Core.Grid.Direction.East] = "b" }, MapPosition: new(1, 2)),
                new RoomSetup("b", Room(".........\n.........\n.........\n.........\n.>.......\n.........\n.........\n.........\n........."),
                    new Dictionary<Woad.Core.Grid.Direction, string> { [Woad.Core.Grid.Direction.West] = "a" }, MapPosition: new(2, 2), Role: RoomRole.Stairs),
                new RoomSetup("c", Room(".........\n.........\n.........\n..1...2..\n.........\n..3...4..\n.........\n.........\n........."),
                    Floor: 2, MapPosition: new(2, 2), Role: RoomRole.Arrival),
            ],
            StartRoomId = "a",
        };
    }

    /// <summary>
    /// For --resultstest: one empty room with an open exit two steps from seats 1 and 2, so a run can be
    /// finished in a few clicks.
    /// </summary>
    public static RunSetup ExitRoom(IReadOnlyList<HeroSetup> heroes, ulong seed) => new()
    {
        Seed = seed,
        ThemeId = "emerald_keep",
        Heroes = heroes,
        Rooms = [new RoomSetup("exit_hall", RoomTemplate.Parse(
            ".........\n.........\n.........\n....*....\n...1.2...\n.........\n.........\n.........\n.........",
            ContentCatalog.LoadDefault()), Role: RoomRole.Boss)],
        StartRoomId = "exit_hall",
    };

    /// <summary>
    /// For --hazardtest: Aria above a slimeling at the edge of a row of pits, with a room under them on
    /// floor 2, so a shove drops the slimeling and a leap crosses (or drops Aria).
    /// </summary>
    public static RunSetup Hazards(ulong seed)
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        RoomTemplate Room(string text) => RoomTemplate.Parse(text, content);
        return new RunSetup
        {
            Seed = seed,
            ThemeId = "emerald_keep",
            Heroes = [DebugHeroes[0] with { BaseStats = DebugHeroes[0].BaseStats with { Hearts = 9 } }],
            Rooms =
            [
                new RoomSetup("ledge", Room(".........\n.........\n.........\n.~~~~~~~.\n....l....\n....1....\n.........\n.........\n........."), MapPosition: new(2, 2)),
                new RoomSetup("below", Room(".........\n.........\n.........\n.........\n.........\n.........\n.........\n.........\n........."),
                    Floor: 2, MapPosition: new(2, 2), Role: RoomRole.Arrival),
            ],
            StartRoomId = "ledge",
        };
    }

    /// <summary>
    /// For --keeltest: Aria and Bran (with a single heart) in an empty room, a hidden spike trap
    /// between them, so Bran keels over on it right beside Aria.
    /// </summary>
    public static RunSetup Keeling(ulong seed)
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        var spikes = new Dictionary<Woad.Core.Grid.GridPosition, TrapSetup> { [new(5, 4)] = new(Woad.Core.Model.TrapKind.Spikes, 9) };
        return new RunSetup
        {
            Seed = seed,
            ThemeId = "emerald_keep",
            Heroes =
            [
                DebugHeroes[0] with { BaseStats = DebugHeroes[0].BaseStats with { Hearts = 9 } },
                DebugHeroes[1] with { BaseStats = DebugHeroes[1].BaseStats with { Hearts = 1 } },
            ],
            Rooms = [new RoomSetup("crypt", RoomTemplate.Parse(".........\n.........\n.........\n.........\n....1.2..\n.........\n.........\n.........\n.........", content),
                Features: new RoomFeatures { Traps = spikes })],
            StartRoomId = "crypt",
        };
    }

    /// <summary>
    /// For --firetest: a tough Aria hemmed in by three Cinder Imps, whose fire attacks set her alight
    /// sooner or later, with shallow water a few steps away.
    /// </summary>
    public static RunSetup Fire(ulong seed)
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        return new RunSetup
        {
            Seed = seed,
            ThemeId = "emerald_keep",
            Heroes = [DebugHeroes[0] with { BaseStats = DebugHeroes[0].BaseStats with { Hearts = 40, DefenseDice = 3 } }],
            Rooms = [new RoomSetup("forge", RoomTemplate.Parse(".........\n.........\n.........\n...i.i...\n....1....\n....i....\n.........\n.........\n,,,,,,,,,", content))],
            StartRoomId = "forge",
        };
    }

    /// <summary>
    /// For --cameratest: a 15x13 hall, bigger than the view, with Aria in one corner and Bran in the
    /// opposite one, so the view has to scroll between them.
    /// </summary>
    public static RunSetup BigHall(ulong seed)
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        string[] rows = Enumerable.Repeat(new string('.', 15), 13).ToArray();
        rows[1] = ".1" + new string('.', 13);
        rows[11] = new string('.', 13) + "2.";
        return new RunSetup
        {
            Seed = seed,
            ThemeId = "emerald_keep",
            Heroes = DebugHeroes,
            Rooms = [new RoomSetup("hall", RoomTemplate.Parse(string.Join('\n', rows), content))],
            StartRoomId = "hall",
        };
    }

    /// <summary>
    /// For --bombtest: a tough Aria with a bomb, three slimes in a huddle a few tiles away, and an
    /// unlit bomb lying beside her.
    /// </summary>
    public static RunSetup Bombs(ulong seed)
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        return new RunSetup
        {
            Seed = seed,
            ThemeId = "emerald_keep",
            Heroes = [DebugHeroes[0] with { BaseStats = DebugHeroes[0].BaseStats with { Hearts = 30 }, CardIds = ["bomb"] }],
            Rooms = [new RoomSetup("armory", RoomTemplate.Parse("....s....\n...s.s...\n.........\n.........\n.........\n.........\n....1....\n.........\n.........", content))],
            StartRoomId = "armory",
        };
    }

    /// <summary>For --rumbletest: a tough Aria with a slime right beside her.</summary>
    public static RunSetup Duel(ulong seed) => new()
    {
        Seed = seed,
        ThemeId = "emerald_keep",
        Heroes = [DebugHeroes[0] with { BaseStats = DebugHeroes[0].BaseStats with { Hearts = 20 } }],
        Rooms = [new RoomSetup("yard", RoomTemplate.Parse(".........\n.........\n.........\n.........\n....1s...\n.........\n.........\n.........\n.........", ContentCatalog.LoadDefault()))],
        StartRoomId = "yard",
    };

    /// <summary>For --clicktest: a quick Aria (3 Wend dice) across the yard from a slime.</summary>
    public static RunSetup Stalk(ulong seed) => new()
    {
        Seed = seed,
        ThemeId = "emerald_keep",
        Heroes = [DebugHeroes[0] with { BaseStats = DebugHeroes[0].BaseStats with { Hearts = 20, MoveDice = 3 } }],
        Rooms = [new RoomSetup("yard", RoomTemplate.Parse(".........\n.........\n.........\n.........\n.1....s..\n.........\n.........\n.........\n.........", ContentCatalog.LoadDefault()))],
        StartRoomId = "yard",
    };

    /// <summary>The debug party in the single hand-made room, for --uitest.</summary>
    public static RunSetup SingleRoom(ulong seed) => new()
    {
        Seed = seed,
        ThemeId = "emerald_keep",
        Heroes = DebugHeroes,
        Rooms = [new RoomSetup("entry_hall", RoomTemplate.Parse(EntryHall, ContentCatalog.LoadDefault()))],
        StartRoomId = "entry_hall",
    };
}
