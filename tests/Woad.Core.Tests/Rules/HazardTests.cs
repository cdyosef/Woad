using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class HazardTests
{
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

    /// <summary>A hero at (4,4) with a pit straight ahead at (4,3).</summary>
    private const string PitAhead = """
        .........
        .........
        .........
        ....~....
        ....1....
        .........
        .........
        .........
        .........
        """;

    /// <summary>A hero at (4,4), a dummy (3 hearts, no defense) at (4,3) and a pit behind it at (4,2).</summary>
    private const string DummyByThePit = """
        .........
        .........
        ....~....
        ....d....
        ....1....
        .........
        .........
        .........
        .........
        """;

    /// <summary>The room, with room "B" one floor down under it when <paramref name="withRoomBelow"/>.</summary>
    private static List<RoomSetup> Rooms(string room, bool withRoomBelow, RoomFeatures? features = null) => withRoomBelow
        ? [new RoomSetup("A", TestRun.Room(room), Features: features), new RoomSetup("B", TestRun.Room(Empty), Floor: 2, Role: RoomRole.Arrival)]
        : [new RoomSetup("A", TestRun.Room(room), Features: features)];

    [Fact]
    public void WalkingIntoAPitDropsYouIntoTheRoomBelowUnderIt()
    {
        GameSession session = TestRun.Start(Rooms(PitAhead, withRoomBelow: true), new ScriptedDice(1));
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));

        IReadOnlyList<GameEvent> events = session.Do(new MoveHero(hero.Id, new GridPosition(4, 3)));

        Assert.Equal(new FellIntoPit(hero.Id, "A", new GridPosition(4, 3), Bottomless: false), events.SingleOf<FellIntoPit>());
        Assert.Equal(("B", new GridPosition(4, 3)), (hero.RoomId, hero.Position));
        Assert.Equal(GameRules.HeroBaseStats.Hearts - GameRules.FallDamage, hero.Hearts);
        Assert.Equal(0, session.State.Turn!.MovementPoints);
        Assert.Equal("B", session.RoomById().BelowRoomId);
    }

    [Fact]
    public void FromABottomlessPitYouClimbBackOut()
    {
        GameSession session = TestRun.Start(PitAhead, new ScriptedDice(1));
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));

        IReadOnlyList<GameEvent> events = session.Do(new MoveHero(hero.Id, new GridPosition(4, 3)));

        Assert.True(events.SingleOf<FellIntoPit>().Bottomless);
        var climbed = events.SingleOf<ClimbedOut>();
        Assert.Equal(1, climbed.Position.ManhattanDistanceTo(new GridPosition(4, 3)));
        Assert.Equal(("A", climbed.Position), (hero.RoomId, hero.Position));
        Assert.Equal(GameRules.HeroBaseStats.Hearts - GameRules.FallDamage, hero.Hearts);
    }

    [Fact]
    public void AShoveIntoAPitDropsTheMonsterIntoTheRoomBelowAndClearsThisOne()
    {
        GameSession session = TestRun.Start(Rooms(DummyByThePit, withRoomBelow: true), new ScriptedDice(3));
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new Shove(hero.Id, dummy.Id));

        Assert.Equal(new Shoved(hero.Id, dummy.Id, new GridPosition(4, 3), new GridPosition(4, 2)), events.SingleOf<Shoved>());
        Assert.Equal(new MonsterFell(dummy.Id, "A", "B", new GridPosition(4, 2)), events.SingleOf<MonsterFell>());
        Assert.Contains(new RoomCleared("A"), events);
        Assert.Same(dummy, session.OnlyMonster("B"));
        Assert.Equal(dummy.MaxHearts - GameRules.FallDamage, dummy.Hearts);
        Assert.Equal(1, session.State.Turn!.AttackDiceUsed);
    }

    [Fact]
    public void AShoveIntoABottomlessPitKillsTheMonster()
    {
        GameSession session = TestRun.Start(DummyByThePit, new ScriptedDice(3));
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new Shove(hero.Id, dummy.Id));

        Assert.True(events.SingleOf<FellIntoPit>().Bottomless);
        Assert.Contains(new MonsterDefeated(dummy.Id, hero.Id), events);
        Assert.Empty(session.RoomById().Monsters);
        Assert.Equal(1, hero.Kills);
    }

    [Fact]
    public void AShoveThatDoesntBeatTheDefenseIsResisted()
    {
        GameSession session = TestRun.Start(DummyByThePit.Replace('d', 'B'), new ScriptedDice(2, 5));
        Hero hero = session.HeroInSeat();
        Monster brute = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new Shove(hero.Id, brute.Id));

        Assert.Equal(new ShoveResisted(hero.Id, brute.Id), events.SingleOf<ShoveResisted>());
        Assert.Equal(new GridPosition(4, 3), brute.Position);
        Assert.Equal(brute.MaxHearts, brute.Hearts);
    }

    [Fact]
    public void BossesAndMonstersWithNowhereToGoCantBeShoved()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            ....#....
            ....d.W..
            ....1....
            .........
            .........
            .........
            .........
            """);
        Hero hero = session.HeroInSeat();
        Room room = session.RoomById();

        Assert.Contains("nowhere to shove", session.Refuse(new Shove(hero.Id, room.Monsters.Single(monster => monster.Definition.Id == "dummy").Id)));
        Assert.Empty(session.GetShoveTargets());
        hero.Position = new GridPosition(5, 4);
        Assert.Contains("too mighty", session.Refuse(new Shove(hero.Id, room.Monsters.Single(monster => monster.Definition.Boss).Id)));
    }

    [Fact]
    public void AShoveCanPushDiagonallyOntoAHiddenTrap()
    {
        var spikes = new RoomFeatures { Traps = new Dictionary<GridPosition, TrapSetup> { [new(6, 2)] = new(TrapKind.Spikes, 4) } };
        GameSession session = TestRun.Start([new RoomSetup("A", TestRun.Room("""
            .........
            .........
            .........
            .....d...
            ....1....
            .........
            .........
            .........
            .........
            """), Features: spikes)], new ScriptedDice(3));
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new Shove(hero.Id, dummy.Id));

        Assert.Equal(new GridPosition(6, 2), dummy.Position);
        Assert.Equal(new MonsterTrapped(dummy.Id, TrapKind.Spikes, new GridPosition(6, 2)), events.SingleOf<MonsterTrapped>());
        Assert.True(session.RoomById().TrapAt(new GridPosition(6, 2))!.Revealed);
        Assert.Equal(dummy.MaxHearts - GameRules.FallDamage, dummy.Hearts);
    }

    [Theory]
    [InlineData(6, 4, 2)]
    [InlineData(4, 4, 2)]
    [InlineData(3, 4, 3)]
    [InlineData(1, 4, 4)]
    public void ALeapCarriesHalfTheRollAndNeverPastTheTarget(int roll, int landingX, int landingY)
    {
        // Aiming two tiles north from (4,4), over the pit at (4,3) to (4,2).
        GameSession session = TestRun.Start(PitAhead, new ScriptedDice(roll));
        Hero hero = session.HeroInSeat();
        Assert.Contains(new GridPosition(4, 2), session.GetLeapTargets());

        IReadOnlyList<GameEvent> events = session.Do(new Leap(hero.Id, new GridPosition(4, 2)));

        var landing = new GridPosition(landingX, landingY);
        Assert.Equal(new Leaped(hero.Id, new GridPosition(4, 4), new GridPosition(4, 2), roll, landing), events.SingleOf<Leaped>());
        Assert.Equal(1, session.State.Turn!.MoveDiceUsed);
        if (landing == new GridPosition(4, 3))
        {
            Assert.True(events.SingleOf<FellIntoPit>().Bottomless);
            Assert.Equal(GameRules.HeroBaseStats.Hearts - GameRules.FallDamage, hero.Hearts);
        }
        else
        {
            Assert.Equal(landing, hero.Position);
        }
    }

    [Fact]
    public void LeapsGoInStraightLinesUpToThreeTilesAndNotThroughWalls()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            ....#....
            ....~....
            ...m1....
            .........
            .........
            .........
            .........
            """);
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GridPosition> targets = session.GetLeapTargets();

        Assert.DoesNotContain(new GridPosition(4, 2), targets);
        Assert.DoesNotContain(new GridPosition(4, 1), targets);
        Assert.Contains(new GridPosition(2, 4), targets);
        Assert.DoesNotContain(new GridPosition(3, 4), targets);
        Assert.Contains(new GridPosition(7, 7), targets);
        Assert.DoesNotContain(new GridPosition(8, 8), targets);
        Assert.DoesNotContain(new GridPosition(6, 5), targets);
        Assert.Contains("straight line", session.Refuse(new Leap(hero.Id, new GridPosition(6, 5))));
    }

    [Fact]
    public void RoughGroundAndWaterCostTwoStepsAndMonstersCantSwim()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ....=....
            ....:....
            ....1,...
            .........
            .........
            .........
            """, new ScriptedDice(3));
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));

        IReadOnlyDictionary<GridPosition, int> reachable = session.GetReachableTiles();

        Assert.Equal(2, reachable[new GridPosition(4, 4)]);
        Assert.Equal(4, reachable[new GridPosition(4, 3)]);
        Assert.Equal(2, reachable[new GridPosition(5, 5)]);
        session.Do(new MoveHero(hero.Id, new GridPosition(4, 3)));
        Assert.Equal(1, session.State.Turn!.MovementPoints);
        Assert.True(TileKind.DeepWater.MoveCost() == 2 && !TileKind.DeepWater.IsWalkable() && TileKind.DeepWater.IsWalkable(flying: true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EndingATurnInDeepWaterTakesARumbleDieOrYouDrownBackToShore(bool diceLeft)
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ....=....
            ....1....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(1, 5, 5), heroes: 2);
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));
        session.Do(new MoveHero(hero.Id, new GridPosition(4, 3)));
        if (!diceLeft)
        {
            session.State.Turn!.AttackDiceUsed = hero.EffectiveStats.AttackDice;
        }

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        if (diceLeft)
        {
            Assert.Equal(new StayedAfloat(hero.Id), events.SingleOf<StayedAfloat>());
            Assert.Equal(new GridPosition(4, 3), hero.Position);
            Assert.Equal(GameRules.HeroBaseStats.Hearts, hero.Hearts);
        }
        else
        {
            Assert.Equal(new Drowned(hero.Id, new GridPosition(4, 3), new GridPosition(4, 4)), events.SingleOf<Drowned>());
            Assert.Equal(new GridPosition(4, 4), hero.Position);
            Assert.Equal(GameRules.HeroBaseStats.Hearts - GameRules.DrownDamage, hero.Hearts);
        }

        Assert.Equal(new TurnStarted(session.HeroInSeat(1).Id), events[^1]);
    }

    [Fact]
    public void AMonsterShovedIntoDeepWaterLosesAHeartAndScramblesOut()
    {
        GameSession session = TestRun.Start(DummyByThePit.Replace('~', '='), new ScriptedDice(3));
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new Shove(hero.Id, dummy.Id));

        Assert.Equal(dummy.MaxHearts - 1, dummy.Hearts);
        Assert.NotEqual(TileKind.DeepWater, session.RoomById().Layout[dummy.Position]);
        Assert.Equal(new ClimbedOut(dummy.Id, dummy.Position), events.SingleOf<ClimbedOut>());
    }

    [Fact]
    public void GeneratedPitsAboveFloorsLeadToTheRoomUnderneath()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        for (ulong seed = 1; seed <= 60; seed++)
        {
            RunSetup setup = DungeonGenerator.CreateRun(content, "emerald_keep", [new HeroSetup("Bot", GameRules.HeroBaseStats, GameRules.StarterKit)], seed);
            GameState state = new GameSession(content, setup).State;
            foreach (Room room in state.Rooms.Values.Where(room => room.BelowRoomId is { } below))
            {
                Room below = state.Rooms[room.BelowRoomId!];
                Assert.Equal((room.Floor + 1, room.MapPosition), (below.Floor, below.MapPosition));
                Assert.NotEqual(RoomRole.Boss, below.Role);
            }

            // A trap door always has a room under it.
            Assert.All(state.Rooms.Values.Where(room => room.Traps.Values.Any(trap => trap.Kind == TrapKind.TrapDoor)),
                room => Assert.NotNull(room.BelowRoomId));
        }
    }
}
