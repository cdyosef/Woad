using Woad.Core.Characters;
using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>The Party menu's rules in a run: levelling up mid-dungeon, and companions' policies.</summary>
public class PartyTests
{
    private const string Open = """
        .........
        .........
        .........
        .........
        ....12...
        .........
        .........
        .........
        .........
        """;

    /// <summary>A player (5 XP short of level 2) and their companion.</summary>
    private static GameSession Start()
    {
        var setup = new RunSetup
        {
            Seed = TestRun.Seed,
            ThemeId = TestContent.ThemeId,
            Heroes =
            [
                new HeroSetup("Hero", TestRun.HeroStats, [], Allocation: StatAllocation.None, Xp: 5),
                new HeroSetup("Pal", TestRun.HeroStats, [], OwnerSeat: 0, Allocation: StatAllocation.None),
            ],
            Rooms = [new RoomSetup("A", TestRun.Room(Open))],
            StartRoomId = "A",
        };
        var session = new GameSession(TestContent.Catalog, setup);
        session.Begin();
        return session;
    }

    [Fact]
    public void ALevelGainedInTheDungeonCanBeSpentThere()
    {
        GameSession session = Start();
        Hero hero = session.HeroInSeat();
        Assert.Contains("no points", session.CheckSpendPoint(hero.Id, StatKind.Hearts));

        hero.Xp = 5;
        Assert.Equal((2, 1), (hero.Level, hero.PointsToSpend));
        Assert.Contains("costs 3", session.CheckSpendPoint(hero.Id, StatKind.AttackDice));

        int hearts = hero.Hearts;
        IReadOnlyList<GameEvent> events = session.Do(new SpendPoint(hero.Id, hero.Id, StatKind.Hearts));

        Assert.Equal(new PointSpent(hero.Id, StatKind.Hearts, 2, 0), events.SingleOf<PointSpent>());
        Assert.Equal(hearts + 1, hero.Hearts);
        Assert.Equal(TestRun.HeroStats.Hearts + 1, hero.MaxHearts);
        Assert.Equal(1, RunResultsFor(session, hero).Allocation!.Hearts);
    }

    [Fact]
    public void OnlyYourOwnCompanionsPolicyIsYoursToSet()
    {
        GameSession session = Start();
        Hero hero = session.HeroInSeat();
        Hero pal = session.HeroInSeat(1);
        var policy = new CompanionPolicy { DrinksPotions = false, PicksUpLoot = true };

        session.Do(new SetCompanionPolicy(hero.Id, pal.Id, policy));

        Assert.Equal(policy, pal.Policy);
        Assert.Contains("not one of your companions", session.CheckSetPolicy(hero.Id));
    }

    [Fact]
    public void PointsSpentInARunAreKeptByTheCharacter()
    {
        Character character = Character.Create("Wren", "frost", StatAllocation.None, Woad.Core.Content.ContentCatalog.LoadDefault(), DateTimeOffset.UtcNow);
        int unspent = character.UnspentPoints;
        var result = new HeroRunResult
        {
            HeroId = 1,
            CharacterId = character.Id,
            Name = character.Name,
            Seat = 0,
            Portrait = "frost",
            Status = HeroStatus.Escaped,
            Won = true,
            Kills = 0,
            Xp = 10,
            Gold = 0,
            DeepestFloor = 1,
            Equipped = [],
            Belt = [],
            Backpack = [],
            Allocation = new StatAllocation { Hearts = 1 },
            Policy = new CompanionPolicy { PicksUpLoot = true },
        };

        character.ApplyRunResult(result);

        Assert.Equal(1, character.Allocation.Hearts);
        Assert.Equal(unspent + 1 - 1, character.UnspentPoints);
        Assert.True(character.Policy.PicksUpLoot);
    }

    private static HeroRunResult RunResultsFor(GameSession session, Hero hero)
    {
        session.State.Phase = RunPhase.Ended;
        return RunResults.For(session.State).Single(result => result.HeroId == hero.Id);
    }
}
