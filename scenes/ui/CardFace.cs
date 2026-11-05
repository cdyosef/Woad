using Godot;
using Woad.Core.Content;

namespace Woad.Scenes.Ui;

/// <summary>
/// How every card is drawn, like a playing card. Face up: cream, with rounded corners, its corner index
/// (a small copy of its picture, the stack's count under it) top left, its picture big in the middle and
/// its name in ink along the bottom; a weapon that shoots shows its ammo top right, with how many are to
/// hand. Face down (an empty slot, or a card tapped for the turn): woad blue, a diamond lattice in a cream
/// frame, the slot's mark faint in the middle and its name under it. What a card adds is on its face too:
/// its dice down its left edge and its bonuses in the bottom right corner, each in its pool's color, and
/// the symbols of the dice pools it can add to (Rumble, Od Hand, Wend) along its top right.
/// </summary>
public static class CardFace
{
    public static readonly Color Cream = new("ede3c7");
    public static readonly Color CreamDark = new("b8a88a");
    public static readonly Color CreamLit = new("f8f0d8");
    public static readonly Color Woad = new("24406e");
    public static readonly Color WoadDark = new("182c4e");
    public static readonly Color WoadLine = new("3a5c94");
    public static readonly Color BackText = new("c9d6ee");

    private const float Index = 7;

    private static ImageTexture? _lattice;

    /// <summary>What's written on a face besides its name: the count in the corner index, ammo top right, and a note (charges, "!" unloaded, "-1" worn).</summary>
    public readonly record struct Marks(int Count = 1, Texture2D? Ammo = null, int AmmoCount = 0, string Note = "", bool NoteIsBad = false,
        IReadOnlyList<(string Text, Color Color)>? Bonuses = null, IReadOnlyList<Color>? Dice = null, IReadOnlyList<Texture2D>? Pools = null);

    /// <summary>
    /// The dice pools a card can add to, as their symbols for its top right corner: Rumble (weapons, torches,
    /// gear adding Rumble), Od Hand (tools, torches, and weapons that go in the off hand) and Wend (boots, gear adding Wend).
    /// </summary>
    public static IReadOnlyList<Texture2D> Pools(CardDefinition card)
    {
        Stats stats = card.Modifiers;
        var icons = new List<string>();
        if (card.Type == CardType.Weapon || card.Torch || stats.AttackDice > 0 || stats.WeaponDice > 0 || card.RumbleType is not null)
        {
            icons.Add("icon_rumble");
        }

        if (card.Tool is not null || card.Torch || stats.OdHandDice > 0 || stats.Tools > 0 || stats.ToolDice > 0
            || (card.Type == CardType.Weapon && (card.Slot == EquipSlot.OffHand || card.AltSlot == EquipSlot.OffHand)))
        {
            icons.Add("icon_odhand");
        }

        if (stats.MoveDice > 0 || stats.Speed > 0)
        {
            icons.Add("icon_wend");
        }

        return [.. icons.Select(icon => GameArt.Texture($"res://assets/ui/{icon}.png")).OfType<Texture2D>()];
    }

    /// <summary>
    /// What a card adds, as its face shows it: bonuses (Rumble red for a weapon's, Defense blue for armor and
    /// Block, Wend green, tool orange, hearts and luck) and dice, each its pool's color.
    /// </summary>
    public static (IReadOnlyList<(string Text, Color Color)> Bonuses, IReadOnlyList<Color> Dice) Adds(CardDefinition card)
    {
        Stats stats = card.Modifiers;
        var bonuses = new List<(string, Color)>();
        var dice = new List<Color>();
        void Bonus(int amount, string prefix, Color color)
        {
            if (amount != 0)
            {
                bonuses.Add(($"{prefix}{(amount > 0 ? "+" : "")}{amount}", color));
            }
        }

        Bonus(stats.Weapon + card.AttackBonus, "", Palette.DieRumble);
        Bonus(stats.Armor, "", Palette.DieDefense);
        Bonus(stats.Block, "B", Palette.Block);
        Bonus(stats.Speed, "", Palette.DieWend);
        Bonus(stats.Tools + (card.Tool?.Bonus ?? 0), "", Palette.DieTool);
        Bonus(stats.Hearts, "", Palette.Heart);
        Bonus(stats.Luck, "", Palette.Loot);
        dice.AddRange(Enumerable.Repeat(Palette.DieRumble, Mathf.Max(0, stats.WeaponDice)));
        dice.AddRange(Enumerable.Repeat(Palette.DieDefense, Mathf.Max(0, stats.DefenseDice)));
        dice.AddRange(Enumerable.Repeat(Palette.DieWend, Mathf.Max(0, stats.MoveDice)));
        dice.AddRange(Enumerable.Repeat(Palette.DieTool, Mathf.Max(0, stats.ToolDice + (card.Tool?.Dice ?? 0))));
        return (bonuses, dice);
    }

