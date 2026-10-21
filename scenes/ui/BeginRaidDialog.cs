using Godot;

namespace Woad.Scenes.Ui;

/// <summary>
/// Asks "Begin Raid?" over the screen before any mode starts: Raid! begins it, Chill closes the
/// question and stays put.
/// </summary>
public partial class BeginRaidDialog : Control
{
    private Label _detail = null!;

    /// <summary>The raid was begun.</summary>
    public event Action? Raided;

    internal Button ChillButton { get; private set; } = null!;

    internal Button RaidButton { get; private set; } = null!;

    public override void _Ready()
    {
        Size = new Vector2(MenuScreen.Width, MenuScreen.Height);
        Visible = false;
        // Covers the screen so nothing behind it takes clicks while it asks.
        AddChild(new ColorRect { Color = new Color(Palette.Background, 0.75f), Size = Size });

        var panel = new Panel { Position = new Vector2(190, 110), Size = new Vector2(260, 130), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(panel);
        Label title = UiTheme.Heading(UiTheme.Label(new Vector2(0, 14), new Vector2(260, 28), "Begin Raid?", 22, Palette.TrimBright), 3);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        panel.AddChild(title);
        _detail = UiTheme.Label(new Vector2(16, 52), new Vector2(228, 28), color: Palette.TextDim);
        _detail.HorizontalAlignment = HorizontalAlignment.Center;
        panel.AddChild(_detail);

        ChillButton = UiTheme.Button(new Vector2(16, 88), new Vector2(110, 28), "Chill");
        ChillButton.TooltipText = "Not yet: back to the party.";
        ChillButton.Pressed += Hide;
        panel.AddChild(ChillButton);
        RaidButton = UiTheme.Accent(UiTheme.Button(new Vector2(134, 88), new Vector2(110, 28), "Raid!"), Palette.DieRumble);
        RaidButton.Pressed += () =>
        {
            Hide();
            Raided?.Invoke();
        };
        panel.AddChild(RaidButton);
    }

    /// <summary>Asks the question, with a line saying where the raid goes and who's going.</summary>
    public void Ask(string detail)
    {
        _detail.Text = detail;
        Show();
    }
}
