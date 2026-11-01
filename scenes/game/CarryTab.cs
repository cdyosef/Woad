using Godot;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The carrying slots: a little window that slides out from behind the character sheet's edge while
/// the hero carries something (no Rumble, no Od Hand meanwhile), and back in once their arms are free.
/// A card that fills both slots (the Relic) takes the whole window. Click a card to pick it up for its
/// buttons (Equip, Belt, Stow, Drop).
/// </summary>
public partial class CarryTab : Control
{
    private const float Width = 72;
    private const float Height = 58;
    private const double SlideSeconds = 0.18;

    private readonly SheetCard[] _cards = new SheetCard[GameRules.CarrySlots];
    private Panel _window = null!;
    private Label _title = null!;
    private Tween? _slide;
    private bool _out;

    /// <summary>A carried card was clicked: it's selected, for its buttons.</summary>
    public event Action<int>? CardSelected;

    /// <summary>Whether the window is out (or sliding out).</summary>
    internal bool IsOut => _out;

    internal SheetCard Card(int index) => _cards[index];

    public override void _Ready()
    {
        // The window slides within this strip, which hides whatever of it is still behind the sheet.
        Size = new Vector2(Width, Height);
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;
        _window = new Panel { Size = Size, Position = new Vector2(-Width, 0) };
        AddChild(_window);
        _title = UiTheme.Label(new Vector2(4, 1), new Vector2(Width - 8, 12), "Carrying", color: Palette.TrimBright);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _window.AddChild(_title);
        for (int i = 0; i < _cards.Length; i++)
        {
            var card = new SheetCard { Position = new Vector2(5 + i * 32, 15), Size = new Vector2(30, 40), Small = true, Back = "back_bag" };
            card.Pressed += () =>
            {
                if (card.Card is { } held)
                {
                    CardSelected?.Invoke(held.Id);
                }
            };
            _window.AddChild(card);
            _cards[i] = card;
        }

        Visible = false;
    }

    /// <summary>Shows what the hero carries, sliding the window out (or back in, with nothing carried).</summary>
    public void ShowHero(Hero? hero, int? selectedCardId)
    {
        IReadOnlyList<CardInstance> carried = hero?.Carried ?? [];
        bool both = carried.Count == 1 && carried[0].Definition.CarrySlots == GameRules.CarrySlots;
        for (int i = 0; i < _cards.Length; i++)
        {
            CardInstance? card = i < carried.Count ? carried[i] : null;
            _cards[i].Show(card, i == 1 && both ? "" : "Free", card is not null && card.Id == selectedCardId);
            _cards[i].Visible = !(both && i == 1);
        }

        // One card filling both arms takes the window's whole width.
        _cards[0].Size = both ? new Vector2(62, 40) : new Vector2(30, 40);
        _title.TooltipText = "While you carry anything: no Rumble and no Od Hand. Drop it, or put it away, to fight.";
        Slide(carried.Count > 0);
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
            _slide.TweenCallback(Callable.From(() => Visible = false));
        }
    }
}
