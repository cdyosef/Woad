using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Grid;
using Woad.Core.Rules;

namespace Woad.Core.Tests.Dungeon;

/// <summary>The Sunken Temple's boss room, the Baths: a great pool filling most of it, its shallows reaching out to the doors.</summary>
public class BathsTests
{
    private static readonly ContentCatalog Content = ContentCatalog.LoadDefault();

    [Theory]
    [InlineData(1UL)]
    [InlineData(42UL)]
    [InlineData(2026UL)]
    [InlineData(31337UL)]
    public void TheBathsAreMostlyPoolWithShallowsByTheDoorsAndLeviathanJrInTheDeep(ulong seed)
    {
        RunSetup run = DungeonGenerator.CreateRun(Content, "sunken_temple", [new HeroSetup("Hero", GameRules.HeroBaseStats, GameRules.StarterKit)], seed);
        RoomSetup baths = run.Rooms.Single(room => room.Role == RoomRole.Boss);
        RoomLayout layout = baths.Template.Layout;
        int open = layout.Positions.Count(tile => layout[tile] != TileKind.Wall);
        int deep = layout.Positions.Count(tile => layout[tile] == TileKind.DeepWater);
        int water = deep + layout.Positions.Count(tile => layout[tile] == TileKind.ShallowWater);

        Assert.True(water * 2 > open, $"the pool fills most of the room ({water} of {open})");
        Assert.True(deep * 3 > open, $"most of the pool is deep ({deep} of {open})");
        Assert.Equal(TileKind.DeepWater, layout[baths.Template.Monsters.Single(spawn => spawn.Monster.Boss).Position]);
        Assert.Equal("Leviathan Jr.", baths.Template.Monsters.Single(spawn => spawn.Monster.Boss).Monster.Name);

        // Just inside each doorway, the shallows begin within a few steps.
        foreach (GridPosition door in baths.DoorTiles!.Values)
        {
            Assert.Contains(layout.Positions, tile => layout[tile] == TileKind.ShallowWater && Math.Abs(tile.X - door.X) + Math.Abs(tile.Y - door.Y) <= 4);
        }
    }
}
