using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Left panel: the active hero's character sheet. On top, their name and hearts; level, title, kills
/// and XP; gold, keys, torches and treasure; and the Power Dice bank. Under it the cards, laid out like
/// the body: accessory, head and Magic across the top, off hand, body and main hand across the middle,
/// and the bag (a deck, its cards listed under it), feet and belt (a fanned hand) along the bottom. Each
/// pool of dice sits under the card it goes with (see <see cref="Pool"/>): Luck under the accessory, Od
/// Hand under the off hand, Defense under the body, Rumble under the main hand, Wend under the feet.
/// End turn is at the bottom.
/// </summary>
public partial class HeroPanel : Panel
{
    public const float CardWidth = 48;
    public const float CardHeight = 50;

    /// <summary>The three columns, and the tops of the three rows of cards.</summary>
    public static readonly float[] Columns = [4, 64, 124];
    public static readonly float[] Rows = [52, 130, 208];

    private static readonly (EquipSlot Slot, int Column, int Row, string Back, string Name)[] Slots =
    [
        (EquipSlot.Accessory, 0, 0, "back_accessory", "Accessory"),
        (EquipSlot.Head, 1, 0, "back_head", "Head"),
        (EquipSlot.OffHand, 0, 1, "back_off_hand", "Off hand"),
        (EquipSlot.Body, 1, 1, "back_body", "Body"),
        (EquipSlot.MainHand, 2, 1, "back_main_hand", "Main hand"),
        (EquipSlot.Feet, 1, 2, "back_feet", "Feet"),
    ];

    private readonly Dictionary<EquipSlot, SheetCard> _slots = [];
    private readonly SheetCard[] _belt = new SheetCard[GameRules.BeltCapacity];
    private readonly CardSlot[] _backpack = new CardSlot[GameRules.BackpackCapacity];
    private Label _name = null!;
    private HeartsView _hearts = null!;
    private RichTextLabel _level = null!;
    private XpBar _xp = null!;
    private RichTextLabel _gold = null!;
    private RichTextLabel _luck = null!;
    private RichTextLabel _magicRank = null!;
    private SheetCard _bag = null!;
    private Panel _bagView = null!;
    private Label _bagTitle = null!;
    private readonly SheetCard[] _bagCards = new SheetCard[GameRules.BackpackCapacity];
    private Label _turn = null!;
    private int? _goldHeroId;
    private int _goldShown;

    public event Action<int>? CardSelected;

    /// <summary>A worn card was right-clicked: pick it up for its buttons, whatever a click on it would do.</summary>
    public event Action<int>? CardInspected;

    /// <summary>The bag was opened or closed.</summary>
    public event Action? BagToggled;

    /// <summary>Where the board starts, to the right of the sheet: the open bag goes over it.</summary>
    private const float SideOfBoard = 176;

    /// <summary>Puts the open bag away.</summary>
    public void CloseBag() => _bagView.Visible = false;

    /// <summary>The Magic card was clicked: cast a spell.</summary>
    public event Action? MagicClicked;

    internal CardSlot EquipmentSlot(EquipSlot slot) => _slots[slot];

    internal CardSlot BeltSlot(int index) => _belt[index];

    internal CardSlot BackpackSlot(int index) => _backpack[index];

    /// <summary>The bag's deck: click it to spread the bag's cards out over the board.</summary>
    internal SheetCard BagDeck => _bag;

    internal bool IsBagOpen => _bagView.Visible;

    /// <summary>The open bag, for the game screen to put over the board (above it, so it takes the clicks).</summary>
    public Control BagView => _bagView;

    /// <summary>A card in the open bag.</summary>
    internal SheetCard BagCard(int index) => _bagCards[index];

    /// <summary>The Magic card, top right: its spells, to cast.</summary>
    internal SheetCard MagicCard { get; private set; } = null!;

    /// <summary>The dice left this turn, under their cards, kept up to date by the game screen.</summary>
    public DicePoolView Pool { get; } = new() { Position = Vector2.Zero, Size = new(176, 330) };

