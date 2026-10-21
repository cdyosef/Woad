using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Grid;
using Woad.Core.Rules;

namespace Woad.Core.Tests.Dungeon;

public class DungeonGeneratorTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();

    private static IReadOnlyList<HeroSetup> Party(int size) =>
        Enumerable.Range(1, size).Select(number => new HeroSetup($"Hero {number}", GameRules.HeroBaseStats, GameRules.StarterKit)).ToList();

    private static RunSetup Generate(ulong seed, int partySize = 2) =>
        DungeonGenerator.CreateRun(Content, "emerald_keep", Party(partySize), seed);

    private static readonly ulong[] SeedValues = [1, 2, 3, 42, 777, 2026, 31337, 90210];

    public static TheoryData<ulong> Seeds => new(SeedValues);

    [Theory]
    [MemberData(nameof(Seeds))]
    public void EveryFloorHasSixToEightConnectedRoomsPlusPerhapsASecretOne(ulong seed)
    {
        RunSetup run = Generate(seed);

        foreach (IGrouping<int, RoomSetup> floor in run.Rooms.GroupBy(room => room.Floor))
        {
            Assert.InRange(floor.Count(room => room.Role is not (RoomRole.Secret or RoomRole.Den)), DungeonGenerator.MinRooms, DungeonGenerator.MaxRooms);
            Assert.InRange(floor.Count(room => room.Role == RoomRole.Secret), 0, 1);
            Assert.Equal(floor.Key == 1 ? 1 : 0, floor.Count(room => room.Role == RoomRole.Den));

            var reached = new HashSet<string> { floor.First().Id };
            var frontier = new Queue<RoomSetup>([floor.First()]);
            while (frontier.Count > 0)
            {
                foreach (string next in frontier.Dequeue().Doors!.Values.Where(reached.Add))
                {
                    frontier.Enqueue(floor.Single(room => room.Id == next));
                }
            }

            Assert.Equal(floor.Count(), reached.Count);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void TheFirstFloorAlwaysHasTheOgreChampsDenOverARoomBelow(ulong seed)
    {
        foreach (int partySize in new[] { 1, 4, 8 })
        {
            RunSetup run = Generate(seed, partySize);

            RoomSetup den = Assert.Single(run.Rooms, room => room.Role == RoomRole.Den);
            Assert.Equal(1, den.Floor);
            MonsterSpawn keeper = Assert.Single(den.Template.Monsters);
            Assert.Equal("ogre_champ", keeper.Monster.Id);
            Assert.Single(den.Doors!);
            Assert.Contains(run.Rooms, room => room.Floor == 2 && room.MapPosition == den.MapPosition && room.Role is not (RoomRole.Boss or RoomRole.Secret));

            // Pits all round; the bridges lead from the door to the Ogre Champ and on to the grand chest under its lock.
            RoomLayout layout = den.Template.Layout;
            Assert.All(layout.Positions.Where(tile => tile.X == 0 || tile.Y == 0 || tile.X == layout.Width - 1 || tile.Y == layout.Height - 1)
                .Where(tile => tile != den.DoorTiles!.Single().Value), tile => Assert.Equal(TileKind.Pit, layout[tile]));
            GridPosition chest = layout.Positions.Single(tile => layout[tile] == TileKind.Chest);
            Assert.Contains(chest, den.Features!.GrandChests);
            Assert.True(den.Features.LockedChests.ContainsKey(chest));
            PathSearch walk = PathSearch.Run(den.DoorTiles!.Single().Value, tile => layout[tile].IsWalkable());
            Assert.True(walk.CanReach(keeper.Position));
            Assert.Contains(chest.OrthogonalNeighbors(), walk.CanReach);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void FloorsHaveTheRightRoles(ulong seed)
    {
        RunSetup run = Generate(seed);

        Assert.Equal([1, 2, 3], run.Rooms.Select(room => room.Floor).Distinct().Order());
        foreach (IGrouping<int, RoomSetup> floor in run.Rooms.GroupBy(room => room.Floor))
        {
            Assert.Single(floor, room => room.Role == RoomRole.Arrival);
            Assert.Single(floor, room => room.Role == RoomRole.Treasure);
            RoomRole wayOn = floor.Key == 3 ? RoomRole.Boss : RoomRole.Stairs;
            Assert.Single(floor, room => room.Role == wayOn);
            Assert.DoesNotContain(floor, room => room.Role == (floor.Key == 3 ? RoomRole.Stairs : RoomRole.Boss));
            Assert.Equal(floor.Key == 3 ? 1 : 0, floor.Count(room => room.Role == RoomRole.Vault));

            // Below the first floor, the arrival room has the stairs back up, under the stairs room above.
            RoomSetup arrival = floor.Single(room => room.Role == RoomRole.Arrival);
            Assert.Equal(floor.Key > 1 ? 1 : 0, arrival.Template.TilesOf(TileKind.StairsUp).Count());
            if (floor.Key > 1)
            {
                Assert.Equal(run.Rooms.Single(room => room.Floor == floor.Key - 1 && room.Role == RoomRole.Stairs).MapPosition, arrival.MapPosition);
            }
        }

        Assert.Equal(run.StartRoomId, run.Rooms.Single(room => room.Floor == 1 && room.Role == RoomRole.Arrival).Id);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void MonstersAppearWhereTheyShould(ulong seed)
    {
        RunSetup run = Generate(seed);

        Assert.All(run.Rooms.Where(room => room.Role == RoomRole.Arrival), room => Assert.Empty(room.Template.Monsters));
        Assert.Single(run.Rooms.SelectMany(room => room.Template.Monsters), spawn => spawn.Monster.Boss);
        Assert.All(run.Rooms.SelectMany(room => room.Template.Monsters.Select(spawn => (room, spawn))),
            pair => Assert.True(pair.room.Template.Layout[pair.spawn.Position].IsWalkable()));
        Assert.DoesNotContain(run.Rooms.Where(room => room.Floor == 1).SelectMany(room => room.Template.Monsters),
            spawn => spawn.Monster.Id is "archer_guard" or "skeleton");
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void DoorsAreMutualAndOpenOntoFloor(ulong seed)
    {
        RunSetup run = Generate(seed);
        Dictionary<string, RoomSetup> byId = run.Rooms.ToDictionary(room => room.Id);

        foreach (RoomSetup room in run.Rooms)
        {
            foreach ((Direction side, string target) in room.Doors!)
            {
                Assert.Equal(room.Id, byId[target].Doors![side.Opposite()]);
                Assert.Equal(room.Floor, byId[target].Floor);
                Assert.Equal(TileKind.Floor, room.Template.Layout[room.DoorTiles![side]]);
            }
        }
    }

    [Fact]
    public void RoomsComeInManySizesMostlyStandardWithTheBossArenaTheBiggest()
    {
        List<RoomSetup> all = SeedValues.SelectMany(seed => Generate(seed).Rooms).ToList();
        int Area(RoomSetup room) => room.Template.Layout.Width * room.Template.Layout.Height;

        Assert.All(all.Where(room => room.Role == RoomRole.Boss), boss => Assert.Equal((DungeonGenerator.BossSize, DungeonGenerator.BossSize), (boss.Template.Layout.Width, boss.Template.Layout.Height)));
        Assert.All(all.Where(room => room.Role != RoomRole.Boss), room => Assert.True(Area(room) < DungeonGenerator.BossSize * DungeonGenerator.BossSize));
        Assert.All(all.Where(room => room.Role is RoomRole.Arrival or RoomRole.Vault), room => Assert.Equal(81, Area(room)));

        int standard = all.Count(room => Area(room) == 81);
        Assert.True(standard > all.Count / 2, $"{standard} of {all.Count} rooms are 9x9");
        Assert.Contains(all, room => Area(room) < 81);
        Assert.Contains(all, room => Area(room) > 81 && room.Role != RoomRole.Boss);
        Assert.Contains(all, room => room.Template.Layout.Positions.Any(tile => room.Template.Layout[tile] == TileKind.Rock));
    }

    [Fact]
    public void DoorsSitAlongTheirEdgesNotAlwaysInTheMiddle()
    {
        List<RoomSetup> all = SeedValues.SelectMany(seed => Generate(seed).Rooms).ToList();

        Assert.All(all.SelectMany(room => room.DoorTiles!.Select(door => (room, door))), pair =>
            Assert.Equal(pair.door.Value, pair.room.Template.Layout.EdgeTile(pair.door.Key, pair.door.Key is Direction.North or Direction.South ? pair.door.Value.X : pair.door.Value.Y)));
        Assert.Contains(all, room => room.DoorTiles!.Any(door => door.Value != room.Template.Layout.MiddleOf(door.Key)));
    }

    [Fact]
    public void TheSameSeedBuildsTheSameDungeon()
    {
        static string Describe(RunSetup run) => string.Join("|", run.Rooms.Select(room =>
            $"{room.Id}:{room.Role}:{room.Trap}:{string.Join(",", room.Doors!.OrderBy(door => door.Key).Select(door => $"{door.Key}>{door.Value}"))}:" +
            string.Join(",", room.Template.Monsters.Select(spawn => $"{spawn.Monster.Id}@{spawn.Position}")) + ":" +
            string.Join("", RoomGrid.AllPositions().Select(position => (int)room.Template.Layout[position]))));

        Assert.Equal(Describe(Generate(123)), Describe(Generate(123)));
        Assert.NotEqual(Describe(Generate(123)), Describe(Generate(124)));
    }

    [Fact]
    public void BiggerPartiesFaceMoreMonsters()
    {
        int Threat(int partySize) => SeedValues.Sum(seed => Generate(seed, partySize).Rooms
            .SelectMany(room => room.Template.Monsters)
            .Where(spawn => !spawn.Monster.Boss)
            .Sum(spawn => spawn.Monster.Threat));

        Assert.True(Threat(4) > Threat(1));
    }

    [Fact]
    public void DeeperFloorsHaveTrapRooms()
    {
        RunSetup run = Generate(5);

        Assert.DoesNotContain(run.Rooms, room => room.Floor == 1 && room.Trap);
        Assert.Single(run.Rooms, room => room.Floor == 2 && room.Trap);
        Assert.All(run.Rooms.Where(room => room.Trap), room => Assert.Equal(RoomRole.Combat, room.Role));
    }

    [Fact]
    public void ShippedThemeHasLayoutsForEveryRole()
    {
        IReadOnlyList<ThemeRoom> rooms = Content.RoomsFor("emerald_keep");

        // Secret rooms use treasure layouts, and the den has its own.
        Assert.All(Enum.GetValues<RoomRole>().Where(role => role is not (RoomRole.Secret or RoomRole.Den)), role => Assert.Contains(rooms, room => room.Roles.Contains(role)));
    }
}
