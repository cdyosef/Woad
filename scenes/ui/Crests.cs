using Godot;
using Woad.Core.Model;

namespace Woad.Scenes.Ui;

/// <summary>A crest to draw: its field's pattern, its charge, and their colors (the rim and the field's second color).</summary>
public sealed record Crest(string Field, string Charge, Color FieldColor, Color ChargeColor, Color Rim);

/// <summary>A choice in the crest builder: its id and what it's called.</summary>
public sealed record CrestPart(string Id, string Name);

/// <summary>
/// Crests: a shield with a field (plain, or parted into two colors: the field's and the rim's), and a charge on
/// it. A character's crest follows their portrait's emblem and their main color unless they pick otherwise.
/// </summary>
public static class Crests
{
    public static IReadOnlyList<CrestPart> Fields { get; } =
    [
        new("plain", "Plain"),
        new("per_pale", "Halved"),
        new("per_fess", "Parted"),
        new("per_bend", "Slanted"),
        new("quarterly", "Quartered"),
        new("chief", "Chief"),
        new("pale", "Pale"),
        new("fess", "Band"),
        new("bend", "Bend"),
        new("chevron", "Chevron"),
        new("cross", "Cross"),
        new("saltire", "Saltire"),
        new("bordure", "Bordered"),
    ];

    /// <summary>The charges: the eight first emblems (the portraits' sigils, in order), then the rest.</summary>
    public static IReadOnlyList<CrestPart> Charges { get; } =
    [
        new("roundel", "Roundel"),
        new("lozenge", "Lozenge"),
        new("triangle", "Triangle"),
        new("tau", "Tau"),
        new("spark", "Spark"),
        new("ring", "Ring"),
        new("bars", "Bars"),
        new("vee", "Vee"),
        new("sun", "Sun"),
        new("moon", "Moon"),
        new("star", "Star"),
        new("tower", "Tower"),
        new("sword", "Sword"),
        new("axe", "Axe"),
        new("skull", "Skull"),
        new("eagle", "Eagle"),
        new("fish", "Fish"),
        new("tree", "Tree"),
        new("crown", "Crown"),
        new("key", "Key"),
        new("flame", "Flame"),
        new("hand", "Hand"),
        new("heart", "Heart"),
        new("anvil", "Anvil"),
        new("frog", "Frog"),
        new("none", "None"),
    ];

    /// <summary>The crest's colors: "Follow" (the character's), then the colors to pick.</summary>
    public static IReadOnlyList<Swatch> ColorChoices { get; } = [new("", "Follow", Godot.Colors.Transparent), .. Looks.Colors];

    /// <summary>The crest a character shows: their picks, else their portrait's emblem in their own colors (the crest as it always was).</summary>
    public static Crest Resolve(Appearance appearance, string portraitId, LookColors? colors = null)
    {
        Portrait portrait = Portraits.Get(portraitId);
        Color main = colors?.Main ?? portrait.Color;
        string field = Fields.Any(part => part.Id == appearance.CrestField) ? appearance.CrestField : Fields[0].Id;
        string charge = Charges.Any(part => part.Id == appearance.CrestCharge) ? appearance.CrestCharge : Charges[Math.Clamp(portrait.Sigil, 0, 7)].Id;
        Color fieldColor = Looks.Find(Looks.Colors, appearance.CrestColor)?.Color ?? main.Darkened(0.4f);
        Color chargeColor = Looks.Find(Looks.Colors, appearance.CrestInk)?.Color ?? main.Lightened(0.3f);
        return new Crest(field, charge, fieldColor, chargeColor, colors?.Trim ?? Palette.Trim);
    }

    public static string Name(IReadOnlyList<CrestPart> parts, string id) => parts.FirstOrDefault(part => part.Id == id)?.Name ?? parts[0].Name;

