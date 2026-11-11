using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// An open bag, seen from above: its cloth round the edge, the drawstring gathered at its knot (top middle, the button
/// that closes it), and the dark mouth inside, where its cards lie face up in their slots. The main pack and every bag
/// under the belt open into one of these.
/// </summary>
public partial class BagMouthView : Control
{
    /// <summary>Where the cards start inside, below the knot and the title.</summary>
    public const float Inside = 30;

    private readonly Label _title;
    private bool _knotHover;

    public BagMouthView()
    {
        MouseFilter = MouseFilterEnum.Stop;
        _title = UiTheme.Label(new(0, 12), new(10, 12), color: Palette.TrimBright);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_title);
    }

    public Color Cloth { get; set; } = new(0.40f, 0.28f, 0.18f);

    public Color Trim { get; set; } = new(0.66f, 0.52f, 0.34f);

    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    /// <summary>The knot at the top was clicked: close the bag.</summary>
    public event Action? KnotPressed;

    internal Vector2 KnotCenter => new(Size.X / 2, 6);

    /// <summary>The knot, for clicking it (relative to the view).</summary>
    internal Vector2 KnotPoint => KnotCenter;

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            _title.Size = new Vector2(Size.X, 12);
        }
        else if (what == NotificationMouseExit && _knotHover)
        {
            _knotHover = false;
            QueueRedraw();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            bool over = motion.Position.DistanceTo(KnotCenter) <= 6;
            if (over != _knotHover)
            {
                _knotHover = over;
                QueueRedraw();
            }
        }
        else if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click && click.Position.DistanceTo(KnotCenter) <= 6)
        {
            Sfx.Play("click");
            KnotPressed?.Invoke();
            AcceptEvent();
        }
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) => false;

    public override void _Draw()
    {
        // The title in ink on pale cloth, in gold on dark.
        _title.AddThemeColorOverride("font_color", Cloth.Luminance > 0.55f ? Palette.Ink : Palette.TrimBright);
        var outer = new Rect2(Vector2.Zero, Size);
        DrawRect(outer, Cloth);
        DrawRect(outer, Palette.Ink, filled: false, width: 1);

        // The mouth: the rim of the opening, and the dark inside.
        var mouth = new Rect2(4, Inside - 6, Size.X - 8, Size.Y - Inside + 2);
        DrawRect(mouth, Palette.Ink.Darkened(0.4f));
        DrawRect(new Rect2(mouth.Position, new Vector2(mouth.Size.X, 4)), Palette.Ink.Darkened(0.7f));
        DrawRect(mouth, Trim.Darkened(0.2f), filled: false, width: 1.5f);

        // The drawstring gathered into the knot, its folds running out from it.
        Vector2 knot = KnotCenter;
        for (int i = -3; i <= 3; i++)
        {
            DrawLine(knot, new Vector2(knot.X + i * Size.X / 8, mouth.Position.Y), Cloth.Darkened(0.3f), 1);
        }

        Color knotColor = _knotHover ? Palette.TrimBright : Trim.Lightened(0.15f);
        DrawCircle(knot, 5, knotColor, antialiased: true);
        DrawCircle(knot, 5, Palette.Ink, filled: false, width: 1, antialiased: true);
        DrawLine(knot + new Vector2(-3, 2), knot + new Vector2(-6, 6), knotColor.Darkened(0.25f), 1.5f);
        DrawLine(knot + new Vector2(3, 2), knot + new Vector2(6, 6), knotColor.Darkened(0.25f), 1.5f);
    }
}
