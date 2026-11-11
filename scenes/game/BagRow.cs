using Godot;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The bags hung under the belt, on the leg: each a pouch on a strap, its knot (or flap) on top a button that opens it
/// (lit while it's open), its body showing what's on top inside (picture and count). Click the body to pick that up,
/// double-click it to use it (the Rock Bag asks which hand), drag it out into a slot; drop a card on a bag to pack it in.
/// </summary>
public partial class BagRow : Control
{
    public const float RowWidth = 56;
    public const float RowHeight = 24;

    private const float PouchWidth = 12;
    private const float KnotY = 5;
    private const float BodyTop = 8;

    private Hero? _hero;
    private int? _selectedCardId;
    private int? _openBagId;
    private int _hover = -1;
    private bool _hoverKnot;

    /// <summary>A bag's knot was clicked: open it, or close it if it's open.</summary>
    public event Action<int>? KnotClicked;

    /// <summary>A bag's body was clicked: pick up what's on top inside.</summary>
    public event Action<int>? CardSelected;

    /// <summary>A bag's body was double-clicked (the bag's id).</summary>
    public event Action<int>? BagUsed;

    public override void _Ready()
    {
        Size = new Vector2(RowWidth, RowHeight);
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = " ";
    }

    public void ShowHero(Hero? hero, int? selectedCardId, int? openBagId)
    {
        _hero = hero;
        _selectedCardId = selectedCardId;
        _openBagId = openBagId;
        Visible = hero is not null && hero.Bags.Any();
        QueueRedraw();
    }

    private IReadOnlyList<CardInstance> Bags => _hero is { } hero ? [.. hero.Bags] : [];

    /// <summary>How far apart the pouches hang: their own width apart, closer when there are many.</summary>
    private float Step => Bags.Count <= 1 ? 0 : Math.Min(PouchWidth + 2, (RowWidth - PouchWidth - 2) / (Bags.Count - 1));

    private Rect2 PouchRect(int index) => new(1 + index * Step, BodyTop, PouchWidth, RowHeight - BodyTop - 1);

    private Vector2 KnotAt(int index) => new(PouchRect(index).GetCenter().X, KnotY);

    internal Vector2 KnotPoint(int index) => KnotAt(index);

    internal Vector2 BodyPoint(int index) => PouchRect(index).GetCenter() + new Vector2(0, 2);

    /// <summary>The bag under <paramref name="at"/>, and whether it's on the knot; -1 for none.</summary>
    private (int Index, bool Knot) HitTest(Vector2 at)
    {
        for (int i = Bags.Count - 1; i >= 0; i--)
        {
            if (at.DistanceTo(KnotAt(i)) <= 4)
            {
                return (i, true);
            }

            if (PouchRect(i).Grow(1).HasPoint(at))
            {
                return (i, false);
            }
        }

        return (-1, false);
    }

    /// <summary>What's on top in a bag: its first stack.</summary>
    internal static CardInstance? Top(CardInstance bag) => bag.Contents.FirstOrDefault();

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

        (int index, bool knot) = HitTest(click.Position);
        if (index < 0)
        {
            return;
        }

        CardInstance bag = Bags[index];
        Sfx.Play("click");
        if (knot)
        {
            KnotClicked?.Invoke(bag.Id);
        }
        else if (click.DoubleClick)
        {
            BagUsed?.Invoke(bag.Id);
        }
        else if (Top(bag) is { } top)
        {
            CardSelected?.Invoke(top.Id);
        }
        else
        {
            KnotClicked?.Invoke(bag.Id);
        }

