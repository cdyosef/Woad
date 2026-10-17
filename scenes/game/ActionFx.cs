using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// A fighting-game callout over the board (HIT!, MISS, BLOCKED!, K.O.!, STUN!...): big slanted
/// letters on a dark outline that slam in oversized, hold a moment, then slide off and fade.
/// </summary>
public partial class ActionCallout : Node2D
{
    private const double SlamSeconds = 0.09;
    private const double HoldSeconds = 0.4;
    private const double LeaveSeconds = 0.25;

    private static readonly Vector2[] Outline =
        [new(-2, 0), new(2, 0), new(0, -2), new(0, 2), new(-1, -1), new(1, 1), new(-1, 1), new(1, -1), new(-2, 1), new(2, 1), new(0, 3)];

    private double _age;

    public string Text { get; init; } = "";

    public Color Color { get; init; } = Colors.White;

    /// <summary>Multiples of the heading font's pixel size: 2 for most callouts, 3 for K.O.! and DOWN!.</summary>
    public int Size { get; init; } = 2;

    public override void _Ready() => ZIndex = 20;

    public override void _Process(double delta)
    {
        _age += delta;
        if (_age >= SlamSeconds + HoldSeconds + LeaveSeconds)
        {
            QueueFree();
            return;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        Font font = GameArt.HeadingFont ?? ThemeDB.FallbackFont;
        int fontSize = UiTheme.HeadingPixelSize * Size;
        float width = font.GetStringSize(Text, fontSize: fontSize).X;

        // Slam: from double size down to normal with a slight overshoot. Leave: slide right and fade.
        float scale = 1, slide = 0, alpha = 1;
        if (_age < SlamSeconds)
        {
            float t = (float)(_age / SlamSeconds);
            scale = Mathf.Lerp(2.2f, 0.92f, t);
        }
        else if (_age < SlamSeconds + 0.06)
        {
            scale = Mathf.Lerp(0.92f, 1f, (float)((_age - SlamSeconds) / 0.06));
        }
        else if (_age > SlamSeconds + HoldSeconds)
        {
            float t = (float)((_age - SlamSeconds - HoldSeconds) / LeaveSeconds);
            slide = 18 * t * t;
            alpha = 1 - t;
        }

        DrawSetTransformMatrix(new Transform2D(0, new Vector2(scale, scale), -0.28f, new Vector2(slide, 0)));
        Vector2 at = new(-width / 2, fontSize * 0.35f);
        foreach (Vector2 offset in Outline)
        {
            DrawString(font, at + offset, Text, fontSize: fontSize, modulate: Palette.Ink with { A = alpha });
        }

        // A darker band under the letters for depth, then the letters, then a bright top edge.
        DrawString(font, at + new Vector2(1, 1), Text, fontSize: fontSize, modulate: Color.Darkened(0.5f) with { A = alpha });
        DrawString(font, at, Text, fontSize: fontSize, modulate: Color with { A = alpha });
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}

/// <summary>A strike landing on a tile: a bright slash across it and a burst of sparks, gone in a blink.</summary>
public partial class HitSpark : Node2D
{
    private const double Seconds = 0.35;

    private readonly List<(Vector2 Velocity, float Size)> _sparks = [];
    private double _age;

    /// <summary>The sparks' color: the attacker's (seat color, or the monster's), or blue off a shield.</summary>
    public Color Color { get; init; } = Colors.White;

    /// <summary>Draw the slash too (a hit); a block is sparks only.</summary>
    public bool Slash { get; init; } = true;

    /// <summary>The slash goes down to the right, or (false) down to the left.</summary>
    public bool Backhand { get; init; }

    public override void _Ready()
    {
        ZIndex = 15;
        int count = Slash ? 10 : 7;
        for (int i = 0; i < count; i++)
        {
            float angle = (float)(Random.Shared.NextDouble() * Mathf.Tau);
            float speed = 50 + (float)Random.Shared.NextDouble() * 70;
            _sparks.Add((Vector2.FromAngle(angle) * speed, 1.5f + (float)Random.Shared.NextDouble() * 1.5f));
        }
    }

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
        if (Slash && t < 0.6f)
        {
            // The streak shoots across, then thins out.
            float reach = Mathf.Min(1, t / 0.2f);
            float fade = t < 0.25f ? 1 : 1 - (t - 0.25f) / 0.35f;
            Vector2 from = new(Backhand ? 14 : -14, -14), to = new(Backhand ? -14 : 14, 14);
            Vector2 end = from.Lerp(to, reach);
            DrawLine(from, end, Color with { A = 0.6f * fade }, 6, antialiased: true);
            DrawLine(from, end, Colors.White with { A = fade }, 2.5f, antialiased: true);
        }

        foreach ((Vector2 velocity, float size) in _sparks)
        {
            Vector2 at = velocity * (float)_age * (1 - t * 0.5f);
            float s = size * (1 - t);
            DrawRect(new Rect2(at - new Vector2(s, s) / 2, new Vector2(s, s)), (t < 0.3f ? Colors.White : Color) with { A = 1 - t });
        }
    }
}
