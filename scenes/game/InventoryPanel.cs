using Godot;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

public enum CardAction
{
    Use,
    Equip,
    Belt,
    Stow,
    Drop,
}

/// <summary>Which card buttons make sense right now. The rules engine still has the final say.</summary>
public sealed record CardActions(bool Use, bool Equip, bool Belt, bool Stow, bool Drop)
{
    public static CardActions None { get; } = new(false, false, false, false, false);

    public bool Allows(CardAction action) => action switch
    {
        CardAction.Use => Use,
        CardAction.Equip => Equip,
        CardAction.Belt => Belt,
        CardAction.Stow => Stow,
        CardAction.Drop => Drop,
        _ => false,
    };
}

/// <summary>Top of the right panel: belt, backpack, the selected card's text and what you can do with it.</summary>
public partial class InventoryPanel : Control
{
    private readonly CardSlot[] _belt = new CardSlot[GameRules.BeltCapacity];
    private readonly CardSlot[] _backpack = new CardSlot[GameRules.BackpackCapacity];
    private readonly Dictionary<CardAction, Button> _actions = [];
    private Label _details = null!;

    public event Action<int>? CardSelected;

    public event Action<CardAction>? ActionRequested;

    public override void _Ready()
    {
        Size = new Vector2(176, 168);
        MouseFilter = MouseFilterEnum.Ignore;

        AddChild(UiTheme.Heading(UiTheme.Label(new(8, 4), new(160, 12), "BELT", color: Palette.Trim), 1));
        for (int i = 0; i < _belt.Length; i++)
        {
            _belt[i] = AddSlot(new Vector2(8 + i * 82, 16));
        }

        AddChild(UiTheme.Heading(UiTheme.Label(new(8, 36), new(160, 12), "BACKPACK", color: Palette.Trim), 1));
        for (int i = 0; i < _backpack.Length; i++)
        {
            _backpack[i] = AddSlot(new Vector2(8 + i % 2 * 82, 49 + i / 2 * 18));
        }

        _details = UiTheme.Label(new(8, 105), new(160, 40), color: Palette.Text);
        AddChild(_details);

        CardAction[] actions = Enum.GetValues<CardAction>();
        for (int i = 0; i < actions.Length; i++)
        {
            CardAction action = actions[i];
            Button button = UiTheme.Button(new(8 + i * 32, 147), new(30, 16), action.ToString());
            button.Pressed += () => ActionRequested?.Invoke(action);
            AddChild(button);
            _actions[action] = button;
        }
    }

    internal CardSlot BeltSlot(int index) => _belt[index];

    internal CardSlot BackpackSlot(int index) => _backpack[index];

    internal Button ActionButton(CardAction action) => _actions[action];

    public void ShowHero(Hero? hero, int? selectedCardId, CardActions actions)
    {
        for (int i = 0; i < _belt.Length; i++)
        {
            CardInstance? card = hero is not null && i < hero.Belt.Count ? hero.Belt[i] : null;
            _belt[i].ShowCard(card, "(empty)", card is not null && card.Id == selectedCardId);
        }

        for (int i = 0; i < _backpack.Length; i++)
        {
            CardInstance? card = hero is not null && i < hero.Backpack.Count ? hero.Backpack[i] : null;
            _backpack[i].ShowCard(card, "", card is not null && card.Id == selectedCardId);
        }

        CardInstance? selected = selectedCardId is int id ? hero?.FindCard(id)?.Card : null;
        _details.Text = selected is null
            ? "Select a card to see what it does."
            : $"{CardSlot.Title(selected)}: {selected.Definition.Text}";

        foreach ((CardAction action, Button button) in _actions)
        {
            button.Disabled = !actions.Allows(action);
        }
    }

    private CardSlot AddSlot(Vector2 position)
    {
        var slot = new CardSlot { Position = position, Size = new Vector2(78, 16) };
        slot.Pressed += () =>
        {
            if (slot.Card is { } card)
            {
                CardSelected?.Invoke(card.Id);
            }
        };
        AddChild(slot);
        return slot;
    }
}
