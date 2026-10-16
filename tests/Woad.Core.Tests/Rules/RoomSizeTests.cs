using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class RoomSizeTests
{
    [Fact]
    public void RoomsOfAnySizeConnectThroughDoorsPlacedAnywhereAlongTheirEdges()
    {
        // A long 13x5 gallery whose east door is near the top, into a 5x5 closet whose west door is near the bottom.
        RoomSetup[] rooms =
        [
            new("gallery", TestRun.Room(".............\n..........1..\n.............\n.............\n............."),
                new Dictionary<Direction, string> { [Direction.East] = "closet" },
                DoorTiles: new Dictionary<Direction, GridPosition> { [Direction.East] = new(12, 1) }),
            new("closet", TestRun.Room(".....\n.....\n.....\n.....\n....."),
                new Dictionary<Direction, string> { [Direction.West] = "gallery" }, MapPosition: new(1, 0),
                DoorTiles: new Dictionary<Direction, GridPosition> { [Direction.West] = new(0, 3) }),
        ];
        GameSession session = TestRun.Start(rooms, new ScriptedDice(3));
        var hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));

        Assert.All(session.GetReachableTiles().Keys, tile => Assert.True(session.RoomById("gallery").Layout.Contains(tile)));
        session.Do(new MoveHero(hero.Id, new GridPosition(12, 1)));
        var entered = session.Do(new GoThroughDoor(hero.Id, Direction.East)).SingleOf<HeroEnteredRoom>();

        Assert.Equal(("closet", new GridPosition(0, 3)), (entered.RoomId, entered.Position));
    }
}
