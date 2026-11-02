using Godot;
using Woad.Core.Content;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>Where a card can be dropped: an equip square (which slot), the belt, the bag, the carry tab, the board, or anywhere else on the sheet.</summary>
public enum DropKind
{
    Equip,
    Belt,
    Bag,
    Carry,
    Board,
    Sheet,
}

public readonly record struct DropTarget(DropKind Kind, EquipSlot? Slot = null);

/// <summary>
/// Dragging cards about: any card the hero holds can be picked up from where it's shown (an equip square,
/// the belt, the bag, the carry tab) and let go over where it should go. Sources call <see cref="Begin"/>
/// from their drag data, targets <see cref="Accepts"/> and <see cref="Drop"/>; the game screen listens to
/// <see cref="Dropped"/> and gives the matching command.
/// </summary>
public static class CardDrag
{
    private const string Key = "card";
    private const float PreviewSize = 22;

    /// <summary>A card was let go over a target.</summary>
    public static event Action<int, DropTarget>? Dropped;

    /// <summary>A card's drag data, with its picture following the cursor.</summary>
    public static Variant Begin(Control source, int cardId, string picture)
    {
        var holder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        var image = new TextureRect
        {
            Texture = GameArt.Texture($"res://assets/cards/{picture}.png"),
            Position = new Vector2(-PreviewSize / 2, -PreviewSize / 2),
            Size = new Vector2(PreviewSize, PreviewSize),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Modulate = new Color(1, 1, 1, 0.85f),
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        };
        holder.AddChild(image);
        source.SetDragPreview(holder);
        return new Godot.Collections.Dictionary { [Key] = cardId };
    }

    /// <summary>The card being dragged, if the data is a card's.</summary>
    public static int? CardOf(Variant data) =>
        data.VariantType == Variant.Type.Dictionary && data.AsGodotDictionary() is { } fields && fields.TryGetValue(Key, out Variant id) ? id.AsInt32() : null;

    public static bool Accepts(Variant data) => CardOf(data) is not null;

    public static void Drop(Variant data, DropTarget target)
    {
        if (CardOf(data) is int cardId)
        {
            Dropped?.Invoke(cardId, target);
        }
    }

    /// <summary>A card's being dragged right now.</summary>
    public static bool Dragging(Node node) => node.GetViewport().GuiIsDragging() && Accepts(node.GetViewport().GuiGetDragData());
}
