using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// An ability card in a pool's hand: what it shows (its picture and name), the button whose action it
/// takes (and whose Disabled and tooltip say whether it can be done now, and why not), and whether it's
/// lit: something in reach it could be used on right now.
/// </summary>
public sealed record AbilityCard(string Picture, string Name, Button Action, Func<bool> Lit, Func<bool>? InHand = null)
{
    public bool Held => InHand?.Invoke() ?? true;

    public bool IsLit => !Action.Disabled && Lit();
}

/// <summary>
/// A card in a pool's square on the sheet: the pool's dice card, the item equipped there or an item that
/// adds to the pool's dice (a weapon, a tool), or the skill last played from its hand. It carries its
/// static bonus (shown in its bottom-left corner) and the dice on it, and is tapped once it can't be used
/// again this turn. Its border is its own pool's color (a shield's blue in the off hand's square).
/// </summary>
public sealed record StackCard(string Key, string Picture, string Name, bool Tapped, string Bonus = "", int Dice = 0, Color? Border = null, int? CardId = null);

/// <summary>
/// A pool's hand of cards. With a dice action (Rumble, Wend, Od Hand) it fills an equipment square on the
/// sheet: the square's cards staggered so each name shows, the selected card in front on the right with the
/// dice card lit just behind it, the rest dim further back, and tapped ones on their side at the very back.
/// Dice put on a card sit on it, the stack spreading to show them all. Under the square, the pool's dice
/// still to put on a card (up to four; with more, the fourth spot is the pool's symbol with the number held
/// in reserve), and at the right the hand itself, a few cards fanned: click it to spread the hand over the
/// board and play one, which then stays in the square as the pool's last skill. Clicking a card behind
/// brings it to the front; double-clicking the front card uses it; clicking a die moves it between the pool
/// and the card. A hand with no dice action (the Magic hand) is all fan.
/// </summary>
public partial class HandView : Control
{
    private IReadOnlyList<AbilityCard> _cards = [];

    /// <summary>The square, the dice row under it and the fan at its right.</summary>
    private const float SquareWidth = 48;
    private const float SquareHeight = 50;
    private const float DiceY = SquareHeight + 2;
    private const float DieSize = 9;
    private const float DieStep = 10;
    private const float FanX = 41;

    /// <summary>Dice in the row under the square before the last spot counts the rest.</summary>
    private const int PoolSpots = 4;

    /// <summary>How far each card behind sits above the one in front of it: enough to read its name.</summary>
    private const float TitleBand = 9;

    /// <summary>A card's dice: small, in a column along its left edge, as many as fit, then another column.</summary>
    private const float CardDie = 6;

    /// <summary>The pool's own dice action (Attack, Wend, Search), or null for a hand with no square.</summary>
    public AbilityCard? Default { get; init; }

    /// <summary>The skill last played from the hand, kept in the square; null for none yet.</summary>
    public AbilityCard? Played { get; set; }

    /// <summary>The square's cards: the dice card's key is "dice".</summary>
    public IReadOnlyList<StackCard> Stack { get; private set; } = [];

    /// <summary>The selected card's key (the one in front).</summary>
    public string Front { get; private set; } = DiceKey;

    /// <summary>The pool's dice not on a card.</summary>
    public int PoolDice { get; private set; }

    /// <summary>The pool's dice color, and whether its dice can be moved now.</summary>
    public Color DiceColor { get; init; } = Colors.White;

    private bool _diceEnabled;

    /// <summary>A short note under the dice (Wend's steps left), or "".</summary>
    private string _note = "";

    public const string DiceKey = "dice";

    public const string SkillKey = "skill";

    /// <summary>A card was clicked (once): it's to be selected, and brought to the front.</summary>
    public event Action<HandView, StackCard>? CardSelected;

    /// <summary>The front card was double-clicked: it's to be used.</summary>
    public event Action<HandView, StackCard>? CardUsed;

    /// <summary>A die in the pool was clicked: it goes on the dice card.</summary>
    public event Action<HandView>? DieToCard;

    /// <summary>A die on the dice card was clicked: it goes back to the pool.</summary>
    public event Action<HandView>? DieToPool;

    public void ShowStack(IReadOnlyList<StackCard> stack, string front, int poolDice, bool diceEnabled, string note = "")
    {
        Stack = stack;
        Front = stack.Any(card => card.Key == front) ? front : DiceKey;
        PoolDice = poolDice;
        _diceEnabled = diceEnabled;
        _note = note;
        QueueRedraw();
    }

