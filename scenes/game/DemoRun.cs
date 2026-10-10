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
        new HeroSetup("Aria", GameRules.HeroBaseStats with { Luck = 1 }, [.. GameRules.StarterKit, "bomb"], Portrait: "ember"),
        new HeroSetup("Bran", GameRules.HeroBaseStats with { Hearts = 5 }, [.. GameRules.StarterKit, "boomerang"], Portrait: "woad"),
    ];

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
