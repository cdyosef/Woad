using Woad.Core.Characters;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class RunResultsTests
{
    private const string ExitBeside = """
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

    [Fact]
    public void EscapedHeroesKeepTheirGearAndTurnTreasureIntoGold()
    {
        GameSession session = TestRun.Start(ExitBeside, new ScriptedDice(3), cards: ["sword", "potion", "coin", "shield"]);
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));
        session.Do(new MoveHero(hero.Id, new GridPosition(4, 4)));
        session.Do(new Escape(hero.Id));

        HeroRunResult result = Assert.Single(RunResults.For(session.State));

        Assert.Equal(HeroStatus.Escaped, result.Status);
        Assert.True(result.Won);
        Assert.Equal(5, result.Gold);
        Assert.Equal(GameRules.XpForEscaping, result.Xp);
        Assert.Equal(["sword", "shield"], result.Equipped);
        Assert.Equal(["potion"], result.Belt);
        Assert.Empty(result.Backpack);
    }

    [Fact]
    public void HeroesWhoDieKeepOnlyWhatTheyBroughtIn()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ....m....
            ....1....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 1, 1, 3), cards: ["sword"]);
        Hero hero = session.HeroInSeat();
        hero.AddToBackpack(new CardInstance(session.State.NextId(), TestContent.Catalog.GetCard("big_sword"), broughtInByHeroId: null));
        hero.Hearts = 1;
        session.EndRound();
        hero.DeathMarks = 2;
        session.Do(new RollKeel(hero.Id));

        HeroRunResult result = Assert.Single(RunResults.For(session.State));

        Assert.Equal(HeroStatus.Dead, result.Status);
        Assert.False(result.Won);
        Assert.Equal(0, result.Gold);
        Assert.Equal(["sword"], result.Equipped);
        Assert.Empty(result.Backpack);
    }

    [Fact]
    public void ResultsWaitForTheRunToEnd()
    {
        GameSession session = TestRun.Start(ExitBeside);

        Assert.Throws<InvalidOperationException>(() => RunResults.For(session.State));
    }

    [Fact]
    public void CharactersRecordTheirRuns()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        Character character = Character.Create("Morgan", "moss", StatAllocation.None, content, DateTimeOffset.UnixEpoch);
        var result = new HeroRunResult
        {
            HeroId = 1,
            CharacterId = character.Id,
            Name = "Morgan",
            Seat = 0,
            Portrait = "moss",
            Status = HeroStatus.Escaped,
            Won = true,
            Kills = 9,
            Xp = 34,
            Gold = 27,
            DeepestFloor = 3,
            Equipped = ["knights_sword", "buckler"],
            Belt = ["bomb"],
            Backpack = ["wooden_sword"],
        };

        RunReward reward = character.ApplyRunResult(result);

        Assert.Equal(3, character.Level);
        Assert.Equal(2, reward.LevelsGained);
        Assert.Equal(6 + 2, character.UnspentPoints);
        Assert.Equal(27, character.Gold);
        Assert.Equal((1, 1), (character.RunsPlayed, character.Wins));
        Assert.Equal(["knights_sword", "buckler"], character.Equipped);
        Assert.Equal(["bomb"], character.Belt);
        Assert.Equal(["wooden_sword"], character.Backpack);
        Assert.Equal(["knights_sword", "bomb"], reward.CardsGained);
        Assert.Equal(["health_potion"], reward.CardsLost);
    }

    [Fact]
    public void ResultsOnlyApplyToTheirOwnCharacter()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        Character character = Character.Create("Morgan", "moss", StatAllocation.None, content, DateTimeOffset.UnixEpoch);
        var stranger = new HeroRunResult
        {
            HeroId = 1, CharacterId = Guid.NewGuid(), Name = "Wren", Seat = 0, Portrait = "", Status = HeroStatus.Dead, Won = false,
            Kills = 0, Xp = 0, Gold = 0, DeepestFloor = 1, Equipped = [], Belt = [], Backpack = [],
        };

        Assert.Throws<ArgumentException>(() => character.ApplyRunResult(stranger));
    }
}