    public string Icon { get; init; } = "";

    public Color Color { get; init; } = Colors.White;

    public string Title { get; init; } = "";

    public event Action<HandView>? Opened;

    public IReadOnlyList<AbilityCard> Cards => _cards;

    public HandView()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Size = new Vector2(SquareWidth + 4, DiceY + 22);
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public override void _Ready()
    {
        if (Default is null)
        {
            Size = new Vector2(13, 14);
        }
    }

    /// <summary>Where on the hand to click to spread it (the fan), in its own coordinates.</summary>
    public Vector2 FanPoint => Default is null ? new Vector2(6, 7) : new Vector2(FanX + 4, DiceY + 6);

    /// <summary>Where a card of the square shows, in its own coordinates, for a click on it.</summary>
    public Vector2 CardPoint(string key) => Placed().FirstOrDefault(placed => placed.Card.Key == key) is { Card: not null } found
        ? found.Rect.Position + new Vector2(found.Card.Key == Front ? found.Rect.Size.X / 2 : 2, TitleBand / 2)
        : FanPoint;

    /// <summary>Where the pool's first die under the square is, to click it.</summary>
    public Vector2 PoolDiePoint => new(DieSize / 2, DiceY + DieSize / 2);

    /// <summary>The cards in the hand, before there's anything to light them by.</summary>
    public void Hold(IReadOnlyList<AbilityCard> cards) => _cards = cards;

    public void ShowCards(IReadOnlyList<AbilityCard> cards)
    {
        _cards = cards;
        AbilityCard? lit = cards.FirstOrDefault(card => card.Held && card.IsLit);
        TooltipText = $"{Title}: click to spread out your hand of cards" + (lit is null ? "." : $". {lit.Name} can be used now!");
        QueueRedraw();
    }

    /// <summary>
    /// Where each card of the square lies, back to front: tapped cards first (on their side), then the cards
    /// not selected (dim), then the dice card (lit, unless it's the one in front), then the front card. Each
    /// sits a name's height above and a little left of the one in front of it, more to bare its dice.
    /// </summary>
    private List<(StackCard Card, Rect2 Rect, bool Lit)> Placed()
    {
        // The selected card in front; tapped, the dice card or the first card still usable comes forward instead.
        StackCard? front = Stack.FirstOrDefault(card => card.Key == Front && !card.Tapped)
            ?? Stack.FirstOrDefault(card => card.Key == DiceKey && !card.Tapped)
            ?? Stack.FirstOrDefault(card => !card.Tapped);
        StackCard? dice = Stack.FirstOrDefault(card => card.Key == DiceKey && card != front && !card.Tapped);
        List<StackCard> order =
        [
            .. Stack.Where(card => !card.Tapped && card != front && card != dice),
            .. dice is null ? [] : new[] { dice },
            .. front is null ? [] : new[] { front },
        ];
        List<StackCard> tapped = [.. Stack.Where(card => card.Tapped)];
        var lying = new List<(StackCard, Rect2, bool)>();
        for (int i = 0; i < tapped.Count; i++)
        {
            // On their side along the bottom of the square, at the very back.
            lying.Add((tapped[i], new Rect2(i * 6, SquareHeight - 22 - i * 3, 30, 22), false));
        }

        if (order.Count == 0)
        {
            return lying;
        }
        // Each card behind shows a strip at its left: wide enough for its dice (a column of them per three).
        float Strip(StackCard card) => Math.Max(7, 2 + (float)Math.Ceiling(card.Dice / 3f) * (CardDie + 1));
        float behind = order.Take(order.Count - 1).Sum(Strip);
        float width = Math.Max(22, SquareWidth - behind);
        float height = SquareHeight - TitleBand * (order.Count - 1) - (tapped.Count > 0 ? 6 : 0);
        var placed = lying;
        float x = 0;
        for (int i = 0; i < order.Count; i++)
        {
            StackCard card = order[i];
            bool lit = card == front || (card == dice && front is not null && front.Key != DiceKey);
            placed.Add((card, new Rect2(x, TitleBand * i, width, Math.Max(16, height)), lit));
            x += Strip(card);
        }

        return placed;
    }

    public override bool _HasPoint(Vector2 point) =>
        Default is null ? new Rect2(Vector2.Zero, Size).HasPoint(point)
            : new Rect2(0, 0, SquareWidth, SquareHeight).HasPoint(point) || new Rect2(0, DiceY, SquareWidth, DieSize + 2).HasPoint(point);

