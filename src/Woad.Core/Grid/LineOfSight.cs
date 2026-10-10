namespace Woad.Core.Grid;

/// <summary>
/// Sight lines run between tile centers. A wall blocks only when the line passes through its
/// interior, so grazing a corner is allowed and sight is always symmetric.
/// </summary>
public static class LineOfSight
{
    private const double CornerTolerance = 1e-6;

    public static bool IsClear(RoomLayout layout, GridPosition from, GridPosition to)
    {
        int minX = Math.Min(from.X, to.X);
        int maxX = Math.Max(from.X, to.X);
        int minY = Math.Min(from.Y, to.Y);
        int maxY = Math.Max(from.Y, to.Y);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                var tile = new GridPosition(x, y);
                if (tile != from && tile != to && layout[tile].BlocksSight() && Crosses(from, to, tile))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // Liang-Barsky clip of the segment against the tile's square, shrunk slightly so corners don't count.
    private static bool Crosses(GridPosition from, GridPosition to, GridPosition tile)
    {
        double half = 0.5 - CornerTolerance;
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double enter = 0;
        double exit = 1;

        return Clip(-dx, from.X - (tile.X - half), ref enter, ref exit)
            && Clip(dx, tile.X + half - from.X, ref enter, ref exit)
            && Clip(-dy, from.Y - (tile.Y - half), ref enter, ref exit)
            && Clip(dy, tile.Y + half - from.Y, ref enter, ref exit);
    }

    private static bool Clip(double p, double q, ref double enter, ref double exit)
    {
        if (p == 0)
        {
            return q >= 0;
        }

        double t = q / p;
        if (p < 0)
        {
            if (t > exit)
            {
                return false;
            }

            enter = Math.Max(enter, t);
        }
        else
        {
            if (t < enter)
            {
                return false;
            }

            exit = Math.Min(exit, t);
        }

        return enter <= exit;
    }
}
