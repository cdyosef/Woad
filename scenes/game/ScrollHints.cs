using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Over the board's frame: a gently pulsing arrow at each edge the room runs on past, so a room
/// bigger than the view shows there's more of it to pan to (right-drag or the arrow keys).
/// </summary>
public partial class ScrollHints : Control
{
    private const float Half = 6;

    private (bool Left, bool Up, bool Right, bool Down) _shown;
    private float _time;

    public BoardView Board { get; init; } = null!;

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Process(double delta)
    {
        _time += (float)delta;
        (bool, bool, bool, bool) more = Board.MoreBeyond;
        if (more != _shown || more != (false, false, false, false))
        {
            _shown = more;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        float nudge = 2 * Mathf.Sin(_time * 4);
        float alpha = 0.7f + 0.2f * Mathf.Sin(_time * 4);
        Vector2 middle = Size / 2;
        if (_shown.Left)
        {
            Arrow(new Vector2(6 - nudge, middle.Y), Vector2.Left, alpha);
        }

        if (_shown.Right)
        {
            Arrow(new Vector2(Size.X - 6 + nudge, middle.Y), Vector2.Right, alpha);
        }

        if (_shown.Up)
        {
            Arrow(new Vector2(middle.X, 6 - nudge), Vector2.Up, alpha);
        }

        if (_shown.Down)
        {
            Arrow(new Vector2(middle.X, Size.Y - 6 + nudge), Vector2.Down, alpha);
        }
    }

    /// <summary>A small gold triangle on a dark outline, its tip at <paramref name="tip"/> pointing <paramref name="way"/>.</summary>
    private void Arrow(Vector2 tip, Vector2 way, float alpha)
    {
        Vector2 side = new(-way.Y, way.X);
        Vector2[] points = [tip, tip - way * Half + side * Half, tip - way * Half - side * Half];
        Vector2[] outline = [tip + way * 1.5f, tip - way * (Half + 1) + side * (Half + 1.5f), tip - way * (Half + 1) - side * (Half + 1.5f)];
        DrawColoredPolygon(outline, new Color(Palette.Ink, alpha));
        DrawColoredPolygon(points, new Color(Palette.TrimBright, alpha));
    }
}
