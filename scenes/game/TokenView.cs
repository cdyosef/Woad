using Godot;
using Woad.Core.Grid;
using Woad.Core.Model;
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
    private float _time;
    private bool _frozen;
    private bool _collapsed;
    private (int Life, int Death)? _keeling;
    private int _fireDice;
    private int _stunMarks;
    private bool _carriesLoot;
    private bool _torch;
    private bool _prone;
    private bool _diving;
    private IReadOnlyDictionary<Condition, int> _conditions = new Dictionary<Condition, int>();

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

    /// <summary>A swimming monster: in deep water it's drawn under the surface, only its top showing.</summary>
    public bool Swimmer { get; init; }

    private bool _submerged;

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

    /// <summary>Holding a lit torch: a warm glow around them, and a flame at the hand.</summary>
    public bool Torch
    {
        set { _torch = value; QueueRedraw(); }
    }

    /// <summary>A merchant chained to the wall: a shackle and chain at their feet.</summary>
    public bool Chained
    {
        set { _chained = value; QueueRedraw(); }
    }

    private bool _chained;

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

    /// <summary>
    /// Tokens typed dice left on it: a colored pip for each kind down its left side (with the count when
    /// there's more than one), a glow while it shines, ice over it while it freezes, earth over it while
    /// buried, and Black Hearts among its heart pips.
    /// </summary>
    public IReadOnlyDictionary<Condition, int> Conditions
    {
        set { _conditions = value; QueueRedraw(); }
    }

    private int Count(Condition condition) => _conditions.GetValueOrDefault(condition);

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

    public override void _Process(double delta)
    {
        if (_torch && !_active)
        {
            _time += (float)delta;
            QueueRedraw();
        }

        if (_active)
        {
            _time += (float)delta;
            QueueRedraw();
        }

        // A swimmer goes under as it moves into deep water and comes up out of it.
        bool submerged = Swimmer && !_collapsed && GetParent() is BoardView board && board.IsDeepWater(Tile);
        if (submerged != _submerged)
        {
            _submerged = submerged;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (Count(Condition.Shine) > 0)
        {
            // It shines: a pale glow around it, brighter for each token.
            var middle = new Vector2(BoardView.TileSize / 2f, BoardView.TileSize / 2f);
            for (int ring = 3; ring >= 1; ring--)
            {
                DrawCircle(middle, 8 + ring * 5, DieTypes.Tint(Condition.Shine) with { A = 0.07f * Math.Min(3, Count(Condition.Shine)) }, antialiased: true);
            }
        }

        if (_torch)
        {
            // The light the torch throws, flickering, under everything else.
            float flicker = 0.04f * Mathf.Sin(_time * 11) + 0.03f * Mathf.Sin(_time * 7.3f);
            var middle = new Vector2(BoardView.TileSize / 2f, BoardView.TileSize / 2f);
            for (int ring = 3; ring >= 1; ring--)
            {
                DrawCircle(middle, 16 + ring * 10, Palette.FireCore with { A = 0.05f + flicker * 0.5f }, antialiased: true);
            }
        }

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
            DrawString(ThemeDB.FallbackFont, new Vector2(Center.X + 7, 6), "z", fontSize: UiTheme.FontSize, modulate: Palette.TrimBright);
        }

        DrawConditions();

        if (_fireDice > 0)
        {
            DrawFlame();
        }

        if (_stunMarks > 0)
        {
            DrawStars();
        }

        if (_chained)
        {
            // A shackle at the ankle and a chain of links running off to the wall.
            Color iron = new(0.55f, 0.57f, 0.62f);
            DrawArc(new Vector2(BoardView.TileSize / 2f, 25), 3, 0, Mathf.Tau, 10, iron, 1.5f);
            for (int link = 0; link < 4; link++)
            {
                DrawArc(new Vector2(BoardView.TileSize / 2f - 5 - link * 4, 26), 2, 0, Mathf.Tau, 8, iron, 1);
            }
        }

        if (_torch)
        {
            // The torch's flame at their hand.
            var hand = new Vector2(BoardView.TileSize - 7, 15);
            float lick = 1.5f * Mathf.Sin(_time * 13);
            DrawColoredPolygon([hand + new Vector2(-3, 0), hand + new Vector2(3, 0), hand + new Vector2(0.5f, -8 - lick)], Palette.Fire);
            DrawColoredPolygon([hand + new Vector2(-1.5f, 0), hand + new Vector2(1.5f, 0), hand + new Vector2(0, -4 - lick / 2)], Palette.FireCore);
            DrawLine(hand, hand + new Vector2(-2, 6), new Color(0.45f, 0.3f, 0.18f), 2);
        }

        if (_active)
        {
            // Whose turn it is: a gold arrow bobbing over their head.
            float bob = 2 * Mathf.Sin(_time * 5);
            var tip = new Vector2(BoardView.TileSize / 2f, -1 + bob);
            Vector2[] arrow = [tip, tip + new Vector2(-5, -6), tip + new Vector2(5, -6)];
            Vector2[] outline = [tip + new Vector2(0, 1.5f), tip + new Vector2(-6.5f, -7), tip + new Vector2(6.5f, -7)];
            DrawColoredPolygon(outline, Palette.Ink);
            DrawColoredPolygon(arrow, Palette.TrimBright);
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
        if (_submerged)
        {
            // Under the surface: the water covers all but its top, with a ripple where it breaks.
            DrawRect(new Rect2(1, 13, BoardView.TileSize - 2, 14), Palette.DeepWater with { A = 0.8f });
            DrawLine(new Vector2(4, 13), new Vector2(BoardView.TileSize - 4, 13), Palette.Text with { A = 0.6f }, 1);
        }
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
            DrawString(ThemeDB.FallbackFont, new Vector2(BoardView.TileSize / 2f + 12, 6), _stunMarks.ToString(), fontSize: UiTheme.FontSize, modulate: Palette.TrimBright);
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
            DrawString(ThemeDB.FallbackFont, at + new Vector2(9, 10), _fireDice.ToString(), fontSize: UiTheme.FontSize, modulate: Palette.FireCore);
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

        // Black Hearts are the last of the empty ones, drawn black.
        int black = Math.Min(Count(Condition.BlackHeart), _maxHearts - _hearts);
        float pip = (width - (_maxHearts - 1)) / _maxHearts;
        for (int i = 0; i < _maxHearts; i++)
        {
            Color color = i < _hearts ? Palette.Heart : i >= _maxHearts - black ? DieTypes.Tint(Condition.BlackHeart) : Palette.HeartEmpty;
            DrawRect(new Rect2(left + i * (pip + 1), top, pip, 3), color);
        }
    }

    private void DrawConditions()
    {
        if (Count(Condition.Freeze) > 0)
        {
            // Ice creeping over it: thicker the deeper it's frozen.
            float depth = Math.Min(1, Count(Condition.Freeze) / 3f);
            DrawRect(new Rect2(4, 30 - 24 * depth, BoardView.TileSize - 8, 24 * depth), DieTypes.Tint(Condition.Freeze) with { A = 0.35f });
        }

        if (Count(Condition.Buried) > 0)
        {
            // A mound of earth heaped over it.
            Color earth = DieTypes.Tint(Condition.Buried);
            DrawColoredPolygon([new Vector2(2, 30), new Vector2(8, 16), new Vector2(BoardView.TileSize / 2f, 12), new Vector2(BoardView.TileSize - 8, 16), new Vector2(BoardView.TileSize - 2, 30)], earth);
            DrawPolyline([new Vector2(2, 30), new Vector2(8, 16), new Vector2(BoardView.TileSize / 2f, 12), new Vector2(BoardView.TileSize - 8, 16), new Vector2(BoardView.TileSize - 2, 30)], earth.Darkened(0.4f), 1);
        }

        // A pip for each kind of token down the left side, the count beside it.
        float y = 5;
        foreach ((Condition condition, int count) in _conditions.Where(entry => entry.Key is not Condition.BlackHeart).OrderBy(entry => entry.Key))
        {
            var at = new Vector2(4, y);
            DrawCircle(at, 3.2f, Palette.Ink, antialiased: true);
            DrawCircle(at, 2.4f, DieTypes.Tint(condition), antialiased: true);
            if (count > 1)
            {
                DrawString(ThemeDB.FallbackFont, at + new Vector2(3, 3), count.ToString(), fontSize: 7, modulate: DieTypes.Tint(condition));
            }

            y += 7;
        }
    }
}
