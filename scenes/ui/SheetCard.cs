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

    public SheetCard()
    {
        Flat = true;
        ClipText = false;
    }

    /// <param name="owner">Whose card it is, for the ammo a weapon that shoots has to hand (the Sling's Rocks).</param>
    public void Show(CardInstance? card, string emptyName, bool selected, Hero? owner = null)
    {
        ShowCard(card, emptyName, selected);
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
            // Face down: the slot it's for.
            CardFace.DrawBack(this, rect, GameArt.Texture($"res://assets/cards/{Back}.png"), _emptyName, Small);
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
