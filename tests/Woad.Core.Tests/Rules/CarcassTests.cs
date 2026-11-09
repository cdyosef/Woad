using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Leviathan Jr. slain floats belly-up where it fell: three tiles of its body over the water, to stand on.</summary>
public sealed class CarcassTests
{
    [Fact]
    public void SlainItFloatsBellyUpAcrossTheWaterASurfaceToStandOn()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ..=====..
            ..=====..
            ..=====..
            ....1....
            ...¥.....
            .........
            """, new ScriptedDice(5));
        Hero hero = session.HeroInSeat();
        Monster floater = session.OnlyMonster();
        floater.Position = new GridPosition(4, 5);

        CarcassFloated floated = session.Do(new AttackMonster(hero.Id, floater.Id)).SingleOf<CarcassFloated>();

        GridPosition[] body = [new(3, 5), new(4, 5), new(5, 5)];
        Assert.Equal(body, floated.Tiles);
        Room room = session.RoomById();
        Assert.All(body, tile => Assert.Equal(TileKind.Floor, room.Layout[tile]));
        Assert.Equal(TileKind.DeepWater, room.Layout[new GridPosition(4, 4)]);
        Assert.Equal("floater", Assert.Single(room.Carcasses).MonsterId);
    }
}
