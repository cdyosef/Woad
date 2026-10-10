using Godot;

namespace Woad.Scenes.Ui;

/// <summary>A character's crest. Clickable ones are used to pick a portrait.</summary>
public partial class PortraitView : Control
{
    private string _portraitId = "";
    private bool _selected;

    public event Action? Clicked;

    public bool Clickable { get; init; }

    public string PortraitId
    {
        get => _portraitId;
        set { _portraitId = value; QueueRedraw(); }
    }

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
        DrawCrest(this, new Rect2(Vector2.Zero, Size), Portraits.Get(PortraitId));
        if (_selected)
        {
            DrawRect(new Rect2(-3, -3, Size.X + 6, Size.Y + 6), Palette.TrimBright, filled: false, width: 1.5f);
        }
    }

    /// <summary>Draws a shield in the portrait's color with its sigil.</summary>
    public static void DrawCrest(CanvasItem canvas, Rect2 rect, Portrait portrait)
    {
        Vector2 o = rect.Position;
        float w = rect.Size.X;
        float h = rect.Size.Y;
        Vector2[] shield = [o, o + new Vector2(w, 0), o + new Vector2(w, h * 0.55f), o + new Vector2(w / 2, h), o + new Vector2(0, h * 0.55f)];
        canvas.DrawColoredPolygon(shield, portrait.Color.Darkened(0.4f));
        canvas.DrawPolyline([.. shield, shield[0]], Palette.Trim, 1.5f, antialiased: true);

        Vector2 c = o + new Vector2(w / 2, h * 0.42f);
        float s = w * 0.2f;
        Color ink = portrait.Color.Lightened(0.3f);
        switch (portrait.Sigil)
        {
            case 0:
                canvas.DrawCircle(c, s, ink, antialiased: true);
                break;
            case 1:
                canvas.DrawColoredPolygon([c + new Vector2(0, -1.3f * s), c + new Vector2(s, 0), c + new Vector2(0, 1.3f * s), c + new Vector2(-s, 0)], ink);
                break;
            case 2:
                canvas.DrawColoredPolygon([c + new Vector2(0, -1.2f * s), c + new Vector2(1.1f * s, 0.9f * s), c + new Vector2(-1.1f * s, 0.9f * s)], ink);
                break;
            case 3:
                canvas.DrawRect(new Rect2(c.X - 0.3f * s, c.Y - 1.2f * s, 0.6f * s, 2.4f * s), ink);
                canvas.DrawRect(new Rect2(c.X - 1.1f * s, c.Y - 0.5f * s, 2.2f * s, 0.6f * s), ink);
                break;
            case 4:
                canvas.DrawColoredPolygon([c + new Vector2(0, -1.4f * s), c + new Vector2(0.35f * s, 0), c + new Vector2(0, 1.4f * s), c + new Vector2(-0.35f * s, 0)], ink);
                canvas.DrawColoredPolygon([c + new Vector2(-1.4f * s, 0), c + new Vector2(0, -0.35f * s), c + new Vector2(1.4f * s, 0), c + new Vector2(0, 0.35f * s)], ink);
                break;
            case 5:
                canvas.DrawArc(c, s * 0.9f, 0, Mathf.Tau, 24, ink, s * 0.45f, antialiased: true);
                break;
            case 6:
                canvas.DrawRect(new Rect2(c.X - 1.1f * s, c.Y - 0.8f * s, 2.2f * s, 0.5f * s), ink);
                canvas.DrawRect(new Rect2(c.X - 1.1f * s, c.Y + 0.3f * s, 2.2f * s, 0.5f * s), ink);
                break;
            default:
                canvas.DrawPolyline([c + new Vector2(-1.1f * s, -0.6f * s), c + new Vector2(0, 0.7f * s), c + new Vector2(1.1f * s, -0.6f * s)], ink, 0.5f * s, antialiased: true);
                break;
        }
    }
}
