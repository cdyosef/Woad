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
    private RichTextLabel _record = null!;
    private Label _turn = null!;

    public event Action<int>? CardSelected;

    /// <summary>The dice left this turn, kept up to date by the game screen.</summary>
    public DicePoolView Pool { get; } = new() { Position = new(8, 188), Size = new(160, 60) };

    public override void _Ready()
    {
        Size = new Vector2(176, 360);

        _crest = Add(new PortraitView { Position = new(8, 5), Size = new(13, 16) });
        _name = Add(UiTheme.Label(new(26, 4), new(94, 16), fontSize: UiTheme.HeadingSize));
        _seat = Add(UiTheme.Label(new(120, 8), new(48, 12), color: Palette.TextDim));
        _seat.HorizontalAlignment = HorizontalAlignment.Right;
        _hearts = Add(new HeartsView { Position = new(8, 24), Size = new(160, 9) });
        _stats = Add(UiTheme.RichLabel(new(8, 35), new(160, 28)));
        _record = Add(UiTheme.RichLabel(new(8, 63), new(160, 14)));

        Add(UiTheme.Heading(UiTheme.Label(new(8, 78), new(160, 12), "EQUIPMENT", color: Palette.Trim), 1));
        for (int i = 0; i < Slots.Length; i++)
        {
            (EquipSlot slot, string caption) = Slots[i];
            float y = 91 + i * 16;
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

        Add(UiTheme.Heading(UiTheme.Label(new(8, 173), new(160, 12), "THIS TURN", color: Palette.Trim), 1));
        Add(Pool);
        _turn = Add(UiTheme.Label(new(72, 174), new(96, 12), color: Palette.TextDim));
        _turn.HorizontalAlignment = HorizontalAlignment.Right;
    }

    public void ShowHero(Hero? hero, TurnState? turn, int? selectedCardId)
    {
        if (hero is null)
        {
            _crest.Visible = false;
            Pool.Visible = false;
            _name.Text = "Run over";
            _seat.Text = _stats.Text = _record.Text = _turn.Text = "";
            _hearts.ShowHearts(0, 0);
            foreach (CardSlot slot in _slots.Values)
            {
                slot.ShowCard(null, "", selected: false);
            }

            return;
        }

        _crest.Visible = true;
        _crest.PortraitId = hero.Portrait;
        _name.Text = hero.Name;
        _name.AddThemeColorOverride("font_color", Palette.Seat(hero.Seat));
        _seat.Text = $"Player {hero.Seat + 1}";
        _hearts.ShowHearts(hero.Hearts, hero.MaxHearts);

        Stats stats = hero.EffectiveStats;
        _stats.Text = $"{UiTheme.Icon("icon_rumble", "Rumble")} {UiTheme.Dice(stats.AttackDice, stats.Weapon)}  " +
                      $"{UiTheme.Icon("icon_wend", "Wend")} {UiTheme.Dice(stats.MoveDice, stats.Speed)}  " +
                      $"{UiTheme.Icon("icon_odhand", "Od Hand")} {UiTheme.Dice(stats.OdHandDice, stats.Tools)}\n" +
                      $"Defense {UiTheme.Dice(stats.DefenseDice, stats.Armor)}   Luck {hero.LuckRemaining}/{stats.Luck}";
        string dim = Palette.Hex(Palette.TextDim);
        string keys = hero.Keys > 0 || hero.HasBossKey
            ? $"  {UiTheme.Icon("icon_key", "Keys")} {hero.Keys}{(hero.HasBossKey ? " " + UiTheme.Icon("icon_boss_key", "Boss Key") : "")}"
            : "";
        _record.Text = $"[color={dim}]Kills {hero.Kills}  {hero.TreasureValue}g  XP {hero.Xp}[/color]{keys}";

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


    private T Add<T>(T node) where T : Node
    {
        AddChild(node);
        return node;
    }
}
