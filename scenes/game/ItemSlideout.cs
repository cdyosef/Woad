using Godot;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The item slide-out: picked up from your inventory, a card's whole story slides out from behind the character sheet,
/// below the carry and fire tabs. On top its picture, name and what it is; under that what you can do with it now (Use,
/// Equip, Belt, Stow, Carry, Drop: greyed out with why, if not); then its full text, its numbers, every way it can be
/// used, and its base price (before any haggling). Put it down (pick nothing) and it slides back in.
/// </summary>
public partial class ItemSlideout : Control
{
    public const float Width = 196;
    private const double SlideSeconds = 0.16;

    private Panel _window = null!;
    private TextureRect _picture = null!;
    private Label _title = null!;
    private Label _kind = null!;
    private RichTextLabel _body = null!;
    private ScrollContainer _scroll = null!;
    private Tween? _slide;
    private bool _out;

    /// <summary>The six action buttons, along the top under the card's name.</summary>
    public Dictionary<CardAction, Button> Actions { get; } = [];

    public event Action<CardAction>? ActionRequested;

    /// <summary>A greyed-out button was clicked; the game says why it's unavailable.</summary>
    public event Action<CardAction>? BlockedActionClicked;

    internal bool IsOut => _out;

    internal string Body => _body.Text;

    /// <summary>The story's text size: a step up on a bigger screen.</summary>
    public int TextSize
    {
        set => _body.AddThemeFontSizeOverride("normal_font_size", value);
    }

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Ignore;
        _window = new Panel { Position = new Vector2(-Width, 0), Size = new Vector2(Width, Size.Y) };
        _window.AddThemeStyleboxOverride("panel", UiTheme.Box(Palette.Ink, Palette.Trim));
        AddChild(_window);

        _picture = new TextureRect { Position = new Vector2(4, 3), Size = new Vector2(18, 18), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.Nearest };
        _window.AddChild(_picture);
        _title = UiTheme.Label(new Vector2(25, 1), new Vector2(Width - 29, 12), color: Palette.TrimBright);
        _title.ClipText = true;
        _window.AddChild(_title);
        _kind = UiTheme.Label(new Vector2(25, 11), new Vector2(Width - 29, 12), color: Palette.TextDim);
        _kind.ClipText = true;
        _window.AddChild(_kind);

        CardAction[] actions = Enum.GetValues<CardAction>();
        for (int i = 0; i < actions.Length; i++)
        {
            CardAction action = actions[i];
            Button button = UiTheme.CompactButton(new Vector2(4 + i * 31.5f, 25), new Vector2(30, 16), action.ToString());
            button.Name = $"Item{action}";
            button.Pressed += () => ActionRequested?.Invoke(action);
            button.GuiInput += input =>
            {
                if (button.Disabled && input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                {
                    BlockedActionClicked?.Invoke(action);
                }
            };
            _window.AddChild(button);
            Actions[action] = button;
        }

        _scroll = new ScrollContainer { Position = new Vector2(4, 44), Size = new Vector2(Width - 8, Size.Y - 48), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _window.AddChild(_scroll);
        _body = UiTheme.RichLabel(Vector2.Zero, new Vector2(Width - 18, 10));
        _body.FitContent = true;
        _body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _body.CustomMinimumSize = new Vector2(Width - 18, 0);
        _scroll.AddChild(_body);
        Visible = false;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized && _window is not null)
        {
            Fit();
        }
    }

    /// <summary>
    /// Fits the window to the room it's given: its six buttons in a row when it's its full width, in two rows of three
    /// when narrower (beside the bag's drawer).
    /// </summary>
    private void Fit()
    {
        float width = Size.X;
        bool narrow = width < Width - 1;
        int perRow = narrow ? 3 : 6;
        float step = (width - 8) / perRow;
        int i = 0;
        foreach (Button button in Actions.Values)
        {
            button.Position = new Vector2(4 + i % perRow * step, 25 + i / perRow * 17);
            button.Size = new Vector2(step - 1.5f, 16);
            i++;
        }

        float top = narrow ? 61 : 44;
        _window.Size = new Vector2(width, Size.Y);
        if (!_out)
        {
            _window.Position = _window.Position with { X = -width };
        }

        _title.Size = _kind.Size = new Vector2(width - 29, 12);
        _scroll.Position = new Vector2(4, top);
        _scroll.Size = new Vector2(width - 8, Size.Y - top - 4);
        _body.CustomMinimumSize = new Vector2(width - 18, 0);
        _body.Size = _body.Size with { X = width - 18 };
    }

