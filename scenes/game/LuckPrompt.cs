using Godot;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// "Try your Luck?" over the dice tray, while a roll could use it: the dice below are outlined green to
/// click, and a countdown runs (5 seconds on a roll that would fail, 3 on a Wend roll). No, or the time
/// running out, lets the roll stand.
/// </summary>
public partial class LuckPrompt : Panel
{
    private Label _question = null!;
    private double _left;
    private Action? _expired;

    internal Button NoButton { get; private set; } = null!;

    /// <summary>Seconds left to decide.</summary>
    internal double SecondsLeft => _left;

    public override void _Ready()
    {
        Visible = false;
        ZIndex = 5;
        Size = new Vector2(172, 20);
        _question = UiTheme.Label(new Vector2(6, 3), new Vector2(124, 14), "", color: Palette.Heal);
        AddChild(_question);
        NoButton = UiTheme.CompactButton(new Vector2(132, 2), new Vector2(36, 16), "No");
        NoButton.TooltipText = "Let the roll stand.";
        NoButton.Pressed += () => Expire();
        AddChild(NoButton);
    }

    /// <summary>Asks, with <paramref name="seconds"/> to decide; <paramref name="expired"/> runs on No or when the time's up.</summary>
    public void Offer(double seconds, Action expired)
    {
        _left = seconds;
        _expired = expired;
        Visible = true;
        MoveToFront();
        UpdateText();
    }

    public void Close()
    {
        _expired = null;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        if (!Visible || _expired is null)
        {
            return;
        }

        _left -= delta;
        UpdateText();
        if (_left <= 0)
        {
            Expire();
        }
    }

    private void Expire()
    {
        Action? expired = _expired;
        Close();
        expired?.Invoke();
    }

    private void UpdateText() => _question.Text = $"Try your Luck?  {Math.Max(0, (int)Math.Ceiling(_left))}";
}
