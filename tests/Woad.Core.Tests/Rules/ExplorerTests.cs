using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>
/// What lies about: the first Search in a room picks up a Rock or a Stick (both on a 4) and earns an
/// Explorer bonus; more lie hidden on the tiles; whoever explored most on a run won takes the Explorer Award.
/// </summary>
public sealed class ExplorerTests
{
    private const string Open = """
        .........
        .........
        .........
        .........
        ...1*....
        .........
        .........
        .........
        .........
        """;

    /// <param name="finds">Scripts what lies about: how many hide on tiles (and where, and what), then the first Search's pick.</param>
    private static GameSession Run(ScriptedDice dice, ScriptedDice finds, int heroes = 1)
    {
        GameSession session = TestRun.Create([new RoomSetup("A", TestRun.Room(Open))], dice, heroes, themeId: TestContent.FindsThemeId, finds: finds);
        session.Begin();
        return session;
    }

    [Fact]
    public void TheFirstSearchInARoomPicksUpSomethingLyingAboutAndEarnsAnExplorerBonus()
    {
        // Nothing hidden; the search finds a Stick (the second of the two).
        GameSession session = Run(new ScriptedDice(2, 2), new ScriptedDice(0, 1));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new Search(hero.Id));

        Assert.Equal(["T-Stick"], events.SingleOf<Explored>().Found);
        Assert.Equal(GameRules.ExplorerXp, events.SingleOf<XpGained>().Amount);
        Assert.Equal(1, hero.ExplorerBonus);
        Assert.Equal(1, hero.Card("t_stick").Count);

        // Only the first: the room's been searched.
        session.Do(new EndTurn(hero.Id));
        Assert.Empty(session.Do(new Search(hero.Id)).OfType<Explored>());
        Assert.Equal(1, hero.ExplorerBonus);
    }

    [Fact]
    public void AFirstSearchOfFourPicksUpBoth()
    {
        GameSession session = Run(new ScriptedDice(4), new ScriptedDice(0));
        Hero hero = session.HeroInSeat();

        Assert.Equal(["T-Rock", "T-Stick"], session.Do(new Search(hero.Id)).SingleOf<Explored>().Found);
        Assert.NotNull(hero.Card("t_rock"));
        Assert.NotNull(hero.Card("t_stick"));
    }

    [Fact]
    public void MoreLiesHiddenOnTheTilesForASearchToFind()
    {
        // One hidden, on the first open tile, a Rock; the search's own pick is a Rock too.
        GameSession session = Run(new ScriptedDice(3), new ScriptedDice(1, 0, 0, 0));
        Hero hero = session.HeroInSeat();
        Room room = session.RoomById();
        (GridPosition tile, Cache cache) = Assert.Single(room.Caches);
        Assert.Equal(["t_rock"], cache.Finds);
        Assert.Equal(GameRules.FindDifficulty(room.Floor), cache.Difficulty);

        IReadOnlyList<GameEvent> events = session.Do(new Search(hero.Id));

        Assert.Equal([tile], events.SingleOf<SecretsFound>().Stashes);
        Assert.Single(room.LootAt(tile), card => card.Definition.Id == "t_rock");
    }

    [Fact]
    public void WhoeverExploredMostOnARunThatGotOutTakesTheExplorerAward()
    {
        GameSession session = Run(new ScriptedDice(2, 3), new ScriptedDice(0, 0));
        Hero hero = session.HeroInSeat();
        session.Do(new Search(hero.Id));
        session.Do(new EndTurn(hero.Id));
        session.Do(new RollMovement(hero.Id));
        session.Do(new MoveHero(hero.Id, new GridPosition(4, 4)));
        session.Do(new Escape(hero.Id));

        HeroRunResult result = Assert.Single(RunResults.For(session.State));

        Assert.True(result.ExplorerAward);
        Assert.Equal(1, result.ExplorerBonus);
        Assert.Equal(GameRules.ExplorerXp + GameRules.XpForEscaping + GameRules.ExplorerAwardXp, result.Xp);
    }
}
