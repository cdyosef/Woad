using Godot;

namespace Woad.Scenes.Ui;

/// <summary>A hero figure in its look and colors, scaled up to fill the control (whole pixels look best).</summary>
public partial class FigureView : Control
{
    private LookColors? _colors;

    public LookColors? Colors
    {
        get => _colors;
        set { _colors = value; QueueRedraw(); }
    }

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        if (_colors is not null)
        {
            Looks.Draw(this, new Rect2(Vector2.Zero, Size), _colors);
        }
    }
}
