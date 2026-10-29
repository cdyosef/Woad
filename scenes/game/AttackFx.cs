using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>What flies across the board on a ranged attack.</summary>
public enum ShotKind
{
    Arrow,
    Bolt,
    Boomerang,
    Dagger,

    /// <summary>A spell (or a scroll's, or a monster's magic): a glowing ball in the die type's color.</summary>
    Orb,
    Chain,
}

/// <summary>A shot in flight from one point on the board to another, gone when it gets there.</summary>
public partial class ShotFx : Node2D
{
    private readonly List<Vector2> _trail = [];
    private double _age;

    public Vector2 From { get; init; }

    public Vector2 To { get; init; }

    public ShotKind Kind { get; init; }

    public Color Color { get; init; } = Colors.White;

    /// <summary>How long it takes to get there: quicker for bolts, slower for a boomerang's curve.</summary>
    public double Seconds => Mathf.Clamp(From.DistanceTo(To) / Speed, 0.12, 0.45);

    private float Speed => Kind switch
    {
        ShotKind.Bolt => 520,
        ShotKind.Arrow => 420,
        ShotKind.Boomerang => 260,
        ShotKind.Orb => 300,
        _ => 360,
    };

    public override void _Ready() => ZIndex = 14;

    public override void _Process(double delta)
    {
        _age += delta;
        if (_age >= Seconds)
        {
            QueueFree();
            return;
        }

        _trail.Add(Where((float)(_age / Seconds)));
        if (_trail.Count > 7)
        {
            _trail.RemoveAt(0);
        }

        QueueRedraw();
    }

    /// <summary>Along the way: straight, but a boomerang bows out to one side.</summary>
    private Vector2 Where(float t)
    {
        Vector2 at = From.Lerp(To, t);
        if (Kind == ShotKind.Boomerang)
        {
            Vector2 side = (To - From).Orthogonal().Normalized();
            at += side * 14 * Mathf.Sin(t * Mathf.Pi);
        }

        return at;
    }

    public override void _Draw()
    {
        float t = (float)(_age / Seconds);
        Vector2 at = Where(t);
        Vector2 dir = (To - From).Normalized();
        switch (Kind)
        {
            case ShotKind.Arrow:
            case ShotKind.Bolt:
            {
                float length = Kind == ShotKind.Arrow ? 10 : 7;
                Vector2 tail = at - dir * length;
                DrawLine(tail - dir * 6, tail, Colors.White with { A = 0.25f }, 1);
                DrawLine(tail, at, new Color("c89a5a"), Kind == ShotKind.Arrow ? 1.5f : 2.5f);
                DrawColoredPolygon([at + dir * 3, at + dir.Orthogonal() * 2, at - dir.Orthogonal() * 2], new Color("d8dce4"));
                Color fletch = Kind == ShotKind.Arrow ? Color : new Color("8a5a2b");
                DrawLine(tail, tail - dir * 3 + dir.Orthogonal() * 2.5f, fletch, 1.5f);
                DrawLine(tail, tail - dir * 3 - dir.Orthogonal() * 2.5f, fletch, 1.5f);
                break;
            }

            case ShotKind.Boomerang:
            {
                DrawSetTransform(at, (float)_age * 28, Vector2.One);
                DrawLine(new Vector2(-5, -3), Vector2.Zero, new Color("c89a5a"), 3);
                DrawLine(Vector2.Zero, new Vector2(-5, 3), new Color("a87a3a"), 3);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                break;
            }

            case ShotKind.Dagger:
            {
                DrawSetTransform(at, (float)_age * 30, Vector2.One);
                DrawLine(new Vector2(-4, 0), new Vector2(4, 0), new Color("d8dce4"), 2);
                DrawLine(new Vector2(-4, -2), new Vector2(-4, 2), new Color("8a5a2b"), 2);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                break;
            }

            case ShotKind.Chain:
            {
                for (float d = 0; d < From.DistanceTo(at); d += 4)
                {
                    DrawArc(From + dir * d, 1.8f, 0, Mathf.Tau, 6, new Color(0.6f, 0.62f, 0.68f), 1);
                }

                break;
            }

            default:
            {
                // A glowing ball with a fading tail behind it.
                for (int i = 0; i < _trail.Count; i++)
                {
                    float fade = (i + 1f) / _trail.Count;
                    DrawCircle(_trail[i], 1.5f + 2.5f * fade, Color with { A = 0.35f * fade }, antialiased: true);
                }

                DrawCircle(at, 6, Color with { A = 0.3f }, antialiased: true);
                DrawCircle(at, 3.5f, Color, antialiased: true);
                DrawCircle(at, 1.5f, Colors.White, antialiased: true);
                break;
            }
        }

    }
}

/// <summary>A weapon's swing: a bright crescent sweeping round the attacker toward the target.</summary>
public partial class SwingFx : Node2D
{
    private const double Seconds = 0.2;
    private double _age;

    /// <summary>Toward the target.</summary>
    public float Angle { get; init; }

    public Color Color { get; init; } = Colors.White;

    /// <summary>A thrust (a spear) jabs straight out instead of sweeping round.</summary>
    public bool Thrust { get; init; }

    public bool Backhand { get; init; }

