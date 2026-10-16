using Godot;
using Woad.Core.Characters;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Menu;

/// <summary>One of the eight seats: shows who sits there (and whose companion), in that seat's color.</summary>
public partial class SeatButton : Button
{
    private Character? _character;
    private string? _owner;
    private int _playerNumber;

    public SeatButton()
    {
        FocusMode = FocusModeEnum.None;
        Pressed += () => Sfx.Play("click");
    }

    public int Seat { get; init; }

    /// <param name="owner">For a companion, the name of the player it belongs to.</param>
    /// <param name="playerNumber">For a player, which player they are, counting players only.</param>
    public void ShowCharacter(Character? character, string? owner = null, int playerNumber = 0)
    {
        _character = character;
        _owner = owner;
        _playerNumber = playerNumber;
        Disabled = character is null;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Color seatColor = Palette.Seat(Seat);
        DrawRect(new Rect2(2, 2, 4, Size.Y - 4), _character is null ? Palette.TrimDim : seatColor);
        Font font = GetThemeDefaultFont();
        string heading = (_character, _owner) switch
        {
            (null, _) => $"Seat {Seat + 1}",
            ({ IsCompanion: true }, { } owner) => $"with {owner}",
            ({ } seated, _) => $"Player {_playerNumber}   level {seated.Level}",
        };
        DrawString(font, new Vector2(12, 14), heading, fontSize: UiTheme.FontSize, modulate: _character is null ? Palette.TextDim : seatColor);
        if (_character is null)
        {
            DrawString(font, new Vector2(12, 30), "(empty)", fontSize: UiTheme.FontSize, modulate: Palette.TextDim);
            return;
        }

        Looks.Draw(this, new Rect2(Size.X - 36, Size.Y / 2 - 16, 32, 32), Looks.Resolve(_character.Appearance, _character.Portrait));
        DrawString(font, new Vector2(12, 31), _character.Name, HorizontalAlignment.Left, Size.X - 46, 10, Palette.Text, TextServer.JustificationFlag.None, TextServer.Direction.Auto, TextServer.Orientation.Horizontal);
    }
}
