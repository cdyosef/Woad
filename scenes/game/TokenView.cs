using Godot;
using Woad.Core.Grid;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>A hero (circle) or monster (square) standing on the board, with heart pips underneath.</summary>
public partial class TokenView : Node2D
{
    private const float Radius = 10;
    private static readonly Vector2 Center = new(BoardView.TileSize / 2f, 14);

    private int _hearts;
    private int _maxHearts;
    private bool _active;
    private bool _stunned;
    private bool _collapsed;

    public int EntityId { get; init; }

    public bool IsHero { get; init; }

    public string Glyph { get; init; } = "";

    public Color Fill { get; init; } = Colors.White;

    public void SetHearts(int hearts, int maxHearts)
    {
        _hearts = hearts;
        _maxHearts = maxHearts;
        QueueRedraw();
    }

    public void SetHearts(int hearts) => SetHearts(hearts, _maxHearts);

    public GridPosition Tile =>
        new(Mathf.RoundToInt(Position.X / BoardView.TileSize), Mathf.RoundToInt(Position.Y / BoardView.TileSize));

    public bool Active
    {
        set { _active = value; QueueRedraw(); }
    }

    public bool Stunned
    {
        set { _stunned = value; QueueRedraw(); }
    }

    public bool Collapsed
    {
        set { _collapsed = value; QueueRedraw(); }
    }

    public override void _Draw()
    {
        Color fill = _collapsed ? Fill.Darkened(0.55f) : Fill;
        if (_active)
        {
            DrawArc(Center, Radius + 3, 0, Mathf.Tau, 32, Palette.TrimBright, 1.5f, antialiased: true);
        }

        if (IsHero)
        {
            DrawCircle(Center, Radius, fill, antialiased: true);
            DrawArc(Center, Radius, 0, Mathf.Tau, 32, Palette.Ink, 1.5f, antialiased: true);
        }
        else
        {
            var body = new Rect2(Center - new Vector2(Radius - 1, Radius - 1), new Vector2(Radius * 2 - 2, Radius * 2 - 2));
            DrawRect(body, fill);
            DrawRect(body, Palette.Ink, filled: false, width: 1.5f);
        }

        Font font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(0, Center.Y + 4), _collapsed ? "x" : Glyph, HorizontalAlignment.Center, BoardView.TileSize, 11, Palette.Ink);

        DrawHeartPips();

        if (_stunned)
        {
            DrawString(font, new Vector2(Center.X + 6, 6), "z", fontSize: 9, modulate: Palette.TrimBright);
        }
    }

    private void DrawHeartPips()
    {
        if (_maxHearts <= 0)
        {
            return;
        }

        const float top = 27;
        const float width = 24;
        float left = Center.X - width / 2;

        if (_maxHearts > 6)
        {
            DrawRect(new Rect2(left, top, width, 3), Palette.HeartEmpty);
            DrawRect(new Rect2(left, top, width * _hearts / _maxHearts, 3), Palette.Heart);
            return;
        }

        float pip = (width - (_maxHearts - 1)) / _maxHearts;
        for (int i = 0; i < _maxHearts; i++)
        {
            DrawRect(new Rect2(left + i * (pip + 1), top, pip, 3), i < _hearts ? Palette.Heart : Palette.HeartEmpty);
        }
    }
}
