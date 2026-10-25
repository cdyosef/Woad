using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Woarld;

/// <summary>
/// What the party ran into on the Woarld Map, over the map: a title, what's happening, and the choices
/// (Fight, Flee, Pay the toll, Turn back, Suss Out...), each a button that runs its own action.
/// </summary>
public partial class EncounterDialog : Control
{
    private Panel _panel = null!;
    private Label _title = null!;
    private Label _text = null!;
    private readonly List<Button> _buttons = [];

    internal IReadOnlyList<Button> Buttons => _buttons;

    internal string TitleText => _title.Text;

    internal string Text => _text.Text;

    public override void _Ready()
    {
        Size = new Vector2(MenuScreen.Width, MenuScreen.Height);
        Visible = false;
        AddChild(new ColorRect { Color = new Color(Palette.Background, 0.75f), Size = Size });
        _panel = new Panel { Position = new Vector2(150, 100), Size = new Vector2(340, 150), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_panel);
        _title = UiTheme.Heading(UiTheme.Label(new Vector2(0, 12), new Vector2(340, 26), "", 18, Palette.Danger), 3);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _panel.AddChild(_title);
        _text = UiTheme.Label(new Vector2(16, 44), new Vector2(308, 52), color: Palette.Text);
        _text.HorizontalAlignment = HorizontalAlignment.Center;
        _panel.AddChild(_text);
    }

    /// <summary>Shows the encounter with its choices (label, tooltip, what it does); a choice may show the dialog again.</summary>
    public void Ask(string title, string text, params (string Label, string Tooltip, Action Chosen)[] choices)
    {
        foreach (Button old in _buttons)
        {
            old.QueueFree();
        }

        _buttons.Clear();
        _title.Text = title;
        _text.Text = text;
        float width = (308 - 6 * (choices.Length - 1)) / (float)Math.Max(1, choices.Length);
        for (int i = 0; i < choices.Length; i++)
        {
            (string label, string tooltip, Action chosen) = choices[i];
            Button button = UiTheme.Button(new Vector2(16 + i * (width + 6), 108), new Vector2(width, 26), label);
            button.TooltipText = tooltip;
            button.Pressed += () =>
            {
                Hide();
                chosen();
            };
            _panel.AddChild(button);
            _buttons.Add(button);
        }

        Show();
    }
}
