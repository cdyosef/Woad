using Godot;
using Woad.Core.Grid;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// A hero or monster standing on the board: its sprite (a hero in their chosen look and colors,
/// on a ring of their seat color) on a small shadow, with heart pips underneath. Without art it falls back to a circle
/// (hero) or square (monster) with a letter.
/// </summary>
public partial class TokenView : Node2D
{
    private const float Radius = 10;
    private static readonly Vector2 Center = new(BoardView.TileSize / 2f, 14);

    private int _hearts;
    private int _maxHearts;
    private bool _active;
    private bool _frozen;
    private bool _collapsed;
    private (int Life, int Death)? _keeling;
    private int _fireDice;
    private int _stunMarks;
    private bool _carriesLoot;
    private bool _prone;
    private bool _diving;

    public int EntityId { get; init; }

    public bool IsHero { get; init; }

    public string Glyph { get; init; } = "";

    public Color Fill { get; init; } = Colors.White;

    /// <summary>The sprite; null falls back to a shape.</summary>
    public Texture2D? Body { get; init; }

    /// <summary>A hero's look and colors, drawn instead of <see cref="Body"/>; <see cref="Fill"/> is then their seat color.</summary>
    public LookColors? Look { get; init; }

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

    public bool Frozen
    {
        set { _frozen = value; QueueRedraw(); }
    }

    public bool Collapsed
    {
        set { _collapsed = value; QueueRedraw(); }
    }

    /// <summary>A monster carrying stolen loot: a little gold sack at its side.</summary>
    public bool CarriesLoot
    {
        set { _carriesLoot = value; QueueRedraw(); }
    }

    /// <summary>Stun on it: one star for each Stun, three for each Powerful Stun, in the top corner.</summary>
    public int StunMarks
    {
        set { _stunMarks = value; QueueRedraw(); }
    }

    /// <summary>Fire dice burning on it: a flame and the count in the corner.</summary>
    public int FireDice
    {
        set { _fireDice = value; QueueRedraw(); }
    }

    /// <summary>Lying down (prone): drawn on its side.</summary>
    public bool Prone
    {
        set { _prone = value; QueueRedraw(); }
    }

    /// <summary>Prone in deep water: drawn half under the surface.</summary>
    public bool Diving
    {
        set { _diving = value; QueueRedraw(); }
    }

    /// <summary>Keeling Over: lying on their side with X eyes, marks of life and death shown instead of hearts. Null when standing.</summary>
    public (int Life, int Death)? Keeling
    {
        set { _keeling = value; QueueRedraw(); }
    }

