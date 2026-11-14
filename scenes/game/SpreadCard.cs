using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>What a spread-out card says: a short line of its key numbers on its face, and the whole of it for the info panel.</summary>
public sealed record CardInfo(string Line, string Body);

/// <summary>
/// A card in a hand spread out over the board: its picture, its name and a line of its key numbers. One that can be used
/// now glows in its hand's colour (Rumble red, Wend green, Od Hand gold, Magic purple); one that can't has a plain grey
/// frame. Hovered, it lifts and the spread shows all about it; clicked, it's played.
/// </summary>
public partial class SpreadCard : Button
{
    public const float Width = 60;
    public const float Height = 86;
    private const float Art = 34;

    private Texture2D? _picture;
    private Color _color;
    private bool _lit;
    private Label _name = null!;
    private Label _line = null!;

    /// <summary>Dragged: what it carries (an ability to play into its square), or nothing.</summary>
    public Func<Control, Variant>? FaceDrag { get; set; }

    public override void _Ready()
    {
        Size = new Vector2(Width, Height);
        FocusMode = FocusModeEnum.None;
        Flat = true;
        _name = UiTheme.Label(new Vector2(2, Art + 6), new Vector2(Width - 4, 12), color: Palette.TrimBright);
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        _name.ClipText = true;
        _name.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_name);
        _line = UiTheme.Label(new Vector2(3, Art + 18), new Vector2(Width - 6, Height - Art - 20), fontSize: UiTheme.FontSize - 2, color: Palette.Text);
        _line.HorizontalAlignment = HorizontalAlignment.Center;
        _line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _line.ClipText = true;
        _line.AddThemeConstantOverride("line_spacing", -3);
        _line.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_line);
        MouseEntered += QueueRedraw;
        MouseExited += QueueRedraw;
    }

    public void Show(string picture, string name, string line, Color color, bool lit)
    {
        _picture = GameArt.Texture($"res://assets/cards/{picture}.png");
        _color = color;
        _lit = lit;
        _name.Text = name;
        _line.Text = line;
        _name.AddThemeColorOverride("font_color", lit ? Palette.TrimBright : Palette.TextDim);
        _line.AddThemeColorOverride("font_color", lit ? Palette.Text : Palette.TextDim);
        QueueRedraw();
    }

    public override Variant _GetDragData(Vector2 atPosition) => FaceDrag is { } drag ? drag(this) : default;

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        bool hover = IsHovered();
        Color frame = _lit ? (hover ? _color.Lightened(0.3f) : _color) : (hover ? Palette.Trim : Palette.TrimDim);
        if (_lit)
        {
            // A soft glow of its hand's colour round a card that can be used now.
            DrawRect(rect.Grow(2), _color with { A = hover ? 0.55f : 0.3f });
        }

        DrawRect(rect, Palette.Ink);
        DrawRect(new Rect2(rect.Position + new Vector2(3, 3), new Vector2(rect.Size.X - 6, Art + 2)), _lit ? new Color(0.94f, 0.9f, 0.8f) : new Color(0.5f, 0.48f, 0.45f));
        if (_picture is not null)
        {
            DrawTextureRect(_picture, new Rect2((Size.X - Art) / 2, 4, Art, Art), tile: false, modulate: _lit ? Colors.White : new Color(0.6f, 0.6f, 0.6f));
        }

        DrawRect(rect, frame, filled: false, width: 2);
    }
}
