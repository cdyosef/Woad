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
    public void KnowsWhetherItIsInsideTheRoom(int x, int y, bool expected)
    {
        Assert.Equal(expected, new GridPosition(x, y).IsInRoom);
    }

    [Fact]
    public void MeasuresManhattanDistance()
    {
        Assert.Equal(7, new GridPosition(1, 2).ManhattanDistanceTo(new GridPosition(5, 5)));
    }

    [Fact]
    public void HasFourOrthogonalNeighbors()
    {
        var neighbors = new GridPosition(4, 4).OrthogonalNeighbors();

        Assert.Equal(
            [new GridPosition(4, 3), new GridPosition(5, 4), new GridPosition(4, 5), new GridPosition(3, 4)],
            neighbors);
    }
}
