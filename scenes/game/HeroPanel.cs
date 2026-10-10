using Godot;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>Left panel: the active hero's hearts, stats, equipment and what's left of their turn.</summary>
public partial class HeroPanel : Panel
{
    private const string Help =
        "Roll, then click a blue tile to move. Click a red-outlined monster to attack, " +
        "or a gold-outlined chest to open it. Select a card to use, equip or drop it. " +
        "Right-click cancels.";

    private static readonly (EquipSlot Slot, string Caption)[] Slots =
    [
        (EquipSlot.Head, "Head"),
        (EquipSlot.Body, "Body"),
        (EquipSlot.MainHand, "Main"),
        (EquipSlot.OffHand, "Off"),
        (EquipSlot.Feet, "Feet"),
    ];

    private readonly Dictionary<EquipSlot, CardSlot> _slots = [];
    private Label _name = null!;
    private Label _seat = null!;
    private HeartsView _hearts = null!;
    private Label _stats = null!;
    private Label _record = null!;
    private Label _turn = null!;

    public event Action<int>? CardSelected;

    public override void _Ready()
    {
        Size = new Vector2(176, 360);

        _name = Add(UiTheme.Label(new(8, 4), new(112, 16), fontSize: UiTheme.HeadingSize));
        _seat = Add(UiTheme.Label(new(120, 8), new(48, 12), color: Palette.TextDim));
        _seat.HorizontalAlignment = HorizontalAlignment.Right;
        _hearts = Add(new HeartsView { Position = new(8, 24), Size = new(160, 9) });
        _stats = Add(UiTheme.Label(new(8, 37), new(160, 26)));
        _record = Add(UiTheme.Label(new(8, 64), new(160, 12), color: Palette.TextDim));

        Add(UiTheme.Label(new(8, 80), new(160, 12), "EQUIPMENT", color: Palette.Trim));
        for (int i = 0; i < Slots.Length; i++)
        {
            (EquipSlot slot, string caption) = Slots[i];
            float y = 93 + i * 18;
            Add(UiTheme.Label(new(8, y + 2), new(32, 12), caption, color: Palette.TextDim));
            CardSlot cardSlot = Add(new CardSlot { Position = new(40, y), Size = new(128, 16) });
            cardSlot.Pressed += () =>
            {
                if (cardSlot.Card is { } card)
                {
                    CardSelected?.Invoke(card.Id);
                }
            };
            _slots[slot] = cardSlot;
        }

        Add(UiTheme.Label(new(8, 190), new(160, 12), "THIS TURN", color: Palette.Trim));
        _turn = Add(UiTheme.Label(new(8, 203), new(160, 36)));
        Add(UiTheme.Label(new(8, 270), new(160, 84), Help, color: Palette.TextDim));
    }

    public void ShowHero(Hero? hero, TurnState? turn, int? selectedCardId)
    {
        if (hero is null)
        {
            _name.Text = "Run over";
            _seat.Text = _stats.Text = _record.Text = _turn.Text = "";
            _hearts.ShowHearts(0, 0);
            foreach (CardSlot slot in _slots.Values)
            {
                slot.ShowCard(null, "", selected: false);
            }

            return;
        }

        _name.Text = hero.Name;
        _name.AddThemeColorOverride("font_color", Palette.Seat(hero.Seat));
        _seat.Text = $"Player {hero.Seat + 1}";
        _hearts.ShowHearts(hero.Hearts, hero.MaxHearts);

        Stats stats = hero.EffectiveStats;
        string speed = stats.Speed >= 0 ? $"+{stats.Speed}" : stats.Speed.ToString();
        _stats.Text = $"Move {stats.MoveDice}d6{speed}     Luck {hero.LuckRemaining}/{stats.Luck}\n" +
                      $"Attack {stats.AttackDice} dice   Defense {stats.DefenseDice} dice";
        _record.Text = $"Kills {hero.Kills}     Treasure {hero.TreasureValue}g";

        foreach ((EquipSlot slot, CardSlot view) in _slots)
        {
            CardInstance? card = hero.Equipment.GetValueOrDefault(slot);
            view.ShowCard(card, "(empty)", card is not null && card.Id == selectedCardId);
        }

        _turn.Text = turn is null
            ? ""
            : $"Move: {MoveStatus(turn)}\n" +
              $"Action: {(turn.HasActed ? "used" : "ready")}\n" +
              $"Belt item: {(turn.UsedBeltItem ? "used" : "ready (free)")}";
    }

    private static string MoveStatus(TurnState turn) =>
        turn.MovementLocked ? "done"
        : turn.MovementRoll is null ? "roll first"
        : $"{turn.MovementPoints} step{(turn.MovementPoints == 1 ? "" : "s")} left";

    private T Add<T>(T node) where T : Node
    {
        AddChild(node);
        return node;
    }
}