    /// <summary>A card's size wherever it's shown: on the sheet's squares (each square a slot a card sits in), the belt and bag views, the carry tab and a spread hand.</summary>
    public static readonly Vector2 Size = new(28, 38);

    /// <summary>The name band along the bottom of a face.</summary>
    private const float Band = 10;

    /// <summary>The <paramref name="i"/>th die on a card in <paramref name="rect"/>: down its left edge under the corner index, then another column.</summary>
    public static Rect2 DieRect(Rect2 rect, int i, bool underIndex = false)
    {
        float top = rect.Position.Y + 2 + Index + (underIndex ? 9 : 1);
        int perColumn = Mathf.Max(1, (int)((rect.End.Y - Band - 1 - top) / 5));
        return new Rect2(rect.Position.X + 2 + i / perColumn * 5, top + i % perColumn * 5, 6, 6);
    }

    /// <summary>Where a card in <paramref name="rect"/> shows its (first) bonus: the bottom right, over the name band.</summary>
    public static Rect2 BonusRect(Rect2 rect) => new(rect.End.X - 16, rect.End.Y - Band - 9, 15, 9);

    /// <summary>A card face up in <paramref name="rect"/>. Small cards (the drag preview) drop the name; dim ones are greyed (can't be used now).</summary>
    public static void DrawFront(CanvasItem canvas, Rect2 rect, Texture2D? picture, string name, Marks? marked = null,
        bool selected = false, bool hover = false, bool dim = false, bool small = false, Color? border = null)
    {
        Marks marks = marked ?? new Marks();
        Font font = GameArt.BodyFont ?? ThemeDB.FallbackFont;
        Color face = dim ? Cream.Darkened(0.35f) : hover ? CreamLit : Cream;
        Color ink = dim ? Palette.Ink.Lightened(0.25f) : Palette.Ink;
        var shade = dim ? new Color(0.6f, 0.6f, 0.6f) : Colors.White;
        canvas.DrawRect(rect, face);
        canvas.DrawRect(rect, selected ? Palette.TrimBright : border ?? Palette.Ink, filled: false, width: selected ? 2 : 1);
        Corners(canvas, rect);

        // The name along the bottom: smaller when it's long, its first word when that won't fit either.
        float band = small ? 0 : Band;
        if (!small)
        {
            canvas.DrawRect(new Rect2(rect.Position.X + 2, rect.End.Y - band, rect.Size.X - 4, 1), CreamDark);
            float room = rect.Size.X - 3;
            int size = font.GetStringSize(name, fontSize: UiTheme.FontSize).X <= room ? UiTheme.FontSize : UiTheme.FontSize - 2;
            string shown = font.GetStringSize(name, fontSize: size).X <= room ? name : name.Split(' ')[0];
            canvas.DrawString(font, new Vector2(rect.Position.X, rect.End.Y - 2), shown, HorizontalAlignment.Center, rect.Size.X, size, ink);
        }

        // The corner index: its picture small, with the count (or a note) under it.
        var index = new Rect2(rect.Position + new Vector2(2, 2), new Vector2(Index, Index));
        if (picture is not null)
        {
            canvas.DrawTextureRect(picture, index, tile: false, modulate: shade);
        }

        string under = marks.Note.Length > 0 ? marks.Note : marks.Count > 1 ? marks.Count.ToString() : "";

        // The picture, big under the corners.
        if (picture is not null)
        {
            float side = Mathf.Min(rect.Size.X - 8, rect.Size.Y - band - Index - 6);
            var at = new Vector2(rect.Position.X + (rect.Size.X - side) / 2, rect.Position.Y + Index + 3);
            canvas.DrawTextureRect(picture, new Rect2(at, new Vector2(side, side)), tile: false, modulate: shade);
        }

        if (under.Length > 0)
        {
            canvas.DrawString(font, new Vector2(index.Position.X, index.End.Y + 8), under, HorizontalAlignment.Left, -1, UiTheme.FontSize, marks.NoteIsBad ? Palette.Danger : ink);
        }

        // Ammo it shoots, top right, and how many are to hand.
        float poolsEnd = rect.End.X - 2;
        if (marks.Ammo is { } ammo)
        {
            var corner = new Rect2(rect.End.X - Index - 2, rect.Position.Y + 2, Index, Index);
            canvas.DrawTextureRect(ammo, corner, tile: false, modulate: shade);
            canvas.DrawString(font, new Vector2(corner.Position.X - 4, corner.End.Y + 8), marks.AmmoCount.ToString(), HorizontalAlignment.Center, Index + 8, UiTheme.FontSize,
                marks.AmmoCount == 0 ? Palette.Danger : ink);
            poolsEnd = corner.Position.X - 1;
        }

        // The pools it can add to, right to left along the top (left of any ammo).
        if (!small)
        {
            const float pool = 6;
            float x = poolsEnd;
            foreach (Texture2D icon in marks.Pools ?? [])
            {
                x -= pool;
                canvas.DrawTextureRect(icon, new Rect2(x, rect.Position.Y + 2, pool, pool), tile: false, modulate: shade);
                x -= 1;
            }
        }

        if (!small)
        {
            // Its dice down its left edge, under the corner index; its bonuses in the bottom right corner.
            for (int i = 0; i < (marks.Dice?.Count ?? 0); i++)
            {
                Rect2 die = DieRect(rect, i, under.Length > 0);
                canvas.DrawRect(die, marks.Dice![i] * shade);
                canvas.DrawRect(die, ink, filled: false, width: 1);
            }

            float y = rect.End.Y - band - 1;
            foreach ((string text, Color color) in marks.Bonuses ?? [])
            {
                canvas.DrawString(font, new Vector2(rect.Position.X, y), text, HorizontalAlignment.Right, rect.Size.X - 2, UiTheme.FontSize, color.Darkened(0.45f) * shade);
                y -= 8;
            }
        }
    }

