using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class SetupAndTurnFlowTests
{
    private const string EmptyRoom = """
        .........
        .........
        .........
        .........
        .........
        .........
        .........
        .........
        .........
        """;

    private const string TwoHeroes = """
        .........
        .........
        .........
        .........
        ...1.2...
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void HeroesStartOnTheirMarksWithFullHeartsAndTheirCards()
    {
        GameSession session = TestRun.Start(TwoHeroes, heroes: 2, cards: ["sword", "cap"]);

        Hero first = session.HeroInSeat(0);
        Assert.Equal(new GridPosition(3, 4), first.Position);
        Assert.Equal(new GridPosition(5, 4), session.HeroInSeat(1).Position);
        Assert.Equal(5, first.Hearts);
        Assert.Equal("A", first.RoomId);
        Assert.True(session.RoomById().Explored);
        Assert.All(first.AllCards, card => Assert.Equal(first.Id, card.BroughtInByHeroId));
    }

    [Fact]
    public void HeroesWithoutMarksStartNearTheCenter()
    {
        GameSession session = TestRun.Start(EmptyRoom, heroes: 2);

        Assert.Equal(new GridPosition(4, 4), session.HeroInSeat(0).Position);
        Assert.Equal(1, session.HeroInSeat(1).Position.ManhattanDistanceTo(new GridPosition(4, 4)));
    }

    [Fact]
    public void SetupRejectsBadRuns()
    {
        RoomSetup room = new("A", TestRun.Room(EmptyRoom));
        RoomSetup oneWay = new("A", TestRun.Room(EmptyRoom), new Dictionary<Direction, string> { [Direction.North] = "B" });
        RoomSetup noReturn = new("B", TestRun.Room(EmptyRoom));

        Assert.Throws<ArgumentException>(() => TestRun.Create([room], heroes: 0));
        Assert.Throws<ArgumentException>(() => TestRun.Create([room], heroes: GameRules.MaxHeroes + 1));
        Assert.Throws<ArgumentException>(() => TestRun.Create([oneWay, noReturn]));
        Assert.Throws<ArgumentException>(() => TestRun.Create([room], cards: Enumerable.Repeat("coin", 7).ToArray()));
    }

    [Fact]
    public void BeginStartsRoundOneWithTheFirstSeat()
    {
        GameSession session = TestRun.Create([new RoomSetup("A", TestRun.Room(TwoHeroes))], heroes: 2);

        IReadOnlyList<GameEvent> events = session.Begin();

        Assert.Equal<GameEvent>([new RoundStarted(1), new TurnStarted(session.HeroInSeat(0).Id)], events);
        Assert.Equal(RunPhase.InProgress, session.State.Phase);
        Assert.Same(session.HeroInSeat(0), session.State.ActiveHero);
        Assert.Throws<InvalidOperationException>(() => session.Begin());
    }

    [Fact]
    public void CommandsBeforeTheRunBeginsAreRefused()
    {
        GameSession session = TestRun.Create([new RoomSetup("A", TestRun.Room(EmptyRoom))]);

        Assert.Contains("isn't in progress", session.Refuse(new EndTurn(session.HeroInSeat(0).Id)));
    }

    [Fact]
    public void OnlyTheActiveHeroCanAct()
    {
        GameSession session = TestRun.Start(TwoHeroes, heroes: 2);

        Assert.Contains("isn't that hero's turn", session.Refuse(new EndTurn(session.HeroInSeat(1).Id)));
    }

    [Fact]
    public void TurnsPassInSeatOrderThenMonstersActThenANewRoundBegins()
    {
        GameSession session = TestRun.Start(TwoHeroes, heroes: 2);
        Hero first = session.HeroInSeat(0);
        Hero second = session.HeroInSeat(1);

        Assert.Equal<GameEvent>([new TurnEnded(first.Id), new TurnStarted(second.Id)], session.Do(new EndTurn(first.Id)));

        Assert.Equal<GameEvent>(
            [new TurnEnded(second.Id), new EnemyPhaseStarted(1), new EnemyPhaseEnded(1), new RoundStarted(2), new TurnStarted(first.Id)],
            session.Do(new EndTurn(second.Id)));
    }

    [Fact]
    public void HeroesWhoAreOutOfTheRunAreSkipped()
    {
        GameSession session = TestRun.Start(EmptyRoom, heroes: 3);
        session.HeroInSeat(1).Status = HeroStatus.Downed;

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(session.HeroInSeat(0).Id));

        Assert.Equal(new TurnStarted(session.HeroInSeat(2).Id), events[^1]);
    }

    [Fact]
    public void RefusedCommandsChangeNothing()
    {
        GameSession session = TestRun.Start(TwoHeroes, new ScriptedDice(2), heroes: 2);
        Hero hero = session.HeroInSeat(0);
        session.Do(new RollMovement(hero.Id));

        session.Refuse(new MoveHero(hero.Id, new GridPosition(0, 0)));

        Assert.Equal(new GridPosition(3, 4), hero.Position);
        Assert.Equal(4, session.State.Turn!.MovementPoints);
        Assert.False(session.State.Turn.HasMoved);
    }

    [Fact]
    public void TheSameSeedReplaysTheSameRun()
    {
        string room = """
            b.......a
            .........
            .........
            .........
            ....1....
            .........
            .........
            .........
            m.......k
            """;

        Assert.Equal(PlayWithRealDice(room), PlayWithRealDice(room));
    }

    /// <summary>Rolls and moves toward the center for a few rounds with seeded dice, recording every event.</summary>
    private static List<string> PlayWithRealDice(string room)
    {
        var setup = new RunSetup
        {
            Seed = 7,
            ThemeId = TestContent.ThemeId,
            Heroes = [new HeroSetup("Hero", GameRules.HeroBaseStats, ["sword", "shield"])],
            Rooms = [new RoomSetup("A", TestRun.Room(room))],
            StartRoomId = "A",
        };
        var session = new GameSession(TestContent.Catalog, setup);
        var log = session.Begin().Select(Describe).ToList();

        for (int turn = 0; turn < 6 && session.State.Phase == RunPhase.InProgress; turn++)
        {
            Hero hero = session.State.ActiveHero!;
            log.AddRange(session.Do(new RollMovement(hero.Id)).Select(Describe));
            SettlePowerDice(session, hero, log);
            if (session.GetAttackableMonsters().FirstOrDefault() is { } target)
            {
                log.AddRange(session.Do(new AttackMonster(hero.Id, target.Id)).Select(Describe));
                SettlePowerDice(session, hero, log);
            }

            log.AddRange(session.Do(new EndTurn(hero.Id)).Select(Describe));
        }

        return log;
    }

    /// <summary>Adds every Power Die the sixes earned (each may earn another).</summary>
    private static void SettlePowerDice(GameSession session, Hero hero, List<string> log)
    {
        while (session.State.Turn?.UnsettledPowerDice > 0)
        {
            log.AddRange(session.Do(new AddPowerDie(hero.Id)).Select(Describe));
        }
    }

    private static string Describe(GameEvent gameEvent) =>
        System.Text.Json.JsonSerializer.Serialize(gameEvent, gameEvent.GetType());
}