    public override void _Ready() => ZIndex = 14;

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
        float fade = t < 0.5f ? 1 : 1 - (t - 0.5f) / 0.5f;
        if (Thrust)
        {
            Vector2 dir = Vector2.FromAngle(Angle);
            float reach = 8 + 24 * Mathf.Min(1, t / 0.4f);
            DrawLine(dir * 6, dir * reach, Color with { A = 0.5f * fade }, 5, antialiased: true);
            DrawLine(dir * 6, dir * reach, Colors.White with { A = fade }, 1.5f, antialiased: true);
            return;
        }

        // The blade's path: a crescent that sweeps 140 degrees, its tail thinning behind it.
        const float sweep = 2.4f, radius = 20;
        float head = Mathf.Min(1, t / 0.55f);
        float start = Angle - sweep / 2;
        for (int i = 0; i < 10; i++)
        {
            float a0 = Mathf.Lerp(0, head, i / 10f), a1 = Mathf.Lerp(0, head, (i + 1) / 10f);
            float width = 1 + 4 * (i + 1) / 10f;
            Vector2 p0 = Vector2.FromAngle(Backhand ? Angle + sweep / 2 - a0 * sweep : start + a0 * sweep) * radius;
            Vector2 p1 = Vector2.FromAngle(Backhand ? Angle + sweep / 2 - a1 * sweep : start + a1 * sweep) * radius;
            DrawLine(p0, p1, Color with { A = 0.55f * fade }, width + 2, antialiased: true);
            DrawLine(p0, p1, Colors.White with { A = fade * (i + 1) / 10f }, width * 0.4f, antialiased: true);
        }

    }
}

/// <summary>A shove: chevrons rushing from the shover into the one being shoved.</summary>
public partial class PushFx : Node2D
{
    private const double Seconds = 0.28;
    private double _age;

    public Vector2 To { get; init; }

    public Color Color { get; init; } = Colors.White;

    public override void _Ready() => ZIndex = 14;

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
        Vector2 dir = To.Normalized(), side = dir.Orthogonal();
        for (int k = 0; k < 3; k++)
        {
            float u = Mathf.Clamp(t * 1.6f - k * 0.2f, 0, 1);
            if (u <= 0)
            {
                continue;
            }

            Vector2 at = To * u;
            float alpha = 1 - u * 0.7f;
            DrawPolyline([at - dir * 4 + side * 5, at, at - dir * 4 - side * 5], Color with { A = alpha }, 2.5f, antialiased: true);
        }
    }
}

/// <summary>Feathers: a few puffing off beating wings, or a great burst when they break, drifting down as they fall.</summary>
public partial class FeatherFx : Node2D
{
    private readonly List<Feather> _feathers = [];
    private double _age;

    /// <summary>A burst (the wings breaking) rather than a puff (a wingbeat).</summary>
    public bool Burst { get; init; }

    private double Seconds => Burst ? 1.6 : 1.0;

    private sealed class Feather
    {
        public Vector2 At;
        public Vector2 Velocity;
        public float Spin;
        public float Angle;
        public float Sway;
        public Color Color;
    }

    public override void _Ready()
    {
        ZIndex = 16;
        Color[] colors = [Colors.White, new("f0ead8"), new("e0d6bc")];
        int count = Burst ? 22 : 5;
        for (int i = 0; i < count; i++)
        {
            float side = i % 2 == 0 ? -1 : 1;
            float angle = Burst ? (float)(Random.Shared.NextDouble() * Mathf.Tau) : Mathf.Pi / 2 + side * (0.6f + (float)Random.Shared.NextDouble() * 0.9f) + Mathf.Pi;
            float speed = Burst ? 40 + (float)Random.Shared.NextDouble() * 70 : 18 + (float)Random.Shared.NextDouble() * 22;
            _feathers.Add(new Feather
            {
                At = Burst ? Vector2.Zero : new Vector2(side * 9, -14),
                Velocity = Vector2.FromAngle(angle) * speed,
                Spin = (float)(Random.Shared.NextDouble() * 6 - 3),
                Angle = (float)(Random.Shared.NextDouble() * Mathf.Tau),
                Sway = (float)(Random.Shared.NextDouble() * Mathf.Tau),
                Color = colors[i % colors.Length],
            });
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

        float dt = (float)delta;
        foreach (Feather feather in _feathers)
        {
            // Air drag slows them fast, then they flutter down, swaying side to side.
            feather.Velocity *= 1 - 3.5f * dt;
            feather.Velocity.Y += 26 * dt;
            feather.At += feather.Velocity * dt + new Vector2(Mathf.Sin((float)_age * 6 + feather.Sway) * 10 * dt, 0);
            feather.Angle += feather.Spin * dt;
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        float fade = (float)Math.Clamp((Seconds - _age) / 0.4, 0, 1);
        foreach (Feather feather in _feathers)
        {
            Vector2 dir = Vector2.FromAngle(feather.Angle) * 2.5f;
            DrawLine(feather.At - dir, feather.At + dir, feather.Color with { A = fade }, 2);
            DrawLine(feather.At - dir, feather.At + dir * 0.6f, new Color("9a8a70") with { A = fade }, 0.6f);
        }
    }
}
