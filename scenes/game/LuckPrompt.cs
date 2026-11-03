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
    private Action? _add;
    private Action? _power;
    private string _asked = "Try your Luck?";

    internal Button NoButton { get; private set; } = null!;

    /// <summary>Adds an Od Hand die to the roll (a save after a fall, a Focus check), when it's on offer.</summary>
    internal Button AddButton { get; private set; } = null!;

    /// <summary>Adds a banked Power Die to the roll, alongside <see cref="AddButton"/>.</summary>
    internal Button PowerButton { get; private set; } = null!;

    /// <summary>Seconds left to decide.</summary>
    internal double SecondsLeft => _left;

    public override void _Ready()
    {
        Visible = false;
        ZIndex = 5;
        Size = new Vector2(264, 20);
        _question = UiTheme.Label(new Vector2(6, 3), new Vector2(124, 14), "", color: Palette.Heal);
        AddChild(_question);
        AddButton = UiTheme.CompactButton(new Vector2(132, 2), new Vector2(46, 16), "+Od Hand");
        AddButton.TooltipText = "Add one of your unspent Od Hand dice to the roll.";
        AddButton.Pressed += () => _add?.Invoke();
        AddChild(AddButton);
        PowerButton = UiTheme.CompactButton(new Vector2(180, 2), new Vector2(42, 16), "+Power");
        PowerButton.TooltipText = "Add one of your banked Power Dice to the roll.";
        PowerButton.Pressed += () => _power?.Invoke();
        AddChild(PowerButton);
        NoButton = UiTheme.CompactButton(new Vector2(224, 2), new Vector2(36, 16), "No");
        NoButton.TooltipText = "Let the roll stand.";
        NoButton.Pressed += () => Expire();
        AddChild(NoButton);
    }

    /// <summary>
    /// Asks, with <paramref name="seconds"/> to decide; <paramref name="expired"/> runs on No or when the time's up.
    /// <paramref name="add"/>, if given, is the +Od Hand button's (shown while <paramref name="canAdd"/>).
    /// </summary>
    public void Offer(double seconds, Action expired, string question = "Try your Luck?", Action? add = null, bool canAdd = false, Action? power = null, bool canPower = false)
    {
        _left = seconds;
        _expired = expired;
        _asked = question;
        _add = add;
        _power = power;
        AddButton.Visible = add is not null;
        AddButton.Disabled = !canAdd;
        PowerButton.Visible = power is not null;
        PowerButton.Disabled = !canPower;
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

        // Real seconds, though the game runs slow while it asks.
        _left -= delta / Math.Max(Engine.TimeScale, 0.01);
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

    /// <summary>Whether the +Od Hand button can be pressed (dice left to add).</summary>
    public void CanAdd(bool can, bool power)
    {
        AddButton.Disabled = !can;
        PowerButton.Disabled = !power;
    }

    private void UpdateText() => _question.Text = $"{_asked}  {Math.Max(0, (int)Math.Ceiling(_left))}";
}
