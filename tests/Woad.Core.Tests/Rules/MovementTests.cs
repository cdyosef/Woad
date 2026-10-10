using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class MovementTests
{
    private const string Open = """
        .........
        .........
        .........
        .........
        ....1....
        .........
        .........
        .........
        .........
        """;

    private const string RoomA = """
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

    [Fact]
    public void YouMustRollBeforeMoving()
    {
        GameSession session = TestRun.Start(Open);

        Assert.Contains("Roll for movement", session.Refuse(new MoveHero(session.HeroInSeat().Id, new GridPosition(4, 3))));
        Assert.Empty(session.GetReachableTiles());
    }

    [Fact]
    public void MovementIsTheMoveDicePlusSpeed()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(3, 4), cards: ["boots"]);
        int heroId = session.HeroInSeat().Id;

        var rolled = session.Do(new RollMovement(heroId)).SingleOf<MovementRolled>();

        Assert.Equal([3, 4], rolled.Roll.Faces);
        Assert.Equal(9, rolled.MovementPoints);
        Assert.Contains("already rolled", session.Refuse(new RollMovement(heroId)));
    }

    [Fact]
    public void EachStepCostsOnePoint()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(3));
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));

        var moved = session.Do(new MoveHero(hero.Id, new GridPosition(4, 1))).SingleOf<HeroMoved>();

        Assert.Equal([new GridPosition(4, 3), new GridPosition(4, 2), new GridPosition(4, 1)], moved.Path);
        Assert.Equal(2, moved.MovementPointsLeft);
        Assert.Equal(new GridPosition(4, 1), hero.Position);

        session.Do(new MoveHero(hero.Id, new GridPosition(5, 1)));
        Assert.Contains("can't reach", session.Refuse(new MoveHero(hero.Id, new GridPosition(7, 1))));
    }

    [Fact]
    public void WallsPitsAndMonstersBlockTheWay()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ...#~#...
            ...#1m...
            ...#.#...
            .........
            .........
            .........
            """, new ScriptedDice(1));
        session.Do(new RollMovement(session.HeroInSeat().Id));

        Assert.Equal(
            new[] { new GridPosition(4, 5), new GridPosition(4, 6), new GridPosition(3, 6), new GridPosition(5, 6), new GridPosition(4, 7) }.ToHashSet(),
            session.GetReachableTiles().Keys.ToHashSet());
    }

    [Fact]
    public void HeroesWalkThroughAlliesButCantStopOnThem()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ....2....
            ....1....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(1), heroes: 2);
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));

        session.Refuse(new MoveHero(hero.Id, new GridPosition(4, 3)));
        var moved = session.Do(new MoveHero(hero.Id, new GridPosition(4, 2))).SingleOf<HeroMoved>();

        Assert.Equal([new GridPosition(4, 3), new GridPosition(4, 2)], moved.Path);
    }

    [Fact]
    public void ActingAfterMovingEndsTheMove()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(3), cards: ["sword", "big_sword"]);
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));
        session.Do(new MoveHero(hero.Id, new GridPosition(4, 3)));

        session.Do(new EquipCard(hero.Id, hero.Card("big_sword").Id));

        Assert.Contains("move is over", session.Refuse(new MoveHero(hero.Id, new GridPosition(4, 2))));
        Assert.Equal(0, session.State.Turn!.MovementPoints);
        Assert.Empty(session.GetReachableTiles());
    }

    [Fact]
    public void ActingBeforeMovingStillAllowsTheMove()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(3), cards: ["sword", "big_sword"]);
        Hero hero = session.HeroInSeat();
        session.Do(new EquipCard(hero.Id, hero.Card("big_sword").Id));

        session.Do(new RollMovement(hero.Id));
        session.Do(new MoveHero(hero.Id, new GridPosition(4, 0)));

        Assert.Equal(new GridPosition(4, 0), hero.Position);
    }

    [Fact]
    public void LuckRerollsAMovementDieBeforeTheFirstStep()
    {
        GameSession session = TestRun.Start(Open, new ScriptedDice(1, 6), stats: GameRules.HeroBaseStats with { Luck = 2 });
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));

        var rerolled = session.Do(new RerollDie(hero.Id, 0)).SingleOf<DieRerolled>();

        Assert.Equal((1, 6, (int?)8), (rerolled.OldFace, rerolled.NewFace, rerolled.MovementPoints));
        Assert.Equal(8, session.State.Turn!.MovementPoints);
        Assert.Equal(1, hero.LuckRemaining);

        session.Do(new MoveHero(hero.Id, new GridPosition(4, 3)));
        Assert.Contains("no roll to reroll", session.Refuse(new RerollDie(hero.Id, 0)));
    }

    [Fact]
    public void DoorsLeadToTheFacingDoorOfTheNextRoom()
    {
        GameSession session = TestRun.Start(TwoRooms(EmptyRoom), new ScriptedDice(1));
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));

        Assert.Contains("doorway", session.Refuse(new GoThroughDoor(hero.Id, Direction.East)));
        session.Do(new MoveHero(hero.Id, Direction.East.DoorTile()));
        Assert.Equal([Direction.East], session.GetUsableDoors());

        var entered = session.Do(new GoThroughDoor(hero.Id, Direction.East)).SingleOf<HeroEnteredRoom>();

        Assert.Equal(new HeroEnteredRoom(hero.Id, "A", "B", new GridPosition(0, 4), FirstVisit: true), entered);
        Assert.Equal("B", hero.RoomId);
        Assert.Equal(new GridPosition(0, 4), hero.Position);
        Assert.Equal(1, session.State.Turn!.MovementPoints);
        Assert.True(session.RoomById("B").Explored);
    }

    [Fact]
    public void AnOccupiedDoorwayPutsYouOnTheNearestFreeTile()
    {
        GameSession session = TestRun.Start(TwoRooms("""
            .........
            .........
            .........
            .........
            d........
            .........
            .........
            .........
            .........
            """), new ScriptedDice(1));
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));
        session.Do(new MoveHero(hero.Id, Direction.East.DoorTile()));

        Assert.Equal([Direction.East], session.GetUsableDoors());
        var entered = session.Do(new GoThroughDoor(hero.Id, Direction.East)).SingleOf<HeroEnteredRoom>();

        // The dummy stands in the west doorway (0,4); the nearest free tiles are (0,3), (1,4) and (0,5).
        Assert.Equal(new GridPosition(0, 3), entered.Position);
        Assert.Equal(new GridPosition(0, 3), hero.Position);
    }

    [Fact]
    public void ThereMustBeADoorOnThatSide()
    {
        GameSession session = TestRun.Start(TwoRooms(EmptyRoom), new ScriptedDice(6));
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));
        session.Do(new MoveHero(hero.Id, Direction.North.DoorTile()));

        Assert.Contains("no door", session.Refuse(new GoThroughDoor(hero.Id, Direction.North)));
    }

    private static RoomSetup[] TwoRooms(string roomB) =>
    [
        new RoomSetup("A", TestRun.Room(RoomA), new Dictionary<Direction, string> { [Direction.East] = "B" }),
        new RoomSetup("B", TestRun.Room(roomB), new Dictionary<Direction, string> { [Direction.West] = "A" }),
    ];
}