        AcceptEvent();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit)
        {
            SetHover((-1, false));
        }
    }

    private void SetHover((int Index, bool Knot) hover)
    {
        if (hover.Index != _hover || hover.Knot != _hoverKnot)
        {
            (_hover, _hoverKnot) = hover;
            QueueRedraw();
        }
    }

    public override Variant _GetDragData(Vector2 atPosition) =>
        HitTest(atPosition) is { Index: >= 0, Knot: false } hit && Top(Bags[hit.Index]) is { } card
            ? CardDrag.Begin(this, card.Id, card.Definition.Id, card.Definition.Name, card.Count) : default;

    public override bool _CanDropData(Vector2 atPosition, Variant data) => CardDrag.Accepts(data) && HitTest(atPosition).Index >= 0;

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (HitTest(atPosition) is { Index: >= 0 } hit)
        {
            CardDrag.Drop(data, new DropTarget(DropKind.Pack, BagId: Bags[hit.Index].Id));
        }
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        if (HitTest(atPosition) is not { Index: >= 0 } hit)
        {
            return "";
        }

        CardInstance bag = Bags[hit.Index];
        BagSpec spec = bag.Definition.Hangs!;
        string inside = bag.Contents.Count == 0 ? "Empty." : string.Join("\n", bag.Contents.Select(CardSlot.Title));
        return $"{bag.Definition.Name} ({bag.Contents.Count}/{spec.Slots})\n{inside}\n"
            + (hit.Knot ? "Click the knot to open it." : "Click to pick up what's on top, double-click to use it; drag it out, or drop a card here to pack it.");
    }

    public override void _Draw()
    {
        Font font = GameArt.BodyFont ?? ThemeDB.FallbackFont;
        for (int i = 0; i < Bags.Count; i++)
        {
            DrawPouch(font, i, Bags[i]);
        }
    }

    private void DrawPouch(Font font, int index, CardInstance bag)
    {
        Rect2 body = PouchRect(index);
        (Color cloth, Color trim) = Colors(bag.Definition.Id);
        bool open = bag.Id == _openBagId;
        bool hover = index == _hover;
        if (hover && !_hoverKnot)
        {
            cloth = cloth.Lightened(0.15f);
        }

        // The strap up to the belt, then the pouch: narrow at the neck, round at the bottom.
        Vector2 knot = KnotAt(index);
        DrawLine(new Vector2(knot.X, 0), knot, trim.Darkened(0.3f), 1);
        Vector2[] shape =
        [
            new(body.Position.X + 3, body.Position.Y),
            new(body.End.X - 3, body.Position.Y),
            new(body.End.X, body.Position.Y + 5),
            new(body.End.X, body.End.Y - 3),
            new(body.End.X - 2, body.End.Y),
            new(body.Position.X + 2, body.End.Y),
            new(body.Position.X, body.End.Y - 3),
            new(body.Position.X, body.Position.Y + 5),
        ];
        DrawColoredPolygon(shape, cloth);
        DrawPolyline([.. shape, shape[0]], Palette.Ink, 1);
        DrawMark(bag.Definition.Id, body, trim);

        // What's on top inside: its picture and count.
        if (Top(bag) is { } top)
        {
            if (GameArt.Texture($"res://assets/cards/{top.Definition.Id}.png") is { } picture)
            {
                DrawTextureRect(picture, new Rect2(body.GetCenter() - new Vector2(4, 2), new Vector2(8, 8)), tile: false);
            }

            int count = bag.Contents.Sum(card => card.Count);
            if (count > 1)
            {
                DrawString(font, new Vector2(body.Position.X - 1, body.End.Y + 1), count.ToString(), HorizontalAlignment.Right, PouchWidth + 2, UiTheme.FontSize - 3, Palette.Text);
            }
        }

        if (bag.Contents.Any(card => card.Id == _selectedCardId))
        {
            DrawRect(body.Grow(1), Palette.TrimBright, filled: false, width: 1);
        }

        // The knot: the button, lit while the bag's open or under the cursor.
        Color knotColor = open ? Palette.TrimBright : hover && _hoverKnot ? trim.Lightened(0.4f) : trim;
        DrawCircle(knot, open ? 3.5f : 3, knotColor, antialiased: true);
        DrawCircle(knot, open ? 3.5f : 3, Palette.Ink, filled: false, width: 1, antialiased: true);
        DrawLine(knot + new Vector2(-2, 1), knot + new Vector2(-4, 4), knotColor.Darkened(0.2f), 1);
        DrawLine(knot + new Vector2(2, 1), knot + new Vector2(4, 4), knotColor.Darkened(0.2f), 1);
    }

    /// <summary>Each kind of bag's cloth and its tie.</summary>
    internal static (Color Cloth, Color Trim) Colors(string bagId) => bagId switch
    {
        "rock_bag" or "t_rockbag" => (new Color(0.42f, 0.30f, 0.20f), new Color(0.62f, 0.50f, 0.34f)),
        "medicine_bag" or "t_medsack" => (new Color(0.88f, 0.85f, 0.76f), new Color(0.72f, 0.18f, 0.16f)),
        "burgling_tools" or "t_kit" => (new Color(0.20f, 0.18f, 0.20f), new Color(0.55f, 0.52f, 0.48f)),
        "bomb_bag" => (new Color(0.14f, 0.14f, 0.16f), new Color(0.80f, 0.42f, 0.16f)),
        "sack" or "t_sack" => (new Color(0.70f, 0.60f, 0.42f), new Color(0.50f, 0.40f, 0.26f)),
        _ => (new Color(0.46f, 0.32f, 0.20f), new Color(0.66f, 0.52f, 0.34f)),
    };

    /// <summary>Each kind's mark on its cloth: a red cross, tool handles, a fuse, burlap weave.</summary>
    private void DrawMark(string bagId, Rect2 body, Color trim)
    {
        switch (bagId)
        {
            case "medicine_bag" or "t_medsack":
                DrawRect(new Rect2(body.End.X - 5, body.Position.Y + 2, 3, 1), trim);
                DrawRect(new Rect2(body.End.X - 4, body.Position.Y + 1, 1, 3), trim);
                break;
            case "burgling_tools" or "t_kit":
                DrawLine(new Vector2(body.Position.X + 3, body.Position.Y), new Vector2(body.Position.X + 3, body.Position.Y - 3), trim, 1);
                DrawLine(new Vector2(body.End.X - 3, body.Position.Y), new Vector2(body.End.X - 3, body.Position.Y - 2), trim, 1);
                break;
            case "bomb_bag":
                DrawLine(new Vector2(body.End.X - 3, body.Position.Y + 1), new Vector2(body.End.X, body.Position.Y - 2), trim, 1);
                break;
            case "sack" or "t_sack":
                for (float y = body.Position.Y + 3; y < body.End.Y - 1; y += 3)
                {
                    DrawLine(new Vector2(body.Position.X + 1, y), new Vector2(body.End.X - 1, y), trim with { A = 0.5f }, 1);
                }

                break;
        }
    }
}