    /// <summary>A card face down in <paramref name="rect"/>: the lattice back, the slot's <paramref name="mark"/> faint in the middle and its <paramref name="name"/> under it.</summary>
    public static void DrawBack(CanvasItem canvas, Rect2 rect, Texture2D? mark, string name, bool small = false)
    {
        Font font = GameArt.BodyFont ?? ThemeDB.FallbackFont;
        canvas.DrawRect(rect, Woad);
        canvas.DrawTextureRect(Lattice(), rect.Grow(-3), tile: true);
        canvas.DrawRect(rect, Palette.Ink, filled: false, width: 1);
        canvas.DrawRect(rect.Grow(-2), Cream, filled: false, width: 1);
        Corners(canvas, rect);

        float band = small || name.Length == 0 ? 0 : 11;
        if (mark is not null)
        {
            float side = Mathf.Min(rect.Size.X - 16, rect.Size.Y - band - 10);
            if (side >= 6)
            {
                var at = new Vector2(rect.Position.X + (rect.Size.X - side) / 2, rect.Position.Y + 4 + (rect.Size.Y - band - 8 - side) / 2);
                var patch = new Rect2(at - Vector2.One, new Vector2(side + 2, side + 2));
                canvas.DrawRect(patch, WoadDark);
                canvas.DrawRect(patch, WoadLine, filled: false, width: 1);
                canvas.DrawTextureRect(mark, new Rect2(at, new Vector2(side, side)), tile: false, modulate: new Color(1, 1, 1, 0.55f));
            }
        }

        if (band > 0)
        {
            canvas.DrawRect(new Rect2(rect.Position.X + 3, rect.End.Y - band - 2, rect.Size.X - 6, band - 1), WoadDark);
            int size = font.GetStringSize(name, fontSize: UiTheme.FontSize).X <= rect.Size.X - 6 ? UiTheme.FontSize : UiTheme.FontSize - 2;
            canvas.DrawString(font, new Vector2(rect.Position.X, rect.End.Y - 5), name, HorizontalAlignment.Center, rect.Size.X, size, BackText);
        }
    }

    /// <summary>Rounded corners: one pixel of ink at each.</summary>
    private static void Corners(CanvasItem canvas, Rect2 rect)
    {
        foreach (Vector2 corner in new[] { rect.Position, new Vector2(rect.End.X - 1, rect.Position.Y), new Vector2(rect.Position.X, rect.End.Y - 1), rect.End - Vector2.One })
        {
            canvas.DrawRect(new Rect2(corner, Vector2.One), Palette.Ink);
        }
    }

    /// <summary>The back's diamond lattice, one tile of it, made once.</summary>
    private static ImageTexture Lattice()
    {
        if (_lattice is not null)
        {
            return _lattice;
        }

        const int Tile = 6;
        Image image = Image.CreateEmpty(Tile, Tile, false, Image.Format.Rgba8);
        image.Fill(Woad);
        for (int i = 0; i < Tile; i++)
        {
            image.SetPixel(i, i, WoadLine);
            image.SetPixel(Tile - 1 - i, i, WoadLine);
        }

        _lattice = ImageTexture.CreateFromImage(image);
        return _lattice;
    }
}
