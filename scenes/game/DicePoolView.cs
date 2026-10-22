using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

public enum DicePool
{
    Rumble,
    Wend,
    OdHand,
    Defense,
    Power,
}

/// <summary>
/// The dice the active hero has left this turn, one row per pool, each led by its symbol: Rumble
/// (a fist) dice plus the weapon's value, Wend (a boot) dice plus the flat Wend bonus, Od Hand (a hamsa) dice plus
/// the tools' value, Defense (a shield: the base die, framed, and extra dice ready or spent, plus armor
/// and Block), and banked Power Dice. Selected tokens (bright, gold-edged) are what the next attack,
/// move or Od Hand roll will use (selected Power Dice go into whichever comes first); clicking a token
/// selects or deselects it (Defense can't be clicked: it's used when you're attacked). Hovering a row explains it.
/// </summary>
public partial class DicePoolView : Control
{
    private const float LabelWidth = 17;
    private const float Token = 11;
    private const float Gap = 3;
    private const float RowHeight = 13;

    private readonly Row[] _rows =
    [
        new("icon_rumble", "Rumble: your attack dice this turn, plus your weapon's value on one attack. Click dice to hold some back.") { Color = Palette.DieRumble },
        new("icon_wend", "Wend: your move dice this turn, plus your flat Wend bonus on one move. Click dice to hold some back.") { Color = Palette.DieWend },
        new("icon_odhand", "Od Hand: dice for picking locks, searching, disarming traps and stealing. Your tools (lockpicks) add their value and dice to one lock-picking roll a turn.") { Color = Palette.DieOdHand },
        new("icon_defense", "Defense: your base die (framed) and armor defend against every attack. When a defense is losing, your Block goes in, then your extra dice one at a time; they're spent until your next turn.") { IsDefense = true, Color = Palette.DieDefense },
        new("icon_power", "Power Dice, earned by rolling a 6. Click to add banked ones to your next roll.") { IsPower = true, Color = Palette.Power },
    ];

    private bool _enabled;

    /// <summary>A token was clicked: a die (its index) or the bonus token (index -1).</summary>
    public event Action<DicePool, int>? TokenClicked;

    /// <param name="bonus">The weapon, Speed or tools still available this turn (0 when used or none).</param>
    /// <param name="note">Text drawn after the tokens, such as the steps left in a move.</param>
    /// <param name="bonusDice">Dice the weapon or tools add along with their value (the Longsword's or a lockpick's d6).</param>
    public void ShowPool(DicePool pool, int dice, int selected, int bonus, bool bonusSelected, string note = "", int bonusDice = 0)
    {
        Row row = _rows[(int)pool];
        row.Dice = dice;
        row.BonusDice = bonus != 0 ? bonusDice : 0;
        row.Selected = selected;
        row.Bonus = bonus;
        row.BonusSelected = bonusSelected;
        row.Note = note;
        QueueRedraw();
    }

    /// <summary>The Defense row: the base die, the extra dice (the first <paramref name="ready"/> of them ready, the rest spent), armor and Block.</summary>
    public void ShowDefense(int baseDice, int extraDice, int ready, int armor, int block, bool blockReady)
    {
        Row row = _rows[(int)DicePool.Defense];
        row.Dice = baseDice + extraDice;
        row.Base = baseDice;
        row.Selected = baseDice + ready;
        row.Bonus = armor;
        row.Block = block;
        row.BlockReady = blockReady;
        row.Note = "";
        QueueRedraw();
    }

    public bool Enabled
    {
        set { _enabled = value; QueueRedraw(); }
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        int row = (int)(atPosition.Y / RowHeight);
        return row >= 0 && row < _rows.Length ? _rows[row].Help : "";
    }

    public override void _Ready() => TooltipText = " ";

    public override void _Draw()
    {
        Font font = ThemeDB.FallbackFont;
        for (int r = 0; r < _rows.Length; r++)
        {
            Row row = _rows[r];
            float top = r * RowHeight;
            if (GameArt.Ui(row.Icon) is { } icon)
            {
                DrawTextureRect(icon, new Rect2(0, top, 12, 12), tile: false);
            }
            else if (row.IsPower)
            {
                DrawString(font, new Vector2(1, top + 10), "6!", fontSize: UiTheme.FontSize, modulate: Palette.Power);
            }

            if (row.IsDefense)
            {
                DrawDefense(font, r, row);
                continue;
            }

            for (int i = 0; i < row.Dice; i++)
            {
                DrawDieToken(TokenRect(r, i), _enabled && i < row.Selected, row.Color);
            }

            float x = LabelWidth + row.Dice * (Token + Gap);
            if (row.Bonus != 0)
            {
                Rect2 rect = BonusRect(r, row);
                bool on = _enabled && row.BonusSelected;
                DrawRect(rect, on ? Palette.PanelPressed : Palette.PanelDark);
                DrawRect(rect, on ? Palette.TrimBright : Palette.TrimDim, filled: false, width: 1);
                string text = (row.Bonus > 0 ? $"+{row.Bonus}" : row.Bonus.ToString()) + (row.BonusDice > 0 ? $"+{row.BonusDice}d6" : "");
                DrawString(font, rect.Position + new Vector2(3, 9), text, fontSize: UiTheme.FontSize, modulate: on ? Palette.TrimBright : Palette.TextDim);
                x = rect.End.X + Gap;
            }
            else if (row.Dice == 0)
            {
                string empty = row.IsPower ? "roll a 6 to earn one" : "used";
                DrawString(font, new Vector2(x, top + 10), empty, fontSize: UiTheme.FontSize, modulate: Palette.TextDim);
                x += 24;
            }

            if (row.Note.Length > 0)
            {
                DrawString(font, new Vector2(x + 2, top + 10), row.Note, fontSize: UiTheme.FontSize, modulate: Palette.Text);
            }
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click || !_enabled)
        {
            return;
        }

        for (int r = 0; r < _rows.Length; r++)
        {
            Row row = _rows[r];
            if (row.IsDefense)
            {
                continue;
            }

            for (int i = 0; i < row.Dice; i++)
            {
                if (TokenRect(r, i).Grow(1).HasPoint(click.Position))
                {
                    Pick((DicePool)r, i);
                    return;
                }
            }

            if (row.Bonus != 0 && BonusRect(r, row).Grow(1).HasPoint(click.Position))
            {
                Pick((DicePool)r, -1);
                return;
            }
        }
    }

