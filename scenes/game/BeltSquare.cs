using Godot;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The belt square: the equip square that wears the belt. The belt's art wraps around the square, its
/// ends darkening as they go round the back, with the buckle in the middle, a button. The belt's cards
/// stand tucked behind it like playing cards, in a straight line on each side of the buckle, filling
/// from the buckle outward (left, then right, and so on); those nearest the buckle lie on top, and each
/// shows its corner index (its picture and count) in its outer top corner. The card under the cursor
/// rises in front of the rest without leaving its slot. Beneath the square, the belt fan opens the full
/// view of the belt's cards. With no belt worn, the square says Beltless.
/// </summary>
public partial class BeltSquare : Control
{
    public const float SquareWidth = 48;
    public const float SquareHeight = 50;

    private const float CardWidth = 14;
    private const float CardHeight = 28;
    private const float CardTop = 4;
    private const float BandTop = 30;
    private const float BandHeight = 8;
    private const float BuckleLeft = 18;
    private const float BuckleSize = 12;
    private const float FanX = 41;
    private const float FanY = 52;
    private const float Lift = 4;

    private static readonly Color Cream = new(0.93f, 0.89f, 0.78f);
    private static readonly Color CreamDark = new(0.72f, 0.66f, 0.54f);

    private Hero? _hero;
    private int? _selectedCardId;

    /// <summary>The slot under the cursor (its card rises), or -1; the buckle is <see cref="BuckleSlot"/>.</summary>
    private int _hover = -1;

    private const int BuckleSlot = -2;
    private const int FanSlot = -3;

    /// <summary>A belt card was clicked once: it's selected, for its buttons.</summary>
    public event Action<int>? CardSelected;

    /// <summary>A belt card was double-clicked: use it from the belt, or equip it into a hand.</summary>
    public event Action<int>? CardUsed;

    /// <summary>The buckle was clicked.</summary>
    public event Action? BuckleClicked;

    /// <summary>The belt fan was clicked: show every card on the belt.</summary>
    public event Action? FanClicked;

    private int Slots => _hero?.BeltCapacity ?? 0;

    public override void _Ready()
    {
        Size = new Vector2(SquareWidth, FanY + 10);
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = " ";
    }

    public void ShowHero(Hero? hero, int? selectedCardId)
    {
        _hero = hero;
        _selectedCardId = selectedCardId;
        QueueRedraw();
    }

    /// <summary>
    /// Where the belt's card in slot <paramref name="index"/> lies: slots alternate left and right of the
    /// buckle, nearest first. Cards on a side step apart as far as the square lets them.
    /// </summary>
    private Rect2 SlotRect(int index)
    {
        bool left = index % 2 == 0;
        int k = index / 2;
        int onSide = left ? (Slots + 1) / 2 : Slots / 2;
        float step = onSide <= 1 ? 0 : Math.Min(7, 10f / (onSide - 1));
        float x = left ? BuckleLeft + 2 - CardWidth - k * step : BuckleLeft + BuckleSize - 2 + k * step;
        return new Rect2(x, CardTop, CardWidth, CardHeight);
    }

    private static Rect2 BuckleRect => new(BuckleLeft, BandTop - 2, BuckleSize, BuckleSize);

    private static Rect2 FanRect => new(FanX - 2, FanY - 1, 12, 11);

    /// <summary>The slots from the back to the front: the outermost first on each side, the nearest the buckle last.</summary>
    private IEnumerable<int> BackToFront() =>
        Enumerable.Range(0, Slots).OrderByDescending(index => index / 2).ThenBy(index => index % 2);

    /// <summary>A point on the part of slot <paramref name="index"/>'s card that shows (for clicking it), relative to the square.</summary>
    internal Vector2 CardPoint(int index)
    {
        Rect2 rect = SlotRect(index);
        bool left = index % 2 == 0;
        bool covered = index / 2 > 0;
        float x = !covered ? rect.GetCenter().X : left ? rect.Position.X + 1.5f : rect.End.X - 1.5f;
        return new Vector2(x, CardTop + 8);
    }

    internal Vector2 BucklePoint => BuckleRect.GetCenter();

    internal Vector2 FanPoint => FanRect.GetCenter();

