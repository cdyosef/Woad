using Godot;
using Woad.Core.Content;
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
public sealed record StackCard(string Key, string Picture, string Name, bool Tapped, string Bonus = "", int Dice = 0, Color? Border = null, int? CardId = null, int Count = 1);

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
            return;
        }

        AddChild(new FanDrop(this) { Position = new Vector2(FanX - 3, DiceY - 3), Size = new Vector2(Size.X - FanX + 3, Size.Y - DiceY + 3) });
    }

    /// <summary>The fan, as a place to let go of this hand's ability card: it goes back into the hand. Clicks pass on through to the hand.</summary>
    private sealed partial class FanDrop(HandView hand) : Control
    {
        public FanDrop() : this(null!)
        {
        }

        public override void _Ready() => MouseFilter = MouseFilterEnum.Pass;

        public override bool _CanDropData(Vector2 atPosition, Variant data) => CardDrag.AbilityOf(data) is var (title, _) && title == hand.Title;

        public override void _DropData(Vector2 atPosition, Variant data) => CardDrag.DropAbility(data, intoSquare: false);
    }

    /// <summary>Where on the hand to click to spread it (the fan), in its own coordinates.</summary>
    public Vector2 FanPoint => Default is null ? new Vector2(6, 7) : new Vector2(FanX + 4, DiceY + 6);

    /// <summary>Where a card of the square shows, in its own coordinates, for a click on it.</summary>
    public Vector2 CardPoint(string key) => Placed().FirstOrDefault(placed => placed.Card.Key == key) is { Card: not null } found
        ? found.Rect.Position + (found.Card.Key == Front ? found.Rect.Size / 2 : new Vector2(3, 3))
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
    /// Where each card of the square lies, back to front: tapped cards first (on their side, face down), then the
    /// cards not selected (dim), then the dice card (lit, unless it's the one in front), then the front card. Each
    /// is a playing card (see <see cref="CardFace.Size"/>) sitting a little above and left of the one in front of
    /// it, so its corner index and the dice down its left edge show.
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
        Vector2 size = CardFace.Size;
        List<StackCard> tapped = [.. Stack.Where(card => card.Tapped)];
        var placed = new List<(StackCard, Rect2, bool)>();
        for (int i = 0; i < tapped.Count; i++)
        {
            // On their side along the bottom of the square, at the very back.
            placed.Add((tapped[i], new Rect2(i * 3, SquareHeight - size.X - i * 2, size.Y, size.X), false));
        }

        int count = order.Count;
        float dx = count > 1 ? Math.Min(9, (SquareWidth - size.X) / (count - 1)) : 0;
        float dy = count > 1 ? Math.Min(5, (SquareHeight - size.Y) / (count - 1)) : 0;
        var start = new Vector2((SquareWidth - size.X - dx * (count - 1)) / 2, (SquareHeight - size.Y - dy * (count - 1)) / 2);
        for (int i = 0; i < count; i++)
        {
            StackCard card = order[i];
            bool lit = card == front || (card == dice && front is not null && front.Key != DiceKey);
            placed.Add((card, new Rect2(start + new Vector2(dx * i, dy * i), size), lit));
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

    /// <summary>The slot this square equips (main hand, off hand, feet), for cards dropped on it; null takes none.</summary>
    public EquipSlot? Slot { get; set; }

    /// <summary>
    /// An item in the square drags like any card; the skill played into it drags back out to the fan. Ability
    /// cards only go between this hand's own fan and square.
    /// </summary>
    public override Variant _GetDragData(Vector2 atPosition) =>
        Default is null ? default
        : CardAt(atPosition) is { CardId: int cardId } card ? CardDrag.Begin(this, cardId, card.Picture, card.Name, card.Count)
        : CardAt(atPosition) is { Key: SkillKey } && Played is { } played ? CardDrag.BeginAbility(this, Title, played.Name, played.Picture)
        : default;

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        CardDrag.AbilityOf(data) is var (hand, _) ? Default is not null && hand == Title : Slot is not null && CardDrag.Accepts(data);

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (CardDrag.AbilityOf(data) is not null)
        {
            // Over the square, it's played into it (the fan takes its own drops: see FanDrop).
            CardDrag.DropAbility(data, intoSquare: true);
            return;
        }

        CardDrag.Drop(data, new DropTarget(DropKind.Equip, Slot));
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
            rects.Add(CardFace.DieRect(dice.Rect, i));
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

    /// <summary>The square, a slot face down with the slot's mark, and its cards on it back to front.</summary>
    private void DrawSquare()
    {
        string back = Slot switch
        {
            EquipSlot.MainHand => "back_main_hand",
            EquipSlot.OffHand => "back_off_hand",
            _ => "back_feet",
        };
        CardFace.DrawBack(this, new Rect2(0, 0, SquareWidth, SquareHeight), GameArt.Texture($"res://assets/cards/{back}.png"), "");
        foreach ((StackCard card, Rect2 rect, bool lit) in Placed())
        {
            Color border = card.Border ?? Color;
            if (card.Tapped)
            {
                // Tapped: on its side and face down, its picture faint on the back.
                CardFace.DrawBack(this, rect, Picture(card), "", small: true);
                continue;
            }

            if (lit)
            {
                DrawRect(rect.Grow(1), border with { A = 0.45f });
            }

            // Its bonus in the corner and its dice down its edge: the pool's own on the dice card, the tools' on the rest.
            var marks = new CardFace.Marks(
                Count: card.Count,
                Bonuses: card.Bonus.Length > 0 ? [(card.Bonus, border)] : null,
                Dice: [.. Enumerable.Repeat(card.Key == DiceKey ? DiceColor : Palette.DieTool, card.Dice)]);
            CardFace.DrawFront(this, rect, Picture(card), card.Name, marks, dim: !lit, border: lit ? border : border.Darkened(0.4f));
        }
    }

    private Texture2D? Picture(StackCard card) => GameArt.Texture($"res://assets/cards/{card.Picture}.png");

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
                // Card backs, edged in the pool's color while one can be used.
                var back = new Rect2(-2.5f, -9, 5, 9);
                DrawRect(back, CardFace.Woad);
                DrawRect(back, lit is null ? CardFace.Cream.Darkened(0.3f) : Color, filled: false, width: 1);
            }

            return;
        }

        // Two cards fanned behind, then the front one.
        for (int k = 2; k >= 1; k--)
        {
            DrawSetTransform(new Vector2(4 + k * 1.5f, 13), -0.2f * k, Vector2.One);
            DrawRect(new Rect2(-4, -12, 9, 12), CardFace.Woad);
            DrawRect(new Rect2(-4, -12, 9, 12), CardFace.Cream.Darkened(0.3f), filled: false, width: 1);
        }

        float rise = lit is null ? 0 : 2;
        DrawSetTransform(new Vector2(0, -rise), 0, Vector2.One);
        var front = new Rect2(0, 1, 11, 13);
        if (lit is not null)
        {
            DrawRect(front.Grow(1), Color with { A = 0.45f });
        }

        DrawRect(front, lit is null ? CardFace.Cream.Darkened(0.25f) : CardFace.Cream);
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
        float width = CardFace.Size.X, height = CardFace.Size.Y;
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
            if (hand.Default is not null)
            {
                // Dragged to its own hand's square, it's played into it without being used.
                view.FaceDrag = source => CardDrag.BeginAbility(source, hand.Title, card.Name, card.Picture);
            }
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
