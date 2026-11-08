using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Wall sconces: torches taken down and put up, and the light they give.</summary>
public class SconceTests
{
    private static GameSession Room(bool lit, bool dark = true)
    {
        string rows = string.Join('\n', Enumerable.Range(0, 9).Select(y => y == 4 ? ".1......." : "........."));
        return TestRun.Start(
        [
            new RoomSetup("A", TestRun.Room(rows), Features: new RoomFeatures { Sconces = new Dictionary<GridPosition, bool> { [new GridPosition(0, 4)] = lit } }, Dark: dark),
        ], new ScriptedDice());
    }

    [Fact]
    public void ATorchTakenFromASconceGoesInAFreeHandAndPutBackLightsIt()
    {
        GameSession session = Room(lit: true);
        Hero hero = session.HeroInSeat();
        Room room = session.RoomById();
        var sconce = new GridPosition(0, 4);
        Assert.False(session.InDarkness(room, hero.Position));

        session.Do(new TakeTorch(hero.Id, sconce));
        Assert.False(room.Sconces[sconce]);
        Assert.Contains(hero.EquippedCards, card => card.Definition.Id == GameSession.SconceTorchId);
        Assert.Contains("empty", session.CheckTakeTorch(sconce));

        session.Do(new PutTorch(hero.Id, sconce));
        Assert.True(room.Sconces[sconce]);
        Assert.DoesNotContain(hero.EquippedCards, card => card.Definition.Torch);
    }

    [Fact]
    public void AnEmptySconceInADarkRoomGivesNoLight()
    {
        GameSession session = Room(lit: false);
        Hero hero = session.HeroInSeat();
        Assert.True(session.InDarkness(session.RoomById(), hero.Position));
        Assert.Contains("no torch", session.CheckPutTorch(new GridPosition(0, 4)));
    }

    [Fact]
    public void SomeRoomsHaveSconcesAndDarkOnesStartEmpty()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        List<RoomSetup> rooms = [.. Enumerable.Range(1, 20).SelectMany(seed => DungeonGenerator.CreateRun(content, "sunken_temple", [new HeroSetup("Bot", GameRules.HeroBaseStats, GameRules.StarterKit)], (ulong)seed).Rooms)];
        Assert.Contains(rooms, room => room.Features?.Sconces.Values.Any(lit => lit) == true);
        Assert.All(rooms.Where(room => room.Dark), room => Assert.DoesNotContain(true, room.Features?.Sconces.Values ?? []));
    }
}
