using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Dungeon;

/// <summary>The Emerald Keep's tower: stairs up from floor 1, two levels of rooms, and the roof, its edge open air.</summary>
public class TowerTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();

    private static HeroSetup[] Party(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new HeroSetup($"Hero{i}", GameRules.HeroBaseStats with { Hearts = 10 }, GameRules.StarterKit))];

    [Theory]
    [InlineData(1UL)]
    [InlineData(42UL)]
    [InlineData(2026UL)]
    public void StairsClimbFromFloorOneThroughTwoLevelsToTheRoof(ulong seed)
    {
        RunSetup run = DungeonGenerator.CreateRun(Content, "emerald_keep", Party(2), seed);
        RoomSetup host = Assert.Single(run.Rooms, room => room.Level == 0 && room.StairsUpTo is not null);
        Assert.Equal(1, host.Floor);
        Assert.Single(host.Template.TilesOf(TileKind.StairsUp));

        string below = host.Id;
        string? up = host.StairsUpTo;
        for (int level = 1; level <= DungeonGenerator.TowerLevels; level++)
        {
            List<RoomSetup> rooms = [.. run.Rooms.Where(room => room.Level == level)];
            Assert.InRange(rooms.Count(room => room.Role is not (RoomRole.Secret or RoomRole.Den)), DungeonGenerator.MinRooms, DungeonGenerator.MaxRooms);
            RoomSetup arrival = rooms.Single(room => room.Role == RoomRole.Arrival);
            Assert.Equal(up, arrival.Id);
            Assert.Equal(below, arrival.StairsDownTo);
            Assert.Single(arrival.Template.TilesOf(TileKind.StairsDown));
            RoomSetup stairs = rooms.Single(room => room.StairsUpTo is not null);
            Assert.Single(stairs.Template.TilesOf(TileKind.StairsUp));
            Assert.DoesNotContain(rooms, room => room.Template.TilesOf(TileKind.Pit).Any());
            below = stairs.Id;
            up = stairs.StairsUpTo;
        }

        RoomSetup roof = run.Rooms.Single(room => room.Id == up);
        Assert.Equal((DungeonGenerator.TowerLevels + 1, below, run.StartRoomId), (roof.Level, roof.StairsDownTo, roof.FallsTo));
        Assert.Equal((DungeonGenerator.RoofSize, DungeonGenerator.RoofSize), (roof.Template.Layout.Width, roof.Template.Layout.Height));
        RoomLayout layout = roof.Template.Layout;
        List<GridPosition> edge = [.. layout.Positions.Where(tile => tile.X == 0 || tile.Y == 0 || tile.X == layout.Width - 1 || tile.Y == layout.Height - 1)];
        Assert.All(edge, tile => Assert.Contains(layout[tile], new[] { TileKind.Wall, TileKind.Pit }));
        Assert.True(edge.Count(tile => layout[tile] == TileKind.Pit) > 40, "open air between the pillars all round");
        Assert.Equal(TileKind.StairsDown, layout[layout.Center]);
    }

    [Fact]
    public void OtherDungeonsHaveNoTower() =>
        Assert.DoesNotContain(DungeonGenerator.CreateRun(Content, "sunken_temple", Party(2), seed: 3).Rooms, room => room.Level > 0 || room.StairsUpTo is not null);

    [Fact]
    public void TheStairsUpClimbToLevelOneAndBackDown()
    {
        var session = new GameSession(Content, DungeonGenerator.CreateRun(Content, "emerald_keep", Party(1), seed: 42));
        session.Begin();
        Hero hero = session.State.Heroes[0];
        Room host = session.State.Rooms.Values.Single(room => room.Level == 0 && room.StairsUpTo is not null);
        hero.RoomId = host.Id;
        hero.Position = host.TilesOfKind(TileKind.StairsUp).Single().Step(Direction.North) is var spot && host.Layout[spot].IsWalkable() ? spot
            : host.Positions.First(tile => host.Layout[tile].IsWalkable() && tile.ChebyshevDistanceTo(host.TilesOfKind(TileKind.StairsUp).Single()) == 1);

        HeroAscended up = session.Do(new TakeStairs(hero.Id)).SingleOf<HeroAscended>();
        Room arrival = session.State.Rooms[up.RoomId];
        Assert.Equal((1, RoomRole.Arrival), (arrival.Level, arrival.Role));
        Assert.Equal(1, hero.Position.ChebyshevDistanceTo(arrival.TilesOfKind(TileKind.StairsDown).Single()));

        HeroDescended down = session.Do(new TakeStairs(hero.Id)).SingleOf<HeroDescended>();
        Assert.Equal(host.Id, down.RoomId);
        Assert.Equal(1, hero.Position.ChebyshevDistanceTo(host.TilesOfKind(TileKind.StairsUp).Single()));
    }

    [Fact]
    public void KnockedOffTheRoofYouFallToTheCourtyard()
    {
        var session = new GameSession(Content, DungeonGenerator.CreateRun(Content, "emerald_keep", Party(2), seed: 42), new GameRandom(5) { Dice = new ScriptedDice(1, 1, 1, 1, 1, 1) });
        session.Begin();
        Hero pusher = session.HeroInSeat(0), pushed = session.HeroInSeat(1);
        Room roof = session.State.Rooms[DungeonGenerator.RoofId];
        foreach (Hero hero in new[] { pusher, pushed })
        {
            hero.RoomId = roof.Id;
        }

        // (1, 0) is open air between two pillars: pushed there from (1, 1), by someone at (1, 2).
        Assert.Equal(TileKind.Pit, roof.Layout[new GridPosition(1, 0)]);
        pushed.Position = new GridPosition(1, 1);
        pusher.Position = new GridPosition(1, 2);
        int hearts = pushed.Hearts;

        IReadOnlyList<GameEvent> events = session.Do(new NudgeAlly(pusher.Id, pushed.Id));

        HeroFell fell = events.SingleOf<HeroFell>();
        string courtyard = session.State.Rooms.Values.Single(room => room.Floor == 1 && room.Level == 0 && room.Role == RoomRole.Arrival).Id;
        Assert.Equal((roof.Id, courtyard), (fell.FromRoomId, fell.RoomId));
        Assert.Equal(courtyard, pushed.RoomId);
        Assert.Equal(DungeonGenerator.TowerLevels + 1, fell.Floors);
        Assert.True(pushed.Hearts <= hearts - DungeonGenerator.TowerLevels - 1, $"three storeys down hurts ({hearts} to {pushed.Hearts})");
    }
}
