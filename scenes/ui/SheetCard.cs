using Godot;
using Woad.Core.Model;

namespace Woad.Scenes.Ui;

/// <summary>
/// A card on the character sheet, drawn like a playing card (see <see cref="CardFace"/>): face up with its
/// picture (res://assets/cards/{id}.png), name and corner index, gilt-edged when it's selected. An empty
/// slot lies face down, with the slot's mark and name. Stacks show their count, charged cards their charges.
/// </summary>
public partial class SheetCard : CardSlot
{
    private string _emptyName = "";
    private string? _picture;
    private string? _caption;
    private Color? _accent;
    private CardInstance? _ammo;

    /// <summary>The card back shown while the slot is empty (back_head, back_bag...).</summary>
    public string Back { get; set; } = "back_bag";

    /// <summary>Drawn small (the belt's fan): picture only, no name.</summary>
    public bool Small { get; set; }

    /// <summary>A card that's gone from this slot for now (Sticky Gloves into a hand): drawn faint in the empty slot.</summary>
    public CardInstance? Ghost { get; private set; }

    /// <summary>Double-clicked (on its card, or on the ghost of one).</summary>
    public event Action? DoubleClicked;

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: true } && (Card is not null || Ghost is not null))
        {
            DoubleClicked?.Invoke();
        }
    }

    /// <summary>Shows the ghost of a card that's gone from the empty slot, clickable, with a tooltip on what to do with it.</summary>
    public void ShowGhost(CardInstance ghost, string tooltip)
    {
        Ghost = ghost;
        Disabled = false;
        TooltipText = tooltip;
        QueueRedraw();
    }

    public SheetCard()
    {
        Flat = true;
        ClipText = false;
    }

    /// <param name="owner">Whose card it is, for the ammo a weapon that shoots has to hand (the Sling's Rocks).</param>
    public void Show(CardInstance? card, string emptyName, bool selected, Hero? owner = null)
    {
        ShowCard(card, emptyName, selected);
        Ghost = null;
        _ammo = card is not null ? owner?.AmmoFor(card) : null;
        Text = "";
        _emptyName = emptyName;
        _picture = null;
        _caption = null;
        _accent = null;
        QueueRedraw();
    }

    /// <summary>A card that isn't an item: an ability, or the Magic card. Clickable unless <paramref name="enabled"/> is false.</summary>
    public void ShowFace(string picture, string caption, Color accent, bool enabled, string tooltip)
    {
        ShowCard(null, "", selected: false);
        Disabled = !enabled;
        Text = "";
        TooltipText = tooltip;
        _picture = picture;
        _caption = caption;
        _accent = accent;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        if (Card is null && _picture is null)
        {
            // Face down: the slot it's for; with the faint ghost of what's gone into a hand.
            CardFace.DrawBack(this, rect, GameArt.Texture($"res://assets/cards/{Back}.png"), Ghost is null ? _emptyName : "", Small);
            if (Ghost is { } ghost)
            {
                Rect2 face = Size.X > CardFace.Size.X + 4 ? new Rect2((Size - CardFace.Size) / 2, CardFace.Size) : rect;
                CardFace.DrawFront(this, face, GameArt.Texture($"res://assets/cards/{ghost.Definition.Id}.png"), ghost.Definition.Name, selected: false, hover: IsHovered(), small: Small);
                // Washed out, a dotted edge round it: it's only a ghost.
                DrawRect(face, new Color(0.10f, 0.12f, 0.18f, IsHovered() ? 0.4f : 0.6f));
                for (float x = face.Position.X; x < face.End.X; x += 4)
                {
                    DrawLine(new Vector2(x, face.Position.Y), new Vector2(x + 2, face.Position.Y), new Color(1, 1, 1, 0.6f), 1);
                    DrawLine(new Vector2(x, face.End.Y - 1), new Vector2(x + 2, face.End.Y - 1), new Color(1, 1, 1, 0.6f), 1);
                }
            }

            return;
        }

        // A square on the sheet is a slot: face down, with the card sitting in it.
        if (Size.X > CardFace.Size.X + 4)
        {
            CardFace.DrawBack(this, rect, GameArt.Texture($"res://assets/cards/{Back}.png"), "", Small);
            rect = new Rect2((Size - CardFace.Size) / 2, CardFace.Size);
        }

        bool hover = IsHovered() && !Disabled;
        string id = Card?.Definition.Id ?? _picture!;
        Texture2D? picture = GameArt.Texture($"res://assets/cards/{id}.png");
        if (Card is null)
        {
            // An ability, or the Magic card: its pool's color round it while it can be used.
            CardFace.DrawFront(this, rect, picture, _caption ?? "", selected: ButtonPressed, hover: hover, dim: Disabled, small: Small, border: Disabled ? null : _accent);
            return;
        }

        // A stack's count; charges left, a crossbow waiting to be reloaded or acid's wear instead; ammo to hand.
        CardInstance card = Card;
        (IReadOnlyList<(string Text, Color Color)> bonuses, IReadOnlyList<Color> dice) = CardFace.Adds(card.Definition);
        var marks = new CardFace.Marks(
            Bonuses: bonuses,
            Dice: dice,
            Pools: CardFace.Pools(card.Definition),
            Count: card.Count,
            Ammo: card.Definition.Ammo is { } ammo ? GameArt.Texture($"res://assets/cards/{ammo}.png") : null,
            AmmoCount: _ammo?.Count ?? 0,
            Note: card.Definition.Usage == Woad.Core.Content.CardUsage.Charges ? $"{card.ChargesRemaining}/{card.Definition.Charges}"
                : card.Unloaded ? "!"
                : card.Wear > 0 ? $"-{card.Wear}"
                : "",
            NoteIsBad: card.Unloaded || card.Wear > 0);
        CardFace.DrawFront(this, rect, picture, card.Definition.Name, marks, selected: ButtonPressed, hover: hover, small: Small);
    }
}
