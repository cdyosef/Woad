using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Menu;

/// <summary>
/// Creates a character (name, look and colors, crest, point-buy) or edits one: rename, restyle and
/// spend points earned since. Points already spent can't be taken back.
/// </summary>
public partial class CharacterEditor : MenuScreen
{
    private static readonly (StatKind Stat, string Name, string Icon)[] StatRows =
    [
        (StatKind.Hearts, "Hearts", "heart_full"),
        (StatKind.Speed, "Wend", "icon_wend"),
        (StatKind.AttackDice, "Rumble", "icon_rumble"),
        (StatKind.DefenseDice, "Defense", "icon_defense"),
        (StatKind.OdHandDice, "Od Hand", "icon_odhand"),
        (StatKind.Luck, "Luck", "icon_luck"),
    ];

    private readonly List<PortraitView> _portraits = [];
    private readonly Dictionary<StatKind, (Button Minus, Label Value, Button Plus)> _statControls = [];
    private readonly List<(Label Caption, Button Previous, ColorRect Swatch, Label Value, Button Next)> _styleRows = [];

    private StatAllocation _allocation = StatAllocation.None;
    private StatAllocation _floor = StatAllocation.None;

    /// <summary>The stat whose row the mouse is over: its explanation shows in the preview panel.</summary>
    private StatKind? _explaining;

    /// <summary>What each stat does, for the hover help.</summary>
    private static readonly Dictionary<StatKind, string> StatHelp = new()
    {
        [StatKind.Hearts] = "HEARTS\nHow much punishment you can take: each hit that gets through costs one. With none left you Keel Over and roll the Keel Die each turn until you stand back up, or die. Each point buys another heart.",
        [StatKind.Speed] = "WEND\nHow far you move. Each move rolls one or more Wend dice for that many steps, plus your Wend bonus once a turn. Each point adds +1 to your Wend bonus.",
        [StatKind.AttackDice] = "RUMBLE\nYour attack dice. Each attack or shove uses one or more, and your weapon adds to one attack a turn. Beat the defender's roll to hit. Each point buys another die.",
        [StatKind.DefenseDice] = "DEFENSE\nThe dice you roll, all of them, whenever you're attacked; your armor adds to each roll. Tie or beat the attack to block it. Each point buys another die.",
        [StatKind.OdHandDice] = "OD HAND\nYour dice for picking locks, searching, disarming traps, stealing and rousing allies who've Keeled Over. Lockpicks help with locks. Each point buys another die.",
        [StatKind.Luck] = "LUCK\nRerolls of a single die, refilled on each new floor: after any roll, click a die to roll it again. Each point buys another reroll.",
    };
    private int _budget;
    private int _level;
    private string _portrait = Portraits.All[0].Id;
    private bool _saving;
    private bool _isCompanion;
    private string _look = "";
    private string _main = "";
    private string _trim = "";
    private string _skin = "";
    private string _hair = "";

    private Label _points = null!;
    private Label _error = null!;
    private PortraitView _preview = null!;
    private FigureView _figure = null!;
    private Label _previewName = null!;
    private HeartsView _previewHearts = null!;
    private Label _previewStats = null!;
    private Button _heroType = null!;
    private Button _companionType = null!;

    public ContentCatalog Content { get; init; } = null!;

    public ICharacterRepository Repository { get; init; } = null!;

    /// <summary>The character being edited, or null to create a new one.</summary>
    public Character? Editing { get; init; }

    /// <summary>Raised after saving or cancelling.</summary>
    public event Action? Closed;

    internal LineEdit NameEdit { get; private set; } = null!;

    internal Button SaveButton { get; private set; } = null!;

    internal Button CompanionButton => _companionType;

    internal int PointsLeft => _budget - PointBuy.Cost(_allocation);

    /// <summary>The preview panel's text: the character's summary, or the help for the stat under the mouse.</summary>
    internal string PreviewText => _previewStats.Text;

    internal PortraitView PortraitButton(int index) => _portraits[index];