    private void Pick(DicePool pool, int index)
    {
        Sfx.Play("click");
        TokenClicked?.Invoke(pool, index);
        AcceptEvent();
    }

    /// <summary>
    /// The Defense row: the base die always lit, in a double frame (it can't be held back); extra dice
    /// lit while ready and dark once spent; then armor, and Block (dim once spent).
    /// </summary>
    private void DrawDefense(Font font, int r, Row row)
    {
        float top = r * RowHeight;
        for (int i = 0; i < row.Dice; i++)
        {
            Rect2 rect = TokenRect(r, i);
            DrawDieToken(rect, i < row.Selected, row.Color);
            if (i < row.Base)
            {
                DrawRect(rect.Grow(1), Palette.Ink, filled: false, width: 1);
                DrawRect(rect.Grow(2), Palette.TrimBright, filled: false, width: 1);
            }
        }

        float x = LabelWidth + row.Dice * (Token + Gap) + (row.Base > 0 ? 1 : 0);
        if (row.Dice == 0)
        {
            DrawString(font, new Vector2(x, top + 9), "none", fontSize: UiTheme.FontSize, modulate: Palette.TextDim);
            x += 24;
        }

        if (row.Bonus != 0)
        {
            var rect = new Rect2(new Vector2(x, top), new Vector2(18, Token));
            DrawRect(rect, Palette.PanelDark);
            DrawRect(rect, Palette.TrimDim, filled: false, width: 1);
            DrawString(font, rect.Position + new Vector2(3, 9), $"+{row.Bonus}", fontSize: UiTheme.FontSize, modulate: Palette.Text);
            x = rect.End.X + Gap;
        }

        if (row.Block > 0)
        {
            var rect = new Rect2(new Vector2(x, top), new Vector2(46, Token));
            DrawRect(rect, row.BlockReady ? Palette.PanelPressed : Palette.PanelDark);
            DrawRect(rect, row.BlockReady ? row.Color : Palette.TrimDim, filled: false, width: 1);
            DrawString(font, rect.Position + new Vector2(3, 9), $"Block {row.Block}", fontSize: UiTheme.FontSize, modulate: row.BlockReady ? row.Color : Palette.TextDim);
        }
    }

    /// <summary>A die token in its pool's color (Rumble red, Wend green, Od Hand yellow, Power purple); held-back ones are dark with a colored rim.</summary>
    private void DrawDieToken(Rect2 rect, bool selected, Color color)
    {
        DrawRect(rect, selected ? color : Palette.PanelHover);
        DrawRect(rect, selected ? Palette.TrimBright : color.Darkened(0.35f), filled: false, width: 1);
        Color pip = selected ? Palette.Ink : Palette.TextDim;
        foreach (float t in new[] { 0.27f, 0.5f, 0.73f })
        {
            DrawCircle(rect.Position + new Vector2(t, t) * rect.Size, 1.1f, pip, antialiased: true);
        }
    }

    private static Rect2 TokenRect(int row, int index) =>
        new(new Vector2(LabelWidth + index * (Token + Gap), row * RowHeight), new Vector2(Token, Token));

    private static Rect2 BonusRect(int row, Row data) =>
        new(new Vector2(LabelWidth + data.Dice * (Token + Gap), row * RowHeight), new Vector2(data.BonusDice > 0 ? 34 : 18, Token));

    private sealed class Row(string icon, string help)
    {
        public string Icon { get; } = icon;

        public string Help { get; } = help;

        public bool IsPower { get; init; }

        /// <summary>The Defense row: not clickable, drawn by <see cref="DrawDefense"/>.</summary>
        public bool IsDefense { get; init; }

        /// <summary>Defense: how many of the dice are the base die (always rolled).</summary>
        public int Base { get; set; }

        public int Block { get; set; }

        public bool BlockReady { get; set; }

        /// <summary>The pool's dice color, matching its dice in the dice bar and its buttons.</summary>
        public Color Color { get; init; } = Palette.Text;

        public int Dice { get; set; }

        public int Selected { get; set; }

        public int Bonus { get; set; }

        public int BonusDice { get; set; }

        public bool BonusSelected { get; set; }

        public string Note { get; set; } = "";
    }
}
