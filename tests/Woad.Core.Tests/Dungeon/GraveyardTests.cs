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
        // The skeleton climbs out beside the grave and strikes: 1 against the hero's 6.
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
            """, new ScriptedDice(1, 6), risers: new() { [new GridPosition(1, 4)] = "bones" });
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
            """, empty: [new GridPosition(1, 4)]);

        ChestOpened opened = session.Do(new OpenChest(session.HeroInSeat().Id, new GridPosition(1, 4))).SingleOf<ChestOpened>();

        Assert.Empty(opened.CardIds);
        Assert.False(session.RoomById().HasLootAt(new GridPosition(1, 4)));
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
            """);
        Hero hero = session.HeroInSeat();

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
            """);
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
            """, new ScriptedDice(5, 1));
        Hero hero = session.HeroInSeat();
        Monster bones = session.OnlyMonster();
        bones.Asleep = false;
        hero.Position = new GridPosition(1, 4);

        IReadOnlyList<GameEvent> events = session.Do(new Escape(hero.Id));

        Assert.Equal(new ParthianSwing(bones.Id, hero.Id), events.SingleOf<ParthianSwing>());
        Assert.Equal(hero.MaxHearts - 1, hero.Hearts);
        Assert.Equal(HeroStatus.Escaped, hero.Status);
    }

    private static List<GridPosition> GraveyardChests(RoomSetup room) => [.. room.Features!.ChestLooks.Keys];
}
