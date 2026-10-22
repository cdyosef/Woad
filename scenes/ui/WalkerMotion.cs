using Godot;
using Woad.Core.Grid;

namespace Woad.Scenes.Ui;

/// <summary>
/// Where a line of walkers is drawn, gliding from tile to tile instead of jumping: each eases
/// toward its tile at walking pace, and bobs a little in its step. Positions are in tiles.
/// </summary>
public sealed class WalkerMotion
{
    /// <summary>How far a walker bobs up mid-step, in tiles.</summary>
    private const float BobHeight = 0.08f;

    /// <summary>Farther than this, a walker jumps straight there (a new map, the door, a resumed raid).</summary>
    private const float JumpDistance = 2.5f;

    private readonly List<Vector2> _shown = [];

    /// <summary>Seconds a step takes to walk: the time until the next step comes.</summary>
    public double StepSeconds { get; set; } = 0.13;

    /// <summary>Someone is still on their way to their tile.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>Where walker <paramref name="index"/> is drawn, in tiles (the top-left of their tile), bob included.</summary>
    public Vector2 Shown(int index)
    {
        Vector2 at = _shown[index];
        // Bobs up as it leaves a tile and settles as it arrives.
        float between = Math.Max(Math.Abs(at.X - MathF.Round(at.X)), Math.Abs(at.Y - MathF.Round(at.Y)));
        return at - new Vector2(0, MathF.Sin(between * MathF.PI) * BobHeight);
    }

    /// <summary>Where the leader is drawn, without the bob: what the camera follows.</summary>
    public Vector2 Leader => _shown.Count > 0 ? _shown[0] : Vector2.Zero;

    /// <summary>Eases everyone toward their tiles; true while anyone is still moving.</summary>
    public bool Update(IReadOnlyList<GridPosition> tiles, double delta)
    {
        while (_shown.Count > tiles.Count)
        {
            _shown.RemoveAt(_shown.Count - 1);
        }

        float reach = (float)(delta / Math.Max(0.01, StepSeconds));
        IsMoving = false;
        for (int i = 0; i < tiles.Count; i++)
        {
            var target = new Vector2(tiles[i].X, tiles[i].Y);
            if (i >= _shown.Count)
            {
                _shown.Add(target);
                continue;
            }

            Vector2 at = _shown[i];
            float distance = at.DistanceTo(target);
            _shown[i] = distance > JumpDistance || distance <= reach ? target : at + (target - at) / distance * reach;
            IsMoving |= _shown[i] != target;
        }

        return IsMoving;
    }
}
