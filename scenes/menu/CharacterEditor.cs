using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Menu;

/// <summary>
/// Creates a character (name, portrait, point-buy) or edits one: rename, change portrait and
/// spend points earned since. Points already spent can't be taken back.
/// </summary>
public partial class CharacterEditor : MenuScreen
{
    private static readonly (StatKind Stat, string Name)[] StatRows =
    [
        (StatKind.Hearts, "Hearts"),
        (StatKind.Speed, "Speed"),
        (StatKind.AttackDice, "Attack"),
        (StatKind.DefenseDice, "Defense"),
        (StatKind.Luck, "Luck"),
    ];

    private readonly List<PortraitView> _portraits = [];
    private readonly Dictionary<StatKind, (Button Minus, Label Value, Button Plus)> _statControls = [];

    private StatAllocation _allocation = StatAllocation.None;
    private StatAllocation _floor = StatAllocation.None;
    private int _budget;
    private int _level;
    private string _portrait = Portraits.All[0].Id;
    private bool _saving;

    private Label _points = null!;
    private Label _error = null!;
    private PortraitView _preview = null!;
    private Label _previewName = null!;
    private HeartsView _previewHearts = null!;
    private Label _previewStats = null!;

    public ContentCatalog Content { get; init; } = null!;

    public ICharacterRepository Repository { get; init; } = null!;

    /// <summary>The character being edited, or null to create a new one.</summary>
    public Character? Editing { get; init; }

    /// <summary>Raised after saving or cancelling.</summary>
    public event Action? Closed;

    internal LineEdit NameEdit { get; private set; } = null!;

    internal Button SaveButton { get; private set; } = null!;

    internal int PointsLeft => _budget - PointBuy.Cost(_allocation);

    internal PortraitView PortraitButton(int index) => _portraits[index];

    internal Button PlusButton(StatKind stat) => _statControls[stat].Plus;

    internal Button MinusButton(StatKind stat) => _statControls[stat].Minus;

    public override void _Ready()
    {
        if (Editing is { } character)
        {
            _allocation = _floor = character.Allocation;
            _budget = PointBuy.Cost(character.Allocation) + character.UnspentPoints;
            _level = character.Level;
            _portrait = character.Portrait;
        }
        else
        {
            _budget = PointBuy.CreationPoints;
            _level = 1;
        }

        BuildFrame(Editing is null ? "NEW CHARACTER" : "EDIT CHARACTER");

        Caption(new Vector2(24, 48), "NAME");
        NameEdit = Add(new LineEdit
        {
            Position = new Vector2(24, 60),
            Size = new Vector2(200, 22),
            MaxLength = Character.MaxNameLength,
            PlaceholderText = "Enter a name",
            Text = Editing?.Name ?? "",
        });
        NameEdit.TextChanged += _ => Refresh();

        Caption(new Vector2(24, 92), "PORTRAIT");
        for (int i = 0; i < Portraits.All.Count; i++)
        {
            string id = Portraits.All[i].Id;
            PortraitView portrait = Add(new PortraitView { Clickable = true, Position = new Vector2(26 + i * 38, 106), Size = new Vector2(28, 34), TooltipText = Portraits.All[i].Name });
            portrait.PortraitId = id;
            portrait.Clicked += () =>
            {
                _portrait = id;
                Refresh();
            };
            _portraits.Add(portrait);
        }

        Caption(new Vector2(24, 152), "STATS");
        _points = Add(UiTheme.Label(new Vector2(120, 152), new Vector2(200, 12), color: Palette.TrimBright));
        for (int i = 0; i < StatRows.Length; i++)
        {
            (StatKind stat, string name) = StatRows[i];
            float y = 166 + i * 24;
            Add(UiTheme.Label(new Vector2(24, y + 4), new Vector2(60, 12), name));
            Add(UiTheme.Label(new Vector2(84, y + 4), new Vector2(60, 12), $"{PointBuy.CostPerStep(stat)} pt each", color: Palette.TextDim));
            Button minus = Add(UiTheme.Button(new Vector2(150, y), new Vector2(20, 20), "-"));
            Label value = Add(UiTheme.Label(new Vector2(172, y + 4), new Vector2(28, 12)));
            value.HorizontalAlignment = HorizontalAlignment.Center;
            Button plus = Add(UiTheme.Button(new Vector2(202, y), new Vector2(20, 20), "+"));
            minus.Pressed += () => Step(stat, -1);
            plus.Pressed += () => Step(stat, +1);
            _statControls[stat] = (minus, value, plus);
        }

        Add(new Panel { Position = new Vector2(356, 46), Size = new Vector2(262, 220), MouseFilter = MouseFilterEnum.Ignore });
        _preview = Add(new PortraitView { Position = new Vector2(370, 58), Size = new Vector2(44, 52) });
        _previewName = Add(UiTheme.Label(new Vector2(424, 60), new Vector2(186, 22), fontSize: 16, color: Palette.TrimBright));
        _previewHearts = Add(new HeartsView { Position = new Vector2(424, 88), Size = new Vector2(180, 9) });
        _previewStats = Add(UiTheme.Label(new Vector2(370, 120), new Vector2(240, 140)));

        _error = Add(UiTheme.Label(new Vector2(24, 296), new Vector2(320, 14), color: Palette.Danger));
        SaveButton = Add(UiTheme.Button(new Vector2(420, 316), new Vector2(96, 26), Editing is null ? "Create" : "Save"));
        SaveButton.Pressed += Save;
        Button cancel = Add(UiTheme.Button(new Vector2(522, 316), new Vector2(96, 26), "Cancel"));
        cancel.Pressed += () => Closed?.Invoke();

        Refresh();
        NameEdit.GrabFocus();
    }