    /// <summary>Draws the crest into <paramref name="rect"/>: the shield, its field, the charge, and the rim.</summary>
    public static void Draw(CanvasItem canvas, Rect2 rect, Crest crest)
    {
        Vector2 o = rect.Position;
        float w = rect.Size.X;
        float h = rect.Size.Y;
        Vector2 P(float x, float y) => o + new Vector2(x * w, y * h);
        Vector2[] shield = [P(0, 0), P(1, 0), P(1, 0.55f), P(0.5f, 1), P(0, 0.55f)];
        canvas.DrawColoredPolygon(shield, crest.FieldColor);

        // The field's second color, clipped to the shield.
        void Part(params Vector2[] shape)
        {
            foreach (Vector2[] piece in Geometry2D.IntersectPolygons(shape, shield))
            {
                canvas.DrawColoredPolygon(piece, crest.Rim);
            }
        }

        switch (crest.Field)
        {
            case "per_pale":
                Part(P(0.5f, -0.1f), P(1.1f, -0.1f), P(1.1f, 1.1f), P(0.5f, 1.1f));
                break;
            case "per_fess":
                Part(P(-0.1f, 0.45f), P(1.1f, 0.45f), P(1.1f, 1.1f), P(-0.1f, 1.1f));
                break;
            case "per_bend":
                Part(P(-0.1f, -0.1f), P(1.1f, 1.1f), P(-0.1f, 1.1f));
                break;
            case "quarterly":
                Part(P(0.5f, -0.1f), P(1.1f, -0.1f), P(1.1f, 0.45f), P(0.5f, 0.45f));
                Part(P(-0.1f, 0.45f), P(0.5f, 0.45f), P(0.5f, 1.1f), P(-0.1f, 1.1f));
                break;
            case "chief":
                Part(P(-0.1f, -0.1f), P(1.1f, -0.1f), P(1.1f, 0.28f), P(-0.1f, 0.28f));
                break;
            case "pale":
                Part(P(0.36f, -0.1f), P(0.64f, -0.1f), P(0.64f, 1.1f), P(0.36f, 1.1f));
                break;
            case "fess":
                Part(P(-0.1f, 0.32f), P(1.1f, 0.32f), P(1.1f, 0.54f), P(-0.1f, 0.54f));
                break;
            case "bend":
                Part(P(-0.1f, 0.12f), P(0.12f, -0.1f), P(1.1f, 0.88f), P(0.88f, 1.1f));
                break;
            case "chevron":
                Part(P(-0.1f, 0.78f), P(0.5f, 0.3f), P(1.1f, 0.78f), P(1.1f, 1.02f), P(0.5f, 0.54f), P(-0.1f, 1.02f));
                break;
            case "cross":
                Part(P(0.38f, -0.1f), P(0.62f, -0.1f), P(0.62f, 1.1f), P(0.38f, 1.1f));
                Part(P(-0.1f, 0.3f), P(1.1f, 0.3f), P(1.1f, 0.5f), P(-0.1f, 0.5f));
                break;
            case "saltire":
                Part(P(-0.1f, 0.1f), P(0.1f, -0.1f), P(1.1f, 0.9f), P(0.9f, 1.1f));
                Part(P(0.9f, -0.1f), P(1.1f, 0.1f), P(0.1f, 1.1f), P(-0.1f, 0.9f));
                break;
            case "bordure":
                canvas.DrawColoredPolygon(shield, crest.Rim);
                canvas.DrawColoredPolygon([P(0.14f, 0.1f), P(0.86f, 0.1f), P(0.86f, 0.52f), P(0.5f, 0.86f), P(0.14f, 0.52f)], crest.FieldColor);
                break;
        }

        DrawCharge(canvas, o + new Vector2(w / 2, h * 0.42f), w * 0.2f, crest.Charge, crest.ChargeColor, crest.FieldColor);
        canvas.DrawPolyline([.. shield, shield[0]], crest.Rim, 1.5f, antialiased: true);
    }

