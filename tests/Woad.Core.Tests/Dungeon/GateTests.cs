using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;

namespace Woad.Core.Tests.Dungeon;

/// <summary>The Emerald Keep's way in: a courtyard, a moat and a bridge, and a gate the Emerald Knight holds.</summary>
public class GateTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();

    private static HeroSetup[] Party(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new HeroSetup($"Hero{i}", GameRules.HeroBaseStats, GameRules.StarterKit))];

    private static RoomSetup Courtyard(RunSetup run) => run.Rooms.Single(room => room.Floor == 1 && room.Role == RoomRole.Arrival);

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 4)]
    [InlineData(7, 6)]
    public void TheEmeraldKnightHoldsTheGateWithTwoGuardsForEveryThreeHeroes(int heroes, int guards)
    {
        RunSetup run = DungeonGenerator.CreateRun(Content, "emerald_keep", Party(heroes), seed: 11);
        RoomSetup courtyard = Courtyard(run);

        Assert.Single(courtyard.Template.Monsters, spawn => spawn.Monster.Id == "emerald_knight");
        Assert.Equal(guards, courtyard.Template.Monsters.Count(spawn => spawn.Monster.Id == "emerald_guard"));
        Assert.Equal((KeepCourtyard.Id, KeepCourtyard.Width, KeepCourtyard.Height), (courtyard.Id, courtyard.Template.Layout.Width, courtyard.Template.Layout.Height));
        Assert.Equal(4, courtyard.Template.TilesOf(TileKind.Exit).Count());
        Assert.All(courtyard.Template.Monsters, spawn => Assert.True(spawn.Position.Y < 3, "the gate's guard stands on the apron, across the moat"));
    }

    [Fact]
    public void TheMoatRunsRightAcrossWithADrawbridgeFourWide()
    {
        RoomLayout layout = Courtyard(DungeonGenerator.CreateRun(Content, "emerald_keep", Party(2), seed: 11)).Template.Layout;
        List<int> moatRows = [.. Enumerable.Range(0, layout.Height).Where(y => layout[new GridPosition(0, y)] == TileKind.DeepWater)];

        Assert.Equal(2, moatRows.Count);
        foreach (int y in moatRows)
        {
            List<int> dry = [.. Enumerable.Range(0, layout.Width).Where(x => layout[new GridPosition(x, y)] != TileKind.DeepWater)];
            Assert.Equal(4, dry.Count);
            Assert.All(dry, x => Assert.Equal(TileKind.Floor, layout[new GridPosition(x, y)]));
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(11UL)]
    [InlineData(42UL)]
    [InlineData(2026UL)]
    public void TheGreatDoorLeadsIntoAnEntranceHallWithThePortal(ulong seed)
    {
        RunSetup run = DungeonGenerator.CreateRun(Content, "emerald_keep", Party(3), seed);
        RoomSetup courtyard = Courtyard(run);
        RoomSetup hall = run.Rooms.Single(room => room.Role == RoomRole.Hall);

        Assert.Equal(courtyard.Id, run.StartRoomId);
        Assert.Equal(new Dictionary<Direction, string> { [Direction.North] = hall.Id }, courtyard.Doors);
        Assert.Equal(KeepCourtyard.Door, courtyard.DoorTiles![Direction.North]);
        Assert.Equal(courtyard.Id, hall.Doors![Direction.South]);
        Assert.Equal(hall.MapPosition.Step(Direction.South), courtyard.MapPosition);
        Assert.Single(run.Rooms, room => room.Floor == 1 && room.Level == 0 && room.MapPosition == courtyard.MapPosition);
        Assert.Contains(run.Passages, passage => passage is { RoomId: KeepCourtyard.Id, Side: Direction.North, Kind: PassageKind.Gate });
        Assert.Empty(hall.Template.Monsters);
        Assert.Single(hall.Template.TilesOf(TileKind.Portal));
        Assert.Empty(courtyard.Template.TilesOf(TileKind.Portal));
        Assert.Empty(hall.Template.TilesOf(TileKind.Exit));
    }

    [Fact]
    public void OtherDungeonsHaveAPlainEntranceHall()
    {
        RoomSetup hall = Courtyard(DungeonGenerator.CreateRun(Content, "sunken_temple", Party(2), seed: 11));

        Assert.Empty(hall.Template.Monsters);
        Assert.Equal((DungeonGenerator.EntranceWidth, DungeonGenerator.EntranceHeight), (hall.Template.Layout.Width, hall.Template.Layout.Height));
    }

    [Fact]
    public void OnlyTheEmeraldKeyOpensTheGateAndItStaysInTheKeep()
    {
        var session = new GameSession(Content, DungeonGenerator.CreateRun(Content, "emerald_keep", Party(1), seed: 11));
        session.Begin();
        Hero hero = session.State.Heroes[0];
        Room courtyard = session.State.RoomOf(hero);
        (Direction side, Passage gate) = courtyard.Passages.First(door => door.Value.NeedsGateKey);
        Assert.True(gate.IsLocked);
        foreach (Monster monster in courtyard.Monsters.ToList())
        {
            courtyard.RemoveMonster(monster);
        }

        hero.Position = courtyard.DoorTile(side).Step(side.Opposite());
        hero.Keys = 3;
        Assert.Contains("only its key", session.Execute(new UnlockDoor(hero.Id, side)).Error);
        Assert.NotNull(session.Execute(new PickLock(hero.Id, courtyard.DoorTile(side))).Error);

        hero.AddToBackpack(new CardInstance(session.State.NextId(), Content.GetCard("emerald_key"), broughtInByHeroId: null));
        Assert.Null(session.Execute(new UnlockDoor(hero.Id, side)).Error);
        Assert.False(gate.IsLocked);
        Assert.Equal(3, hero.Keys);
        Assert.True(GameSession.HasGateKey(hero));

        // Out by the gateway: the key stays behind.
        hero.Position = courtyard.TilesOfKind(TileKind.Exit).First();
        session.Execute(new Escape(hero.Id));
        Assert.DoesNotContain("emerald_key", RunResults.For(session.State).Single().Backpack);
    }

    [Fact]
    public void TheEmeraldKnightCarriesTheKey() =>
        Assert.Contains(Content.GetMonster("emerald_knight").Carries, carried => carried.Card == "emerald_key" && carried.Chance == 100);
}
