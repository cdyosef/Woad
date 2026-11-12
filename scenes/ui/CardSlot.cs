using Godot;
using Woad.Core.Content;
using Woad.Core.Model;

namespace Woad.Scenes.Ui;

/// <summary>A clickable card in an equipment slot, the belt or the backpack.</summary>
public partial class CardSlot : Button
{
    public CardSlot()
    {
        ToggleMode = true;
        FocusMode = FocusModeEnum.None;
        Alignment = HorizontalAlignment.Left;
        ClipText = true;
        TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        Pressed += () => Sfx.Play("click");
    }

    public CardInstance? Card { get; private set; }

    /// <summary>The card can be dragged from here (to an equip square, the belt, the bag...).</summary>
    public bool Draggable { get; set; }

    /// <summary>What letting a dragged card go over this slot does; null takes no drops.</summary>
    public Game.DropTarget? DropsAs { get; set; }

    /// <summary>The drag data for a card that isn't an item (an ability card in a spread hand); null for none.</summary>
    public Func<Control, Variant>? FaceDrag { get; set; }

    public override Variant _GetDragData(Vector2 atPosition) =>
        Draggable && Card is { } card ? Game.CardDrag.Begin(this, card.Id, card.Definition.Id, card.Definition.Name, card.Count)
        : Card is null && FaceDrag is { } drag ? drag(this)
        : default;

    public override bool _CanDropData(Vector2 atPosition, Variant data) => DropsAs is not null && Game.CardDrag.Accepts(data);

    public override void _DropData(Vector2 atPosition, Variant data) => Game.CardDrag.Drop(data, DropsAs!.Value);

    public void ShowCard(CardInstance? card, string emptyText, bool selected)
    {
        Card = card;
        Disabled = card is null;
        SetPressedNoSignal(selected && card is not null);
        Text = card is null ? emptyText : Title(card);
        TooltipText = card is null ? "" : $"{card.Definition.Name}\n{card.Definition.Text}";
    }

    /// <summary>A card came under the mouse (or none now): the game's hover box tells about it.</summary>
    public static event Action<CardInstance?>? Hovered;

    /// <summary>Tells the hover box a card is under the mouse (from anywhere that shows cards).</summary>
    public static void Hover(CardInstance? card) => Hovered?.Invoke(card);

    public override void _Notification(int what)
    {
        if (what == NotificationMouseEnter && Card is not null)
        {
            Hover(Card);
        }
        else if (what == NotificationMouseExit && Card is not null)
        {
            Hover(null);
        }
    }

    public static string Title(CardInstance card) =>
        card.Wear > 0 ? $"{card.Definition.Name} (worn -{card.Wear})"
        : card.Definition.Usage == CardUsage.Charges
            ? $"{card.Definition.Name} ({card.ChargesRemaining}/{card.Definition.Charges})"
            : card.Unloaded ? $"{card.Definition.Name} (unloaded)"
            : card.Count > 1 ? $"{card.Definition.Name} x{card.Count}" : card.Definition.Name;
}