    /// <summary>A charge centered on <paramref name="c"/>, about 2.4 <paramref name="s"/> across; <paramref name="back"/> cuts holes in it.</summary>
    private static void DrawCharge(CanvasItem canvas, Vector2 c, float s, string charge, Color ink, Color back)
    {
        Vector2 V(float x, float y) => c + new Vector2(x * s, y * s);
        void Poly(params Vector2[] points) => canvas.DrawColoredPolygon(points, ink);
        void Box(float x, float y, float width, float height, Color? color = null) => canvas.DrawRect(new Rect2(c.X + x * s, c.Y + y * s, width * s, height * s), color ?? ink);
        void Line(float x0, float y0, float x1, float y1, float width) => canvas.DrawLine(V(x0, y0), V(x1, y1), ink, width * s, antialiased: true);
        void Dot(float x, float y, float radius, Color? color = null) => canvas.DrawCircle(V(x, y), radius * s, color ?? ink, antialiased: true);

        switch (charge)
        {
            case "roundel":
                Dot(0, 0, 1);
                break;
            case "lozenge":
                Poly(V(0, -1.3f), V(1, 0), V(0, 1.3f), V(-1, 0));
                break;
            case "triangle":
                Poly(V(0, -1.2f), V(1.1f, 0.9f), V(-1.1f, 0.9f));
                break;
            case "tau":
                Box(-0.3f, -1.2f, 0.6f, 2.4f);
                Box(-1.1f, -0.5f, 2.2f, 0.6f);
                break;
            case "spark":
                Poly(V(0, -1.4f), V(0.35f, 0), V(0, 1.4f), V(-0.35f, 0));
                Poly(V(-1.4f, 0), V(0, -0.35f), V(1.4f, 0), V(0, 0.35f));
                break;
            case "ring":
                canvas.DrawArc(c, s * 0.9f, 0, Mathf.Tau, 24, ink, s * 0.45f, antialiased: true);
                break;
            case "bars":
                Box(-1.1f, -0.8f, 2.2f, 0.5f);
                Box(-1.1f, 0.3f, 2.2f, 0.5f);
                break;
            case "vee":
                canvas.DrawPolyline([V(-1.1f, -0.6f), V(0, 0.7f), V(1.1f, -0.6f)], ink, 0.5f * s, antialiased: true);
                break;
            case "sun":
                for (int ray = 0; ray < 8; ray++)
                {
                    float angle = ray * Mathf.Tau / 8;
                    var along = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    canvas.DrawLine(c + along * 0.8f * s, c + along * 1.4f * s, ink, 0.3f * s, antialiased: true);
                }

                Dot(0, 0, 0.7f);
                break;
            case "moon":
                Dot(0, 0, 1.05f);
                Dot(0.5f, -0.25f, 0.9f, back);
                break;
            case "star":
                Poly([.. Enumerable.Range(0, 10).Select(point =>
                {
                    float angle = -Mathf.Pi / 2 + point * Mathf.Pi / 5;
                    float radius = point % 2 == 0 ? 1.35f : 0.55f;
                    return c + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * s;
                })]);
                break;
            case "tower":
                Box(-0.75f, -0.6f, 1.5f, 1.8f);
                Box(-0.75f, -1.05f, 0.4f, 0.5f);
                Box(-0.2f, -1.05f, 0.4f, 0.5f);
                Box(0.35f, -1.05f, 0.4f, 0.5f);
                Box(-0.25f, 0.5f, 0.5f, 0.7f, back);
                break;
            case "sword":
                Poly(V(-0.18f, 0.55f), V(-0.18f, -1.1f), V(0, -1.45f), V(0.18f, -1.1f), V(0.18f, 0.55f));
                Box(-0.75f, 0.45f, 1.5f, 0.28f);
                Box(-0.13f, 0.7f, 0.26f, 0.55f);
                Dot(0, 1.35f, 0.22f);
                break;
            case "axe":
                Line(-0.5f, 1.3f, 0.4f, -1.2f, 0.25f);
                Poly(V(0.1f, -1.1f), V(1.2f, -1.25f), V(1.2f, 0.25f), V(0.15f, -0.2f));
                break;
            case "skull":
                Dot(0, -0.25f, 0.95f);
                Box(-0.55f, 0.35f, 1.1f, 0.65f);
                Dot(-0.38f, -0.25f, 0.27f, back);
                Dot(0.38f, -0.25f, 0.27f, back);
                Box(-0.06f, 0.55f, 0.12f, 0.45f, back);
                break;
            case "eagle":
                Poly(V(0, -1.25f), V(0.3f, -0.6f), V(1.45f, -0.95f), V(1.05f, 0.1f), V(0.35f, 0.25f), V(0.55f, 1.25f), V(0, 0.85f),
                    V(-0.55f, 1.25f), V(-0.35f, 0.25f), V(-1.05f, 0.1f), V(-1.45f, -0.95f), V(-0.3f, -0.6f));
                break;
            case "frog":
                // Squatting, seen from above: a round body, splayed legs, two bulging eyes.
                Dot(0, 0.15f, 0.75f);
                Line(-0.55f, 0.45f, -1.2f, 1.05f, 0.3f);
                Line(0.55f, 0.45f, 1.2f, 1.05f, 0.3f);
                Line(-0.5f, -0.2f, -1.15f, -0.55f, 0.25f);
                Line(0.5f, -0.2f, 1.15f, -0.55f, 0.25f);
                Dot(-0.38f, -0.6f, 0.32f);
                Dot(0.38f, -0.6f, 0.32f);
                Dot(-0.38f, -0.65f, 0.12f, back);
                Dot(0.38f, -0.65f, 0.12f, back);
                break;
            case "fish":
                Poly([.. Enumerable.Range(0, 16).Select(point =>
                {
                    float angle = point * Mathf.Tau / 16;
                    return c + new Vector2(Mathf.Cos(angle) * 0.95f - 0.2f, Mathf.Sin(angle) * 0.5f) * s;
                })]);
                Poly(V(0.55f, 0), V(1.35f, -0.6f), V(1.35f, 0.6f));
                Dot(-0.65f, -0.1f, 0.12f, back);
                break;
            case "tree":
                Box(-0.18f, 0.2f, 0.36f, 1.1f);
                Dot(0, -0.55f, 0.75f);
                Dot(-0.55f, -0.05f, 0.55f);
                Dot(0.55f, -0.05f, 0.55f);
                break;
            case "crown":
                Box(-1f, 0.2f, 2f, 0.6f);
                Poly(V(-1f, 0.25f), V(-1f, -0.8f), V(-0.5f, -0.2f), V(0, -1.05f), V(0.5f, -0.2f), V(1f, -0.8f), V(1f, 0.25f));
                break;
            case "key":
                canvas.DrawArc(V(0, -0.85f), s * 0.42f, 0, Mathf.Tau, 16, ink, s * 0.24f, antialiased: true);
                Box(-0.12f, -0.45f, 0.24f, 1.75f);
                Box(0.1f, 0.85f, 0.45f, 0.2f);
                Box(0.1f, 0.5f, 0.32f, 0.2f);
                break;
            case "flame":
                Poly(V(0, -1.45f), V(0.55f, -0.55f), V(0.85f, 0.35f), V(0.5f, 1.05f), V(0, 1.2f), V(-0.5f, 1.05f), V(-0.85f, 0.35f), V(-0.4f, -0.3f), V(-0.15f, 0.1f));
                break;
            case "hand":
                Box(-0.6f, -0.2f, 1.2f, 1.2f);
                for (int finger = 0; finger < 4; finger++)
                {
                    Box(-0.6f + finger * 0.31f, -1.2f + (finger is 1 or 2 ? -0.1f : 0.1f), 0.26f, 1.05f);
                }

                Poly(V(0.55f, 0.15f), V(1.15f, -0.4f), V(1.25f, -0.2f), V(0.6f, 0.6f));
                break;
            case "heart":
                Dot(-0.48f, -0.35f, 0.58f);
                Dot(0.48f, -0.35f, 0.58f);
                Poly(V(-1.03f, -0.2f), V(1.03f, -0.2f), V(0, 1.15f));
                break;
            case "anvil":
                Poly(V(-1.3f, -0.55f), V(0.9f, -0.55f), V(1.35f, -0.85f), V(1.1f, -0.15f), V(0.4f, 0f), V(0.4f, 0.5f), V(-0.4f, 0.5f), V(-0.4f, 0f), V(-1.1f, -0.15f));
                Box(-0.85f, 0.5f, 1.7f, 0.45f);
                break;
        }
    }
}