    internal Button PlusButton(StatKind stat) => _statControls[stat].Plus;

    internal Button MinusButton(StatKind stat) => _statControls[stat].Minus;

    /// <summary>The arrow buttons of the look rows: 0 look, 1 main color, 2 trim, 3 skin, 4 hair.</summary>
    internal Button StyleButton(int row, bool next) => next ? _styleRows[row].Next : _styleRows[row].Previous;

    internal Button RandomButton { get; private set; } = null!;

    internal Appearance Appearance => new(_look, _main, _trim, _skin, _hair);

    public override void _Ready()
    {
        if (Editing is { } character)
        {
            _allocation = _floor = character.Allocation;
            _budget = PointBuy.Cost(character.Allocation) + character.UnspentPoints;
            _level = character.Level;
            _portrait = character.Portrait;
            (_look, _main, _trim, _skin, _hair) = character.Appearance;
        }
        else
        {
            _budget = PointBuy.CreationPoints;
            _level = 1;
        }

        _look = Looks.Get(_look).Id;
        _main = Looks.MainId(new Appearance(Main: _main), _portrait);
        _trim = Looks.Find(Looks.Colors, _trim)?.Id ?? Looks.DefaultTrim;

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

        // A new character is a hero or a companion for good; an existing one just says which.
        _heroType = Add(TypeButton(new Vector2(232, 60), 50, "Hero", companion: false));
        _companionType = Add(TypeButton(new Vector2(286, 60), 64, "Companion", companion: true));
        _companionType.TooltipText = "Companions join a player's party. The game plays them, following that player's orders.";
        _isCompanion = Editing?.IsCompanion ?? false;

        Caption(new Vector2(24, 92), "CREST");
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
            (StatKind stat, string name, string icon) = StatRows[i];
            float y = 164 + i * 21;
            // The stat's symbol and name; hovering them explains the stat in the preview panel.
            Control hover = Add(new Control { Position = new Vector2(22, y + 2), Size = new Vector2(60, 16), MouseFilter = MouseFilterEnum.Stop, MouseDefaultCursorShape = CursorShape.Help });
            hover.AddChild(new TextureRect
            {
                Texture = GameArt.Ui(icon),
                Position = new Vector2(2, 2),
                Size = new Vector2(12, 12),
                StretchMode = TextureRect.StretchModeEnum.KeepCentered,
                TextureFilter = TextureFilterEnum.Nearest,
                MouseFilter = MouseFilterEnum.Ignore,
            });
            Label label = UiTheme.Label(new Vector2(17, 2), new Vector2(43, 12), name);
            label.MouseFilter = MouseFilterEnum.Ignore;
            hover.AddChild(label);
            hover.MouseEntered += () => Explain(stat);
            hover.MouseExited += () => Explain(null);
            Add(UiTheme.Label(new Vector2(84, y + 4), new Vector2(60, 12), $"{PointBuy.CostPerStep(stat)} pt each", color: Palette.TextDim));
            Button minus = Add(UiTheme.Button(new Vector2(150, y), new Vector2(20, 19), "-"));
            Label value = Add(UiTheme.Label(new Vector2(172, y + 4), new Vector2(28, 12)));
            value.HorizontalAlignment = HorizontalAlignment.Center;
            Button plus = Add(UiTheme.Button(new Vector2(202, y), new Vector2(20, 19), "+"));
            minus.Pressed += () => Step(stat, -1);
            plus.Pressed += () => Step(stat, +1);
            _statControls[stat] = (minus, value, plus);
        }

