using Godot;
using Woad.Core.Characters;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Menu;

/// <summary>One of the eight seats: shows who sits there, in that seat's player color.</summary>
public partial class SeatButton : Button
{
    private Character? _character;

    public SeatButton()
    {
        FocusMode = FocusModeEnum.None;
        Pressed += () => Sfx.Play("click");
    }

    public int Seat { get; init; }

    public void ShowCharacter(Character? character)
    {
        _character = character;
        Disabled = character is null;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Color seatColor = Palette.Seat(Seat);
        DrawRect(new Rect2(2, 2, 4, Size.Y - 4), _character is null ? Palette.TrimDim : seatColor);
        Font font = GetThemeDefaultFont();
        string level = _character is null ? "" : $"   level {_character.Level}";
        DrawString(font, new Vector2(12, 14), $"Player {Seat + 1}{level}", fontSize: UiTheme.FontSize, modulate: _character is null ? Palette.TextDim : seatColor);
        if (_character is null)
        {
            DrawString(font, new Vector2(12, 30), "(empty)", fontSize: UiTheme.FontSize, modulate: Palette.TextDim);
            return;
        }

        PortraitView.DrawCrest(this, new Rect2(Size.X - 30, 5, 24, 29), Portraits.Get(_character.Portrait));
        DrawString(font, new Vector2(12, 31), _character.Name, HorizontalAlignment.Left, Size.X - 46, 10, Palette.Text, TextServer.JustificationFlag.None, TextServer.Direction.Auto, TextServer.Orientation.Horizontal);
    }
}