    public override string _GetTooltip(Vector2 atPosition)
    {
        if (Default is null || atPosition.X >= FanX && atPosition.Y >= DiceY)
        {
            return TooltipText;
        }

        if (atPosition.Y >= DiceY)
        {
            return $"Your {Title} dice: click one to put it on the {Default.Name} card.";
        }

        return CardAt(atPosition) is not { } card ? ""
            : card.Tapped ? $"{card.Name}: used up for this turn."
            : card.Key == Front ? $"{card.Name}: double-click to use it with the dice on your cards." + (card.Key == DiceKey ? " Click a die on it to take it off." : "")
            : $"{card.Name}: click to select it.";
    }

    private StackCard? CardAt(Vector2 point)
    {
        List<(StackCard Card, Rect2 Rect, bool Lit)> placed = Placed();
        for (int i = placed.Count - 1; i >= 0; i--)
        {
            if (placed[i].Rect.HasPoint(point))
            {
                return placed[i].Card;
            }
        }

        return null;
    }

    /// <summary>The dice card's dice, where they're drawn, for a click on one.</summary>
    private List<Rect2> CardDice()
    {
        var rects = new List<Rect2>();
        if (Placed().FirstOrDefault(placed => placed.Card.Key == DiceKey) is not { Card: not null } dice)
        {
            return rects;
        }

        for (int i = 0; i < dice.Card.Dice; i++)
        {
            rects.Add(new Rect2(dice.Rect.Position + new Vector2(1 + i / 3 * (CardDie + 1), TitleBand + 1 + i % 3 * (CardDie + 1)), new Vector2(CardDie, CardDie)));
        }

        return rects;
    }

