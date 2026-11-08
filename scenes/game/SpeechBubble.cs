using Godot;
using Woad.Core.Content;
using Script = Woad.Core.Content.Script;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Words said, in a bubble over the speaker, styled to their language: written first in its script, then, for those
/// who understand, turning to English letter by letter (slowly, for Gravel read through Primordial; only the words they
/// know, for Gurgle heard by a Primordial speaker). After a while it shrinks to a little bubble over their head, which
/// opens to the words again under the mouse.
/// </summary>
public partial class SpeechBubble : Node2D
{
    private const float LetterSize = 7;
    private const float LineHeight = 10;
    private const int LineLength = 24;

    /// <summary>How long it stays open once every letter that will turn has turned.</summary>
    private const float Linger = 5;

    private string _text = "";
    private Language _language = Languages.Common;
    private Understanding _understanding = Understanding.Fully;
    private bool[] _turns = [];
    private bool[] _turned = [];
    private float _clock;
    private float _settled = -1;
    private bool _hovered;

    /// <summary>Where the speaker's head is (the bubble sits above it, its tail pointing down to it).</summary>
    public Vector2 Head { get; set; } = new(16, 0);

    /// <summary>Where the speaker stands on the board now (their figure's position), or null once they're gone: it follows them.</summary>
    public Func<Vector2?>? Follow { get; set; }

    public void Say(string text, Language language, Understanding understanding)
    {
        _text = text;
        _language = language;
        _understanding = understanding;
        _turned = new bool[text.Length];
        _turns = new bool[text.Length];

        // Which letters turn to English: all of them, or only those of the words they know.
        int start = 0;
        foreach (string word in text.Split(' '))
        {
            bool known = Languages.MakesOut(language, understanding, word);
            for (int i = 0; i < word.Length; i++)
            {
                _turns[start + i] = known || language.Script == Script.Plain;
            }

            start += word.Length + 1;
        }

        _clock = 0;
        _settled = -1;
        ZIndex = 50;
        QueueRedraw();
    }

    private bool Collapsed => _settled >= 0 && _clock - _settled > Linger && !_hovered;

    public override void _Process(double delta)
    {
        _clock += (float)delta;
        if (Follow?.Invoke() is { } at)
        {
            Position = at;
        }
        float pace = _understanding == Understanding.Slowly ? 0.22f : 0.04f;

        // The runes hold a moment, then turn one letter at a time.
        int due = _clock < 0.8f ? 0 : (int)((_clock - 0.8f) / pace);
        int done = 0;
        for (int i = 0; i < _text.Length && done < due; i++)
        {
            if (_turns[i] && !_turned[i] && char.IsLetterOrDigit(_text[i]))
            {
                _turned[i] = true;
                done++;
            }
            else if (_turns[i] && _turned[i])
            {
                done++;
            }
        }

        if (_settled < 0 && Enumerable.Range(0, _text.Length).All(i => !_turns[i] || _turned[i] || !char.IsLetterOrDigit(_text[i])) && _clock > 0.8f)
        {
            _settled = _clock;
        }

        // Shrunk, it opens again under the mouse.
        _hovered = _settled >= 0 && Bounds(collapsed: true).Grow(2).HasPoint(GetLocalMousePosition());
        QueueRedraw();
    }

    private List<(int Start, int Length)> Lines()
    {
        var lines = new List<(int, int)>();
        int start = 0;
        while (start < _text.Length)
        {
            int length = Math.Min(LineLength, _text.Length - start);
            if (start + length < _text.Length && _text.LastIndexOf(' ', start + length, length) is int space and > 0 && space > start)
            {
                length = space - start;
            }

            lines.Add((start, length));
            start += length + 1;
        }

        return lines;
    }

    private Rect2 Bounds(bool collapsed)
    {
        if (collapsed)
        {
            return new Rect2(Head + new Vector2(4, -16), new Vector2(14, 10));
        }

        List<(int Start, int Length)> lines = Lines();
        float width = lines.Max(line => line.Length) * LetterSize + 10;
        float height = lines.Count * LineHeight + 8;
        return new Rect2(Head + new Vector2(-width / 2, -height - 10), new Vector2(width, height));
    }

