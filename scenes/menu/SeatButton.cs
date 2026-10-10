using Godot;
using Woad.Core.Characters;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Menu;

/// <summary>One of the four seats: shows who sits there, in that seat's player color.</summary>
public partial class SeatButton : Button
{
    private Character? _character;

    public SeatButton()
    {
        FocusMode = FocusModeEnum.None;
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
        DrawString(font, new Vector2(12, 14), $"Player {Seat + 1}", fontSize: UiTheme.FontSize, modulate: _character is null ? Palette.TextDim : seatColor);
        if (_character is null)
        {
            DrawString(font, new Vector2(12, 30), "(empty)", fontSize: UiTheme.FontSize, modulate: Palette.TextDim);
            return;
        }

        PortraitView.DrawCrest(this, new Rect2(Size.X - 30, 5, 24, 29), Portraits.Get(_character.Portrait));
        DrawString(font, new Vector2(12, 31), $"{_character.Name}   level {_character.Level}", fontSize: 12, modulate: Palette.Text);
    }
}
