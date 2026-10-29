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
/// A pool's hand of ability cards, small, where the pool's symbol goes on the sheet: a few cards fanned
/// behind the front one, which shows the pool's symbol, or the picture of a card that's lit. While any
/// card is lit the hand glows and its front card stands up. Click it to spread the hand over the board.
/// </summary>
public partial class HandView : Control
{
    private IReadOnlyList<AbilityCard> _cards = [];

    /// <summary>The pool's symbol, on the front card while nothing is lit (icon_rumble...).</summary>
    public string Icon { get; init; } = "";

    public Color Color { get; init; } = Colors.White;

    public string Title { get; init; } = "";

    public event Action<HandView>? Opened;

    public IReadOnlyList<AbilityCard> Cards => _cards;

    public HandView()
    {
        MouseFilter = MouseFilterEnum.Stop;
        Size = new Vector2(13, 14);
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    /// <summary>The cards in the hand, before there's anything to light them by.</summary>
    public void Hold(IReadOnlyList<AbilityCard> cards) => _cards = cards;

    public void ShowCards(IReadOnlyList<AbilityCard> cards)
    {
        _cards = cards;
        AbilityCard? lit = cards.FirstOrDefault(card => card.Held && card.IsLit);
        TooltipText = $"{Title}: click to spread out your hand of cards" + (lit is null ? "." : $". {lit.Name} can be used now!");
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            Sfx.Play("click");
            Opened?.Invoke(this);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        AbilityCard? lit = _cards.FirstOrDefault(card => card.Held && card.IsLit);
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

    /// <summary>A card was picked: its action to take (or explain).</summary>
    public event Action<AbilityCard>? Picked;

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
                Picked?.Invoke(card);
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
