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
/// The dice the active hero has left this turn, each pool led by its symbol and laid out under its card
/// on the character sheet (see <see cref="Place"/>): Rumble (a fist) dice plus the weapon's value, Wend
/// (a boot) dice plus the flat Wend bonus, Od Hand (a hamsa) dice plus the tools' value, Defense (a
/// shield: the base die, framed, and extra dice ready or spent, plus armor and Block), and the Power
/// Dice bank (filled and empty slots). Each pool takes two short lines: its dice, then its bonus, a
/// note and the weapon swap. Selected tokens (bright, gold-edged) are what the next attack, move or Od
/// Hand roll will use (selected Power Dice go into whichever comes first); clicking a token selects or
/// deselects it (Defense can't be clicked: it's used when you're attacked). Hovering a pool explains it.
/// </summary>
public partial class DicePoolView : Control
{
    private const float Icon = 11;
    private const float Token = 10;
    private const float Step = 12;
    private const float Line = 12;

    private readonly Row[] _rows =
    [
        new("icon_rumble", "Rumble: your attack dice this turn, plus your weapon's value on one attack. Click dice to hold some back.") { Color = Palette.DieRumble },
        new("icon_wend", "Wend: your move dice this turn, plus your flat Wend bonus on one move. Click dice to hold some back.") { Color = Palette.DieWend },
        new("icon_odhand", "Od Hand: dice for picking locks, searching, disarming traps and stealing. Your tools (lockpicks) add their value and dice to one lock-picking roll a turn.") { Color = Palette.DieOdHand },
        new("icon_defense", "Defense: your base die (framed) and armor defend against every attack. When a defense is losing, your Block goes in, then your extra dice one at a time; they're spent until your next turn. Gold dice are Guard Dice, for an ally beside you.") { IsDefense = true, Color = Palette.DieDefense },
        new("icon_power", "Power Dice, earned by rolling a 6: your bank, filled and empty. Click to add banked ones to your next roll.") { IsPower = true, Color = Palette.Power },
    ];

    private bool _enabled;

    public DicePoolView()
    {
        for (int r = 0; r < _rows.Length; r++)
        {
            _rows[r].Origin = new Vector2(0, r * 2 * Line);
            _rows[r].Width = 160;
        }
    }

    /// <summary>A token was clicked: a die (its index) or the bonus token (index -1).</summary>
    public event Action<DicePool, int>? TokenClicked;

    /// <summary>A row's swap button was clicked (the Rumble row's weapon, for a hero holding two).</summary>
    public event Action<DicePool>? SwapClicked;

    /// <summary>For the click tests: as if the Rumble row's swap button were clicked.</summary>
    internal void EmitSwap() => SwapClicked?.Invoke(DicePool.Rumble);

    /// <summary>Puts a pool at <paramref name="origin"/> (in this view), <paramref name="width"/> wide: two lines, its dice wrapping onto the second.</summary>
    public void Place(DicePool pool, Vector2 origin, float width)
    {
        _rows[(int)pool].Origin = origin;
        _rows[(int)pool].Width = width;
        QueueRedraw();
    }

    /// <summary>Leaves a pool's symbol undrawn: its hand of cards goes there instead (see <see cref="HandView"/>).</summary>
    /// <summary>Leaves a pool out altogether: its dice are drawn with its cards (see <see cref="HandView"/>).</summary>
    public void Hide(DicePool pool)
    {
        _rows[(int)pool].Hidden = true;
        QueueRedraw();
    }

    /// <param name="width">How much of the row's start the cards take (its dice start after).</param>
    /// <param name="below">The cards take the whole first line: the dice go on the second, after <paramref name="width"/>.</param>
    public void CoverIcon(DicePool pool, float width = Icon, bool below = false)
    {
        _rows[(int)pool].Covered = true;
        _rows[(int)pool].Reserve = width;
        _rows[(int)pool].Below = below;
        QueueRedraw();
    }

    /// <summary>The area a pool takes up, to put something beside it (its hand of cards).</summary>
    public Rect2 Bounds(DicePool pool) => new(_rows[(int)pool].Origin, new Vector2(_rows[(int)pool].Width, 2 * Line));

    /// <summary>A small button at the end of a row (the weapon an attack uses), or null for none.</summary>
    public void ShowSwap(DicePool pool, string? text, string tooltip = "")
    {
        Row row = _rows[(int)pool];
        row.Swap = text;
        row.SwapHelp = tooltip;
        QueueRedraw();
    }

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

    /// <summary>The Power Dice bank: how many slots it has, shown empty past the dice in it.</summary>
    public void ShowCapacity(int capacity)
    {
        _rows[(int)DicePool.Power].Capacity = capacity;
        QueueRedraw();
    }