    /// <summary>Shows a picked-up card (sliding out), or nothing (sliding back in).</summary>
    public void ShowCard(CardInstance? card, Hero? hero, CardActions actions)
    {
        foreach ((CardAction action, Button button) in Actions)
        {
            button.Disabled = !actions.Allows(action);
            button.TooltipText = actions.Explain(action);
        }

        // A scroll can't be worn, but its spell can be learned: Equip becomes Learn.
        Actions[CardAction.Equip].Text = card?.Definition.Teaches is not null ? "Learn" : "Equip";
        if (card is null || hero is null)
        {
            Slide(false);
            return;
        }

        CardDefinition definition = card.Definition;
        _picture.Texture = GameArt.Texture($"res://assets/cards/{definition.Id}.png");
        _title.Text = CardSlot.Title(card);
        _kind.Text = ItemInfo.Kind(definition);
        _body.Text = ItemInfo.Body(card, hero, actions);
        _scroll.ScrollVertical = 0;
        Slide(true);
    }

    private void SetClickable(bool clickable)
    {
        MouseFilterEnum filter = clickable ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        _window.MouseFilter = filter;
        _scroll.MouseFilter = clickable ? MouseFilterEnum.Pass : MouseFilterEnum.Ignore;
        _body.MouseFilter = clickable ? MouseFilterEnum.Pass : MouseFilterEnum.Ignore;
        foreach (Button button in Actions.Values)
        {
            button.MouseFilter = filter;
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
        // While it slides away it lets clicks through to the board (aiming the card it was showing).
        SetClickable(open);
        if (open)
        {
            // Out at once, so its buttons are there to click the moment a card's picked up; it slides away.
            _window.Position = _window.Position with { X = 0 };
            return;
        }

        _slide = CreateTween();
        _slide.TweenProperty(_window, "position:x", open ? 0f : -Size.X, SlideSeconds).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        if (!open)
        {
            _slide.TweenCallback(Callable.From(() => Visible = false));
        }
    }
}

/// <summary>What there is to say about an item: what kind of thing it is, its numbers, every way to use it, its base price.</summary>
public static class ItemInfo
{
    public static string Kind(CardDefinition card) =>
        card.Type switch
        {
            CardType.Weapon => card.TwoHanded ? "Two-handed weapon" : "Weapon",
            CardType.Treasure => "Treasure",
            _ => card.Type.ToString(),
        } + (card.Usage == CardUsage.SingleUse ? ", single use" : "") + (card.IsStackable ? ", stacks" : "");

    private static string Slot(EquipSlot slot) => slot switch
    {
        EquipSlot.MainHand => "main hand",
        EquipSlot.OffHand => "off hand",
        _ => slot.ToString().ToLowerInvariant(),
    };

    /// <summary>The card's numbers: what it adds, as "Weapon +2, Armor 1".</summary>
    public static IEnumerable<string> Numbers(CardDefinition card)
    {
        Stats m = card.Modifiers;
        (int Value, string Name)[] numbers =
        [
            (m.Weapon + card.AttackBonus, "Weapon"), (m.WeaponDice, "weapon dice"), (m.Armor, "Armor"), (m.Block, "Block"), (m.Hearts, "Hearts"),
            (m.AttackDice, "Rumble dice"), (m.DefenseDice, "Defense dice"), (m.MoveDice, "Wend dice"), (m.Speed, "Wend"), (m.OdHandDice, "Od Hand dice"),
            (m.Tools, "Tools"), (m.ToolDice, "tool dice"), (m.Luck, "Luck"), (m.Magic, "Magic"),
        ];
        foreach ((int value, string name) in numbers.Where(number => number.Value != 0))
        {
            yield return $"{name} {(value > 0 ? "+" : "")}{value}";
        }

        if (card.AttackRange > 1)
        {
            yield return $"{(card.Reach ? "Reach" : "Range")} {card.AttackRange}";
        }
    }

