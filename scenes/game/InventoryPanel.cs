using Godot;
using Woad.Core.Content;
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
    Carry,
    Drop,
}

/// <summary>
/// What each card button would do right now: <see cref="Blocked"/> holds why an action can't be
/// taken (from the rules engine's own checks), and <see cref="Hints"/> what an allowed one costs.
/// </summary>
public sealed record CardActions(IReadOnlyDictionary<CardAction, string> Blocked, IReadOnlyDictionary<CardAction, string> Hints)
{
    public static CardActions None { get; } = Unavailable("Select a card first.");

    public static CardActions Unavailable(string reason) =>
        new(Enum.GetValues<CardAction>().ToDictionary(action => action, _ => reason), new Dictionary<CardAction, string>());

    public bool Allows(CardAction action) => !Blocked.ContainsKey(action);

    public string Explain(CardAction action) =>
        Blocked.TryGetValue(action, out string? reason) ? reason : Hints.GetValueOrDefault(action, "");
}

/// <summary>
/// The questions the game asks in the right panel (which hand, aid or carry, drop it here?), each a line and its
/// buttons. A picked-up card's text and its action buttons are in the item slide-out (<see cref="Slideout"/>).
/// </summary>
public partial class InventoryPanel : Control
{
    private Panel _handChoice = null!;
    private Label _handQuestion = null!;
    private readonly List<Button> _hands = [];

    /// <summary>The item slide-out, where a picked-up card's text and buttons are.</summary>
    public ItemSlideout Slideout { get; init; } = null!;

    /// <summary>The card the hand choice is for, while it's showing.</summary>
    private int? _choosingFor;

    /// <summary>What each button of a general choice does (see <see cref="AskChoice"/>), while one is showing.</summary>
    private IReadOnlyList<Action>? _choices;

    public event Action<CardAction>? ActionRequested;

    /// <summary>A question came up, or went away: the panel around it makes room.</summary>
    public event Action? QuestionChanged;

    /// <summary>A greyed-out button was clicked; the game says why it's unavailable.</summary>
    public event Action<CardAction>? BlockedActionClicked;

    /// <summary>A hand was picked for a card that goes in either (the Boomerang).</summary>
    public event Action<int, EquipSlot>? HandChosen;

    public override void _Ready()
    {
        Size = new Vector2(176, 66);
        MouseFilter = MouseFilterEnum.Ignore;
        Slideout.ActionRequested += action => ActionRequested?.Invoke(action);
        Slideout.BlockedActionClicked += action => BlockedActionClicked?.Invoke(action);

        // Asks which hand a card that goes in either (the Boomerang) is equipped into, over its text.
        _handChoice = new Panel { Position = new Vector2(4, 0), Size = new Vector2(168, 72), Visible = false };
        _handQuestion = UiTheme.Label(new Vector2(6, 2), new Vector2(156, 26), color: Palette.TrimBright);
        _handQuestion.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _handQuestion.VerticalAlignment = VerticalAlignment.Center;
        _handChoice.AddChild(_handQuestion);
        for (int i = 0; i < 2; i++)
        {
            Button hand = UiTheme.Button(new Vector2(4, 30 + i * 20), new Vector2(160, 18), "");
            int choice = i;
            hand.Pressed += () =>
            {
                if (_choices is { } choices)
                {
                    _handChoice.Visible = false;
                    QuestionChanged?.Invoke();
                    _choosingFor = null;
                    _choices = null;
                    choices[choice]();
                }
                else if (_choosingFor is int cardId && hand.GetMeta("slot").AsInt32() is var slot)
                {
                    _handChoice.Visible = false;
                    QuestionChanged?.Invoke();
                    _choosingFor = null;
                    HandChosen?.Invoke(cardId, (EquipSlot)slot);
                }
            };
            _handChoice.AddChild(hand);
            _hands.Add(hand);
        }

        AddChild(_handChoice);
    }

    internal Button HandButton(int index) => _hands[index];

    /// <summary>Widened with the right panel: the question and its buttons run its width.</summary>
    public void Widen(float width)
    {
        _handChoice.Size = new Vector2(width - 8, 72);
        _handQuestion.Size = new Vector2(width - 20, 26);
        foreach (Button hand in _hands)
        {
            hand.Size = new Vector2(width - 16, 18);
        }
    }

    internal bool IsChoosingHand => _handChoice.Visible;

    /// <summary>Asks what to do with a card, two ways (use it, or draw it into a hand): each button does its own.</summary>
    public void AskChoice(int? cardId, string question, IReadOnlyList<(string Text, Action Then)> choices)
    {
        _choosingFor = cardId;
        _choices = [.. choices.Select(choice => choice.Then)];
        _handQuestion.Text = question;
        for (int i = 0; i < _hands.Count; i++)
        {
            _hands[i].Visible = i < choices.Count;
            if (i < choices.Count)
            {
                _hands[i].Text = choices[i].Text;
                _hands[i].Disabled = false;
                _hands[i].TooltipText = "";
            }
        }

        _handChoice.Visible = true;
        QuestionChanged?.Invoke();
    }

    /// <summary>Asks which hand to equip a card into: each choice says what it would stow, or why it can't be.</summary>
    public void AskHand(int cardId, string cardName, IReadOnlyList<(EquipSlot Slot, string Text, string? Blocked)> hands)
    {
        _choosingFor = cardId;
        _choices = null;
        _handQuestion.Text = hands.All(hand => hand.Slot is EquipSlot.MainHand or EquipSlot.OffHand) ? $"Which hand for the {cardName}?" : $"Where to wear the {cardName}?";
        for (int i = 0; i < _hands.Count; i++)
        {
            Button button = _hands[i];
            button.Visible = i < hands.Count;
            if (i < hands.Count)
            {
                (EquipSlot slot, string text, string? blocked) = hands[i];
                button.SetMeta("slot", (int)slot);
                button.Text = text;
                button.Disabled = blocked is not null;
                button.TooltipText = blocked ?? "";
            }
        }

        _handChoice.Visible = true;
        QuestionChanged?.Invoke();
    }

    internal Button ActionButton(CardAction action) => Slideout.Actions[action];

    public void ShowHero(Hero? hero, int? selectedCardId, CardActions actions)
    {
        if (_choosingFor != selectedCardId && _handChoice.Visible)
        {
            _handChoice.Visible = false;
            QuestionChanged?.Invoke();
            _choosingFor = null;
            _choices = null;
        }

        CardInstance? selected = selectedCardId is int id ? hero?.FindCard(id)?.Card : null;
        Slideout.ShowCard(selected, hero, actions);
    }
}
