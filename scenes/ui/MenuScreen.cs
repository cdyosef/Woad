using Godot;

namespace Woad.Scenes.Ui;

/// <summary>Common frame for the menu screens: background, gold-trimmed panel and a title.</summary>
public partial class MenuScreen : Control
{
    public const float Width = 640;
    public const float Height = 360;

    protected void BuildFrame(string title)
    {
        Size = new Vector2(Width, Height);
        AddChild(new ColorRect { Color = Palette.Background, Size = Size, MouseFilter = MouseFilterEnum.Ignore });
        AddChild(new Panel { Position = new Vector2(8, 8), Size = new Vector2(Width - 16, Height - 16), MouseFilter = MouseFilterEnum.Ignore });
        if (title.Length > 0)
        {
            AddChild(UiTheme.Label(new Vector2(20, 14), new Vector2(400, 24), title, 18, Palette.TrimBright));
        }
    }

    protected T Add<T>(T node) where T : Node
    {
        AddChild(node);
        return node;
    }

    protected Label Caption(Vector2 position, string text, float width = 200) =>
        Add(UiTheme.Label(position, new Vector2(width, 12), text, color: Palette.Trim));
}