    /// <summary>Every way the card can be used.</summary>
    public static IEnumerable<string> Uses(CardDefinition card)
    {
        if (card.Slot is { } slot)
        {
            yield return card.AltSlot is { } alt ? $"Held in either hand ({Slot(slot)} or {Slot(alt)})" + (card.TwoHanded ? ", two-handed" : "")
                : card.TwoHanded ? "Held in both hands" : $"Worn or held: {Slot(slot)}";
        }

        if (card.ModFor is { } on)
        {
            yield return $"Goes on your {Slot(on)}" + (card.Hangs is { } bag ? $" and hangs under it: holds {bag.Slots} {(bag.Slots == 1 ? "stack" : "stacks")}{(bag.Holds.Count > 0 ? $" of {string.Join(", ", bag.Holds.Take(4))}{(bag.Holds.Count > 4 ? "..." : "")}" : "")}" : "");
        }

        if (card.Belts)
        {
            yield return "Rides on your belt, to hand in a fight";
        }

        if (card.Effect is { } effect)
        {
            string reach = effect.Range > 0 ? $" up to {effect.Range} tiles" : " on yourself";
            yield return $"Use it{reach}{(effect.AreaRadius > 0 ? $", hitting everything within {effect.AreaRadius}" : "")}" + (card.Usage == CardUsage.SingleUse ? ": gone once used" : "");
        }

        if (card.ThrownRange > 0)
        {
            yield return $"Thrown up to {card.ThrownRange} tiles";
        }

        if (card.Ammo is { } ammo)
        {
            yield return $"Shoots {ammo.Replace('_', ' ')}s";
        }

        if (card.Tool is { } tool)
        {
            yield return $"A tool for {tool.For.ToString().Replace(", ", " and ")}: +{tool.Bonus} and {tool.Dice}d6, with your Od Hand dice";
        }

        if (card.Torch)
        {
            yield return "Held, a light: lights your way, helps searching, and adds a fire die to your attacks";
        }

        if (card.MedicineBag)
        {
            yield return "Helps every Aid, and cures more with it";
        }

        if (card.Bandage)
        {
            yield return "Goes into an Aid for another die";
        }

        if (card.Swims)
        {
            yield return "Water costs you less, and you stay afloat";
        }

        if (card.Teaches is { } spell)
        {
            yield return $"Study it to learn a spell ({spell.Replace('_', ' ')})";
        }

        if (card.Type == CardType.Treasure)
        {
            yield return "Worth its price to a merchant";
        }

        if (card.IsStackable)
        {
            yield return card.MaxStack > 0 ? $"Stacks up to {card.MaxStack} in one slot" : "Stacks in one slot";
        }

        yield return "Hand it to an ally beside you, or drop it";
    }

    /// <summary>The slide-out's text: the card's full text, then its numbers, ways to use it, and base price.</summary>
    public static string Body(CardInstance card, Hero hero, CardActions actions)
    {
        CardDefinition definition = card.Definition;
        string dim = Palette.Hex(Palette.TextDim), gold = Palette.Hex(Palette.Loot), trim = Palette.Hex(Palette.TrimBright);
        var text = new List<string> { definition.Text.Replace("[", "[lb]") };
        if (hero.HasSetBonus(definition))
        {
            text.Add($"[color={trim}]Set bonus on![/color]");
        }

        if (definition.Effect is not null && !actions.Allows(CardAction.Use))
        {
            text.Add($"[color={Palette.Hex(Palette.Danger)}]Can't use it now: {actions.Explain(CardAction.Use).Replace("[", "[lb]")}[/color]");
        }

        List<string> numbers = [.. Numbers(definition)];
        if (numbers.Count > 0)
        {
            text.Add($"[color={trim}]{string.Join(", ", numbers)}[/color]");
        }

        if (card.Count > 1 || card.Wear > 0)
        {
            text.Add($"[color={dim}]{(card.Count > 1 ? $"{card.Count} in the stack" : "")}{(card.Count > 1 && card.Wear > 0 ? ", " : "")}{(card.Wear > 0 ? $"worn {card.Wear}" : "")}[/color]");
        }

        text.Add($"[color={trim}]Ways to use it:[/color]\n" + string.Join("\n", Uses(definition).Select(use => $"- {use}")));
        text.Add(definition.Value > 0
            ? $"[color={gold}]Base price: {Money.Long(Money.Gold(definition.Value))}{(card.Count > 1 ? " each" : "")}[/color] [color={dim}](before haggling)[/color]"
            : $"[color={dim}]No price: a merchant won't buy it.[/color]");
        return string.Join("\n\n", text);
    }
}
