using Godot;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The turn order along the top: a card for each hero still in play and one for the monsters (a
/// yellow eye in a red orb, eight white spikes around it like a compass, on black with a red trim),
/// starting from whoever's turn it is: that card stands bigger, gilt, at the left, and the cards slide
/// along as the turns go round. Too many to fit, the last place is a deck: click it to see the rest
/// down the line. Click a hero's card to see where they are and their sheet.
/// </summary>
public partial class TurnOrderView : Control
{
    private const float Card = 22;
    private const float Gap = 4;
    private const float Big = 30;

    /// <summary>A place in the order: a hero (their id), or the monsters (null).</summary>
    /// <param name="Enemy">For an enemy turn's card (no hero): whose turn it is.</param>
    public sealed record Entry(int? HeroId, string Name, Color Color, LookColors? Look, bool Down, Faction Enemy = Faction.Monsters);

    private List<Entry> _entries = [];
    private readonly Dictionary<int, float> _shownX = [];
    private bool _deckOpen;
    private int _fits;
    private int? _viewed;

    /// <summary>A hero's card was clicked.</summary>
    public event Action<int>? HeroClicked;

    public override void _Ready() => MouseFilter = MouseFilterEnum.Stop;

    /// <summary>The order from whoever's turn it is now; <paramref name="viewed"/> marks a hero whose sheet is up.</summary>
    public void ShowOrder(IReadOnlyList<Entry> entries, int? viewed)
    {
        _entries = [.. entries];
        _viewed = viewed;
        _fits = Math.Max(2, (int)((Size.X - Big - Gap) / (Card + Gap)) + 1);
        _deckOpen &= _entries.Count > _fits;
        FitDeck();
        QueueRedraw();
    }

    /// <summary>Open, the deck's list hangs down below the strip: the view grows to take its clicks.</summary>
    private void FitDeck() => Size = new Vector2(Size.X, _deckOpen ? 36 + (_entries.Count - Shown) * 15 : 33);

    /// <summary>The cards on show (the deck's place holds the rest).</summary>
    private int Shown => _entries.Count > _fits ? _fits - 1 : _entries.Count;

    private static int Key(Entry entry) => entry.HeroId ?? -1 - (int)entry.Enemy;

    private float TargetX(int index) => index == 0 ? 0 : Big + Gap + (index - 1) * (Card + Gap);

    private Rect2 RectAt(int index, float x) =>
        index == 0 ? new Rect2(x, 1, Big, 31) : new Rect2(x, 5, Card, 27);

    public override void _Process(double delta)
    {
        bool moving = false;
        for (int i = 0; i < Shown; i++)
        {
            int key = Key(_entries[i]);
            float target = TargetX(i);
            float x = _shownX.TryGetValue(key, out float now) ? now : target + 40;
            float next = Mathf.MoveToward(x, target, (float)delta * 260);
            moving |= next != x;
            _shownX[key] = next;
        }

        if (moving)
        {
            QueueRedraw();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            return;
        }

        for (int i = 0; i < Shown; i++)
        {
            if (RectAt(i, TargetX(i)).HasPoint(click.Position) && _entries[i].HeroId is int heroId)
            {
                Sfx.Play("click");
                HeroClicked?.Invoke(heroId);
                AcceptEvent();
                return;
            }
        }

        if (_entries.Count > _fits && DeckRect.HasPoint(click.Position))
        {
            Sfx.Play("click");
            _deckOpen = !_deckOpen;
            FitDeck();
            QueueRedraw();
            AcceptEvent();
            return;
        }

        // The rest of the line, opened down from the deck.
        if (_deckOpen)
        {
            for (int i = Shown; i < _entries.Count; i++)
            {
                if (ListRect(i).HasPoint(click.Position) && _entries[i].HeroId is int heroId)
                {
                    _deckOpen = false;
                    FitDeck();
                    HeroClicked?.Invoke(heroId);
                    AcceptEvent();
                    return;
                }
            }
        }
    }

    private Rect2 DeckRect => new(TargetX(_fits - 1), 3, Card + 4, 29);

    private Rect2 ListRect(int index) => new(DeckRect.Position.X - 40, 34 + (index - Shown) * 15, 70, 14);

    public override string _GetTooltip(Vector2 atPosition)
    {
        for (int i = 0; i < Shown; i++)
        {
            if (RectAt(i, TargetX(i)).HasPoint(atPosition))
            {
                Entry entry = _entries[i];
                return entry.HeroId is not null ? $"{entry.Name}: click to see where they are, and their sheet."
                    : entry.Enemy == Faction.Undead ? "The undead: the horde goes together, after the monsters."
                    : "The monsters: they all go together, after every hero has had a turn.";
            }
        }

        return _entries.Count > _fits && DeckRect.HasPoint(atPosition) ? "More down the line: click to see them." : "";
    }

    public override void _Draw()
    {
        TooltipText = " ";
        Font font = GameArt.BodyFont ?? ThemeDB.FallbackFont;
        for (int i = Shown - 1; i >= 0; i--)
        {
            Entry entry = _entries[i];
            float x = _shownX.TryGetValue(Key(entry), out float shown) ? shown : TargetX(i);
            DrawEntry(font, entry, RectAt(i, x), active: i == 0);
        }

        if (_entries.Count > _fits)
        {
            Rect2 deck = DeckRect;
            for (int k = 2; k >= 0; k--)
            {
                var card = new Rect2(deck.Position + new Vector2(k * 2, -k + 2), new Vector2(Card, 26));
                DrawRect(card, Palette.PanelDark);
                DrawRect(card, Palette.TrimDim, filled: false, width: 1);
            }

            DrawString(font, deck.Position + new Vector2(0, 18), $"+{_entries.Count - Shown}", HorizontalAlignment.Center, Card, UiTheme.FontSize, Palette.TextDim);
            if (_deckOpen)
            {
                for (int i = Shown; i < _entries.Count; i++)
                {
                    Rect2 row = ListRect(i);
                    DrawRect(row, Palette.Ink);
                    DrawRect(row, _entries[i].Color, filled: false, width: 1);
                    DrawString(font, row.Position + new Vector2(3, 10), $"{i + 1}. {_entries[i].Name}", HorizontalAlignment.Left, row.Size.X - 4, UiTheme.FontSize, _entries[i].Color);
                }
            }
        }
    }

    private void DrawEntry(Font font, Entry entry, Rect2 rect, bool active)
    {
        if (entry.HeroId is null)
        {
            if (entry.Enemy == Faction.Undead)
            {
                DrawUndeadCard(rect, active);
            }
            else
            {
                DrawMonsterCard(rect, active);
            }

            return;
        }

        bool viewed = entry.HeroId == _viewed;
        DrawRect(rect, active ? Palette.PanelHover : Palette.PanelDark);
        if (entry.Look is { } look)
        {
            float size = rect.Size.X - 4;
            Looks.Draw(this, new Rect2(rect.Position.X + 2, rect.Position.Y + 1, size, size), look, entry.Down ? new Color(0.5f, 0.5f, 0.5f) : Colors.White);
        }

        DrawRect(new Rect2(rect.Position.X, rect.End.Y - 9, rect.Size.X, 9), Palette.Ink with { A = 0.85f });
        DrawString(font, new Vector2(rect.Position.X - 2, rect.End.Y - 1), entry.Name, HorizontalAlignment.Center, rect.Size.X + 4, UiTheme.FontSize, active ? Palette.TrimBright : entry.Color);
        DrawRect(rect, active ? Palette.TrimBright : viewed ? Palette.Text : entry.Color.Darkened(0.2f), filled: false, width: active || viewed ? 2 : 1);
    }

    /// <summary>The undead's card: black, a sickly green trim, and their symbol: a fanged skull with glowing green eyes.</summary>
    private void DrawUndeadCard(Rect2 rect, bool active)
    {
        Color green = new("60ff80"), dark = new("1a4a24"), bone = new("e8e2d0");
        DrawRect(rect, Colors.Black);
        DrawRect(rect, active ? Palette.TrimBright : green.Darkened(0.3f), filled: false, width: active ? 2 : 1);
        DrawRect(rect.Grow(-2), dark, filled: false, width: 1);

        Vector2 c = rect.GetCenter() + new Vector2(0, -1);
        float s = rect.Size.X / 22f;
        // The skull: a dome, a jaw and its fangs.
        DrawCircle(c, 6.5f * s, bone);
        DrawRect(new Rect2(c.X - 4 * s, c.Y + 3 * s, 8 * s, 4 * s), bone);
        foreach (float x in new[] { -3f, -1f, 1f, 3f })
        {
            bool fang = x is -3f or 3f;
            DrawColoredPolygon([new Vector2(c.X + (x - 0.8f) * s, c.Y + 7 * s), new Vector2(c.X + (x + 0.8f) * s, c.Y + 7 * s), new Vector2(c.X + x * s, c.Y + (fang ? 10 : 8.5f) * s)], bone);
        }

        // Dark sockets, green-glowing irises.
        foreach (float x in new[] { -2.6f, 2.6f })
        {
            DrawCircle(c + new Vector2(x, 0) * s, 2 * s, Colors.Black);
            DrawCircle(c + new Vector2(x, 0) * s, 1.3f * s, green with { A = 0.45f });
            DrawCircle(c + new Vector2(x, 0) * s, 0.8f * s, green);
        }

        DrawColoredPolygon([c + new Vector2(0, 2) * s, c + new Vector2(-0.9f, 3.5f) * s, c + new Vector2(0.9f, 3.5f) * s], Colors.Black);
    }

    /// <summary>The monsters' card: black, a fancy red trim, and their symbol.</summary>
    private void DrawMonsterCard(Rect2 rect, bool active)
    {
        Color red = new("c0242c"), dark = new("6a0e14"), gold = new("ffd040");
        DrawRect(rect, Colors.Black);
        DrawRect(rect, active ? Palette.TrimBright : red, filled: false, width: active ? 2 : 1);
        DrawRect(rect.Grow(-2), dark, filled: false, width: 1);
        foreach (Vector2 corner in new[] { rect.Position + new Vector2(1, 1), new Vector2(rect.End.X - 2, rect.Position.Y + 1), new Vector2(rect.Position.X + 1, rect.End.Y - 2), rect.End - new Vector2(2, 2) })
        {
            DrawRect(new Rect2(corner, Vector2.One), gold);
        }

        Vector2 c = rect.GetCenter();
        float s = rect.Size.X / 22f;
        for (int k = 0; k < 8; k++)
        {
            Vector2 dir = Vector2.FromAngle(Mathf.Pi / 4 * k);
            Vector2 side = new Vector2(-dir.Y, dir.X) * 1.6f * s;
            float length = (k % 2 == 0 ? 9 : 7) * s;
            DrawColoredPolygon([c + dir * 4 * s + side, c + dir * length, c + dir * 4 * s - side], Colors.White);
        }

        DrawCircle(c, 5 * s, red);
        DrawArc(c, 5 * s, 0, Mathf.Tau, 16, dark, 1);
        DrawColoredPolygon([c + new Vector2(-4, 0) * s, c + new Vector2(0, -2.4f) * s, c + new Vector2(4, 0) * s, c + new Vector2(0, 2.4f) * s], gold);
        DrawRect(new Rect2(c.X - 0.5f * s, c.Y - 1.5f * s, s, 3 * s), Colors.Black);
    }
}
