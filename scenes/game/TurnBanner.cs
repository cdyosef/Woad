using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>A band across the board announcing whose turn it is, so hot-seat players know to swap.</summary>
public partial class TurnBanner : Control
{
    private string _text = "";
    private Color _color = Palette.Text;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public async Task Flash(string text, Color color, double holdSeconds = 0.65)
    {
        _text = text;
        _color = color;
        Modulate = Colors.Transparent;
        Visible = true;
        QueueRedraw();

        Tween tween = CreateTween();
        tween.TweenProperty(this, "modulate", Colors.White, 0.15);
        tween.TweenInterval(holdSeconds);
        tween.TweenProperty(this, "modulate", Colors.Transparent, 0.2);
        await ToSignal(tween, Tween.SignalName.Finished);

        Visible = false;
    }

    public override void _Draw()
    {
        var band = new Rect2(0, Size.Y / 2 - 20, Size.X, 40);
        DrawRect(band, Palette.PanelDark with { A = 0.92f });
        DrawLine(band.Position, band.Position + new Vector2(band.Size.X, 0), Palette.Trim, 2);
        DrawLine(band.Position + new Vector2(0, band.Size.Y), band.End, Palette.Trim, 2);
        if (GameArt.HeadingFont is { } heading)
        {
            // Double size when it fits across the band, otherwise single.
            string text = _text.ToUpperInvariant();
            int size = UiTheme.HeadingPixelSize * 2;
            if (heading.GetStringSize(text, fontSize: size).X > Size.X - 16)
            {
                size = UiTheme.HeadingPixelSize;
            }

            DrawString(heading, new Vector2(0, band.Position.Y + 20 + size / 2f), text, HorizontalAlignment.Center, Size.X, size, _color);
        }
        else
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(0, band.Position.Y + 26), _text, HorizontalAlignment.Center, Size.X, 16, _color);
        }
    }
}
