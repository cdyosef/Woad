using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Dungeon;

/// <summary>The Unhallowed Graves: one huge dark room, walled in, with crypts, graves and the dead lying still.</summary>
public class GraveyardTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();

    private const string Theme = "unhallowed_graves";

    private static RunSetup Generate(ulong seed, int heroes = 2) =>
        DungeonGenerator.CreateRun(Content, Theme, [.. Enumerable.Range(1, heroes).Select(number => new HeroSetup($"Hero {number}", GameRules.HeroBaseStats, GameRules.StarterKit))], seed);

    public static TheoryData<ulong> Seeds => new([1, 2, 3, 42, 777, 2026]);

    [Theory]
    [MemberData(nameof(Seeds))]
    public void TheGraveyardIsOneHugeDarkRoomWalledInWithWaysOutReachableFromTheGate(ulong seed)
    {
        RunSetup run = Generate(seed);
        RoomSetup room = Assert.Single(run.Rooms);
        RoomLayout layout = room.Template.Layout;
        Assert.Equal((100, 100, true), (layout.Width, layout.Height, room.Dark));

        // Nothing walks off the map: its edge is all stone, but for the exits.
        foreach (GridPosition tile in layout.Positions.Where(tile => tile.X is 0 or 99 || tile.Y is 0 or 99))
        {
            Assert.Contains(layout[tile], new[] { TileKind.Wall, TileKind.Exit });
        }

        // Every open exit can be walked to from the gate; every walled-up one is behind cracked stone that can be.
        GridPosition gate = room.Template.HeroStarts[0];
        PathSearch search = PathSearch.Run(gate, tile => layout.Contains(tile) && (layout[tile].IsWalkable() || layout[tile] == TileKind.CrackedWall));
        List<GridPosition> exits = [.. layout.Positions.Where(tile => layout[tile] == TileKind.Exit)];
        Assert.InRange(exits.Count, 4, 6);
        Assert.All(exits, exit => Assert.True(search.CanReach(exit), $"The exit at {exit} can't be reached."));
        Assert.InRange(layout.Positions.Count(tile => layout[tile] == TileKind.CrackedWall), 2, 3);
        Assert.Equal(GraveyardChests(room).Count, room.Template.Layout.Positions.Count(tile => layout[tile] == TileKind.Chest) - 1);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void TheHordeOutnumbersTheStraysAndEveryGraveHoldsSomething(ulong seed)
    {
        RunSetup run = Generate(seed);
        RoomSetup room = run.Rooms[0];
        HashSet<string> horde = [.. Content.GetTheme(Theme).Roster.Where(entry => entry.Horde).Select(entry => entry.Monster)];
        int undead = room.Template.Monsters.Count(spawn => horde.Contains(spawn.Monster.Id));
        int strays = room.Template.Monsters.Count - undead;
        Assert.True(undead >= 50 && strays is > 0 and <= 6, $"{undead} of the horde, {strays} strays, seed {seed}");
        Assert.True(room.Template.Monsters.All(spawn => spawn.Position.ChebyshevDistanceTo(room.Template.HeroStarts[0]) >= 12));

        RoomFeatures features = room.Features!;
        Assert.Contains(ChestLook.Grave, features.ChestLooks.Values);
        Assert.Contains(ChestLook.Sarcophagus, features.ChestLooks.Values);
        Assert.NotEmpty(features.Risers);
        Assert.NotEmpty(features.EmptyChests);
        Assert.InRange(features.LitBraziers.Count, 1, 10);
    }

    [Fact]
    public void TheDeadLieStillUntilAHeroComesNear()
    {
        var session = new GameSession(Content, Generate(7, heroes: 1));
        Room room = session.State.Rooms[DungeonGenerator.GraveyardRoomId];
        Assert.All(room.Monsters, monster => Assert.True(monster.Asleep));

        IReadOnlyList<GameEvent> events = session.EndRound();

        // Nobody is within reach of the gate, so nothing stirs or moves.
        Assert.DoesNotContain(events, e => e is MonsterStirred or MonsterMoved);
        Assert.All(room.Monsters, monster => Assert.True(monster.Asleep));
    }

    [Fact]
    public void ASleepingMonsterStirsWhenAHeroComesNear()
    {
        GameSession session = TestRun.StartGraveyard("""
            .........
            .........
            .........
            .........
            1.......k
            .........
            .........
            .........
            .........
            """, new ScriptedDice(1, 1, 1, 1, 1, 1));
        Monster skeleton = session.OnlyMonster();
        Assert.True(skeleton.Asleep);

        session.EndRound();
        Assert.True(skeleton.Asleep);

        session.HeroInSeat().Position = new GridPosition(3, 4);
        IReadOnlyList<GameEvent> events = session.EndRound();
        Assert.Contains(new MonsterStirred(skeleton.Id), events);
        Assert.False(skeleton.Asleep);
    }

    [Fact]
    public void OpeningAGraveWithTheDeadInItRaisesThemAtYou()
    {
        // Initiative 1; the skeleton climbs out beside the grave and strikes: 1 against the hero's 6.
        GameSession session = TestRun.StartGraveyard("""
            .........
            .........
            .........
            .........
            1C.......
            .........
            .........
            .........
            .........
            """, new ScriptedDice(1, 1, 6), risers: new() { [new GridPosition(1, 4)] = "bones" });
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new OpenChest(hero.Id, new GridPosition(1, 4)));

        RoseFromGrave risen = events.SingleOf<RoseFromGrave>();
        Monster skeleton = session.OnlyMonster();
        Assert.Equal((skeleton.Id, hero.Id), (risen.MonsterId, risen.OpenerId));
        Assert.False(skeleton.Asleep);
        Assert.Single(events.OfType<AttackRolled>(), attack => attack.AttackerId == skeleton.Id);
        Assert.True(session.RoomById().IsChestOpened(new GridPosition(1, 4)));
    }

    [Fact]
    public void AnEmptyGraveOpensOnNothing()
    {
        GameSession session = TestRun.StartGraveyard("""
            .........
            .........
            .........
            .........
            1C.......
            .........
            .........
            .........
            .........
            """, new ScriptedDice(1), empty: [new GridPosition(1, 4)]);

        ChestOpened opened = session.Do(new OpenChest(session.HeroInSeat().Id, new GridPosition(1, 4))).SingleOf<ChestOpened>();

        Assert.Empty(opened.CardIds);
        Assert.False(session.RoomById().HasLootAt(new GridPosition(1, 4)));
    }

    [Fact]
    public void TheReliquaryStandsBeforeThePulpitAndOpensOnlyToTheHolyKey()
    {
        RunSetup run = Generate(42, heroes: 1);
        Assert.Equal(new GridPosition(50, 50 - 7 + 2), run.Rooms[0].Features!.RelicChest);

        GameSession session = Reliquary([]);
        Hero hero = session.HeroInSeat();
        Assert.Contains("only the Holy Key", session.CheckOpenChest(new GridPosition(1, 4)));
        Assert.Contains("only the Holy Key", session.Refuse(new PickLock(hero.Id, new GridPosition(1, 4))));
    }

    [Fact]
    public void TheHolyKeyOpensTheReliquaryAndIsGoneAndTheRelicLiesWithin()
    {
        GameSession session = Reliquary(["holy_key"]);
        Hero hero = session.HeroInSeat();

        session.Do(new OpenChest(hero.Id, new GridPosition(1, 4)));

        Assert.DoesNotContain(hero.AllCards, card => card.Definition.HolyKey);
        Assert.Equal("saints_relic", Assert.Single(session.RoomById().LootAt(new GridPosition(1, 4))).Definition.Id);
    }

    /// <summary>A hero beside the reliquary, carrying <paramref name="cards"/> (real content).</summary>
    private static GameSession Reliquary(string[] cards)
    {
        RoomTemplate template = RoomTemplate.Parse("""
            .........
            .........
            .........
            .........
            1C.......
            .........
            .........
            .........
            .........
            """, Content);
        var session = new GameSession(Content, new RunSetup
        {
            Seed = 1,
            ThemeId = Theme,
            Heroes = [new HeroSetup("Wren", GameRules.HeroBaseStats with { OdHandDice = 1 }, cards)],
            Rooms = [new RoomSetup("A", template, Features: new RoomFeatures { RelicChest = new GridPosition(1, 4) })],
            StartRoomId = "A",
        }, new Woad.Core.Randomness.GameRandom(1) { Dice = new ScriptedDice(6, 6, 6, 6) });
        session.Begin();
        return session;
    }

    [Fact]
    public void InAGraveyardTheMonstersGoThenTheUndeadInATurnOfTheirOwn()
    {
        GameSession session = TestRun.StartGraveyard("""
            .........
            .........
            .........
            .........
            1.d....j.
            .........
            .........
            .........
            .........
            """, new ScriptedDice(6, 6));
        Hero hero = session.HeroInSeat();

        // Initiative 6+2 puts the hero first: then the monsters, then the undead.
        List<EnemyPhaseStarted> phases = [.. session.Do(new EndTurn(hero.Id)).OfType<EnemyPhaseStarted>()];

        Assert.Equal([Faction.Monsters, Faction.Undead], phases.Select(phase => phase.Faction));
        Assert.Equal(Faction.Undead, session.FactionOf(session.RoomById().Monsters.Single(monster => monster.Definition.Id == "corpse")));
        Assert.Equal(Faction.Monsters, session.FactionOf(session.RoomById().Monsters.Single(monster => monster.Definition.Id == "dummy")));
    }

    [Fact]
    public void TheMoreOfTheDeadYouWakeTheFartherOffTheyComeForYou()
    {
        // One corpse wakes beside the hero; at the undead's next turn the horde is on alert 1, and the
        // corpse 8 tiles off, out of waking range, stirs anyway.
        GameSession session = TestRun.StartGraveyard("""
            1j.......
            .........
            .........
            .........
            .........
            .........
            .........
            .........
            ........j
            """, new ScriptedDice(1, 1, 1, 1));
        Hero hero = session.HeroInSeat();
        Room yard = session.RoomById();
        Monster far = yard.Monsters.Single(monster => monster.Position == new GridPosition(8, 8));

        session.EndRound();
        Assert.Equal((1, 0, true), (yard.HordeWoken, yard.HordeAlert, far.Asleep));

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal(new HordeRoused(yard.Id, 1), events.SingleOf<HordeRoused>());
        Assert.False(far.Asleep);
    }

    [Fact]
    public void EscapingTheDeadBesideYouGetASwingAtYouOnTheWayOut()
    {
        // The Bones' 5 against the hero's 1: a heart lost, but still up, and away.
        GameSession session = TestRun.StartGraveyard("""
            .........
            .........
            .........
            .........
            1*k......
            .........
            .........
            .........
            .........
            """, new ScriptedDice(1, 5, 1));
        Hero hero = session.HeroInSeat();
        Monster bones = session.OnlyMonster();
        bones.Asleep = false;
        hero.Position = new GridPosition(1, 4);

        IReadOnlyList<GameEvent> events = session.Do(new Escape(hero.Id));

        Assert.Equal(new ParthianSwing(bones.Id, hero.Id), events.SingleOf<ParthianSwing>());
        Assert.Equal(hero.MaxHearts - 1, hero.Hearts);
        Assert.Equal(HeroStatus.Escaped, hero.Status);
    }

    [Fact]
    public void EachRoundEveryoneGoesByInitiativeAndAHeroWhoTiesAnEnemyTurnGoesAfterIt()
    {
        // Hero 1 rolls 3 (+2 = 5, tying the monsters), hero 2 rolls 4 (6): hero 2, the monsters, hero 1, the undead.
        GameSession session = TestRun.StartGraveyard(Empty, new ScriptedDice(3, 4), heroes: 2);
        Hero first = session.HeroInSeat(0), second = session.HeroInSeat(1);

        Assert.Equal(
            [new TurnSlot(second.Id, null, 6), new TurnSlot(null, Faction.Monsters, 5), new TurnSlot(first.Id, null, 5), new TurnSlot(null, Faction.Undead, 1)],
            session.State.Initiative!);
        Assert.Equal(second.Id, session.State.ActiveHero!.Id);
    }

    [Fact]
    public void HeroesTiedOnInitiativeRollOffADieMoreEachTimeTheyTieAgain()
    {
        // Both roll 2 (4 each); a die each: 3 and 3; two dice each: 1+1 and 5+5. Hero 2 goes first.
        GameSession session = TestRun.StartGraveyard(Empty, new ScriptedDice(2, 2, 3, 3, 1, 1, 5, 5), heroes: 2);
        Hero first = session.HeroInSeat(0), second = session.HeroInSeat(1);

        List<int?> order = [.. session.State.Initiative!.Select(slot => slot.HeroId)];

        Assert.Equal([null, second.Id, first.Id, null], order);
    }

    [Fact]
    public void AtTheRitualTheCabalStandsRoundTheCircleAndTheLeaderAtThePulpitAwake()
    {
        RunSetup run = DungeonGenerator.CreateRun(Content, Theme, [new HeroSetup("A", GameRules.HeroBaseStats, GameRules.StarterKit)], 42, ritualRounds: 30);
        Assert.Equal(30, run.RitualRounds);
        Assert.Equal(DungeonGenerator.Cabal, run.Rooms[0].Template.Monsters.Count(spawn => spawn.Monster.Id == "cabal_necromancer"));
        Assert.Single(run.Rooms[0].Template.Monsters, spawn => spawn.Monster.Id == "cult_leader");
        Assert.Equal(2, run.Rooms[0].Features!.LitBraziers.Count);

        var session = new GameSession(Content, run);
        Assert.All(session.State.Rooms[DungeonGenerator.GraveyardRoomId].Monsters.Where(monster => monster.Definition.Ritualist), monster => Assert.False(monster.Asleep));
        Assert.Equal(30, session.State.RitualRoundsLeft);
    }

    [Fact]
    public void WhenTheRoundsRunOutThePortalOpensAndTheDeadAllRise()
    {
        GameSession session = TestRun.StartGraveyard("""
            .........
            .........
            .........
            .........
            1...i....
            .........
            .........
            .........
            ........j
            """, new ScriptedDice(1, 1, 1), ritualRounds: 2);
        Room yard = session.RoomById();

        session.EndRound();
        Assert.Equal((1, false), (session.State.RitualRoundsLeft, session.State.PortalOpen));

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal(new PortalOpened(yard.Id, new GridPosition(4, 4)), events.SingleOf<PortalOpened>());
        Assert.True(session.State.PortalOpen && yard.Cursed);
        Assert.Equal(3, yard.HordeAlert);
        Assert.All(yard.Monsters, monster => Assert.False(monster.Asleep));
        Assert.False(session.InDarkness(yard, new GridPosition(8, 0)));
    }

    [Fact]
    public void KillingEveryCelebrantBreaksTheRitual()
    {
        // The hero's 4 kills the Chanter beside them.
        GameSession session = TestRun.StartGraveyard("""
            .........
            .........
            .........
            .........
            1i.......
            .........
            .........
            .........
            .........
            """, new ScriptedDice(1, 4, 1, 1, 1), ritualRounds: 2);
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id));

        Assert.Single(events.OfType<RitualBroken>());
        Assert.True(session.State.RitualStopped);
        session.EndRound();
        session.EndRound();
        Assert.False(session.State.PortalOpen);
        Assert.True(RunResultsFor(session).All(result => result.RitualStopped));
    }

    [Fact]
    public void ACursedGraveyardGlowsAndItsDeadAreAllAwake()
    {
        GameSession session = TestRun.StartGraveyard("""
            .........
            .........
            .........
            .........
            1........
            .........
            .........
            .........
            ........j
            """, new ScriptedDice(1), cursed: true);
        Room yard = session.RoomById();

        Assert.True(yard.Cursed && session.State.PortalOpen);
        Assert.False(session.OnlyMonster().Asleep);
        Assert.False(session.InDarkness(yard, new GridPosition(8, 8)));
    }

    /// <summary>The run's results as they'd be at its end (the heroes all escaped).</summary>
    private static IReadOnlyList<HeroRunResult> RunResultsFor(GameSession session)
    {
        foreach (Hero hero in session.State.Heroes)
        {
            hero.Status = HeroStatus.Escaped;
        }

        session.State.Phase = RunPhase.Ended;
        return RunResults.For(session.State);
    }

    private const string Empty = """
        .........
        .........
        .........
        .........
        12.......
        .........
        .........
        .........
        .........
        """;

    private static List<GridPosition> GraveyardChests(RoomSetup room) => [.. room.Features!.ChestLooks.Keys];
}