    public override void _Ready()
    {
        Size = new Vector2(176, 360);
        Add(Pool);
        Pool.Place(DicePool.Power, new Vector2(4, 39), 168);
        Pool.Place(DicePool.OdHand, new Vector2(3, Rows[1] + CardHeight + 2), 57);
        Pool.Place(DicePool.Defense, new Vector2(63, Rows[1] + CardHeight + 2), 57);
        Pool.Place(DicePool.Rumble, new Vector2(122, Rows[1] + CardHeight + 2), 50);
        Pool.Place(DicePool.Wend, new Vector2(63, Rows[2] + CardHeight + 2), 57);

        // 1. Name and hearts. 2. Level, title, kills and XP. 3. Gold, keys, torches and treasure.
        _name = Add(UiTheme.Label(new(4, 0), new(80, 16), fontSize: UiTheme.HeadingSize));
        _hearts = Add(new HeartsView { Position = new(60, 5), Size = new(112, 9) });
        _level = Add(UiTheme.RichLabel(new(4, 15), new(118, 12)));
        _xp = Add(new XpBar { Position = new(122, 17), Size = new(50, 8) });
        _gold = Add(UiTheme.RichLabel(new(4, 26), new(168, 12)));
        _gold.PivotOffset = new Vector2(10, 6);

        foreach ((EquipSlot slot, int column, int row, string back, string name) in Slots)
        {
            SheetCard card = AddCard(new Vector2(Columns[column], Rows[row]), back);
            card.Name = name.Replace(" ", "");
            _slots[slot] = card;
            card.SetMeta("empty", name);
        }

        MagicCard = Add(new SheetCard { Position = new(Columns[2], Rows[0]), Size = new(CardWidth, CardHeight), Back = "cast" });
        MagicCard.Pressed += () => MagicClicked?.Invoke();
        _luck = Add(UiTheme.RichLabel(new(3, Rows[0] + CardHeight + 2), new(58, 12)));
        _magicRank = Add(UiTheme.RichLabel(new(122, Rows[0] + CardHeight + 2), new(52, 12)));

        // The bag: a deck, its cards listed under it.
        for (int i = 2; i >= 1; i--)
        {
            Add(new Panel { Position = new(Columns[0] + i * 2, Rows[2] - i * 2), Size = new(CardWidth, CardHeight), MouseFilter = MouseFilterEnum.Ignore });
        }

        _bag = Add(new SheetCard { Position = new(Columns[0], Rows[2]), Size = new(CardWidth, CardHeight), Back = "back_bag", ToggleMode = false });
        _bag.Pressed += () =>
        {
            _bagView.Visible = !_bagView.Visible;
            BagToggled?.Invoke();
        };

        // The bag spread open over the board: its cards face up, two rows of three, to pick from.
        _bagView = new Panel { Position = new(SideOfBoard + 56, 80), Size = new(176, 136), Visible = false, ZIndex = 3 };
        _bagTitle = UiTheme.Label(new(0, 3), new(176, 12), color: Palette.TrimBright);
        _bagTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _bagView.AddChild(_bagTitle);
        for (int i = 0; i < _bagCards.Length; i++)
        {
            var card = new SheetCard { Position = new(10 + i % 3 * 54, 18 + i / 3 * 56), Size = new(CardWidth, CardHeight), Back = "back_bag" };
            card.Pressed += () =>
            {
                if (card.Card is { } held)
                {
                    CardSelected?.Invoke(held.Id);
                }
            };
            _bagView.AddChild(card);
            _bagCards[i] = card;
        }
        for (int i = 0; i < _backpack.Length; i++)
        {
            CardSlot slot = Add(new CardSlot { Position = new(2, Rows[2] + CardHeight + 3 + i * 11), Size = new(60, 11), Flat = true });
            slot.AddThemeFontSizeOverride("font_size", UiTheme.FontSize);
            slot.Pressed += () =>
            {
                if (slot.Card is { } card)
                {
                    CardSelected?.Invoke(card.Id);
                }
            };
            _backpack[i] = slot;
        }

        // The belt, fanned like a hand.
        for (int i = 0; i < _belt.Length; i++)
        {
            SheetCard card = Add(new SheetCard
            {
                Position = new(Columns[2] - 1 + i * 15, Rows[2] + 2 + (i == 0 ? 2 : 0)),
                Size = new(28, 38),
                Small = true,
                Back = "back_belt",
                Rotation = i == 0 ? -0.12f : 0.12f,
                PivotOffset = new(14, 38),
            });
            card.Pressed += () =>
            {
                if (card.Card is { } held)
                {
                    CardSelected?.Invoke(held.Id);
                }
            };
            _belt[i] = card;
        }

        Add(UiTheme.Label(new(Columns[2], Rows[2] + 40), new(CardWidth, 12), "Belt", color: Palette.TextDim)).HorizontalAlignment = HorizontalAlignment.Center;
        _turn = Add(UiTheme.Label(new(118, Rows[2] + CardHeight + 3), new(56, 24), color: Palette.TextDim));
        _turn.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _turn.HorizontalAlignment = HorizontalAlignment.Center;
    }

