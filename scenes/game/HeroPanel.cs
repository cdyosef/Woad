using Godot;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>Left panel: the active hero's hearts, stats, equipment and what's left of their turn (see <see cref="Pool"/>).</summary>
public partial class HeroPanel : Panel
{
    private static readonly (EquipSlot Slot, string Caption)[] Slots =
    [
        (EquipSlot.Head, "Head"),
        (EquipSlot.Body, "Body"),
        (EquipSlot.MainHand, "Main"),
        (EquipSlot.OffHand, "Off"),
        (EquipSlot.Feet, "Feet"),
    ];

    private readonly Dictionary<EquipSlot, CardSlot> _slots = [];
    private PortraitView _crest = null!;
    private Label _name = null!;
    private Label _seat = null!;
    private HeartsView _hearts = null!;
    private RichTextLabel _stats = null!;
    private RichTextLabel _gold = null!;
    private RichTextLabel _record = null!;
    private int? _goldHeroId;
    private int _goldShown;
    private Label _turn = null!;

    public event Action<int>? CardSelected;

    internal CardSlot EquipmentSlot(EquipSlot slot) => _slots[slot];

    /// <summary>The dice left this turn, kept up to date by the game screen.</summary>
    public DicePoolView Pool { get; } = new() { Position = new(8, 200), Size = new(160, 65) };

    public override void _Ready()
    {
        Size = new Vector2(176, 360);

        _crest = Add(new PortraitView { Position = new(8, 5), Size = new(13, 16) });
        _name = Add(UiTheme.Label(new(26, 4), new(94, 16), fontSize: UiTheme.HeadingSize));
        _seat = Add(UiTheme.Label(new(120, 8), new(48, 12), color: Palette.TextDim));
        _seat.HorizontalAlignment = HorizontalAlignment.Right;
        _hearts = Add(new HeartsView { Position = new(8, 24), Size = new(160, 9) });
        _stats = Add(UiTheme.RichLabel(new(8, 35), new(160, 28)));
        _gold = Add(UiTheme.RichLabel(new(8, 63), new(160, 14)));
        _gold.PivotOffset = new Vector2(10, 7);
        _record = Add(UiTheme.RichLabel(new(8, 76), new(160, 14)));

        Add(UiTheme.Heading(UiTheme.Label(new(8, 90), new(160, 12), "EQUIPMENT", color: Palette.Trim), 1));
        for (int i = 0; i < Slots.Length; i++)
        {
            (EquipSlot slot, string caption) = Slots[i];
            float y = 103 + i * 16;
            Add(UiTheme.Label(new(8, y + 2), new(32, 12), caption, color: Palette.TextDim));
            CardSlot cardSlot = Add(new CardSlot { Position = new(40, y), Size = new(128, 15) });
            cardSlot.Pressed += () =>
            {
                if (cardSlot.Card is { } card)
                {
                    CardSelected?.Invoke(card.Id);
                }
            };
            _slots[slot] = cardSlot;
        }

        Add(UiTheme.Heading(UiTheme.Label(new(8, 185), new(160, 12), "THIS TURN", color: Palette.Trim), 1));
        Add(Pool);
        _turn = Add(UiTheme.Label(new(72, 186), new(96, 12), color: Palette.TextDim));
        _turn.HorizontalAlignment = HorizontalAlignment.Right;
    }

