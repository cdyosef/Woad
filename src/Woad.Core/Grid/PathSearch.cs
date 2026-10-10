namespace Woad.Core.Grid;

/// <summary>Breadth-first search over orthogonal steps inside one room.</summary>
public sealed class PathSearch
{
    private readonly Dictionary<GridPosition, int> _distances;
    private readonly Dictionary<GridPosition, GridPosition> _parents;

    private PathSearch(GridPosition start, Dictionary<GridPosition, int> distances, Dictionary<GridPosition, GridPosition> parents)
    {
        Start = start;
        _distances = distances;
        _parents = parents;
    }

    public GridPosition Start { get; }

    /// <summary>Steps needed to reach each reachable tile, including the start at distance 0.</summary>
    public IReadOnlyDictionary<GridPosition, int> Distances => _distances;

    /// <param name="start">Where the search begins. It is never tested with <paramref name="canEnter"/>.</param>
    /// <param name="canEnter">Whether a tile may be stepped onto or through.</param>
    /// <param name="maxSteps">Tiles further away than this are not explored.</param>
    public static PathSearch Run(GridPosition start, Func<GridPosition, bool> canEnter, int maxSteps = int.MaxValue)
    {
        var distances = new Dictionary<GridPosition, int> { [start] = 0 };
        var parents = new Dictionary<GridPosition, GridPosition>();
        var frontier = new Queue<GridPosition>();
        frontier.Enqueue(start);

        while (frontier.Count > 0)
        {
            GridPosition current = frontier.Dequeue();
            int distance = distances[current];
            if (distance >= maxSteps)
            {
                continue;
            }

            foreach (GridPosition next in current.OrthogonalNeighbors())
            {
                if (next.IsInRoom && !distances.ContainsKey(next) && canEnter(next))
                {
                    distances[next] = distance + 1;
                    parents[next] = current;
                    frontier.Enqueue(next);
                }
            }
        }

        return new PathSearch(start, distances, parents);
    }

    public bool CanReach(GridPosition position) => _distances.ContainsKey(position);

    /// <summary>The steps from the start to <paramref name="goal"/>, excluding the start and including the goal.</summary>
    public IReadOnlyList<GridPosition> PathTo(GridPosition goal)
    {
        if (!CanReach(goal))
        {
            throw new InvalidOperationException($"{goal} is not reachable from {Start}.");
        }

        var path = new List<GridPosition>();
        for (GridPosition at = goal; at != Start; at = _parents[at])
        {
            path.Add(at);
        }

        path.Reverse();
        return path;
    }
}
