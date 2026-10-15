using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class ObstacleTests
{
    /// <summary>A hero at (4,6) below a wall across row 5, with a gate spot at (4,5).</summary>
    private const string Divided = """
        .........
        .........
        .........
        .........
        .........
        #########
        ....1....
        .........
        .........
        """;

    private static readonly GridPosition Gate = new(4, 5);

    [Fact]
    public void AKeyOpensALockedGateIntoFloor()
    {
        var features = new RoomFeatures { Gates = new Dictionary<GridPosition, int> { [Gate] = 5 }, Keys = new Dictionary<GridPosition, int> { [new(4, 6)] = 1 } };
        GameSession session = TestRun.Start([new RoomSetup("A", TestRun.Room(Divided), Features: features)]);
        Hero hero = session.HeroInSeat();
        Room room = session.RoomById();
        Assert.Equal(TileKind.Gate, room.Layout[Gate]);
        session.Do(new PickUpLoot(hero.Id));

        IReadOnlyList<GameEvent> events = session.Do(new OpenGate(hero.Id, Gate));

        Assert.Equal(new GateOpened(hero.Id, "A", Gate, Picked: false), events.SingleOf<GateOpened>());
        Assert.Equal(TileKind.Floor, room.Layout[Gate]);
        Assert.Empty(room.Gates);
        Assert.Equal(0, hero.Keys);
    }

    [Fact]
    public void WithoutAKeyAGateIsPickedWithOdHand()
    {
        var features = new RoomFeatures { Gates = new Dictionary<GridPosition, int> { [Gate] = 5 } };
        GameSession session = TestRun.Start([new RoomSetup("A", TestRun.Room(Divided), Features: features)], new ScriptedDice(5));
        Hero hero = session.HeroInSeat();
        Assert.Contains("no keys", session.Refuse(new OpenGate(hero.Id, Gate)));

        IReadOnlyList<GameEvent> events = session.Do(new PickLock(hero.Id, Gate));

        Assert.Equal(new GateOpened(hero.Id, "A", Gate, Picked: true), events.SingleOf<GateOpened>());
        Assert.Equal(TileKind.Floor, session.RoomById().Layout[Gate]);
    }

    [Fact]
    public void ASearchFindsASecretWayThroughADividingWall()
    {
        var features = new RoomFeatures { HiddenGates = new Dictionary<GridPosition, int> { [Gate] = 4 } };
        GameSession session = TestRun.Start([new RoomSetup("A", TestRun.Room(Divided), Features: features)], new ScriptedDice(5));
        Hero hero = session.HeroInSeat();
        Assert.Equal(TileKind.Wall, session.RoomById().Layout[Gate]);

        var found = session.Do(new Search(hero.Id)).SingleOf<SecretsFound>();

        Assert.Equal([Gate], found.Gates);
        Assert.Equal(TileKind.Floor, session.RoomById().Layout[Gate]);
    }

    [Fact]
    public void ABombBreaksBothSidesOfABreachAndHeroesGoThrough()
    {
        string room = """
            .........
            .........
            .........
            .........
            .......1.
            .........
            .........
            .........
            .........
            """;
        GameSession session = TestRun.Start(
            [new RoomSetup("A", TestRun.Room(room)), new RoomSetup("B", TestRun.Room(room.Replace('1', '.')), MapPosition: new GridPosition(1, 0))],
            new ScriptedDice(3), cards: ["bomb"]);
        session.State.Rooms["A"].AddBreach(new GridPosition(8, 2), "B", new GridPosition(0, 2));
        session.State.Rooms["B"].AddBreach(new GridPosition(0, 2), "A", new GridPosition(8, 2));
        session.State.Rooms["A"].Layout.Set(new GridPosition(8, 2), TileKind.CrackedWall);
        session.State.Rooms["B"].Layout.Set(new GridPosition(0, 2), TileKind.CrackedWall);
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> blast = session.Do(new UseCard(hero.Id, hero.Card("bomb").Id, new GridPosition(8, 2)));

        Assert.Contains(new WallBroken("B", new GridPosition(0, 2), null), blast);
        session.Do(new RollMovement(hero.Id));
        session.Do(new MoveHero(hero.Id, new GridPosition(8, 2)));
        var entered = session.Do(new GoThroughBreach(hero.Id)).SingleOf<HeroEnteredRoom>();
        Assert.Equal(("B", new GridPosition(0, 2)), (hero.RoomId, hero.Position));
        Assert.True(entered.FirstVisit);
    }

    [Fact]
    public void GeneratedObstaclesNeverCutOffWhatAPartyNeeds()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        int divided = 0, channels = 0, breaches = 0;
        for (ulong seed = 1; seed <= 150; seed++)
        {
            RunSetup setup = DungeonGenerator.CreateRun(content, "emerald_keep", [new HeroSetup("Bot", GameRules.HeroBaseStats, GameRules.StarterKit)], seed);
            breaches += setup.Breaches.Count;
            GameState state = new GameSession(content, setup).State;
            foreach (Room room in state.Rooms.Values)
            {
                divided += room.Gates.Count + room.HiddenGates.Count;
                channels += room.TilesOfKind(TileKind.DeepWater).Any() || (room.Role != RoomRole.Treasure && room.TilesOfKind(TileKind.Pit).Count() >= 5) ? 1 : 0;

                // On foot (through locked gates, which can always be picked) every doorway, the stairs,
                // the exit and every monster can be reached from every other.
                bool Passable(GridPosition tile) =>
                    (room.Layout[tile].IsWalkable() || room.Layout[tile] == TileKind.Gate) && room.TrapAt(tile) is null;
                List<GridPosition> needed = [.. room.Doors.Keys.Select(side => side.DoorTile()), .. room.TilesOfKind(TileKind.StairsDown),
                    .. room.TilesOfKind(TileKind.StairsUp), .. room.TilesOfKind(TileKind.Exit)];
                if (needed.Count == 0)
                {
                    continue;
                }

                PathSearch walk = PathSearch.Run(needed[0], Passable);
                Assert.True(needed.All(walk.CanReach), $"Seed {seed}, room {room.Id}: a doorway or the stairs is cut off.");
                Assert.True(room.Monsters.Where(monster => !monster.Definition.Flying)
                        .All(monster => monster.Position.OrthogonalNeighbors().Any(walk.CanReach) || walk.CanReach(monster.Position)),
                    $"Seed {seed}, room {room.Id}: a monster is out of reach.");
            }
        }

        Assert.True(divided > 0 && channels > 0 && breaches > 0, $"Expected some of each: {divided} gates, {channels} channels, {breaches} breaches.");
    }
}