    /// <summary>The Defense row: the base die, the extra dice (the first <paramref name="ready"/> of them ready, the rest spent), armor and Block.</summary>
    /// <param name="guard">Guard Dice for allies beside you (the Knight's set), the first <paramref name="guardReady"/> ready.</param>
    public void ShowDefense(int baseDice, int extraDice, int ready, int armor, int block, bool blockReady, int guard = 0, int guardReady = 0)
    {
        Row row = _rows[(int)DicePool.Defense];
        row.Guard = guard;
        row.GuardReady = guardReady;
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
        foreach (Row row in _rows)
        {
            if (row.Swap is not null && row.SwapRect.Grow(1).HasPoint(atPosition))
            {
                return row.SwapHelp;
            }

            if (new Rect2(row.Origin, new Vector2(row.Width, 2 * Line)).HasPoint(atPosition))
            {
                return row.Help;
            }
        }

        return "";
    }

    public override void _Ready()
    {
        TooltipText = " ";
        MouseFilter = MouseFilterEnum.Pass;
    }

    public override void _Draw()
    {
        Font font = GameArt.BodyFont ?? ThemeDB.FallbackFont;
        foreach (Row row in _rows)
        {
            row.Tokens.Clear();
            row.BonusRect = row.SwapRect = default;
            if (row.Hidden)
            {
                continue;
            }

            var cursor = new Cursor(row);
            if (!row.Covered && GameArt.Ui(row.Icon) is { } icon)
            {
                DrawTextureRect(icon, new Rect2(row.Origin, new Vector2(Icon, Icon)), tile: false);
            }

            if (row.IsDefense)
            {
                DrawDefense(font, row, cursor);
                continue;
            }

            // Dice on one line under the cards squeeze up when there are many, leaving room for the bonus.
            float token = Token;
            if (row.Below && row.Dice > 0)
            {
                float room = row.Width - row.Reserve - 2 - (row.Bonus != 0 ? 26 : 0);
                token = Math.Clamp(room / row.Dice - 2, 5, Token);
            }

            for (int i = 0; i < row.Dice; i++)
            {
                Rect2 rect = cursor.Next(token);
                row.Tokens.Add(rect);
                DrawDieToken(rect, _enabled && i < row.Selected, row.Color);
            }

            if (row.IsPower)
            {
                // The empty slots in the bank.
                for (int i = row.Dice; i < row.Capacity; i++)
                {
                    Rect2 rect = cursor.Next(Token);
                    DrawRect(rect, Palette.Ink);
                    DrawRect(rect, Palette.TrimDim, filled: false, width: 1);
                }

                if (row.Capacity == 0 && row.Dice == 0)
                {
                    DrawString(font, cursor.Text(), "roll a 6 to earn one", fontSize: UiTheme.FontSize, modulate: Palette.TextDim);
                }

                continue;
            }

            cursor.NewLine();
            if (row.Bonus != 0)
            {
                string text = (row.Bonus > 0 ? $"+{row.Bonus}" : row.Bonus.ToString()) + (row.BonusDice > 0 ? $"+{row.BonusDice}d6" : "");
                Rect2 rect = cursor.Next(font.GetStringSize(text, fontSize: UiTheme.FontSize).X + 5);
                row.BonusRect = rect;
                bool on = _enabled && row.BonusSelected;
                DrawRect(rect, on ? Palette.PanelPressed : Palette.PanelDark);
                DrawRect(rect, on ? Palette.TrimBright : Palette.TrimDim, filled: false, width: 1);
                DrawString(font, rect.Position + new Vector2(2, 8), text, fontSize: UiTheme.FontSize, modulate: on ? Palette.TrimBright : Palette.TextDim);
            }
            else if (row.Dice == 0)
            {
                DrawString(font, cursor.Text(), "used", fontSize: UiTheme.FontSize, modulate: Palette.TextDim);
                cursor.Next(20);
            }

            if (row.Note.Length > 0)
            {
                DrawString(font, cursor.Text(), row.Note, fontSize: UiTheme.FontSize, modulate: Palette.Text);
                cursor.Next(font.GetStringSize(row.Note, fontSize: UiTheme.FontSize).X);
            }

            if (row.Swap is { } swap)
            {
                Rect2 rect = cursor.Next(Math.Min(font.GetStringSize(swap, fontSize: UiTheme.FontSize).X + 5, row.Width - row.Reserve - 1));
                float width = rect.Size.X;
                row.SwapRect = rect;
                DrawRect(rect, Palette.PanelDark);
                DrawRect(rect, _enabled ? row.Color : Palette.TrimDim, filled: false, width: 1);
                DrawString(font, rect.Position + new Vector2(2, 8), swap, HorizontalAlignment.Left, width - 3, UiTheme.FontSize, _enabled ? Palette.TrimBright : Palette.TextDim);
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

            for (int i = 0; i < row.Tokens.Count; i++)
            {
                if (row.Tokens[i].Grow(1).HasPoint(click.Position))
                {
                    Pick((DicePool)r, i);
                    return;
                }
            }

            if (row.Bonus != 0 && row.BonusRect.Grow(1).HasPoint(click.Position))
            {
                Pick((DicePool)r, -1);
                return;
            }

            if (row.Swap is not null && row.SwapRect.Grow(1).HasPoint(click.Position))
            {
                Sfx.Play("click");
                SwapClicked?.Invoke((DicePool)r);
                AcceptEvent();
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
    /// lit while ready and dark once spent; then, on the second line, armor, Block (dim once spent) and
    /// Guard Dice.
    /// </summary>
    private void DrawDefense(Font font, Row row, Cursor cursor)
    {
        for (int i = 0; i < row.Dice; i++)
        {
            Rect2 rect = cursor.Next(Token);
            DrawDieToken(rect, i < row.Selected, row.Color);
            if (i < row.Base)
            {
                DrawRect(rect.Grow(1), Palette.TrimBright, filled: false, width: 1);
            }
        }

        if (row.Dice == 0)
        {
            DrawString(font, cursor.Text(), "none", fontSize: UiTheme.FontSize, modulate: Palette.TextDim);
        }

        cursor.NewLine();
        if (row.Bonus != 0)
        {
            Rect2 rect = cursor.Next(16);
            DrawRect(rect, Palette.PanelDark);
            DrawRect(rect, Palette.TrimDim, filled: false, width: 1);
            DrawString(font, rect.Position + new Vector2(2, 8), $"+{row.Bonus}", fontSize: UiTheme.FontSize, modulate: Palette.Text);
        }

        if (row.Block > 0)
        {
            string text = $"Block {row.Block}";
            Rect2 rect = cursor.Next(font.GetStringSize(text, fontSize: UiTheme.FontSize).X + 5);
            DrawRect(rect, row.BlockReady ? Palette.PanelPressed : Palette.PanelDark);
            DrawRect(rect, row.BlockReady ? row.Color : Palette.TrimDim, filled: false, width: 1);
            DrawString(font, rect.Position + new Vector2(2, 8), text, fontSize: UiTheme.FontSize, modulate: row.BlockReady ? row.Color : Palette.TextDim);
        }

        // Guard Dice, for allies beside you: gold while ready.
        for (int i = 0; i < row.Guard; i++)
        {
            DrawDieToken(cursor.Next(Token), i < row.GuardReady, Palette.TrimBright);
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
            DrawCircle(rect.Position + new Vector2(t, t) * rect.Size, 1f, pip, antialiased: true);
        }
    }

    /// <summary>Where the next thing in a pool goes: along its line after the icon, wrapping onto the second line.</summary>
    private sealed class Cursor(Row row)
    {
        private float _x = row.Origin.X + row.Reserve + 2;
        private float _y = row.Origin.Y + (row.Below ? Line : 0);

        public Rect2 Next(float width)
        {
            if (_x + width > row.Origin.X + row.Width && _x > row.Origin.X + row.Reserve + 2)
            {
                NewLine();
            }

            // On the last line, what doesn't fit is cut short (a long weapon name) rather than run past the edge.
            if (_y > row.Origin.Y)
            {
                width = Math.Max(8, Math.Min(width, row.Origin.X + row.Width - _x));
            }

            var rect = new Rect2(_x, _y, width, Token);
            _x += width + 2;
            return rect;
        }

        /// <summary>Moves to the second line (once).</summary>
        public void NewLine()
        {
            if (_y == row.Origin.Y)
            {
                _x = row.Origin.X + row.Reserve + 2;
                _y = row.Origin.Y + Line;
            }
        }

        /// <summary>The baseline for text at the cursor.</summary>
        public Vector2 Text() => new(_x, _y + 8);
    }

    private sealed class Row(string icon, string help)
    {
        public string Icon { get; } = icon;

        public string Help { get; } = help;

        public Vector2 Origin { get; set; }

        public float Width { get; set; }

        public bool IsPower { get; init; }

        /// <summary>Its symbol is covered by its hand of cards.</summary>
        public bool Covered { get; set; }

        /// <summary>The width at the row's start its symbol (or its cards) take.</summary>
        public float Reserve { get; set; } = DicePoolView.Icon;

        /// <summary>Its cards take the whole first line, and its dice go on the second.</summary>
        public bool Below { get; set; }

        /// <summary>Not drawn at all: its square's cards show its dice.</summary>
        public bool Hidden { get; set; }

        /// <summary>The Power bank's slots.</summary>
        public int Capacity { get; set; }

        /// <summary>The Defense row: not clickable, drawn by <see cref="DrawDefense"/>.</summary>
        public bool IsDefense { get; init; }

        /// <summary>Defense: how many of the dice are the base die (always rolled).</summary>
        public int Base { get; set; }

        public int Block { get; set; }

        public bool BlockReady { get; set; }

        public int Guard { get; set; }

        public int GuardReady { get; set; }

        /// <summary>The pool's dice color, matching its dice in the dice bar and its cards.</summary>
        public Color Color { get; init; } = Palette.Text;

        public int Dice { get; set; }

        public int Selected { get; set; }

        public int Bonus { get; set; }

        public int BonusDice { get; set; }

        public bool BonusSelected { get; set; }

        public string Note { get; set; } = "";

        public string? Swap { get; set; }

        public string SwapHelp { get; set; } = "";

        /// <summary>Where the dice, bonus and swap button were last drawn, for clicks.</summary>
        public List<Rect2> Tokens { get; } = [];

        public Rect2 BonusRect { get; set; }

        public Rect2 SwapRect { get; set; }
    }
}
