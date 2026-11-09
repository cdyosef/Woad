using Godot;
using Woad.Core.Content;
using Script = Woad.Core.Content.Script;

namespace Woad.Scenes.Ui;

/// <summary>
/// The written look of Woad's languages: each letter a rune of dots and dashes on a 3x3 grid, the same rune for the
/// same letter. Primordial's are cut clean, as if carved into stone, time and space; Gurgle's dashes wave like water
/// with curving lines joining the dots; Gravel's are hacked rough into rock.
/// </summary>
public static class Runes
{
    /// <summary>Draws the rune for <paramref name="letter"/> in a <paramref name="size"/>-pixel cell at <paramref name="at"/> (its top-left).</summary>
    public static void Draw(CanvasItem canvas, Vector2 at, float size, char letter, Script script, Color ink, Color shade)
    {
        uint hash = (uint)char.ToLowerInvariant(letter) * 2654435761u;
        Vector2 Point(int index) => at + new Vector2(index % 3, index / 3) * (size / 2.4f) + new Vector2(size * 0.1f, size * 0.1f);

        // Two to four marks: dots, or dashes between neighbouring points of the grid (across or down).
        int marks = 2 + (int)(hash % 3);
        var dots = new List<Vector2>();
        for (int i = 0; i < marks; i++)
        {
            uint bits = (hash >> (i * 7 + 3)) & 0x7F;
            int from = (int)(bits % 9);
            bool dash = (bits & 0x10) != 0;
            if (!dash)
            {
                dots.Add(Point(from));
                Dot(Point(from));
                continue;
            }

            bool down = (bits & 0x20) != 0;
            int to = down ? (from + 3 < 9 ? from + 3 : from - 3) : (from % 3 < 2 ? from + 1 : from - 1);
            Dash(Point(from), Point(to), i);
        }

        // Gurgle: a curving line joining the first and last dots, like a current.
        if (script == Script.Gurgle && dots.Count >= 2)
        {
            Vector2 a = dots[0], b = dots[^1];
            Vector2 bend = (a + b) / 2 + new Vector2(b.Y - a.Y, a.X - b.X).Normalized() * size * 0.18f;
            canvas.DrawPolyline([.. Enumerable.Range(0, 7).Select(step =>
            {
                float t = step / 6f;
                return a.Lerp(bend, t).Lerp(bend.Lerp(b, t), t);
            })], ink with { A = ink.A * 0.8f }, 1f, antialiased: true);
        }

        void Dot(Vector2 p)
        {
            if (script == Script.Gravel)
            {
                Carved(() => canvas.DrawRect(new Rect2(p - new Vector2(1.2f, 1), new Vector2(2.4f, 2.1f)), shade),
                       () => canvas.DrawRect(new Rect2(p - new Vector2(1.2f, 1.2f), new Vector2(2.2f, 2.3f)), ink));
                return;
            }

            Carved(() => canvas.DrawCircle(p + new Vector2(0.6f, 0.6f), size * 0.09f, shade, antialiased: true),
                   () => canvas.DrawCircle(p, size * 0.09f, ink, antialiased: true));
        }

        void Dash(Vector2 a, Vector2 b, int seed)
        {
            Vector2[] line = script switch
            {
                // A wave instead of a stroke: ~ rather than -.
                Script.Gurgle => [.. Enumerable.Range(0, 9).Select(step =>
                {
                    float t = step / 8f;
                    Vector2 across = new Vector2(b.Y - a.Y, a.X - b.X).Normalized() * size * 0.08f;
                    return a.Lerp(b, t) + across * Mathf.Sin(t * Mathf.Tau);
                })],
                // Hacked rough: a bent stroke.
                Script.Gravel => [a, a.Lerp(b, 0.35f) + new Vector2((seed % 2 == 0 ? 1 : -1) * 0.8f, 0.7f), a.Lerp(b, 0.7f) + new Vector2(-0.6f, (seed % 3 == 0 ? 1 : -1) * 0.8f), b],
                _ => [a, b],
            };
            float width = script == Script.Gravel ? 1.8f : 1.4f;
            Carved(() => canvas.DrawPolyline([.. line.Select(p => p + new Vector2(0.6f, 0.6f))], shade, width, antialiased: true),
                   () => canvas.DrawPolyline(line, ink, width, antialiased: true));
        }

        // Cut into the surface: the shadow of the groove first, then the mark.
        void Carved(Action groove, Action mark)
        {
            if (script != Script.Plain)
            {
                groove();
            }

            mark();
        }
    }
}