    public void ShowHero(Hero? hero, TurnState? turn, int? selectedCardId)
    {
        if (hero is null)
        {
            _crest.Visible = false;
            Pool.Visible = false;
            _name.Text = "Run over";
            _seat.Text = _stats.Text = _gold.Text = _record.Text = _turn.Text = "";
            _goldHeroId = null;
            _hearts.ShowHearts(0, 0);
            foreach (CardSlot slot in _slots.Values)
            {
                slot.ShowCard(null, "", selected: false);
            }

            return;
        }

        _crest.Visible = true;
        _crest.PortraitId = hero.Portrait;
        _crest.Colors = Looks.Resolve(hero.Appearance, hero.Portrait);
        _name.Text = hero.Name;
        _name.AddThemeColorOverride("font_color", Palette.Seat(hero.Seat));
        _seat.Text = $"Lv {hero.Level} " + (hero.IsCompanion ? "Comp." : $"P{hero.PlayerNumber}");
        _hearts.ShowHearts(hero.Hearts, hero.MaxHearts);

        Stats stats = hero.EffectiveStats;
        // Weapon and tool dice show after the bonus (the Longsword: 1d6+1+1d6); tools only help with locks.
        string weaponDice = stats.WeaponDice > 0 ? $"+{stats.WeaponDice}d6" : "";
        string toolDice = stats.Tools > 0 ? $" ({UiTheme.Dice(stats.ToolDice, stats.Tools).Replace("0d6", "")} locks)" : "";
        _stats.Text = $"{UiTheme.Icon("icon_rumble", "Rumble")} {UiTheme.Dice(stats.AttackDice, stats.Weapon)}{weaponDice}  " +
                      $"{UiTheme.Icon("icon_wend", "Wend")} {UiTheme.Dice(stats.MoveDice, stats.Speed)}  " +
                      $"{UiTheme.Icon("icon_odhand", "Od Hand")} {UiTheme.Dice(stats.OdHandDice, 0)}{toolDice}\n" +
                      $"{UiTheme.Icon("icon_defense", "Defense")} {DefenseText(hero, stats)}   {UiTheme.Icon("icon_luck", "Luck")} {hero.LuckRemaining}/{stats.Luck}" +
                      (stats.Magic > 0 ? $"   {UiTheme.Icon("icon_magic", "Magic")} {stats.Magic}" : "");
        // Gold, the treasure carried (what escaping now is worth on top), and keys, together on one line.
        string dim = Palette.Hex(Palette.TextDim);
        int treasure = hero.TreasureValue - hero.Gold;
        string keys = hero.Keys > 0 || hero.HasBossKey
            ? $"   {UiTheme.Icon("icon_key", "Keys")} {hero.Keys}{(hero.HasBossKey ? " " + UiTheme.Icon("icon_boss_key", "Boss Key") : "")}"
            : "";
        _gold.Text = UiTheme.Gold(hero.Gold) +
                     (treasure > 0 ? $"  [color={dim}]+{treasure} in treasure[/color]" : "") + keys;
        _record.Text = $"[color={dim}]Kills {hero.Kills}   XP {hero.Xp}[/color]";
        if (_goldHeroId == hero.Id && hero.Gold != _goldShown)
        {
            PopGold(hero.Gold > _goldShown ? Palette.Heal : Palette.Danger);
        }

        _goldHeroId = hero.Id;
        _goldShown = hero.Gold;

        foreach ((EquipSlot slot, CardSlot view) in _slots)
        {
            CardInstance? card = hero.Equipment.GetValueOrDefault(slot);
            view.ShowCard(card, "(empty)", card is not null && card.Id == selectedCardId);
        }

        Pool.Visible = turn is not null;
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

    /// <summary>The base die and armor, then the extras (dice and Block) that back it up: "1d6+1 (+1d6, Block 1)".</summary>
    private static string DefenseText(Hero hero, Stats stats)
    {
        int extra = Math.Max(0, stats.DefenseDice - 1);
        var extras = new List<string>();
        if (extra > 0)
        {
            extras.Add($"+{extra}d6");
        }

        if (stats.Block > 0)
        {
            extras.Add($"Block {stats.Block}");
        }

        if (stats.GuardDice > 0)
        {
            extras.Add($"Guard {stats.GuardDice}");
        }

        string main = UiTheme.Dice(Math.Min(1, stats.DefenseDice), stats.Armor);
        return extras.Count == 0 ? main : $"{main} ({string.Join(", ", extras)})";
    }
}