    /// <summary>What's at <paramref name="at"/>: a slot, the buckle, the fan, or -1 for nothing.</summary>
    private int HitTest(Vector2 at)
    {
        if (FanRect.HasPoint(at) && Slots > 0)
        {
            return FanSlot;
        }

        if (Slots > 0 && BuckleRect.Grow(1).HasPoint(at))
        {
            return BuckleSlot;
        }

        foreach (int index in BackToFront().Reverse())
        {
            if (SlotRect(index).HasPoint(at))
            {
                return index;
            }
        }

        return -1;
    }

    public override bool _HasPoint(Vector2 point) => new Rect2(-8, 0, SquareWidth + 12, SquareHeight).HasPoint(point) || FanRect.HasPoint(point);

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            SetHover(HitTest(motion.Position));
            return;
        }

        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            return;
        }

        int hit = HitTest(click.Position);
        SetHover(hit);
        switch (hit)
        {
            case FanSlot:
                Sfx.Play("click");
                FanClicked?.Invoke();
                break;
            case BuckleSlot:
                Sfx.Play("click");
                BuckleClicked?.Invoke();
                break;
            case >= 0 when CardIn(hit) is { } card:
                Sfx.Play("click");
                if (click.DoubleClick)
                {
                    CardUsed?.Invoke(card.Id);
                }
                else
                {
                    CardSelected?.Invoke(card.Id);
                }

                break;
            default:
                return;
        }

        AcceptEvent();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit)
        {
            SetHover(-1);
        }
    }

    private void SetHover(int hover)
    {
        if (hover != _hover)
        {
            _hover = hover;
            QueueRedraw();
        }
    }

    private CardInstance? CardIn(int index) => _hero is { } hero && index >= 0 && index < hero.Belt.Count ? hero.Belt[index] : null;

    public override Variant _GetDragData(Vector2 atPosition) =>
        HitTest(atPosition) is >= 0 and var hit && CardIn(hit) is { } card ? CardDrag.Begin(this, card.Id, card.Definition.Id) : default;

    public override bool _CanDropData(Vector2 atPosition, Variant data) => CardDrag.Accepts(data);

    public override void _DropData(Vector2 atPosition, Variant data) => CardDrag.Drop(data, new DropTarget(DropKind.Belt));

    public override string _GetTooltip(Vector2 atPosition)
    {
        if (_hero is not { } hero)
        {
            return "";
        }

        CardInstance? belt = hero.Equipment.GetValueOrDefault(EquipSlot.Belt);
        CardInstance? buckle = hero.Equipment.GetValueOrDefault(EquipSlot.Buckle);
        int hit = HitTest(atPosition);
        return hit switch
        {
            FanSlot => "The belt fan: every card on your belt, face up.",
            BuckleSlot => $"{belt?.Definition.Name}{(buckle is null ? "" : $", {buckle.Definition.Name}")}"
                + (hero.Mods.Count > 0 ? $"\nMods: {string.Join(", ", hero.Mods.Select(mod => mod.Definition.Name))}" : "")
                + $"\n{hero.Belt.Count}/{hero.BeltCapacity} cards. Click the buckle for the belt.",
            >= 0 when CardIn(hit) is { } card => $"{CardSlot.Title(card)}\nClick to pick it up; double-click to use it, or draw it into a hand.",
            >= 0 => "A free place on your belt.",
            _ => belt is null ? "Beltless: put on a belt to carry cards on it." : "",
        };
    }

    public override void _Draw()
    {
        Font font = GameArt.BodyFont ?? ThemeDB.FallbackFont;
        DrawRect(new Rect2(0, 0, SquareWidth, SquareHeight), Palette.Ink);
        DrawRect(new Rect2(0, 0, SquareWidth, SquareHeight), Palette.TrimDim, filled: false, width: 1);
        if (_hero is not { } hero || hero.Equipment.GetValueOrDefault(EquipSlot.Belt) is not { } belt)
        {
            DrawString(font, new Vector2(0, SquareHeight / 2 + 3), "Beltless", HorizontalAlignment.Center, SquareWidth, UiTheme.FontSize, Palette.TextDim);
            return;
        }

        // The cards behind the belt, back to front; the one under the cursor rises last of all.
        foreach (int index in BackToFront().Where(index => index != _hover))
        {
            DrawSlot(font, index, raised: false);
        }

        DrawBelt(hero.Appearance.BeltLook is { Length: > 0 } look ? look : belt.Definition.Id, hero);
        DrawString(font, new Vector2(0, SquareHeight - 2), belt.Definition.Name, HorizontalAlignment.Center, SquareWidth, UiTheme.FontSize - 2, Palette.TextDim);
        if (_hover >= 0 && _hover < Slots)
        {
            DrawSlot(font, _hover, raised: true);
        }

        DrawFan(hero.Belt.Count > 0);
    }

    private void DrawSlot(Font font, int index, bool raised)
    {
        Rect2 rect = SlotRect(index);
        CardInstance? card = CardIn(index);
        bool selected = card is not null && card.Id == _selectedCardId;
        if (raised || selected)
        {
            rect.Position -= new Vector2(0, raised ? Lift : 2);
        }

        if (card is null)
        {
            // A free place: just its outline, tucked behind the belt.
            DrawRect(rect, Palette.Ink.Lightened(0.06f));
            DrawRect(rect, Palette.TrimDim.Darkened(0.3f), filled: false, width: 1);
            return;
        }

        // Nearest the buckle nothing covers it, so its whole face shows; others only their corner, until raised.
        DrawPlayingCard(font, rect, card, outerLeft: index % 2 == 0, selected, raised || index < 2);
    }

    /// <summary>A card's face: cream, with rounded corners, its picture in the middle and its corner index in the outer top corner.</summary>
    private void DrawPlayingCard(Font font, Rect2 rect, CardInstance card, bool outerLeft, bool selected, bool whole)
    {
        DrawRect(rect, Cream);
        DrawRect(new Rect2(rect.Position.X, rect.End.Y - 3, rect.Size.X, 3), CreamDark);
        DrawRect(rect, selected ? Palette.TrimBright : Palette.Ink, filled: false, width: selected ? 1.5f : 1);

        // Rounded corners: the ink shows through at each.
        foreach (Vector2 corner in new[] { rect.Position, new Vector2(rect.End.X - 1, rect.Position.Y), new Vector2(rect.Position.X, rect.End.Y - 1), rect.End - Vector2.One })
        {
            DrawRect(new Rect2(corner, Vector2.One), Palette.Ink);
        }

        Texture2D? picture = GameArt.Texture($"res://assets/cards/{card.Definition.Id}.png");

        // The corner index: the card's picture and count, small, in the corner that peeks out from behind the others.
        var index = new Rect2(outerLeft ? rect.Position.X + 1 : rect.End.X - 7, rect.Position.Y + 1, 6, 6);
        if (picture is not null)
        {
            DrawTextureRect(picture, index, tile: false);
        }

        if (card.Count > 1)
        {
            DrawString(font, new Vector2(outerLeft ? index.Position.X : index.Position.X - 1, index.End.Y + 7), card.Count.ToString(), HorizontalAlignment.Left, 8, UiTheme.FontSize - 2, Palette.Ink);
        }

        // With its whole face showing, its picture big in the middle.
        if (whole && picture is not null)
        {
            DrawTextureRect(picture, new Rect2(rect.Position + new Vector2(2, 10), new Vector2(10, 10)), tile: false);
        }
    }

    /// <summary>The belt band across the square, its ends darker as they wrap round the back, then its mods and the buckle.</summary>
    private void DrawBelt(string beltLook, Hero hero)
    {
        (Color leather, Color stitch) = BeltColors(beltLook);
        float height = beltLook == "giants_belt" ? BandHeight + 2 : BandHeight;
        float top = BandTop - (height - BandHeight) / 2;
        DrawRect(new Rect2(-3, top, SquareWidth + 6, height), leather);
        DrawRect(new Rect2(-3, top, 5, height), leather.Darkened(0.45f));
        DrawRect(new Rect2(SquareWidth - 2, top, 5, height), leather.Darkened(0.45f));
        DrawRect(new Rect2(-3, top + height - 2, SquareWidth + 6, 2), leather.Darkened(0.3f));
        for (float x = 0; x < SquareWidth; x += 3)
        {
            DrawRect(new Rect2(x, top + 1, 1.5f, 0.75f), stitch);
            DrawRect(new Rect2(x, top + height - 3, 1.5f, 0.75f), stitch);
        }

        // Belt Studs: a row of them along the band.
        if (hero.Mods.Any(mod => mod.Definition.ModFor == EquipSlot.Belt))
        {
            for (float x = 3; x < SquareWidth - 2; x += 6)
            {
                if (x < BuckleLeft - 2 || x > BuckleLeft + BuckleSize + 1)
                {
                    DrawCircle(new Vector2(x, top + height / 2), 1.2f, new Color(0.82f, 0.82f, 0.86f), antialiased: true);
                }
            }
        }

        // The buckle: a frame with the band through it, and a prong; it lights up under the cursor.
        CardInstance? buckle = hero.Equipment.GetValueOrDefault(EquipSlot.Buckle);
        string buckleLook = hero.Appearance.BuckleLook is { Length: > 0 } look ? look : buckle?.Definition.Id ?? "";
        Color metal = buckle is null ? leather.Darkened(0.2f) : BuckleMetal(buckleLook);
        if (_hover == BuckleSlot)
        {
            metal = metal.Lightened(0.3f);
        }

        Rect2 frame = BuckleRect;
        DrawRect(frame, metal);
        DrawRect(frame.Grow(-2), leather.Darkened(0.15f));
        DrawRect(new Rect2(frame.Position.X + 2, frame.GetCenter().Y - 0.5f, frame.Size.X - 4, 1), metal.Darkened(0.2f));
        DrawRect(frame, Palette.Ink, filled: false, width: 1);

        // Buckle Horns: a horn curling up from each top corner.
        if (hero.Mods.Any(mod => mod.Definition.ModFor == EquipSlot.Buckle))
        {
            Color ivory = new(0.95f, 0.92f, 0.82f);
            DrawColoredPolygon([frame.Position, frame.Position + new Vector2(3, 0), frame.Position + new Vector2(-2, -5)], ivory);
            Vector2 right = new(frame.End.X, frame.Position.Y);
            DrawColoredPolygon([right, right + new Vector2(-3, 0), right + new Vector2(2, -5)], ivory);
        }
    }

    /// <summary>Each buckle's metal: brass, iron, silver.</summary>
    internal static Color BuckleMetal(string buckleId) => buckleId switch
    {
        "iron_buckle" => new Color(0.48f, 0.50f, 0.54f),
        "silver_buckle" => new Color(0.86f, 0.88f, 0.92f),
        _ => new Color(0.78f, 0.62f, 0.26f),
    };

    /// <summary>Each belt's leather and stitching.</summary>
    internal static (Color Leather, Color Stitch) BeltColors(string beltId) => beltId switch
    {
        "good_belt" => (new Color(0.36f, 0.22f, 0.13f), new Color(0.80f, 0.70f, 0.50f)),
        "better_belt" => (new Color(0.45f, 0.14f, 0.12f), new Color(0.85f, 0.75f, 0.55f)),
        "grand_belt" => (new Color(0.16f, 0.22f, 0.38f), new Color(0.75f, 0.78f, 0.85f)),
        "great_belt" => (new Color(0.12f, 0.11f, 0.12f), new Color(0.85f, 0.68f, 0.25f)),
        "giants_belt" => (new Color(0.30f, 0.34f, 0.26f), new Color(0.70f, 0.70f, 0.60f)),
        "best_belt" => (new Color(0.32f, 0.16f, 0.40f), new Color(0.95f, 0.80f, 0.30f)),
        _ => (new Color(0.46f, 0.30f, 0.17f), new Color(0.70f, 0.55f, 0.35f)),
    };

    /// <summary>The belt fan under the square: three little cards, lit while there's something on the belt.</summary>
    private void DrawFan(bool any)
    {
        bool hover = _hover == FanSlot;
        for (int k = 0; k < 3; k++)
        {
            DrawSetTransform(new Vector2(FanX + 1 + k * 2.5f, FanY + 9), -0.25f + k * 0.25f, Vector2.One);
            var back = new Rect2(-2.5f, -9, 5, 9);
            DrawRect(back, hover ? Palette.PanelHover : any ? Cream.Darkened(0.2f) : Palette.Ink);
            DrawRect(back, any ? Palette.Trim : Palette.TrimDim, filled: false, width: 1);
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