    public override void _Draw()
    {
        (Color fill, Color edge, Color ink, Color shade) = Look(_language.Script);
        if (Collapsed)
        {
            Rect2 icon = Bounds(collapsed: true);
            DrawBubble(icon, fill, edge, new Vector2(icon.Position.X + 3, icon.End.Y));
            for (int i = 0; i < 3; i++)
            {
                DrawCircle(icon.Position + new Vector2(3.5f + i * 3.5f, 5), 0.9f, ink);
            }

            return;
        }

        Rect2 bubble = Bounds(collapsed: false);
        DrawBubble(bubble, fill, edge, Head + new Vector2(0, -3));
        Font font = GameArt.BodyFont ?? ThemeDB.FallbackFont;
        List<(int Start, int Length)> lines = Lines();
        for (int row = 0; row < lines.Count; row++)
        {
            (int start, int length) = lines[row];
            for (int i = 0; i < length; i++)
            {
                int index = start + i;
                char letter = _text[index];
                var at = new Vector2(bubble.Position.X + 5 + i * LetterSize, bubble.Position.Y + 4 + row * LineHeight);
                if (letter == ' ')
                {
                    continue;
                }

                if (_language.Script == Script.Plain || _turned[index] || !char.IsLetterOrDigit(letter))
                {
                    DrawString(font, at + new Vector2(0, 8), letter.ToString(), fontSize: UiTheme.FontSize, modulate: ink);
                }
                else
                {
                    Runes.Draw(this, at, LetterSize + 1, letter, _language.Script, ink, shade);
                }
            }
        }
    }

    /// <summary>The bubble's shape for its language: a round parchment one, a carved stone one, a wavy watery one, a rough rock one.</summary>
    private void DrawBubble(Rect2 rect, Color fill, Color edge, Vector2 tail)
    {
        Vector2 tailBase = new(Math.Clamp(tail.X, rect.Position.X + 4, rect.End.X - 8), rect.End.Y);
        DrawColoredPolygon([tailBase, tailBase + new Vector2(6, 0), tail], fill);
        switch (_language.Script)
        {
            case Script.Gurgle:
            {
                // A wavy edge, like water.
                Vector2[] outline = [.. Enumerable.Range(0, 40).Select(step => Edge(rect, step / 40f, 1.2f * Mathf.Sin(step * 1.6f + _clock * 3)))];
                DrawColoredPolygon(outline, fill);
                DrawPolyline([.. outline, outline[0]], edge, 1, antialiased: true);
                break;
            }

            case Script.Gravel:
            {
                // Rough-hewn rock.
                Vector2[] outline = [.. Enumerable.Range(0, 16).Select(step => Edge(rect, step / 16f, (step * 37 % 5 - 2) * 0.6f))];
                DrawColoredPolygon(outline, fill);
                DrawPolyline([.. outline, outline[0]], edge, 1.5f);
                break;
            }

            default:
                DrawRect(rect, fill);
                DrawRect(rect, edge, filled: false, width: 1);
                if (_language.Script == Script.Primordial)
                {
                    // Cut stone: a second line inside the first.
                    DrawRect(rect.Grow(-2), edge with { A = 0.5f }, filled: false, width: 1);
                }

                break;
        }
    }

    /// <summary>A point a fraction of the way round a rectangle's edge, pushed out by <paramref name="bulge"/>.</summary>
    private static Vector2 Edge(Rect2 rect, float t, float bulge)
    {
        float perimeter = 2 * (rect.Size.X + rect.Size.Y);
        float d = t * perimeter;
        Vector2 center = rect.GetCenter();
        Vector2 point = d < rect.Size.X ? rect.Position + new Vector2(d, 0)
            : d < rect.Size.X + rect.Size.Y ? new Vector2(rect.End.X, rect.Position.Y + d - rect.Size.X)
            : d < 2 * rect.Size.X + rect.Size.Y ? new Vector2(rect.End.X - (d - rect.Size.X - rect.Size.Y), rect.End.Y)
            : new Vector2(rect.Position.X, rect.End.Y - (d - 2 * rect.Size.X - rect.Size.Y));
        return point + (point - center).Normalized() * bulge;
    }

    private static (Color Fill, Color Edge, Color Ink, Color Shade) Look(Script script) => script switch
    {
        Script.Primordial => (new Color("4a4658"), new Color("b8b0d0"), new Color("e8e0ff"), new Color("1a1622")),
        Script.Gurgle => (new Color("1e4a6a"), new Color("7ad0f0"), new Color("c8f0ff"), new Color("0a2234")),
        Script.Gravel => (new Color("5a5048"), new Color("a89880"), new Color("e0d4c0"), new Color("2a241e")),
        _ => (new Color("f0e6cc"), new Color("6a5a40"), new Color("2a2018"), new Color("00000000")),
    };
}
