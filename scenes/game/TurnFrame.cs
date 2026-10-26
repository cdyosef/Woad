using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Over the board's frame, so whose turn it is can't be missed: a thick pulsing border in the active
/// hero's seat color, and a tag in its top-left corner naming them ("PIP'S TURN (ARIA'S COMPANION)").
/// Hidden between turns (while the monsters act).
/// </summary>
public partial class TurnFrame : Control
{
    private const float Thickness = 3;

    private Color _color = Palette.TrimBright;
    private string _text = "";
    private float _time;

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    /// <summary>Shows whose turn it is (in their seat color), or hides the frame with a null text.</summary>
    public void Show(string? text, Color color)
    {
        Visible = text is not null;
        _text = text ?? "";
        _color = color;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (Visible)
        {
            _time += (float)delta;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        float pulse = 0.65f + 0.35f * Mathf.Sin(_time * 3.5f);
        Color edge = _color with { A = pulse };
        DrawRect(new Rect2(0, 0, Size.X, Thickness), edge);
        DrawRect(new Rect2(0, Size.Y - Thickness, Size.X, Thickness), edge);
        DrawRect(new Rect2(0, 0, Thickness, Size.Y), edge);
        DrawRect(new Rect2(Size.X - Thickness, 0, Thickness, Size.Y), edge);

        Font font = ThemeDB.FallbackFont;
        float width = font.GetStringSize(_text, fontSize: UiTheme.FontSize).X;
        var tag = new Rect2(Thickness, Thickness, width + 8, 13);
        DrawRect(tag, _color);
        DrawString(font, new Vector2(tag.Position.X + 4, tag.Position.Y + 10), _text, fontSize: UiTheme.FontSize, modulate: Palette.Ink);
    }
}
