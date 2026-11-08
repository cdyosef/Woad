using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Dungeon;

/// <summary>Portals: in the entrance, floor 1's stairs room and the boss room; lit with a die, then linked.</summary>
public class PortalTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();

    private static HeroSetup[] Party(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new HeroSetup($"Hero{i}", GameRules.HeroBaseStats with { Hearts = 10, Magic = 1 }, GameRules.StarterKit))];

    /// <summary>The room of that role (the entrance: a gated dungeon's entrance hall, inside its great door).</summary>
    private static RoomSetup Host(RunSetup run, int floor, RoomRole role) =>
        run.Rooms.SingleOrDefault(room => room.Floor == floor && room.Level == 0 && room.Role == (role == RoomRole.Arrival ? RoomRole.Hall : role))
        ?? run.Rooms.Single(room => room.Floor == floor && room.Level == 0 && room.Role == role);

    [Theory]
    [InlineData("sunken_temple", 1UL)]
    [InlineData("sunken_temple", 77UL)]
    public void TheEntranceTheFirstStairsAndTheBossRoomEachHoldAPortalYouCanWalkUpTo(string theme, ulong seed)
    {
        RunSetup run = DungeonGenerator.CreateRun(Content, theme, Party(2), seed);
        int floors = Content.Themes[theme].Floors;
        RoomSetup[] hosts = [Host(run, 1, RoomRole.Arrival), Host(run, 1, RoomRole.Stairs), Host(run, floors, RoomRole.Boss)];
        Assert.Equal(3, run.Rooms.Count(room => room.Template.TilesOf(TileKind.Portal).Any()));
        foreach (RoomSetup host in hosts)
        {
            RoomLayout layout = host.Template.Layout;
            GridPosition portal = Assert.Single(host.Template.TilesOf(TileKind.Portal));
            GridPosition door = host.DoorTiles!.Values.First();
            var reached = PathSearch.Run(door, tile => layout.Contains(tile) && layout[tile].IsWalkable()).Distances.Keys.ToHashSet();
            Assert.Contains(reached, tile => tile.ChebyshevDistanceTo(portal) == 1);
        }
    }

    [Theory]
    [InlineData(42UL)]
    [InlineData(2026UL)]
    public void TheEmeraldKeepsPortalsAreRiftsInPlainSight(ulong seed)
    {
        RunSetup run = DungeonGenerator.CreateRun(Content, "emerald_keep", Party(2), seed);
        RoomSetup[] hosts = [Host(run, 1, RoomRole.Arrival), Host(run, 1, RoomRole.Stairs), Host(run, 3, RoomRole.Boss)];
        Assert.DoesNotContain(run.Rooms, room => room.Template.TilesOf(TileKind.Portal).Any());
        Assert.All(hosts, host => Assert.Single(host.Features!.Traps.Values, trap => trap.Kind == TrapKind.Rift));

        GameState state = new GameSession(Content, run).State;
        Assert.All(hosts, host => Assert.Single(state.Rooms[host.Id].Traps.Values, trap => trap is { Kind: TrapKind.Rift, Revealed: true }));
    }

    [Fact]
    public void ADieLightsAPortalAndALitPortalTakesYouToAnotherLitOne()
    {
        RunSetup run = DungeonGenerator.CreateRun(Content, "sunken_temple", Party(1), seed: 5);
        var session = new GameSession(Content, run);
        session.Begin();
        Hero hero = session.State.Heroes[0];
        Room entrance = session.State.Rooms[hero.RoomId];
        Room stairs = session.State.Rooms[Host(run, 1, RoomRole.Stairs).Id];
        GridPosition portal = entrance.TilesOfKind(TileKind.Portal).Single();
        hero.Position = entrance.Layout.Positions.First(tile => tile.ChebyshevDistanceTo(portal) == 1 && entrance.Layout[tile].IsWalkable());

        Assert.Contains("banked Power Die", session.Refuse(new LightPortal(hero.Id)));
        hero.Power.Banked = 1;
        PortalLit lit = session.Do(new LightPortal(hero.Id)).SingleOf<PortalLit>();
        Assert.Equal((entrance.Id, true, 0), (lit.RoomId, entrance.PortalLit, hero.Power.Banked));
        Assert.Contains("already lit", session.Refuse(new LightPortal(hero.Id)));

        Assert.Contains("isn't lit", session.Refuse(new TakePortal(hero.Id, stairs.Id)));
        Assert.Empty(session.GetPortalDestinations());

        stairs.PortalLit = true;
        Assert.Equal([stairs], session.GetPortalDestinations());
        PortalTaken taken = session.Do(new TakePortal(hero.Id, stairs.Id)).SingleOf<PortalTaken>();
        Assert.Equal((stairs.Id, stairs.Id), (taken.RoomId, hero.RoomId));
        Assert.Equal(1, hero.Position.ChebyshevDistanceTo(stairs.TilesOfKind(TileKind.Portal).Single()));
    }

    [Fact]
    public void AMagicDieLightsItTooAndYouMustStandBesideIt()
    {
        RunSetup run = DungeonGenerator.CreateRun(Content, "sunken_temple", Party(1), seed: 5);
        var session = new GameSession(Content, run);
        session.Begin();
        Hero hero = session.State.Heroes[0];
        Room entrance = session.State.Rooms[hero.RoomId];
        GridPosition portal = entrance.TilesOfKind(TileKind.Portal).Single();
        hero.MagicCharged = 1;
        hero.Position = entrance.Layout.Positions.First(tile => tile.ChebyshevDistanceTo(portal) > 2 && entrance.Layout[tile].IsWalkable());
        Assert.Contains("beside the portal", session.Refuse(new LightPortal(hero.Id, Magic: true)));

        hero.Position = entrance.Layout.Positions.First(tile => tile.ChebyshevDistanceTo(portal) == 1 && entrance.Layout[tile].IsWalkable());
        session.Do(new LightPortal(hero.Id, Magic: true));
        Assert.Equal((true, 0), (entrance.PortalLit, hero.MagicCharged));
    }

    [Fact]
    public void StepThroughToTheBossRoomAndYouveReachedItsFloor()
    {
        RunSetup run = DungeonGenerator.CreateRun(Content, "sunken_temple", Party(1), seed: 5);
        var session = new GameSession(Content, run);
        session.Begin();
        Hero hero = session.State.Heroes[0];
        Room entrance = session.State.Rooms[hero.RoomId];
        Room boss = session.State.Rooms[Host(run, Content.Themes["sunken_temple"].Floors, RoomRole.Boss).Id];
        GridPosition portal = entrance.TilesOfKind(TileKind.Portal).Single();
        hero.Position = entrance.Layout.Positions.First(tile => tile.ChebyshevDistanceTo(portal) == 1 && entrance.Layout[tile].IsWalkable());
        entrance.PortalLit = boss.PortalLit = true;

        session.Do(new TakePortal(hero.Id, boss.Id));

        Assert.Equal(boss.Id, hero.RoomId);
        Assert.Equal(boss.Floor, hero.DeepestFloor);
    }
}
