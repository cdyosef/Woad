using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// A bomb going off: a white flash, then a ring of fire spreading over the tiles around the bomb and
/// a scatter of sparks, fading out in half a second. It frees itself when done.
/// </summary>
public partial class ExplosionView : Node2D
{
    private const double Seconds = 0.55;
    private static readonly Vector2[] Sparks = Enumerable.Range(0, 10)
        .Select(i => Vector2.FromAngle(i * Mathf.Tau / 10 + 0.3f) * (0.7f + i % 3 * 0.15f))
        .ToArray();

    private double _age;

    public override void _Process(double delta)
    {
        _age += delta;
        if (_age >= Seconds)
        {
            QueueFree();
            return;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        float t = (float)(_age / Seconds);
        float reach = BoardView.TileSize * 1.5f;
        if (t < 0.15f)
        {
            DrawCircle(Vector2.Zero, reach * 0.6f, Colors.White with { A = 1 - t / 0.15f });
        }

        float radius = reach * Mathf.Sqrt(t);
        DrawCircle(Vector2.Zero, radius, Palette.Fire with { A = 0.55f * (1 - t) });
        DrawCircle(Vector2.Zero, radius * 0.6f, Palette.FireCore with { A = 0.7f * (1 - t) });
        DrawArc(Vector2.Zero, radius, 0, Mathf.Tau, 32, Palette.Ink with { A = 0.6f * (1 - t) }, 2);
        foreach (Vector2 direction in Sparks)
        {
            DrawCircle(direction * reach * t * 1.1f, 2, Palette.FireCore with { A = 1 - t });
        }
    }
}
