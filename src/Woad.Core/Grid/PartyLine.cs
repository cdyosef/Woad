namespace Woad.Core.Grid;

/// <summary>
/// A party walking in a line, as on the Woarld Map and in a tavern: the leader steps, and each
/// follower steps into the place of the hero ahead of them.
/// </summary>
public sealed class PartyLine(GridPosition leader)
{
    private static readonly Direction[] Directions = [Direction.North, Direction.East, Direction.South, Direction.West];

    private readonly List<GridPosition> _trail = [];

    public GridPosition Leader { get; private set; } = leader;

    /// <summary>Where the followers stand, nearest the leader first.</summary>
    public IReadOnlyList<GridPosition> Trail => _trail;

    /// <summary>A line as it was saved.</summary>
    public static PartyLine Restore(GridPosition leader, IEnumerable<GridPosition> trail)
    {
        var line = new PartyLine(leader);
        line._trail.AddRange(trail);
        return line;
    }

    /// <summary>Makes room for this many heroes: new followers join at the back, where the last one stands.</summary>
    public void SetSize(int size)
    {
        int followers = Math.Max(0, size - 1);
        while (_trail.Count > followers)
        {
            _trail.RemoveAt(_trail.Count - 1);
        }

        while (_trail.Count < followers)
        {
            _trail.Add(_trail.Count > 0 ? _trail[^1] : Leader);
        }
    }

    /// <summary>The leader steps to <paramref name="next"/>; everyone else follows.</summary>
    public void MoveTo(GridPosition next)
    {
        if (_trail.Count > 0)
        {
            _trail.Insert(0, Leader);
            _trail.RemoveAt(_trail.Count - 1);
        }

        Leader = next;
    }

    /// <summary>
    /// The shortest walk from <paramref name="from"/> to <paramref name="target"/> over tiles
    /// <paramref name="passable"/> allows: the steps in order (none if already there), or null if there's no way.
    /// </summary>
    public static IReadOnlyList<Direction>? PathTo(GridPosition from, GridPosition target, Func<GridPosition, bool> passable)
    {
        var cameFrom = new Dictionary<GridPosition, (GridPosition From, Direction Step)>();
        var frontier = new Queue<GridPosition>([from]);
        var seen = new HashSet<GridPosition> { from };
        while (frontier.Count > 0)
        {
            GridPosition tile = frontier.Dequeue();
            if (tile == target)
            {
                var path = new List<Direction>();
                while (tile != from)
                {
                    (GridPosition previous, Direction step) = cameFrom[tile];
                    path.Add(step);
                    tile = previous;
                }

                path.Reverse();
                return path;
            }

            foreach (Direction direction in Directions)
            {
                GridPosition next = tile.Step(direction);
                if (passable(next) && seen.Add(next))
                {
                    cameFrom[next] = (tile, direction);
                    frontier.Enqueue(next);
                }
            }
        }

        return null;
    }
}
