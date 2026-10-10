using Godot;
using Woad.Core.Grid;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// A hero or monster standing on the board: its sprite (a hero's tunic tinted with their seat
/// color) on a small shadow, with heart pips underneath. Without art it falls back to a circle
/// (hero) or square (monster) with a letter.
/// </summary>
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

    /// <summary>The sprite; null falls back to a shape.</summary>
    public Texture2D? Body { get; init; }

    /// <summary>Drawn over the body, tinted with <see cref="Fill"/> (heroes' tunics).</summary>
    public Texture2D? Tint { get; init; }

    /// <summary>Shown instead of the body while collapsed (a skeleton's bone pile).</summary>
    public Texture2D? CollapsedBody { get; init; }

    /// <summary>Sprite size relative to a tile: smaller for slimelings, bigger for bosses.</summary>
    public float SpriteScale { get; init; } = 1;

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
        if (Body is not null)
        {
            DrawSprite();
        }
        else
        {
            DrawShape();
        }

        DrawHeartPips();

        if (_stunned)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(Center.X + 7, 6), "z", fontSize: 9, modulate: Palette.TrimBright);
        }
    }

    private void DrawSprite()
    {
        // A shadow under the feet; the active hero's glows gold.
        DrawSetTransform(new Vector2(BoardView.TileSize / 2f, 26), 0, new Vector2(1, 0.35f));
        DrawCircle(Vector2.Zero, 11, _active ? Palette.TrimBright with { A = 0.85f } : new Color(0, 0, 0, 0.35f), antialiased: true);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        float size = BoardView.TileSize * SpriteScale;
        var rect = new Rect2((BoardView.TileSize - size) / 2, 27 - size, size, size);
        if (_collapsed && CollapsedBody is not null)
        {
            DrawTextureRect(CollapsedBody, rect, tile: false);
            return;
        }

        DrawTextureRect(Body!, rect, tile: false, modulate: _collapsed ? new Color(0.5f, 0.5f, 0.5f) : Colors.White);
        if (Tint is not null)
        {
            DrawTextureRect(Tint, rect, tile: false, modulate: Fill);
        }
    }

    private void DrawShape()
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

        DrawString(ThemeDB.FallbackFont, new Vector2(0, Center.Y + 4), _collapsed ? "x" : Glyph, HorizontalAlignment.Center, BoardView.TileSize, 11, Palette.Ink);
    }

    private void DrawHeartPips()
    {
        if (_maxHearts <= 0)
        {
            return;
        }

        const float top = 28;
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
