using Godot;
using Woad.Core.Commands;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The fire slot: a little window that slides out from behind the character sheet's edge, under the carry tab, while
/// the hero burns. It shows a stack of fire cards, one for each fire die, and the total. Double-click it for the quick
/// menu: STOP (all your Rumble), DROP (all your Wend, and you lie down), ROLL (all your Od Hand), each flashing while it
/// can be done, crossed out once that pool's been used this turn, ticked once done. All three in one turn put it out.
/// </summary>
public partial class FireTab : Control
{
    private const float Width = 72;
    private const float Height = 58;
    private const double SlideSeconds = 0.18;

    private static readonly Color Cream = new(0.98f, 0.90f, 0.72f);

    private Panel _window = null!;
    private FireSlot _slot = null!;
    private Panel _menu = null!;
    private readonly Dictionary<FirePart, Button> _buttons = [];
    private readonly Dictionary<FirePart, Tween> _blinks = [];
    private Tween? _slide;
    private bool _out;
    private int _dice;
    private int? _heroId;

    /// <summary>A part was picked from the quick menu.</summary>
    public event Action<FirePart>? PartChosen;

    internal bool IsOut => _out;

    internal Control Slot => _slot;

    internal bool IsMenuOpen => _menu.Visible;

    internal Button PartButton(FirePart part) => _buttons[part];

    public override void _Ready()
    {
        Size = new Vector2(Width, Height + 30);
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;
        _window = new Panel { Size = new Vector2(Width, Height), Position = new Vector2(-Width, 0) };
        _window.AddThemeStyleboxOverride("panel", UiTheme.Box(Palette.Ink, Palette.Fire));
        AddChild(_window);
        Label title = UiTheme.Label(new Vector2(0, 1), new Vector2(Width, 12), "ON FIRE", color: Palette.Fire);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        _window.AddChild(title);
        _slot = new FireSlot { Position = new Vector2(4, 13), Size = new Vector2(Width - 8, Height - 15), Owner_ = this };
        _window.AddChild(_slot);

        // The quick menu, just below the window: three flashing buttons.
        _menu = new Panel { Position = new Vector2(0, Height + 2), Size = new Vector2(Width, 26), Visible = false };
        _menu.AddThemeStyleboxOverride("panel", UiTheme.Box(Palette.Ink, Palette.Trim));
        AddChild(_menu);
        foreach ((FirePart part, Color color, int i) in new[] { (FirePart.Stop, Palette.DieRumble, 0), (FirePart.Drop, Palette.DieWend, 1), (FirePart.Roll, Palette.DieOdHand, 2) })
        {
            var button = UiTheme.CompactButton(new Vector2(2 + i * 23, 3), new Vector2(22, 20), part.ToString().ToUpperInvariant());
            button.Name = part.ToString();
            button.AddThemeFontSizeOverride("font_size", UiTheme.FontSize - 2);
            UiTheme.Accent(button, color);
            button.Pressed += () => PartChosen?.Invoke(part);
            button.AddChild(new Mark { Size = new Vector2(22, 20), MouseFilter = MouseFilterEnum.Ignore, Name = "Mark" });
            _menu.AddChild(button);
            _buttons[part] = button;
        }

        Visible = false;
    }

    /// <summary>Shows the hero's fire: out while they burn (the menu's buttons as the turn allows), back in once it's out.</summary>
    public void ShowHero(Hero? hero, GameSession session, bool theirTurn)
    {
        // Another hero's sheet, or the fire out: the menu closes (not while a part is being played out).
        if (hero?.Id != _heroId)
        {
            _menu.Visible = false;
        }

        _heroId = hero?.Id;
        _dice = hero?.FireDice ?? 0;
        _slot.QueueRedraw();
        _slot.TooltipText = _dice == 0 ? "" : $"On fire: {_dice} fire {(_dice == 1 ? "die" : "dice")} attack you at the start of your turn. Double-click to STOP, DROP and ROLL: all three this turn put it out. Or get into water.";
        if (_dice == 0)
        {
            _menu.Visible = false;
        }

        foreach ((FirePart part, Button button) in _buttons)
        {
            bool done = theirTurn && session.State.Turn?.FireParts.Contains(part) == true;
            string? why = !theirTurn ? "It isn't your turn." : session.CheckFightFire(part);
            button.Disabled = why is not null;
            button.TooltipText = done ? $"{part.ToString().ToUpperInvariant()}: done." : why ?? part switch
            {
                FirePart.Stop => "STOP: all your Rumble dice this turn.",
                FirePart.Drop => "DROP: all your Wend dice this turn, and you lie down.",
                _ => "ROLL: all your Od Hand dice this turn.",
            };
            ((Mark)button.GetNode("Mark")).Show(done ? Mark.Kind.Tick : why is not null ? Mark.Kind.Cross : Mark.Kind.None);
            Blink(part, button, why is null && _menu.Visible);
        }

        Slide(_dice > 0);
    }

