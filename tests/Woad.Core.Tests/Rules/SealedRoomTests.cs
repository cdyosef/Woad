using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>The boss's room and a den shut behind you: sealed till the boss (or the den's keeper) falls.</summary>
public class SealedRoomTests
{
    /// <summary>A hero in the east doorway of a room of the given role, a monster (glyph) across it, the way out east.</summary>
    private static GameSession InRoom(RoomRole role, char monster) => TestRun.Start(
    [
        new RoomSetup("A", TestRun.Room($".........\n.........\n.........\n.........\n.{monster}......1\n.........\n.........\n.........\n........."),
            new Dictionary<Direction, string> { [Direction.East] = "B" }, Role: role),
        new RoomSetup("B", TestRun.Room(".........\n.........\n.........\n.........\n.........\n.........\n.........\n.........\n........."),
            new Dictionary<Direction, string> { [Direction.West] = "A" }),
    ], new ScriptedDice(3, 3, 3, 3));

    [Theory]
    [InlineData(RoomRole.Boss, 'W', "the boss")]
    [InlineData(RoomRole.Den, 'B', "the den's keeper")]
    public void TheBossRoomAndADenStaySealedTillTheirKeeperFalls(RoomRole role, char glyph, string keeper)
    {
        GameSession session = InRoom(role, glyph);
        Hero hero = session.HeroInSeat();
        Room room = session.RoomById();
        Assert.True(room.IsSealed);
        session.Do(new RollMovement(hero.Id));

        Assert.Contains(keeper, session.Refuse(new GoThroughDoor(hero.Id, Direction.East)));

        room.RemoveMonster(session.OnlyMonster());
        Assert.False(room.IsSealed);
        session.Do(new GoThroughDoor(hero.Id, Direction.East));
    }

    [Fact]
    public void AnOrdinaryRoomWithAMonsterInItStaysOpen()
    {
        GameSession session = InRoom(RoomRole.Combat, 'B');
        Assert.False(session.RoomById().IsSealed);
    }
}