        Add(new Panel { Position = new Vector2(356, 46), Size = new Vector2(262, 262), MouseFilter = MouseFilterEnum.Ignore });
        _figure = Add(new FigureView { Position = new Vector2(364, 50), Size = new Vector2(48, 48) });
        _preview = Add(new PortraitView { Position = new Vector2(418, 58), Size = new Vector2(24, 29) });
        _previewName = Add(UiTheme.Label(new Vector2(450, 56), new Vector2(162, 22), fontSize: 16, color: Palette.TrimBright));
        _previewHearts = Add(new HeartsView { Position = new Vector2(450, 82), Size = new Vector2(160, 9) });
        RandomButton = Add(UiTheme.Button(new Vector2(562, 102), new Vector2(50, 19), "Random"));
        RandomButton.TooltipText = "A random look and colors.";
        RandomButton.Pressed += Randomize;
        for (int row = 0; row < 5; row++)
        {
            float y = 102 + row * 21;
            Label caption = Add(UiTheme.Label(new Vector2(366, y + 4), new Vector2(44, 12)));
            int step = row;
            Button previous = Add(UiTheme.Button(new Vector2(410, y), new Vector2(20, 19), "<"));
            previous.Pressed += () => StepStyle(step, -1);
            ColorRect swatch = Add(new ColorRect { Position = new Vector2(436, y + 4), Size = new Vector2(11, 11), MouseFilter = MouseFilterEnum.Ignore });
            Label value = Add(UiTheme.Label(new Vector2(452, y + 4), new Vector2(82, 12)));
            Button next = Add(UiTheme.Button(new Vector2(536, y), new Vector2(20, 19), ">"));
            next.Pressed += () => StepStyle(step, +1);
            _styleRows.Add((caption, previous, swatch, value, next));
        }

        _previewStats = Add(UiTheme.Label(new Vector2(366, 210), new Vector2(246, 96)));

        _error = Add(UiTheme.Label(new Vector2(24, 298), new Vector2(320, 14), color: Palette.Danger));
        SaveButton = Add(UiTheme.Button(new Vector2(420, 316), new Vector2(96, 26), Editing is null ? "Create" : "Save"));
        SaveButton.Pressed += Save;
        Button cancel = Add(UiTheme.Button(new Vector2(522, 316), new Vector2(96, 26), "Cancel"));
        cancel.Pressed += () => Closed?.Invoke();

