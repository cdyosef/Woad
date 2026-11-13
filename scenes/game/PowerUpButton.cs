using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Power up, left of End turn: the power symbol (a ring broken at the top, a bar through the gap). It rolls every die
/// left this turn at once, banks each 6 as a Power Die, and ends the turn.
/// </summary>
public partial class PowerUpButton : Button
{
    public override void _Ready()
    {
        Name = "PowerUp";
        FocusMode = FocusModeEnum.None;
    }

    public override void _Notification(int what)
    {
        if (what is (int)NotificationMouseEnter or (int)NotificationMouseExit or (int)NotificationThemeChanged)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        Color color = Disabled ? Palette.TextDim with { A = 0.5f } : IsHovered() ? Palette.Power.Lightened(0.35f) : Palette.Power;
        Vector2 center = Size / 2 + new Vector2(0, 1);
        float radius = Mathf.Min(Size.X, Size.Y) / 2 - 4;
        // The ring, open at the top...
        DrawArc(center, radius, -Mathf.Pi / 2 + 0.7f, 3 * Mathf.Pi / 2 - 0.7f, 20, color, 2);
        // ...and the bar down through the gap.
        DrawLine(center - new Vector2(0, radius + 2), center - new Vector2(0, 1), color, 2);
    }
}
