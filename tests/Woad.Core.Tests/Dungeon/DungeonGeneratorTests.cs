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
    public void EveryFloorHasSixToEightConnectedRooms(ulong seed)
    {
        RunSetup run = Generate(seed);

        foreach (IGrouping<int, RoomSetup> floor in run.Rooms.GroupBy(room => room.Floor))
        {
            Assert.InRange(floor.Count(), DungeonGenerator.MinRooms, DungeonGenerator.MaxRooms);

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
                Assert.Equal(TileKind.Floor, room.Template.Layout[side.DoorTile()]);
            }
        }
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

        Assert.All(Enum.GetValues<RoomRole>(), role => Assert.Contains(rooms, room => room.Roles.Contains(role)));
    }
}
