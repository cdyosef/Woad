using Woad.Core.Bots;
using Woad.Core.Characters;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class CompanionTests
{
    /// <summary>A player (seat 0) and their companion (seat 1) side by side.</summary>
    private const string Pair = """
        .........
        .........
        .........
        .........
        ...12....
        .........
        .........
        .........
        .........
        """;

    private static readonly int?[] PlayerAndCompanion = [null, 0];

    private static ScriptedDice Fives() => new([.. Enumerable.Repeat(5, 60)]);

    [Fact]
    public void ACompanionBelongsToThePlayerSeatedBeforeIt()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        Character Make(string name, bool companion) =>
            Character.Create(name, "moss", StatAllocation.None, content, DateTimeOffset.UnixEpoch, companion);

        IReadOnlyList<HeroSetup> heroes = Party.ToHeroSetups(
            [Make("Wren", false), Make("Pip", true), Make("Morgan", false), Make("Tad", true), Make("Bea", true)]);

        Assert.Equal([null, 0, null, 2, 2], heroes.Select(hero => hero.OwnerSeat));
        Assert.Throws<ArgumentException>(() => Party.ToHeroSetups([Make("Pip", true), Make("Wren", false)]));
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(1, 1, 2)]
    [InlineData(1, 3, 3)]
    [InlineData(2, 2, 3)]
    [InlineData(1, 7, 5)]
    public void CompanionsCountAsHalfAHeroWhenSizingMonsters(int players, int companions, int strength) =>
        Assert.Equal(strength, GameRules.PartyStrength(players, companions));

    [Fact]
    public void SetupLinksCompanionsToTheirOwnersAndRejectsOrphans()
    {
        GameSession session = TestRun.Start(Pair, heroes: 2, owners: PlayerAndCompanion);

        Assert.False(session.HeroInSeat(0).IsCompanion);
        Assert.Equal(session.HeroInSeat(0).Id, session.HeroInSeat(1).OwnerId);
        Assert.Equal(CompanionOrder.Follow, session.HeroInSeat(1).Order);

        // Players are numbered among themselves: the companion between them doesn't count.
        GameSession three = TestRun.Start(Pair, heroes: 3, owners: [null, 0, null]);
        Assert.Equal([1, 0, 2], three.State.Heroes.Select(hero => hero.PlayerNumber));

        RoomSetup room = new("A", TestRun.Room(Pair));
        Assert.Throws<ArgumentException>(() => TestRun.Create([room], heroes: 2, owners: [1, null]));
        Assert.Throws<ArgumentException>(() => TestRun.Create([room], heroes: 3, owners: [null, 0, 1]));
    }

    [Fact]
    public void OnlyTheOwnerGivesOrdersAndHoldKeepsTheSpot()
    {
        GameSession session = TestRun.Start(Pair, heroes: 3, owners: [null, 0, null]);
        Hero owner = session.HeroInSeat(0);
        Hero companion = session.HeroInSeat(1);

        var ordered = session.Do(new OrderCompanion(owner.Id, companion.Id, CompanionOrder.Hold)).SingleOf<CompanionOrdered>();

        Assert.Equal(new CompanionOrdered(owner.Id, companion.Id, CompanionOrder.Hold), ordered);
        Assert.Equal(("A", companion.Position), companion.HoldAt);
        Assert.Contains("isn't one of your companions", session.Refuse(new OrderCompanion(owner.Id, session.HeroInSeat(2).Id, CompanionOrder.Fight)));

        session.Do(new OrderCompanion(owner.Id, companion.Id, CompanionOrder.Fight));
        Assert.Null(companion.HoldAt);
    }

    [Fact]
    public void TheRunEndsWithTheLastPlayerAndCompanionsLeftBehindLoseWhatTheyFound()
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
            2........
            """, new ScriptedDice(5, 1, 1, 3), heroes: 2, owners: PlayerAndCompanion);
        Hero player = session.HeroInSeat(0);
        Hero companion = session.HeroInSeat(1);
        companion.AddToBackpack(new CardInstance(session.State.NextId(), TestContent.Catalog.GetCard("coin"), broughtInByHeroId: null));
        player.Hearts = 1;
        session.EndRound();
        Assert.Equal(RunPhase.InProgress, session.State.Phase);
        player.DeathMarks = 2;

        IReadOnlyList<GameEvent> events = session.Do(new RollKeel(player.Id));

        Assert.Equal(HeroStatus.Dead, player.Status);
        Assert.Equal(HeroStatus.Active, companion.Status);
        Assert.IsType<RunEnded>(events[^1]);
        HeroRunResult left = RunResults.For(session.State).Single(result => result.HeroId == companion.Id);
        Assert.Empty(left.Backpack);
        Assert.False(left.Won);
    }

    [Fact]
    public void CompanionsInTheRoomEscapeWithTheirOwner()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ...1*2...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(3), heroes: 2, owners: PlayerAndCompanion);
        Hero player = session.HeroInSeat(0);
        session.Do(new RollMovement(player.Id));
        session.Do(new MoveHero(player.Id, new GridPosition(4, 4)));

        IReadOnlyList<GameEvent> events = session.Do(new Escape(player.Id));

        Assert.Equal(2, events.OfType<HeroEscaped>().Count());
        Assert.Equal(HeroStatus.Escaped, session.HeroInSeat(1).Status);
        Assert.Equal([player.Id], session.State.WinnerIds);
    }

    [Fact]
    public void AFollowingCompanionComesThroughTheDoorAfterItsOwner()
    {
        GameSession session = TestRun.Start(
            [
                new RoomSetup("A", TestRun.Room(Pair), new Dictionary<Direction, string> { [Direction.East] = "B" }),
                new RoomSetup("B", TestRun.Room(Empty), new Dictionary<Direction, string> { [Direction.West] = "A" }),
            ],
            Fives(), heroes: 2, owners: PlayerAndCompanion);
        Hero owner = session.HeroInSeat(0);
        Hero companion = session.HeroInSeat(1);
        owner.RoomId = "B";
        owner.Position = new GridPosition(2, 4);

        PlayRounds(session, 3);

        Assert.Equal("B", companion.RoomId);
        Assert.True(companion.Position.ChebyshevDistanceTo(owner.Position) <= 2, $"{companion.Name} stopped at {companion.Position}.");
    }

    [Fact]
    public void AHoldingCompanionStaysPut()
    {
        GameSession session = TestRun.Start(Pair, Fives(), heroes: 2, owners: PlayerAndCompanion);
        Hero owner = session.HeroInSeat(0);
        Hero companion = session.HeroInSeat(1);
        GridPosition post = companion.Position;
        session.Do(new OrderCompanion(owner.Id, companion.Id, CompanionOrder.Hold));
        owner.Position = new GridPosition(0, 0);

        PlayRounds(session, 2);

        Assert.Equal(post, companion.Position);
    }

    [Theory]
    [InlineData(CompanionOrder.Follow, false)]
    [InlineData(CompanionOrder.Fight, true)]
    public void OnlyAFightingCompanionGoesAfterMonsters(CompanionOrder order, bool attacks)
    {
        GameSession session = TestRun.Start("""
            .........
            ........d
            .........
            .........
            ...12....
            .........
            .........
            .........
            .........
            """, Fives(), heroes: 2, owners: PlayerAndCompanion);
        Hero owner = session.HeroInSeat(0);
        session.Do(new OrderCompanion(owner.Id, session.HeroInSeat(1).Id, order));

        IReadOnlyList<GameEvent> events = PlayRounds(session, 2);

        Assert.Equal(attacks, events.OfType<AttackRolled>().Any(attack => attack.AttackerId == session.HeroInSeat(1).Id));
    }

    [Fact]
    public void AFightingCompanionWithABowHangsBackAndShoots()
    {
        GameSession session = TestRun.Start("""
            12.......
            .........
            .........
            .........
            .........
            .........
            .........
            .........
            ........d
            """, Fives(), heroes: 2, owners: PlayerAndCompanion, cards: ["bow"]);
        Hero companion = session.HeroInSeat(1);
        session.Do(new OrderCompanion(session.HeroInSeat(0).Id, companion.Id, CompanionOrder.Fight));
        session.HeroInSeat(0).Position = new GridPosition(0, 8);

        IReadOnlyList<GameEvent> events = PlayRounds(session, 3);

        Assert.Contains(events, gameEvent => gameEvent is AttackRolled attack && attack.AttackerId == companion.Id);
        Assert.True(session.OnlyMonster().NearestTile(companion.Position).ChebyshevDistanceTo(companion.Position) > 1, $"{companion.Name} walked up to {companion.Position}.");
    }

    [Fact]
    public void AStayingBackCompanionBacksAwayFromMonsters()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ......d..
            ...12....
            .........
            .........
            .........
            .........
            """, Fives(), heroes: 2, owners: PlayerAndCompanion);
        Hero companion = session.HeroInSeat(1);
        session.Do(new OrderCompanion(session.HeroInSeat(0).Id, companion.Id, CompanionOrder.StayBack));

        IReadOnlyList<GameEvent> events = PlayRounds(session, 1);

        Assert.True(session.OnlyMonster().Position.ManhattanDistanceTo(companion.Position) >= 4, $"{companion.Name} is at {companion.Position}.");
        Assert.DoesNotContain(events, gameEvent => gameEvent is AttackRolled attack && attack.AttackerId == companion.Id);
    }

    private const string Empty = """
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

    /// <summary>Plays whole rounds: players just end their turns, companions play theirs with the bot.</summary>
    private static List<GameEvent> PlayRounds(GameSession session, int rounds)
    {
        var events = new List<GameEvent>();
        int last = session.State.Round + rounds;
        for (int commands = 0; session.State.Phase == RunPhase.InProgress && session.State.Round < last; commands++)
        {
            Assert.True(commands < 200, "The companion never ended its turn.");
            Hero active = session.State.ActiveHero!;
            events.AddRange(session.Do(active.IsCompanion ? CompanionBot.NextCommand(session) : new EndTurn(active.Id)));
        }

        return events;
    }
    [Fact]
    public void AFightingCompanionGoesRoundAnAllyStandingWhereItWouldStrikeFrom()
    {
        // The companion (2,4) is right behind its owner (3,4), who holds the nearest tile beside the brute
        // (4,4); walls above and below the brute. It has to go round to a free tile to strike.
        var setup = new RunSetup
        {
            Seed = TestRun.Seed,
            ThemeId = TestContent.ThemeId,
            Heroes =
            [
                new HeroSetup("Owner", TestRun.HeroStats with { Hearts = 40 }, []),
                new HeroSetup("Pal", TestRun.HeroStats with { Hearts = 40, MoveDice = 2 }, [], OwnerSeat: 0),
            ],
            Rooms = [new RoomSetup("A", TestRun.Room("""
                .........
                .........
                .........
                ....#....
                ..21B....
                ....#....
                .........
                .........
                .........
                """))],
            StartRoomId = "A",
        };
        var session = new GameSession(TestContent.Catalog, setup);
        session.Begin();
        Hero owner = session.HeroInSeat(), pal = session.HeroInSeat(1);
        session.Do(new OrderCompanion(owner.Id, pal.Id, CompanionOrder.Fight));

        bool attacked = false;
        for (int i = 0; i < 100 && !attacked && session.State.Phase == RunPhase.InProgress; i++)
        {
            Hero active = session.State.ActiveHero!;
            GameCommand command = active.IsCompanion ? CompanionBot.NextCommand(session) : new EndTurn(active.Id);
            attacked = command is AttackMonster && active == pal;
            session.Do(command);
        }

        Assert.True(attacked, $"the companion never got to strike (it's at {pal.Position})");
    }
}
