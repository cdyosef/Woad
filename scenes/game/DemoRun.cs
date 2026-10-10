using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Rules;

namespace Woad.Scenes.Game;

/// <summary>
/// The single hand-made room every run uses until dungeon generation (M4), plus the two fixed
/// heroes the debug modes play with.
/// </summary>
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

    public static RunSetup CreateSetup(IReadOnlyList<HeroSetup> heroes, ulong seed) => new()
    {
        Seed = seed,
        ThemeId = "emerald_keep",
        Heroes = heroes,
        Rooms = [new RoomSetup("entry_hall", RoomTemplate.Parse(EntryHall, ContentCatalog.LoadDefault()))],
        StartRoomId = "entry_hall",
    };

    /// <summary>Aria (with a bomb and Luck) and Bran (with a boomerang), for --uitest and --autoplay.</summary>
    public static RunSetup DebugSetup(ulong seed) => CreateSetup(
    [
        new HeroSetup("Aria", GameRules.HeroBaseStats with { Luck = 1 }, [.. GameRules.StarterKit, "bomb"], Portrait: "ember"),
        new HeroSetup("Bran", GameRules.HeroBaseStats with { Hearts = 5 }, [.. GameRules.StarterKit, "boomerang"], Portrait: "woad"),
    ], seed);
}