    private void Step(StatKind stat, int delta)
    {
        StatAllocation next = _allocation.With(stat, _allocation[stat] + delta);
        if (next[stat] >= _floor[stat] && PointBuy.Check(next, _level, _budget) is null)
        {
            _allocation = next;
            Refresh();
        }
    }

    private void Refresh()
    {
        _points.Text = $"{PointsLeft} of {_budget} points left";
        foreach ((StatKind stat, (Button minus, Label value, Button plus)) in _statControls)
        {
            value.Text = _allocation[stat] > 0 ? $"+{_allocation[stat]}" : "0";
            minus.Disabled = _allocation[stat] <= _floor[stat];
            plus.Disabled = !PointBuy.CanRaise(_allocation, stat, _level, _budget);
        }

        foreach (PortraitView portrait in _portraits)
        {
            portrait.Selected = portrait.PortraitId == _portrait;
        }

        string name = NameEdit.Text.Trim();
        string? nameError = Character.CheckName(name);
        _error.Text = name.Length == 0 ? "" : nameError ?? "";
        SaveButton.Disabled = _saving || nameError is not null;

        Stats stats = PointBuy.ToStats(_allocation);
        string speed = stats.Speed >= 0 ? $"+{stats.Speed}" : stats.Speed.ToString();
        _preview.PortraitId = _portrait;
        _previewName.Text = name.Length > 0 ? name : "Unnamed";
        _previewHearts.ShowHearts(stats.Hearts, stats.Hearts);
        string gear = Editing is null
            ? "Starts with: " + string.Join(", ", GameRules.StarterKit.Select(id => Content.GetCard(id).Name))
            : $"Level {_level}";
        _previewStats.Text =
            $"Move {stats.MoveDice}d6{speed}   Luck {UiTheme.Count(stats.Luck, "reroll", "rerolls")}\n" +
            $"Attack {UiTheme.Count(stats.AttackDice, "die", "dice")}   Defense {UiTheme.Count(stats.DefenseDice, "die", "dice")}\n" +
            "(before gear bonuses)\n\n" +
            gear + "\n\n" +
            (_level <= 1 ? $"At level 1 you can buy at most {PointBuy.LevelOneDiceLimit} extra Attack and Defense die. Unspent points are kept." : "");
    }

    private async void Save()
    {
        if (_saving)
        {
            return;
        }

        _saving = true;
        Refresh();
        try
        {
            Character character;
            if (Editing is null)
            {
                character = Character.Create(NameEdit.Text, _portrait, _allocation, Content, DateTimeOffset.UtcNow);
            }
            else
            {
                character = Editing;
                character.Rename(NameEdit.Text);
                character.ChangePortrait(_portrait);
                character.Allocate(_allocation);
            }

            await Repository.SaveAsync(character);
            Closed?.Invoke();
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            _error.Text = exception.Message;
            _saving = false;
            Refresh();
        }
    }
}