    /// <summary>Where the dice card's first die is, to click it.</summary>
    public Vector2? CardDiePoint => CardDice() is [var first, ..] ? first.GetCenter() : null;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            return;
        }

        AcceptEvent();
        if (Default is null || (click.Position.X >= FanX && click.Position.Y >= DiceY))
        {
            Sfx.Play("click");
            Opened?.Invoke(this);
            return;
        }

        if (click.Position.Y >= DiceY)
        {
            if (_diceEnabled && PoolDice > 0 && click.Position.X < PoolSpots * DieStep)
            {
                Sfx.Play("click");
                DieToCard?.Invoke(this);
            }

            return;
        }

        if (_diceEnabled && CardDice().Any(rect => rect.Grow(0.5f).HasPoint(click.Position)))
        {
            Sfx.Play("click");
            DieToPool?.Invoke(this);
            return;
        }

        if (CardAt(click.Position) is not { } card)
        {
            return;
        }

        Sfx.Play("click");
        if (click.DoubleClick && card.Key == Front)
        {
            CardUsed?.Invoke(this, card);
        }
        else if (!click.DoubleClick)
        {
            CardSelected?.Invoke(this, card);
        }
    }

    /// <summary>For the click tests: as if the front card were double-clicked.</summary>
    internal void UseFront()
    {
        if (Stack.FirstOrDefault(card => card.Key == Front) is { } front)
        {
            CardUsed?.Invoke(this, front);
        }
    }

    public override void _Draw()
    {
        if (Default is not null)
        {
            DrawSquare();
            DrawPool();
        }

        DrawFan();
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>The square's cards, back to front, over the square (the slot's own card beneath stays out of sight).</summary>
    private void DrawSquare()
    {
        Font font = GameArt.BodyFont ?? ThemeDB.FallbackFont;
        DrawRect(new Rect2(0, 0, SquareWidth, SquareHeight), Palette.Ink);
        List<(StackCard Card, Rect2 Rect, bool Lit)> placed = Placed();
        StackCard? front = placed.Count > 0 ? placed[^1].Card : null;
        foreach ((StackCard card, Rect2 rect, bool lit) in placed)
        {
            Color border = card.Border ?? Color;
            if (card.Tapped)
            {
                // On its side and dim: its name reads up its left end.
                DrawRect(rect, Palette.Ink);
                DrawRect(rect, border.Darkened(0.6f), filled: false, width: 1);
                DrawSetTransform(rect.Position + new Vector2(2, rect.Size.Y - 2), -Mathf.Pi / 2, Vector2.One);
                DrawString(font, new Vector2(0, 7), card.Name.Split(' ')[0], HorizontalAlignment.Left, rect.Size.Y - 4, UiTheme.FontSize, Palette.TextDim.Darkened(0.4f));
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
                continue;
            }

            if (lit)
            {
                DrawRect(rect.Grow(1), border with { A = 0.45f });
            }

            DrawCardFace(font, rect, card, border, lit, lit ? 1f : 0.6f, card == front);
        }
    }

    /// <summary>One card: its name along the top, its picture, its bonus bottom-left and its dice down its left edge.</summary>
    private void DrawCardFace(Font font, Rect2 rect, StackCard card, Color border, bool lit, float dim, bool full)
    {
        var shade = new Color(dim, dim, dim);
        DrawRect(rect, lit ? Palette.PanelHover : Palette.PanelDark);
        DrawRect(rect, (lit ? border : border.Darkened(0.4f)) * shade with { A = 1 }, filled: false, width: 1);
        string name = full ? card.Name : card.Name.Split(' ')[0];
        DrawString(font, rect.Position + new Vector2(2, 8), name, HorizontalAlignment.Left, rect.Size.X - 3, UiTheme.FontSize, (lit ? Palette.Text : Palette.TextDim) * shade with { A = 1 });

        Texture2D? picture = card.Key == DiceKey ? GameArt.Ui(Icon) : GameArt.Texture($"res://assets/cards/{card.Picture}.png");
        float side = Math.Min(rect.Size.X - 10, rect.Size.Y - TitleBand - 10);
        if (picture is not null && side >= 8)
        {
            var at = new Vector2(rect.End.X - side - 2, rect.Position.Y + TitleBand + (rect.Size.Y - TitleBand - 8 - side) / 2);
            DrawTextureRect(picture, new Rect2(at, new Vector2(side, side)), tile: false, new Color(1, 1, 1, dim));
        }

        if (card.Bonus.Length > 0)
        {
            DrawString(font, new Vector2(rect.Position.X + 2, rect.End.Y - 2), card.Bonus, HorizontalAlignment.Left, -1, UiTheme.FontSize, Palette.TrimBright * shade with { A = 1 });
        }

        for (int i = 0; i < card.Dice; i++)
        {
            var die = new Rect2(rect.Position + new Vector2(1 + i / 3 * (CardDie + 1), TitleBand + 1 + i % 3 * (CardDie + 1)), new Vector2(CardDie, CardDie));
            DrawRect(die, (card.Key == DiceKey ? DiceColor : Palette.DieTool) * shade with { A = 1 });
            DrawRect(die, Palette.Ink, filled: false, width: 1);
        }
    }

    /// <summary>The pool's dice under the square: up to four, the fourth the reserve's count on the pool's symbol when there are more.</summary>
    private void DrawPool()
    {
        Font font = GameArt.BodyFont ?? ThemeDB.FallbackFont;
        int shown = PoolDice > PoolSpots ? PoolSpots - 1 : PoolDice;
        for (int i = 0; i < shown; i++)
        {
            var die = new Rect2(i * DieStep, DiceY, DieSize, DieSize);
            DrawRect(die, _diceEnabled ? DiceColor : DiceColor.Darkened(0.5f));
            DrawRect(die, Palette.Ink, filled: false, width: 1);
        }

        if (PoolDice > PoolSpots)
        {
            var reserve = new Rect2((PoolSpots - 1) * DieStep, DiceY - 1, DieSize + 2, DieSize + 2);
            if (GameArt.Ui(Icon) is { } icon)
            {
                DrawTextureRect(icon, reserve, tile: false);
            }

            DrawString(font, reserve.Position + new Vector2(2, 9), $"{PoolDice - shown}", HorizontalAlignment.Center, reserve.Size.X - 2, UiTheme.FontSize, Palette.TrimBright);
        }

        if (_note.Length > 0)
        {
            DrawString(font, new Vector2(0, DiceY + DieSize + 10), _note, HorizontalAlignment.Left, SquareWidth + 4, UiTheme.FontSize, Palette.Text);
        }
    }

    /// <summary>The hand: a few cards fanned (glowing while any is lit); with no square, its front card shows the symbol or a lit card.</summary>
    private void DrawFan()
    {
        AbilityCard? lit = _cards.FirstOrDefault(card => card.Held && card.IsLit);
        if (Default is not null)
        {
            for (int k = 0; k < 3; k++)
            {
                DrawSetTransform(new Vector2(FanX + 1 + k * 2.5f, DiceY + 10), -0.25f + k * 0.25f, Vector2.One);
                var back = new Rect2(-2.5f, -9, 5, 9);
                DrawRect(back, lit is null ? Palette.Ink : Palette.PanelHover);
                DrawRect(back, lit is null ? Palette.TrimDim : Color, filled: false, width: 1);
            }

            return;
        }

        // Two cards fanned behind, then the front one.
        for (int k = 2; k >= 1; k--)
        {
            DrawSetTransform(new Vector2(4 + k * 1.5f, 13), -0.2f * k, Vector2.One);
            DrawRect(new Rect2(-4, -12, 9, 12), Palette.Ink);
            DrawRect(new Rect2(-4, -12, 9, 12), Palette.TrimDim, filled: false, width: 1);
        }

        float rise = lit is null ? 0 : 2;
        DrawSetTransform(new Vector2(0, -rise), 0, Vector2.One);
        var front = new Rect2(0, 1, 11, 13);
        if (lit is not null)
        {
            DrawRect(front.Grow(1), Color with { A = 0.45f });
        }

        DrawRect(front, lit is null ? Palette.PanelDark : Palette.PanelHover);
        DrawRect(front, lit is null ? Color.Darkened(0.3f) : Color, filled: false, width: 1);
        Texture2D? picture = lit is not null ? GameArt.Texture($"res://assets/cards/{lit.Picture}.png") : GameArt.Ui(Icon);
        if (picture is not null)
        {
            DrawTextureRect(picture, new Rect2(1, 3, 9, 9), tile: false);
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}

/// <summary>
/// A hand of ability cards spread out over the board: the dull ones at the back (left), the ones that
/// can be used now lit and standing up at the front. Every card can be picked, lit or not; one that
/// can't be done now says why instead. Clicking anywhere else, or right-clicking, puts the hand away.
/// </summary>
public partial class HandSpread : Control
{
    private readonly List<(SheetCard View, AbilityCard Card)> _shown = [];
    private Label _title = null!;

    /// <summary>A card was picked from a hand: its action to take (or explain).</summary>
    public event Action<HandView, AbilityCard>? Picked;

    public event Action? Closed;

    public HandView? Hand { get; private set; }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;
        AddChild(new ColorRect { Size = Size, Color = new Color(0.02f, 0.02f, 0.05f, 0.6f), MouseFilter = MouseFilterEnum.Ignore });
        _title = UiTheme.Label(new Vector2(0, 12), new Vector2(Size.X, 14));
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_title);
    }

    /// <summary>The card for an ability's button, while the hand it's in is spread out.</summary>
    internal SheetCard? CardFor(Button action) => _shown.FirstOrDefault(entry => entry.Card.Action == action).View;

    public void Open(HandView hand)
    {
        Close();
        Hand = hand;
        Visible = true;
        QueueRedraw();
        _title.Text = hand.Title.ToUpperInvariant();
        _title.AddThemeColorOverride("font_color", hand.Color);

        // Dull cards at the back, lit ones at the front, standing up.
        List<AbilityCard> cards = [.. hand.Cards.Where(card => card.Held).OrderBy(card => card.IsLit)];
        const float width = 38, height = 52;
        float spread = Math.Min(40, (Size.X - 30) / Math.Max(1, cards.Count));
        float start = Size.X / 2 - spread * (cards.Count - 1) / 2;
        for (int i = 0; i < cards.Count; i++)
        {
            AbilityCard card = cards[i];
            bool lit = card.IsLit;
            float tilt = (i - (cards.Count - 1) / 2f) * 0.09f;
            var view = new SheetCard
            {
                Size = new Vector2(width, height),
                Position = new Vector2(start + i * spread - width / 2, Size.Y - height - 14 - (lit ? 12 : 0) + Math.Abs(tilt) * 30),
                PivotOffset = new Vector2(width / 2, height),
                Rotation = tilt,
                ToggleMode = false,
            };
            AddChild(view);
            view.ShowFace(card.Picture, card.Name, lit ? hand.Color : Palette.TrimDim, enabled: true, card.Action.TooltipText);
            view.Modulate = lit ? Colors.White : new Color(0.75f, 0.75f, 0.75f);
            view.Pressed += () =>
            {
                Close();
                Picked?.Invoke(hand, card);
            };
            _shown.Add((view, card));
        }
    }

    public void Close()
    {
        foreach ((SheetCard view, _) in _shown)
        {
            RemoveChild(view);
            view.QueueFree();
        }

        _shown.Clear();
        bool was = Visible;
        Visible = false;
        Hand = null;
        if (was)
        {
            Closed?.Invoke();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true })
        {
            Close();
            AcceptEvent();
        }
    }
}