        Refresh();
        NameEdit.GrabFocus();
    }

    private Button TypeButton(Vector2 position, float width, string text, bool companion)
    {
        Button button = UiTheme.Button(position, new Vector2(width, 22), text);
        button.ToggleMode = true;
        button.Pressed += () =>
        {
            _isCompanion = companion;
            Refresh();
        };
        return button;
    }

    private void Explain(StatKind? stat)
    {
        _explaining = stat;
        Refresh();
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

    /// <summary>Steps a look row: the look itself, or one of its colors (wrapping around).</summary>
    private void StepStyle(int row, int delta)
    {
        Look look = Looks.Get(_look);
        switch (row)
        {
            case 0:
                _look = Step(Looks.All.Select(each => each.Id).ToList(), _look, delta);
                break;
            case 1:
                _main = Cycle(Looks.Colors, _main, null, delta);
                break;
            case 2:
                _trim = Cycle(Looks.Colors, _trim, null, delta);
                break;
            case 3:
                _skin = Cycle(Looks.SetSwatches(look.SkinSet), _skin, look.SkinDefault, delta);
                break;
            default:
                _hair = Cycle(Looks.SetSwatches(look.HairSet), _hair, look.HairDefault, delta);
                break;
        }

        Refresh();

        // Steps from the swatch shown for the current choice (which may be the look's starting color).
        static string Cycle(IReadOnlyList<Swatch> swatches, string current, string? fallback, int delta) =>
            Looks.Shown(swatches, current, fallback) is { } shown ? Step(swatches.Select(swatch => swatch.Id).ToList(), shown.Id, delta) : current;

        static string Step(IReadOnlyList<string> ids, string current, int delta)
        {
            int index = Math.Max(0, ids.ToList().IndexOf(current));
            return ids[((index + delta) % ids.Count + ids.Count) % ids.Count];
        }
    }

    private void Randomize()
    {
        Look look = Looks.All[Random.Shared.Next(Looks.All.Count)];
        _look = look.Id;
        _main = Looks.Colors[Random.Shared.Next(Looks.Colors.Count)].Id;
        _trim = Looks.Colors.Where(swatch => swatch.Id != _main).ElementAt(Random.Shared.Next(Looks.Colors.Count - 1)).Id;
        IReadOnlyList<Swatch> skins = Looks.SetSwatches(look.SkinSet);
        IReadOnlyList<Swatch> hairs = Looks.SetSwatches(look.HairSet);
        _skin = skins.Count > 0 ? skins[Random.Shared.Next(skins.Count)].Id : _skin;
        _hair = hairs.Count > 0 ? hairs[Random.Shared.Next(hairs.Count)].Id : _hair;
        Refresh();
    }

    private void RefreshStyle()
    {
        LookColors colors = Looks.Resolve(Appearance, _portrait);
        Look look = colors.Look;
        _figure.Colors = colors;
        _preview.Colors = colors;
        foreach (PortraitView portrait in _portraits)
        {
            portrait.Colors = colors;
        }

        ShowRow(0, "Look", null, look.Name, enabled: true);
        ShowRow(1, "Main", colors.Main, Looks.Find(Looks.Colors, _main)!.Name, enabled: true);
        ShowRow(2, "Trim", colors.Trim, Looks.Find(Looks.Colors, _trim)!.Name, enabled: true);
        ShowSet(3, look.SkinSet, "Skin", _skin, look.SkinDefault, colors.Skin);
        ShowSet(4, look.HairSet, "Hair", _hair, look.HairDefault, colors.Hair);

        void ShowSet(int row, string? set, string fallbackCaption, string id, string? fallback, Color color)
        {
            if (Looks.Shown(Looks.SetSwatches(set), id, fallback) is not { } shown)
            {
                ShowRow(row, fallbackCaption, null, "-", enabled: false);
                return;
            }

            ShowRow(row, Looks.Sets[set!].Name, color, shown.Name, enabled: true);
        }

        void ShowRow(int row, string caption, Color? color, string value, bool enabled)
        {
            (Label label, Button previous, ColorRect swatch, Label shown, Button next) = _styleRows[row];
            label.Text = caption;
            label.Modulate = enabled ? Colors.White : new Color(1, 1, 1, 0.4f);
            previous.Disabled = next.Disabled = !enabled;
            swatch.Visible = color is not null;
            swatch.Color = color ?? Colors.Transparent;
            shown.Text = value;
            shown.Position = new Vector2(color is null ? 438 : 452, shown.Position.Y);
        }
    }

    private void Refresh()
    {
        RefreshStyle();
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

        _heroType.SetPressedNoSignal(!_isCompanion);
        _companionType.SetPressedNoSignal(_isCompanion);
        _heroType.Disabled = _companionType.Disabled = Editing is not null;

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
            $"Wend {stats.MoveDice}d6{speed}   Luck {UiTheme.Count(stats.Luck, "reroll", "rerolls")}\n" +
            $"Rumble {UiTheme.Count(stats.AttackDice, "die", "dice")}   Defense {UiTheme.Count(stats.DefenseDice, "die", "dice")}\n" +
            $"Od Hand {UiTheme.Count(stats.OdHandDice, "die", "dice")}   (before gear bonuses)\n" +
            (_isCompanion ? "A companion: joins a player's party and follows their orders.\n" : "") +
            gear;
        _points.TooltipText = _level <= 1
            ? $"At level 1 you can buy at most {PointBuy.LevelOneDiceLimit} extra Rumble, Defense and Od Hand die each. Unspent points are kept."
            : "Unspent points are kept.";
        if (_explaining is { } explained)
        {
            _previewStats.Text = StatHelp[explained];
        }
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
                character = Character.Create(NameEdit.Text, _portrait, _allocation, Content, DateTimeOffset.UtcNow, _isCompanion, Appearance);
            }
            else
            {
                character = Editing;
                character.Rename(NameEdit.Text);
                character.ChangePortrait(_portrait);
                character.ChangeAppearance(Appearance);
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