    public override void _Draw()
    {
        if ((Look is not null && Looks.Strip(Look.Look) is not null) || Body is not null)
        {
            DrawSprite();
        }
        else
        {
            DrawShape();
        }

        if (_keeling is { } marks)
        {
            DrawMarks(marks.Life, marks.Death);
        }
        else
        {
            DrawHeartPips();
        }

        if (_frozen)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(Center.X + 7, 6), "z", fontSize: 9, modulate: Palette.TrimBright);
        }

        if (_fireDice > 0)
        {
            DrawFlame();
        }

        if (_stunMarks > 0)
        {
            DrawStars();
        }

        if (_carriesLoot)
        {
            var sack = new Vector2(BoardView.TileSize - 8, 18);
            DrawCircle(sack, 4, Palette.Loot);
            DrawRect(new Rect2(sack + new Vector2(-1.5f, -6), new Vector2(3, 3)), Palette.Loot);
            DrawArc(sack, 4, 0, Mathf.Tau, 12, Palette.Ink, 1);
        }
    }

    private void DrawSprite()
    {
        if (_keeling is not null || _prone)
        {
            DrawFallen(xEyes: _keeling is not null);
            return;
        }

        // A shadow under the feet; the active hero's glows gold.
        DrawSetTransform(new Vector2(BoardView.TileSize / 2f, 26), 0, new Vector2(1, 0.35f));
        DrawCircle(Vector2.Zero, 11, _active ? Palette.TrimBright with { A = 0.85f } : new Color(0, 0, 0, 0.35f), antialiased: true);
        if (Look is not null)
        {
            DrawArc(Vector2.Zero, 11, 0, Mathf.Tau, 24, Fill, 2.5f, antialiased: true);
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        float size = BoardView.TileSize * SpriteScale;
        var rect = new Rect2((BoardView.TileSize - size) / 2, 27 - size, size, size);
        if (_collapsed && CollapsedBody is not null)
        {
            DrawTextureRect(CollapsedBody, rect, tile: false);
            return;
        }

        DrawFigure(rect, _collapsed ? new Color(0.5f, 0.5f, 0.5f) : Colors.White);
    }

    private void DrawFigure(Rect2 rect, Color modulate)
    {
        if (Look is null || !Looks.Draw(this, rect, Look, modulate))
        {
            DrawTextureRect(Body!, rect, tile: false, modulate: modulate);
        }
    }

    /// <summary>
    /// Lying down: the sprite tipped onto its side, head to the left. Keeling Over, with an X over each
    /// eye (the look's eye pixels); diving, half under the water.
    /// </summary>
    private void DrawFallen(bool xEyes)
    {
        const float pixel = BoardView.TileSize / 16f;
        DrawSetTransform(new Vector2(BoardView.TileSize / 2f, 24), 0, new Vector2(1.2f, 0.3f));
        DrawCircle(Vector2.Zero, 12, _active ? Palette.TrimBright with { A = 0.85f } : new Color(0, 0, 0, 0.35f), antialiased: true);
        if (Look is not null)
        {
            DrawArc(Vector2.Zero, 12, 0, Mathf.Tau, 24, Fill, 2.5f, antialiased: true);
        }

        DrawSetTransform(new Vector2(BoardView.TileSize / 2f, 17), -Mathf.Pi / 2, Vector2.One);
        float size = BoardView.TileSize;
        var rect = new Rect2(-size / 2, -size / 2, size, size);
        DrawFigure(rect, Colors.White);

        foreach (Vector2I eye in xEyes ? Look?.Look.Eyes ?? [new(6, 5), new(9, 5)] : [])
        {
            Vector2 at = rect.Position + new Vector2(eye.X + 0.5f, eye.Y + 0.5f) * pixel;
            DrawLine(at - new Vector2(1, 1) * pixel, at + new Vector2(1, 1) * pixel, Palette.Ink, 1.2f, antialiased: true);
            DrawLine(at + new Vector2(-1, 1) * pixel, at + new Vector2(1, -1) * pixel, Palette.Ink, 1.2f, antialiased: true);
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        if (_diving)
        {
            DrawRect(new Rect2(2, 16, BoardView.TileSize - 4, 10), Palette.DeepWater with { A = 0.75f });
            DrawLine(new Vector2(5, 16), new Vector2(BoardView.TileSize - 5, 16), Palette.Text with { A = 0.5f }, 1);
        }
    }

    /// <summary>Little stars circling over its head, with the count past three.</summary>
    private void DrawStars()
    {
        int shown = Math.Min(_stunMarks, 3);
        for (int i = 0; i < shown; i++)
        {
            Vector2 at = new(BoardView.TileSize / 2f - (shown - 1) * 4 + i * 8, 2);
            DrawColoredPolygon([at + new Vector2(0, -3), at + new Vector2(1, -1), at + new Vector2(3, 0), at + new Vector2(1, 1), at + new Vector2(0, 3), at + new Vector2(-1, 1), at + new Vector2(-3, 0), at + new Vector2(-1, -1)], Palette.TrimBright);
        }

        if (_stunMarks > 3)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(BoardView.TileSize / 2f + 12, 6), _stunMarks.ToString(), fontSize: 8, modulate: Palette.TrimBright);
        }
    }

    /// <summary>A small flame in the top corner, with how many fire dice burn.</summary>
    private void DrawFlame()
    {
        var at = new Vector2(3, 3);
        DrawColoredPolygon([at + new Vector2(4, 0), at + new Vector2(8, 6), at + new Vector2(6, 10), at + new Vector2(2, 10), at + new Vector2(0, 6)], Palette.Fire);
        DrawColoredPolygon([at + new Vector2(4, 4), at + new Vector2(6, 7), at + new Vector2(5, 10), at + new Vector2(3, 10), at + new Vector2(2, 7)], Palette.FireCore);
        if (_fireDice > 1)
        {
            DrawString(ThemeDB.FallbackFont, at + new Vector2(9, 10), _fireDice.ToString(), fontSize: 8, modulate: Palette.FireCore);
        }
    }

    /// <summary>Instead of hearts: three pips for marks of life (green) and three for marks of death (bone white on black).</summary>
    private void DrawMarks(int life, int death)
    {
        const float top = 28;
        for (int i = 0; i < 3; i++)
        {
            var lifePip = new Rect2(Center.X - 13 + i * 4, top, 3, 3);
            DrawRect(lifePip, i < life ? Palette.Heal : Palette.Heal.Darkened(0.7f));
            var deathPip = new Rect2(Center.X + 2 + i * 4, top, 3, 3);
            DrawRect(deathPip, i < death ? Palette.Text : Palette.TextDim.Darkened(0.6f));
            if (i < death)
            {
                DrawRect(deathPip, Palette.Ink, filled: false, width: 1);
            }
        }
    }

    private void DrawShape()
    {
        Color fill = _collapsed || _keeling is not null ? Fill.Darkened(0.55f) : Fill;
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

        DrawString(ThemeDB.FallbackFont, new Vector2(0, Center.Y + 4), _collapsed || _keeling is not null ? "x" : Glyph, HorizontalAlignment.Center, BoardView.TileSize, 11, Palette.Ink);
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
