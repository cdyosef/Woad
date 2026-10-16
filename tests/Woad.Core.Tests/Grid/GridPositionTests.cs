using Woad.Core.Grid;

namespace Woad.Core.Tests.Grid;

public class GridPositionTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(8, 8, true)]
    [InlineData(4, 4, true)]
    [InlineData(-1, 0, false)]
    [InlineData(0, 9, false)]
    [InlineData(9, 4, false)]
    public void AStandardRoomKnowsWhichTilesAreInside(int x, int y, bool expected)
    {
        Assert.Equal(expected, RoomLayout.Empty.Contains(new GridPosition(x, y)));
    }

    [Fact]
    public void MeasuresManhattanAndChebyshevDistance()
    {
        var from = new GridPosition(1, 2);
        var to = new GridPosition(5, 5);

        Assert.Equal(7, from.ManhattanDistanceTo(to));
        Assert.Equal(4, from.ChebyshevDistanceTo(to));
    }

    [Fact]
    public void HasFourOrthogonalNeighbors()
    {
        var neighbors = new GridPosition(4, 4).OrthogonalNeighbors();

        Assert.Equal(
            [new GridPosition(4, 3), new GridPosition(5, 4), new GridPosition(4, 5), new GridPosition(3, 4)],
            neighbors);
    }

    [Fact]
    public void ListsSurroundingTilesOrthogonalFirst()
    {
        var tiles = new GridPosition(4, 4).SurroundingTiles().ToList();

        Assert.Equal(8, tiles.Distinct().Count());
        Assert.All(tiles.Take(4), tile => Assert.Equal(1, tile.ManhattanDistanceTo(new GridPosition(4, 4))));
        Assert.All(tiles.Skip(4), tile => Assert.Equal(2, tile.ManhattanDistanceTo(new GridPosition(4, 4))));
    }

    [Theory]
    [InlineData(Direction.North, 4, 0)]
    [InlineData(Direction.East, 8, 4)]
    [InlineData(Direction.South, 4, 8)]
    [InlineData(Direction.West, 0, 4)]
    public void DoorsSitInTheMiddleOfEachSide(Direction side, int x, int y)
    {
        Assert.Equal(new GridPosition(x, y), side.DoorTile());
        Assert.Equal(side, side.Opposite().Opposite());
    }
}
