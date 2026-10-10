using Woad.Core.Grid;
using Woad.Core.Randomness;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Grid;

public class LineOfSightTests
{
    [Fact]
    public void OpenRoomsAreClear()
    {
        Assert.True(LineOfSight.IsClear(RoomLayout.Empty, new GridPosition(0, 0), new GridPosition(8, 8)));
        Assert.True(LineOfSight.IsClear(RoomLayout.Empty, new GridPosition(0, 8), new GridPosition(8, 1)));
    }

    [Fact]
    public void WallsInTheWayBlockSight()
    {
        RoomLayout layout = TestRun.Room("""
            .........
            .........
            .........
            .........
            ....#....
            .........
            .........
            .........
            .........
            """).Layout;

        Assert.False(LineOfSight.IsClear(layout, new GridPosition(4, 0), new GridPosition(4, 8)));
        Assert.False(LineOfSight.IsClear(layout, new GridPosition(0, 4), new GridPosition(8, 4)));
        Assert.False(LineOfSight.IsClear(layout, new GridPosition(3, 3), new GridPosition(5, 5)));
        Assert.True(LineOfSight.IsClear(layout, new GridPosition(3, 0), new GridPosition(3, 8)));
    }

    [Fact]
    public void GrazingAWallCornerIsAllowed()
    {
        RoomLayout layout = TestRun.Room("""
            .#.......
            #........
            .........
            .........
            .........
            .........
            .........
            .........
            .........
            """).Layout;

        Assert.True(LineOfSight.IsClear(layout, new GridPosition(0, 0), new GridPosition(2, 2)));
    }

    [Fact]
    public void PitsAndChestsDontBlockSight()
    {
        RoomLayout layout = TestRun.Room("""
            .........
            .........
            .........
            .........
            .~~~C....
            .........
            .........
            .........
            .........
            """).Layout;

        Assert.True(LineOfSight.IsClear(layout, new GridPosition(0, 4), new GridPosition(8, 4)));
    }

    [Fact]
    public void SightIsAlwaysSymmetric()
    {
        var random = new Pcg32(2026);
        for (int layoutNumber = 0; layoutNumber < 5; layoutNumber++)
        {
            var tiles = Enumerable.Range(0, RoomGrid.Size * RoomGrid.Size)
                .Select(_ => random.NextInt(0, 5) == 0 ? TileKind.Wall : TileKind.Floor)
                .ToArray();
            var layout = new RoomLayout(tiles);

            foreach (GridPosition a in RoomGrid.AllPositions())
            {
                foreach (GridPosition b in RoomGrid.AllPositions())
                {
                    Assert.Equal(LineOfSight.IsClear(layout, a, b), LineOfSight.IsClear(layout, b, a));
                }
            }
        }
    }
}