    private SheetCard AddCard(Vector2 position, string back)
    {
        SheetCard card = Add(new SheetCard { Position = position, Size = new(CardWidth, CardHeight), Back = back });
        card.Pressed += () =>
        {
            if (card.Card is { } held)
            {
                CardSelected?.Invoke(held.Id);
            }
        };
        card.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } && card.Card is { } held)
            {
                CardInspected?.Invoke(held.Id);
                card.AcceptEvent();
            }
        };
        return card;
    }

    public void ShowHero(Hero? hero, TurnState? turn, int? selectedCardId)
    {
        if (hero is null)
        {
            CloseBag();
            Pool.Visible = false;
            _name.Text = "Run over";
            _level.Text = _gold.Text = _luck.Text = _magicRank.Text = _turn.Text = "";
            _xp.Visible = false;
            _goldHeroId = null;
            _hearts.ShowHearts(0, 0);
            foreach ((EquipSlot slot, SheetCard card) in _slots)
            {
                card.Show(null, (string)card.GetMeta("empty"), selected: false);
            }

            return;
        }

        _name.Text = hero.Name;
        _name.AddThemeColorOverride("font_color", Palette.Seat(hero.Seat));
        Font heading = _name.GetThemeFont("font");
        float nameWidth = heading.GetStringSize(hero.Name, fontSize: UiTheme.HeadingSize).X;
        _hearts.Position = new Vector2(Math.Min(4 + nameWidth + 5, 100), 5);
        _hearts.Size = new Vector2(172 - _hearts.Position.X, 9);
        _hearts.ShowHearts(hero.Hearts, hero.MaxHearts);

        // The title, for now, is what they look like (Barbarian, Bard...).
        string dim = Palette.Hex(Palette.TextDim);
        string look = hero.Appearance.Look.Length > 0 ? hero.Appearance.Look : hero.Portrait;
        string title = look.Length > 0 ? char.ToUpperInvariant(look[0]) + look[1..].Replace('_', ' ') : "";
        _level.Text = $"[color={Palette.Hex(Palette.TrimBright)}]Lv {hero.Level}[/color] {title}  [color={dim}]{hero.Kills} kills[/color]";
        int total = hero.StartXp + hero.Xp;
        _xp.Visible = true;
        _xp.Show(total - Progression.XpForLevel(hero.Level), Progression.XpForLevel(hero.Level + 1) - Progression.XpForLevel(hero.Level));

        Stats stats = hero.EffectiveStats;
        int treasure = hero.TreasureValue - hero.Gold;
        int torches = hero.AllCards.Where(card => card.Definition.Torch).Sum(card => card.Count);
        _gold.Text = UiTheme.Gold(hero.Gold).Replace(" gold", "")
                     + (hero.Keys > 0 || hero.HasBossKey ? $"  {UiTheme.Icon("icon_key", "Keys")} {hero.Keys}{(hero.HasBossKey ? " " + UiTheme.Icon("icon_boss_key", "Boss Key") : "")}" : "")
                     + (torches > 0 ? $"  [color={Palette.Hex(Palette.Fire)}]Torch {torches}[/color]" : "")
                     + $"  [color={Palette.Hex(Palette.Loot)}]Worth {hero.TreasureValue}[/color]"
                     + (treasure > 0 ? $" [color={dim}]({treasure} in loot)[/color]" : "");
        if (_goldHeroId == hero.Id && hero.Gold != _goldShown)
        {
            PopGold(hero.Gold > _goldShown ? Palette.Heal : Palette.Danger);
        }

        _goldHeroId = hero.Id;
        _goldShown = hero.Gold;

        foreach ((EquipSlot slot, SheetCard card) in _slots)
        {
            CardInstance? held = hero.Equipment.GetValueOrDefault(slot);
            card.Show(held, (string)card.GetMeta("empty"), held is not null && held.Id == selectedCardId);
            if (held is { Definition.Wings: true })
            {
                card.TooltipText += "\nClick for Land. or Fly! Right-click for the card's buttons.";
            }
        }

        for (int i = 0; i < _belt.Length; i++)
        {
            CardInstance? card = i < hero.Belt.Count ? hero.Belt[i] : null;
            _belt[i].Show(card, "", card is not null && card.Id == selectedCardId);
        }

        for (int i = 0; i < _backpack.Length; i++)
        {
            CardInstance? card = i < hero.Backpack.Count ? hero.Backpack[i] : null;
            _backpack[i].ShowCard(card, "", card is not null && card.Id == selectedCardId);
            _backpack[i].Visible = card is not null;
        }

        _bag.Show(null, $"Bag {hero.Backpack.Count}/{GameRules.BackpackCapacity}", selected: false);
        _bag.Disabled = false;
        _bag.TooltipText = (hero.Backpack.Count == 0 ? "The bag is empty." : "In the bag:\n" + string.Join("\n", hero.Backpack.Select(CardSlot.Title))) + "\nClick to open it.";
        _bagTitle.Text = hero.Backpack.Count == 0 ? "THE BAG IS EMPTY" : $"THE BAG {hero.Backpack.Count}/{GameRules.BackpackCapacity}";
        for (int i = 0; i < _bagCards.Length; i++)
        {
            CardInstance? card = i < hero.Backpack.Count ? hero.Backpack[i] : null;
            _bagCards[i].Show(card, "", card is not null && card.Id == selectedCardId);
        }

        _luck.Text = $"{UiTheme.Icon("icon_luck", "Luck")} [color={Palette.Hex(Palette.Heal)}]{hero.LuckRemaining}/{stats.Luck} Luck[/color]";
        bool knowsMagic = stats.Magic > 0 || hero.Spells.Count > 0;
        if (knowsMagic)
        {
            _magicRank.Text = $"{UiTheme.Icon("icon_magic", "Magic")} [color=#b07ce8]{stats.Magic}, {UiTheme.Count(hero.Spells.Count, "spell", "spells")}[/color]";
        }
        else
        {
            MagicCard.Show(null, "Magic", selected: false);
            MagicCard.Disabled = true;
            _magicRank.Text = "";
        }

        Pool.Visible = true;
        Pool.ShowCapacity(hero.Power.Capacity);
        if (turn is null)
        {
            // Not their turn (out on the Woarld Map, or looked over): all their dice, to read, not to pick.
            Pool.Enabled = false;
            Pool.ShowSwap(DicePool.Rumble, null);
            Pool.ShowPool(DicePool.Rumble, stats.AttackDice, 0, stats.Weapon, false, bonusDice: stats.WeaponDice);
            Pool.ShowPool(DicePool.Wend, stats.MoveDice, 0, stats.Speed, false);
            Pool.ShowPool(DicePool.OdHand, stats.OdHandDice, 0, stats.Tools, false, bonusDice: stats.ToolDice);
            Pool.ShowPool(DicePool.Power, hero.Power.Banked, 0, 0, false);
            int defense = Math.Max(0, stats.DefenseDice);
            Pool.ShowDefense(Math.Min(1, defense), Math.Max(0, defense - 1), Math.Max(0, defense - 1), stats.Armor, stats.Block, stats.Block > 0, stats.GuardDice, stats.GuardDice);
        }
        _turn.Text = turn is null
            ? ""
            : (turn.HasActed, turn.UsedBeltItem) switch
            {
                (false, false) => "action + belt ready",
                (false, true) => "action ready",
                (true, false) => "belt item ready",
                _ => "action, belt used",
            };
    }

    /// <summary>The gold line swells and flashes: green when gold comes in, red when it's lost.</summary>
    private void PopGold(Color flash)
    {
        Tween tween = _gold.CreateTween().SetParallel();
        _gold.Scale = new Vector2(1.3f, 1.3f);
        _gold.Modulate = flash.Lightened(0.3f);
        tween.TweenProperty(_gold, "scale", Vector2.One, 0.3).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_gold, "modulate", Colors.White, 0.5);
    }

    private T Add<T>(T node) where T : Node
    {
        AddChild(node);
        return node;
    }

    /// <summary>The Magic card: lit when a spell can be cast now, dim (still showing why) when not.</summary>
    public void ShowMagic(bool knowsMagic, bool ready, string help)
    {
        if (knowsMagic)
        {
            MagicCard.ShowFace("cast", "Cast", new Color("b07ce8"), ready, help);
        }
    }

    /// <summary>The bar to the next level, with the XP toward it.</summary>
    private sealed partial class XpBar : Control
    {
        private int _have;
        private int _need = 1;

        public void Show(int have, int need)
        {
            _have = have;
            _need = Math.Max(1, need);
            TooltipText = $"XP: {have} of {need} to the next level.";
            QueueRedraw();
        }

        public override void _Draw()
        {
            var rect = new Rect2(Vector2.Zero, Size);
            DrawRect(rect, Palette.Ink);
            DrawRect(new Rect2(Vector2.Zero, new Vector2(Size.X * Math.Clamp(_have / (float)_need, 0, 1), Size.Y)), Palette.TrimBright);
            DrawRect(rect, Palette.TrimDim, filled: false, width: 1);
        }
    }
}
