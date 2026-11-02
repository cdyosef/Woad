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
/// <see cref="Dropped"/> and gives the matching command. A quick drag off a stack takes one card; press and
/// hold first (<see cref="HoldSeconds"/>) to take the whole stack. Ability cards drag too, but only between
/// their own hand's fan and its square (<see cref="BeginAbility"/>, <see cref="AbilityDropped"/>).
/// </summary>
public static partial class CardDrag
{
    private const string Key = "card";
    private const string AllKey = "all";
    private const string AbilityKey = "ability";
    private const string HandKey = "hand";

    /// <summary>How long the mouse is held down on a stack before a drag takes all of it.</summary>
    public const double HoldSeconds = 0.45;

    private static readonly Vector2 PreviewSize = new(24, 26);

    /// <summary>When the left mouse button last went down (the game screen notes it), to tell a quick drag from a held one.</summary>
    public static ulong PressedAtMsec { get; set; }

    /// <summary>A card was let go over a target: its id, where, and whether the whole stack was taken.</summary>
    public static event Action<int, DropTarget, bool>? Dropped;

    /// <summary>An ability card was let go over its hand: the hand's title, the ability's name, and whether it went into the square (else back to the fan).</summary>
    public static event Action<string, string, bool>? AbilityDropped;

    /// <summary>A card's drag data, with the card following the cursor: one of a stack of <paramref name="count"/>, or all of them if the mouse was held down first.</summary>
    public static Variant Begin(Control source, int cardId, string picture, string name = "", int count = 1)
    {
        bool all = count > 1 && (Time.GetTicksMsec() - PressedAtMsec) / 1000.0 >= HoldSeconds;
        source.SetDragPreview(new Preview(picture, name, all ? count : 1));
        return new Godot.Collections.Dictionary { [Key] = cardId, [AllKey] = all };
    }

    /// <summary>An ability card's drag data, from <paramref name="hand"/>'s fan or square.</summary>
    public static Variant BeginAbility(Control source, string hand, string name, string picture)
    {
        source.SetDragPreview(new Preview(picture, name, 1));
        return new Godot.Collections.Dictionary { [AbilityKey] = name, [HandKey] = hand };
    }

    /// <summary>The card being dragged, if the data is a card's.</summary>
    public static int? CardOf(Variant data) =>
        Fields(data) is { } fields && fields.TryGetValue(Key, out Variant id) ? id.AsInt32() : null;

    /// <summary>The ability being dragged and the hand it's from, if the data is an ability card's.</summary>
    public static (string Hand, string Name)? AbilityOf(Variant data) =>
        Fields(data) is { } fields && fields.TryGetValue(AbilityKey, out Variant name) ? (fields[HandKey].AsString(), name.AsString()) : null;

    public static bool Accepts(Variant data) => CardOf(data) is not null;

    public static void Drop(Variant data, DropTarget target)
    {
        if (CardOf(data) is int cardId)
        {
            Dropped?.Invoke(cardId, target, Fields(data)![AllKey].AsBool());
        }
    }

    public static void DropAbility(Variant data, bool intoSquare)
    {
        if (AbilityOf(data) is var (hand, name))
        {
            AbilityDropped?.Invoke(hand, name, intoSquare);
        }
    }

    /// <summary>A card's being dragged right now.</summary>
    public static bool Dragging(Node node) => node.GetViewport().GuiIsDragging() && Accepts(node.GetViewport().GuiGetDragData());

    private static Godot.Collections.Dictionary? Fields(Variant data) =>
        data.VariantType == Variant.Type.Dictionary ? data.AsGodotDictionary() : null;

    /// <summary>The card under the cursor while it's dragged: a small playing card, its count showing when it's a whole stack.</summary>
    private sealed partial class Preview : Control
    {
        private readonly string _picture;
        private readonly string _name;
        private readonly int _count;

        public Preview(string picture, string name, int count)
        {
            _picture = picture;
            _name = name;
            _count = count;
            MouseFilter = MouseFilterEnum.Ignore;
            Modulate = new Color(1, 1, 1, 0.9f);
        }

        public override void _Draw() =>
            CardFace.DrawFront(this, new Rect2(-PreviewSize / 2, PreviewSize), GameArt.Texture($"res://assets/cards/{_picture}.png"), _name,
                new CardFace.Marks(Count: _count), small: true);
    }
}
