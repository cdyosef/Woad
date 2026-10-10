using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Rules;

namespace Woad.Scenes.Game;

/// <summary>
/// The fixed room and two heroes used to play-test M2, until character creation (M3) and
/// dungeon generation (M4) replace them.
/// </summary>
public static class DemoRun
{
    // C chest, # wall, ~ pit; s slime, b bat, a archer guard, k skeleton; 1-2 hero starts.
    private const string EntryHall = """
        C...a....
        .s.....b.
        .........
        ..#~~~#..
        ....k....
        .........
        .~.....~.
        ........C
        ...1.2...
        """;

    public static GameSession Create(ulong seed)
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        var setup = new RunSetup
        {
            Seed = seed,
            ThemeId = "emerald_keep",
            Heroes =
            [
                new HeroSetup("Aria", GameRules.HeroBaseStats with { Luck = 1 }, [.. GameRules.StarterKit, "bomb"]),
                new HeroSetup("Bran", GameRules.HeroBaseStats with { Hearts = 5 }, [.. GameRules.StarterKit, "boomerang"]),
            ],
            Rooms = [new RoomSetup("entry_hall", RoomTemplate.Parse(EntryHall, content))],
            StartRoomId = "entry_hall",
        };

        return new GameSession(content, setup);
    }
}
