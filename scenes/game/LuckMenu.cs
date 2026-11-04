using Godot;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The Luck clover on the sheet, over the Luck line: tap it and it glows, so the hero's next roll asks
/// "Try your Luck?" whatever its odds; hold it to open the <see cref="LuckMenu"/>.
/// </summary>
public partial class LuckClover : Control
{
    /// <summary>How long a press must be held to open the menu instead.</summary>
    private const double HoldSeconds = 0.4;

    private double _held = -1;
    private bool _glowing;

    /// <summary>Glowing: the next roll asks.</summary>
    public bool Glowing
    {
        get => _glowing;
        set
        {
            _glowing = value;
            QueueRedraw();
        }
    }

    /// <summary>A tap: glow, or stop glowing.</summary>
    public event Action? Tapped;

    /// <summary>A press held: the Luck menu.</summary>
    public event Action? Held;

    public LuckClover()
    {
        MouseFilter = MouseFilterEnum.Stop;
        MouseDefaultCursorShape = CursorShape.PointingHand;
        TooltipText = "Luck: click to try your Luck on your next roll, whatever its odds. Hold for the Luck menu: which rolls ask when they're about to fail.";
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            return;
        }

        if (button.Pressed)
        {
            _held = 0;
        }
        else if (_held >= 0)
        {
            _held = -1;
            Tapped?.Invoke();
        }

        AcceptEvent();
    }

    public override void _Process(double delta)
    {
        if (_held < 0)
        {
            return;
        }

        // Real seconds, though the game may be running slow.
        _held += delta / Math.Max(Engine.TimeScale, 0.01);
        if (_held >= HoldSeconds)
        {
            _held = -1;
            Held?.Invoke();
        }
    }

    public override void _Draw()
    {
        if (Glowing)
        {
            var rect = new Rect2(Vector2.Zero, Size);
            DrawRect(rect.Grow(1), Palette.Heal with { A = 0.25f });
            DrawRect(rect.Grow(1), Palette.Heal, filled: false, width: 1);
        }
    }
}

/// <summary>
/// The Luck menu: a switch for each kind of roll. On, Luck is asked whenever that roll is about to fail
/// (Wend, which can't, every roll); off, it isn't, unless the clover is glowing. A search can't fail, so it's
/// asked only while the clover glows.
/// </summary>
public partial class LuckMenu : Panel
{
    private static readonly (LuckRoll Roll, string Label)[] Switches =
    [
        (LuckRoll.Rumble, "Rumble attacks"),
        (LuckRoll.Shove, "Shove"),
        (LuckRoll.PickLock, "Pick Lock"),
        (LuckRoll.Disarm, "Disarm"),
        (LuckRoll.Steal, "Steal"),
        (LuckRoll.Aid, "Aid"),
        (LuckRoll.Learn, "Learn"),
        (LuckRoll.Wend, "Wend (every roll)"),
    ];

    private readonly Dictionary<LuckRoll, Button> _buttons = [];
    private LuckRoll _asks;

    /// <summary>The switches were changed.</summary>
    public event Action<LuckRoll>? Changed;

    internal Button SwitchFor(LuckRoll roll) => _buttons[roll];

    public override void _Ready()
    {
        Visible = false;
        ZIndex = 5;
        Size = new Vector2(214, 132);
        AddChild(UiTheme.Label(new Vector2(8, 4), new Vector2(200, 14), "LUCK: ASK WHEN ABOUT TO FAIL", color: Palette.Heal));
        for (int i = 0; i < Switches.Length; i++)
        {
            (LuckRoll roll, string label) = Switches[i];
            Button toggle = UiTheme.CompactButton(new Vector2(8 + i % 2 * 102, 22 + i / 2 * 19), new Vector2(98, 16), label);
            toggle.ToggleMode = true;
            toggle.Name = $"Luck{roll}";
            toggle.Toggled += on =>
            {
                _asks = on ? _asks | roll : _asks & ~roll;
                Changed?.Invoke(_asks);
            };
            AddChild(toggle);
            _buttons[roll] = toggle;
        }

        Button close = UiTheme.CompactButton(new Vector2(Size.X - 52, Size.Y - 20), new Vector2(44, 16), "Close");
        close.Pressed += () => Visible = false;
        AddChild(close);
    }

    /// <summary>Opens the menu with the hero's switches as they are.</summary>
    public void Open(LuckRoll asks)
    {
        _asks = asks;
        foreach ((LuckRoll roll, Button button) in _buttons)
        {
            button.SetPressedNoSignal(asks.HasFlag(roll));
        }

        Visible = true;
        MoveToFront();
    }
}