    internal void ToggleMenu()
    {
        _menu.Visible = !_menu.Visible && _dice > 0;
        foreach ((FirePart part, Button button) in _buttons)
        {
            Blink(part, button, _menu.Visible && !button.Disabled);
        }
    }

    internal void CloseMenu() => _menu.Visible = false;

    private void Blink(FirePart part, Button button, bool on)
    {
        if (on == _blinks.ContainsKey(part))
        {
            return;
        }

        if (on)
        {
            Tween blink = button.CreateTween().SetLoops();
            blink.TweenProperty(button, "modulate", new Color(1, 1, 1, 0.5f), 0.4);
            blink.TweenProperty(button, "modulate", Colors.White, 0.4);
            _blinks[part] = blink;
        }
        else
        {
            _blinks[part].Kill();
            _blinks.Remove(part);
            button.Modulate = Colors.White;
        }
    }

    private void Slide(bool open)
    {
        if (open == _out)
        {
            return;
        }

        _out = open;
        _slide?.Kill();
        Visible = true;
        _slide = CreateTween();
        _slide.TweenProperty(_window, "position:x", open ? 0f : -Width, SlideSeconds).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        if (!open)
        {
            _menu.Visible = false;
            _slide.TweenCallback(Callable.From(() => Visible = false));
        }
    }

    /// <summary>The slot: a fanned stack of fire cards, one a die (five at most shown), and the total under them.</summary>
    private sealed partial class FireSlot : Control
    {
        public FireTab Owner_ { get; set; } = null!;

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
            {
                if (click.DoubleClick)
                {
                    Sfx.Play("click");
                    Owner_.ToggleMenu();
                }

                AcceptEvent();
            }
        }

        public override void _Draw()
        {
            int dice = Owner_._dice;
            int shown = Math.Min(dice, 5);
            float left = Size.X / 2 - 9 - (shown - 1) * 2.5f;
            for (int i = 0; i < shown; i++)
            {
                var card = new Rect2(left + i * 5, 1 + (i % 2), 18, 26);
                DrawRect(card, Cream);
                DrawRect(card, Palette.Ink, filled: false, width: 1);
                Vector2 c = card.GetCenter() + new Vector2(0, 3);
                DrawColoredPolygon([c + new Vector2(-6, 6), c + new Vector2(-5, -3), c + new Vector2(-2, 0), c + new Vector2(0, -10), c + new Vector2(3, -2), c + new Vector2(5, -5), c + new Vector2(6, 6)], Palette.Fire);
                DrawColoredPolygon([c + new Vector2(-3, 6), c + new Vector2(-1, -2), c + new Vector2(1, 1), c + new Vector2(3, 6)], new Color(1, 0.9f, 0.4f));
            }

            Font font = GameArt.BodyFont ?? ThemeDB.FallbackFont;
            DrawString(font, new Vector2(0, Size.Y - 1), $"{dice} fire {(dice == 1 ? "die" : "dice")}", HorizontalAlignment.Center, Size.X, UiTheme.FontSize, Palette.Fire);
        }
    }

    /// <summary>A tick (done) or a red X (can't) over a menu button.</summary>
    private sealed partial class Mark : Control
    {
        public enum Kind { None, Tick, Cross }

        private Kind _kind;

        public void Show(Kind kind)
        {
            _kind = kind;
            QueueRedraw();
        }

        public override void _Draw()
        {
            if (_kind == Kind.Cross)
            {
                DrawLine(new Vector2(3, 3), Size - new Vector2(3, 3), Palette.Danger, 2);
                DrawLine(new Vector2(Size.X - 3, 3), new Vector2(3, Size.Y - 3), Palette.Danger, 2);
            }
            else if (_kind == Kind.Tick)
            {
                DrawPolyline([new Vector2(4, Size.Y / 2), new Vector2(Size.X / 2 - 2, Size.Y - 4), new Vector2(Size.X - 3, 3)], Palette.Heal, 2);
            }
        }
    }
}
