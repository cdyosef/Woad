using Woad.Core.Grid;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Grid;

public class PathSearchTests
{
    private static readonly RoomLayout Walled = TestRun.Room("""
        .........
        .........
        .........
        ..#####..
        .........
        .........
        .........
        .........
        .........
        """).Layout;

    private static PathSearch From(GridPosition start, int maxSteps = int.MaxValue) =>
        PathSearch.Run(start, position => Walled[position].IsWalkable(), maxSteps);

    [Fact]
    public void GoesAroundWalls()
    {
        PathSearch search = From(new GridPosition(4, 4));

        // Straight up is blocked by the wall row, so the route detours 3 tiles out and 3 back.
        Assert.Equal(8, search.Distances[new GridPosition(4, 2)]);
        Assert.Equal(1, search.Distances[new GridPosition(4, 5)]);
        Assert.Equal(0, search.Distances[new GridPosition(4, 4)]);
    }

    [Fact]
    public void PathsStepOneTileAtATime()
    {
        var start = new GridPosition(4, 4);
        var goal = new GridPosition(4, 2);

        IReadOnlyList<GridPosition> path = From(start).PathTo(goal);

        Assert.Equal(8, path.Count);
        Assert.Equal(goal, path[^1]);
        Assert.Equal(1, start.ManhattanDistanceTo(path[0]));
        for (int i = 1; i < path.Count; i++)
        {
            Assert.Equal(1, path[i - 1].ManhattanDistanceTo(path[i]));
            Assert.True(Walled[path[i]].IsWalkable());
        }
    }

    [Fact]
    public void MaxStepsLimitsTheSearch()
    {
        PathSearch search = From(new GridPosition(4, 4), maxSteps: 2);

        Assert.All(search.Distances.Values, distance => Assert.InRange(distance, 0, 2));
        Assert.False(search.CanReach(new GridPosition(4, 7)));
        Assert.True(search.CanReach(new GridPosition(4, 6)));
    }

    [Fact]
    public void UnreachableTilesHaveNoPath()
    {
        PathSearch search = From(new GridPosition(4, 4));

        Assert.False(search.CanReach(new GridPosition(4, 3)));
        Assert.Throws<InvalidOperationException>(() => search.PathTo(new GridPosition(4, 3)));
    }
}
