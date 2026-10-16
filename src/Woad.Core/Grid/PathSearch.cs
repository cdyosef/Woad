namespace Woad.Core.Grid;

/// <summary>
/// Cheapest-path search over orthogonal steps inside one room. Every step costs 1 unless a step cost
/// says otherwise (rough ground and water cost 2); equal costs are explored first come, first served,
/// so with no step costs it's a plain breadth-first search.
/// </summary>
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

    /// <summary>Steps (movement points) needed to reach each reachable tile, including the start at 0.</summary>
    public IReadOnlyDictionary<GridPosition, int> Distances => _distances;

    /// <param name="start">Where the search begins. It is never tested with <paramref name="canEnter"/>.</param>
    /// <param name="canEnter">Whether a tile may be stepped onto or through.</param>
    /// <param name="maxSteps">Tiles costing more than this to reach are not explored.</param>
    /// <param name="canPass">Whether a walk can go on past a tile it entered; false makes it a place to
    /// stop (a trap or a pit). The start can always be left. Null: every tile can be passed.</param>
    /// <param name="stepCost">What stepping onto a tile costs; null: 1 everywhere.</param>
    public static PathSearch Run(
        GridPosition start,
        Func<GridPosition, bool> canEnter,
        int maxSteps = int.MaxValue,
        Func<GridPosition, bool>? canPass = null,
        Func<GridPosition, int>? stepCost = null)
    {
        var distances = new Dictionary<GridPosition, int> { [start] = 0 };
        var parents = new Dictionary<GridPosition, GridPosition>();
        var frontier = new PriorityQueue<GridPosition, (int Cost, int Order)>();
        int order = 0;
        frontier.Enqueue(start, (0, order++));

        while (frontier.TryDequeue(out GridPosition current, out (int Cost, int Order) priority))
        {
            int distance = priority.Cost;
            if (distance > distances[current] || (current != start && canPass?.Invoke(current) == false))
            {
                continue;
            }

            foreach (GridPosition next in current.OrthogonalNeighbors())
            {
                if (next.X is < 0 or >= RoomGrid.MaxSize || next.Y is < 0 or >= RoomGrid.MaxSize || !canEnter(next))
                {
                    continue;
                }

                int cost = distance + (stepCost?.Invoke(next) ?? 1);
                if (cost <= maxSteps && (!distances.TryGetValue(next, out int known) || cost < known))
                {
                    distances[next] = cost;
                    parents[next] = current;
                    frontier.Enqueue(next, (cost, order++));
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
