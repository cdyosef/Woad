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

    /// <summary>For --spelltest: Aria with a Flame Scroll on her belt and a Frost Wand in her off hand, two slimes across the room.</summary>
    public static RunSetup Spells(ulong seed) => new()
    {
        Seed = seed,
        ThemeId = "emerald_keep",
        Heroes = [DebugHeroes[0] with { BaseStats = DebugHeroes[0].BaseStats with { Hearts = 20 }, CardIds = ["wooden_sword", "frost_wand", "flame_scroll"] }],
        Rooms = [new RoomSetup("study", RoomTemplate.Parse(".........\n.........\n.........\n...s.....\n.1.......\n...s.....\n.........\n.........\n.........", ContentCatalog.LoadDefault()))],
        StartRoomId = "study",
    };

    /// <summary>
    /// For --dietypetest: a tough Aria with eight scrolls of typed dice (two on her belt, six in her
    /// pack) among an ogre, a skeleton, a scorpion (poison) and a necromancer (necro).
    /// </summary>
    public static RunSetup Elements(ulong seed) => new()
    {
        Seed = seed,
        ThemeId = "emerald_keep",
        Heroes = [DebugHeroes[0] with
        {
            BaseStats = DebugHeroes[0].BaseStats with { Hearts = 40, DefenseDice = 3 },
            CardIds = ["frost_scroll", "spark_scroll", "venom_scroll", "gale_scroll", "quake_scroll", "bloom_scroll", "mind_scroll", "shadow_scroll"],
        }],
        Rooms = [new RoomSetup("lab", RoomTemplate.Parse(".........\n.........\n...o.z...\n.........\n....1....\n.........\n...k..n..\n.........\n.........", ContentCatalog.LoadDefault()))],
        StartRoomId = "lab",
    };

    /// <summary>For --loottest: Aria with a stack of lockpicks, an emerald and a bomb, beside a chest.</summary>
    public static RunSetup Treasury(ulong seed) => new()
    {
        Seed = seed,
        ThemeId = "emerald_keep",
        Heroes = [DebugHeroes[0] with { CardIds = ["lockpicks", "lockpicks", "lockpicks", "emerald", "bomb"] }],
        Rooms = [new RoomSetup("vault", RoomTemplate.Parse(".........\n.........\n.........\n....C....\n....1....\n.........\n.........\n.........\n.........", ContentCatalog.LoadDefault()))],
        StartRoomId = "vault",
    };

    /// <summary>
    /// For --monstertest: a tough Aria beside a chest that's really a Mimic, with an Ogre, a
    /// Necromancer, a Hexer and a Gelatinous Cube in the corners.
    /// </summary>
    public static RunSetup Menagerie(ulong seed) => new()
    {
        Seed = seed,
        ThemeId = "emerald_keep",
        Heroes = [DebugHeroes[0] with { BaseStats = DebugHeroes[0].BaseStats with { Hearts = 30, Luck = 2 } }],
        Rooms =
        [
            new RoomSetup(
                "den",
                RoomTemplate.Parse("n.......h\n.........\n.........\n.........\n....1C...\n.........\n.........\n.........\no.......u", ContentCatalog.LoadDefault()),
                Features: new RoomFeatures { Mimics = new Dictionary<Woad.Core.Grid.GridPosition, int> { [new(5, 4)] = 20 } }),
        ],
        StartRoomId = "den",
    };

    /// <summary>
    /// For --dentest: a tough Aria in the doorway of a hall that leads north into the Ogre Champ's
    /// den, with a room under the den for its pits to drop into.
    /// </summary>
    public static RunSetup Den(ulong seed)
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        RoomTemplate den = DungeonGenerator.DenLayout(content.GetMonster("ogre_champ"), Woad.Core.Grid.Direction.South);
        Woad.Core.Grid.GridPosition chest = den.TilesOf(Woad.Core.Grid.TileKind.Chest).Single();
        return new RunSetup
        {
            Seed = seed,
            ThemeId = "emerald_keep",
            Heroes = [DebugHeroes[0] with { BaseStats = DebugHeroes[0].BaseStats with { Hearts = 30 } }],
            Rooms =
            [
                new RoomSetup("hall", RoomTemplate.Parse("....1....\n.........\n.........\n.........\n.........\n.........\n.........\n.........\n.........", content),
                    new Dictionary<Woad.Core.Grid.Direction, string> { [Woad.Core.Grid.Direction.North] = "den" }, MapPosition: new(2, 3)),
                new RoomSetup("den", den,
                    new Dictionary<Woad.Core.Grid.Direction, string> { [Woad.Core.Grid.Direction.South] = "hall" }, MapPosition: new(2, 2), Role: RoomRole.Den,
                    Features: new RoomFeatures
                    {
                        LockedChests = new Dictionary<Woad.Core.Grid.GridPosition, int> { [chest] = GameRules.GrandChestLockDifficulty(1) },
                        GrandChests = [chest],
                    }),
                new RoomSetup("cellar", RoomTemplate.Parse(".........\n.........\n.........\n.........\n.........\n.........\n.........\n.........\n.........", content),
                    Floor: 2, MapPosition: new(2, 2)),
            ],
            StartRoomId = "hall",
        };
    }

    /// <summary>For --merchanttest: Aria on a key, beside Silas, a merchant chained to the wall, and a slime across the room.</summary>
    public static RunSetup Market(ulong seed) => new()
    {
        Seed = seed,
        ThemeId = "emerald_keep",
        Heroes = [DebugHeroes[0] with { BaseStats = DebugHeroes[0].BaseStats with { Hearts = 20 } }],
        Rooms =
        [
            new RoomSetup("market", RoomTemplate.Parse(".........\n.........\n.........\n.........\n.1.......\n.........\n.........\n.........\n.......s.", ContentCatalog.LoadDefault()),
                Features: new RoomFeatures
                {
                    Merchant = new MerchantSetup(new Woad.Core.Grid.GridPosition(0, 4), 6, "Silas"),
                    Keys = new Dictionary<Woad.Core.Grid.GridPosition, int> { [new(1, 4)] = 1 },
                }),
        ],
        StartRoomId = "market",
        MerchantGuildLevel = 3,
    };

    /// <summary>For --tradetest: Aria with two potions and her companion Pip beside her, a Knight's Sword in its pack.</summary>
    public static RunSetup Camp(ulong seed) => new()
    {
        Seed = seed,
        ThemeId = "emerald_keep",
        Heroes =
        [
            DebugHeroes[0] with { CardIds = ["health_potion", "health_potion", "wooden_sword"] },
            new HeroSetup("Pip", GameRules.HeroBaseStats, ["wooden_sword", "knights_sword"], OwnerSeat: 0),
        ],
        Rooms = [new RoomSetup("camp", RoomTemplate.Parse(".........\n.........\n.........\n.........\n....1....\n.....2...\n.........\n.........\n.........", ContentCatalog.LoadDefault()))],
        StartRoomId = "camp",
    };

    /// <summary>For --partytest: Aria (level 2, a point to spend) and her companion Pip, a slime across the room.</summary>
    public static RunSetup PartyCheck(ulong seed) => Escort(seed) with
    {
        Heroes =
        [
            DebugHeroes[0] with { BaseStats = DebugHeroes[0].BaseStats with { Hearts = 20 }, Allocation = Woad.Core.Characters.StatAllocation.None, Xp = 10, UnspentPoints = 1 },
            new HeroSetup("Pip", GameRules.HeroBaseStats with { Hearts = 20, Luck = 2 }, ["wooden_sword"], OwnerSeat: 0),
        ],
    };

    /// <summary>For --bosstest: Aria and her companion Pip, starting in a generated Emerald Keep's boss room.</summary>
    public static RunSetup BossRoom(ulong seed)
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        RunSetup run = Woad.Core.Dungeon.DungeonGenerator.CreateRun(content, "emerald_keep",
        [
            DebugHeroes[0] with { BaseStats = DebugHeroes[0].BaseStats with { Hearts = 60 } },
            new HeroSetup("Pip", GameRules.HeroBaseStats with { Hearts = 60, Luck = 2 }, ["wooden_sword"], OwnerSeat: 0),
        ], seed);
        return run with { StartRoomId = run.Rooms.Single(room => room.Role == RoomRole.Boss).Id };
    }

    /// <summary>For --companiontest: Aria and her lucky companion Pip, a slime across the room.</summary>
    public static RunSetup Escort(ulong seed) => new()
    {
        Seed = seed,
        ThemeId = "emerald_keep",
        Heroes =
        [
            DebugHeroes[0] with { BaseStats = DebugHeroes[0].BaseStats with { Hearts = 20 } },
            new HeroSetup("Pip", GameRules.HeroBaseStats with { Hearts = 20, Luck = 2 }, ["wooden_sword"], OwnerSeat: 0),
        ],
        Rooms = [new RoomSetup("yard", RoomTemplate.Parse(".........\n.........\n.........\n.........\n.1.....s.\n.2.......\n.........\n.........\n.........", ContentCatalog.LoadDefault()))],
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
