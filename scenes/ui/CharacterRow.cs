using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;

namespace Woad.Scenes.Ui;

/// <summary>A selectable roster entry: crest, name, level and base stats.</summary>
public partial class CharacterRow : Button
{
    public CharacterRow()
    {
        ToggleMode = true;
        FocusMode = FocusModeEnum.None;
        CustomMinimumSize = new Vector2(280, 40);
    }

    public Character? Character { get; private set; }

    public void ShowCharacter(Character character, bool selected)
    {
        Character = character;
        SetPressedNoSignal(selected);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Character is null)
        {
            return;
        }

        PortraitView.DrawCrest(this, new Rect2(7, 5, 24, 29), Portraits.Get(Character.Portrait));
        Font font = GetThemeDefaultFont();
        DrawString(font, new Vector2(40, 17), Character.Name, fontSize: 12, modulate: ButtonPressed ? Palette.TrimBright : Palette.Text);

        Stats stats = Character.BaseStats;
        DrawString(font, new Vector2(40, 31),
            $"Level {Character.Level}   Hearts {stats.Hearts}   Attack {stats.AttackDice}   Defense {stats.DefenseDice}   Luck {stats.Luck}",
            fontSize: UiTheme.FontSize, modulate: Palette.TextDim);

        if (Character.UnspentPoints > 0)
        {
            DrawString(font, new Vector2(0, 17), $"+{Character.UnspentPoints} pts", HorizontalAlignment.Right, Size.X - 8,
                UiTheme.FontSize, Palette.Heal);
        }
    }
}
