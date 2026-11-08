using Godot;
using Woad.Core.Model;

namespace Woad.Scenes.Ui;

/// <summary>A character's crest. Clickable ones are used to pick a portrait.</summary>
public partial class PortraitView : Control
{
    private string _portraitId = "";
    private bool _selected;
    private LookColors? _colors;

    public event Action? Clicked;

    public bool Clickable { get; init; }

    public string PortraitId
    {
        get => _portraitId;
        set { _portraitId = value; QueueRedraw(); }
    }

    /// <summary>The character's colors, for the shield and its rim; null draws the portrait's own color.</summary>
    public LookColors? Colors
    {
        get => _colors;
        set { _colors = value; QueueRedraw(); }
    }

    /// <summary>The character's crest picks; null for the portrait's own crest.</summary>
    public Appearance? Appearance
    {
        get => _appearance;
        set { _appearance = value; QueueRedraw(); }
    }

    private Appearance? _appearance;

    public bool Selected
    {
        get => _selected;
        set { _selected = value; QueueRedraw(); }
    }

    public override void _Ready()
    {
        MouseFilter = Clickable ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        if (Clickable)
        {
            MouseDefaultCursorShape = CursorShape.PointingHand;
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (Clickable && @event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            Sfx.Play("click");
            Clicked?.Invoke();
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        DrawCrest(this, new Rect2(Vector2.Zero, Size), Portraits.Get(PortraitId), _colors, _appearance);
        if (_selected)
        {
            DrawRect(new Rect2(-3, -3, Size.X + 6, Size.Y + 6), Palette.TrimBright, filled: false, width: 1.5f);
        }
    }

    /// <summary>Draws a character's crest: their picks (<paramref name="appearance"/>), else the portrait's sigil in their colors.</summary>
    public static void DrawCrest(CanvasItem canvas, Rect2 rect, Portrait portrait, LookColors? colors = null, Appearance? appearance = null) =>
        Crests.Draw(canvas, rect, Crests.Resolve(appearance ?? Appearance.Default, portrait.Id, colors));
}
