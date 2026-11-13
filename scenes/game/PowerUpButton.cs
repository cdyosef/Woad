using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Power up, left of End turn: the Power Dice symbol. It rolls every die left this turn at once, banks each 6 as a Power
/// Die, and ends the turn.
/// </summary>
public partial class PowerUpButton : Button
{
    public override void _Ready()
    {
        Name = "PowerUp";
        FocusMode = FocusModeEnum.None;
        Icon = GameArt.Ui("icon_power");
        ExpandIcon = true;
        IconAlignment = HorizontalAlignment.Center;
        VerticalIconAlignment = VerticalAlignment.Center;
        AddThemeConstantOverride("icon_max_width", 14);
    }
}
