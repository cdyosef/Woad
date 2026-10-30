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
        (StatKind.MoveDice, "Wend", "icon_wend"),
        (StatKind.Speed, "Wend +", "icon_wend"),
        (StatKind.AttackDice, "Rumble", "icon_rumble"),
        (StatKind.DefenseDice, "Defense", "icon_defense"),
        (StatKind.OdHandDice, "Od Hand", "icon_odhand"),
        (StatKind.Luck, "Luck", "icon_luck"),
        (StatKind.Magic, "Magic", "icon_magic"),
    ];

    private readonly List<PortraitView> _portraits = [];
    private readonly Dictionary<StatKind, (Button Minus, Label Value, Button Plus)> _statControls = [];
    private readonly Dictionary<StatKind, Label> _costLabels = [];
    private readonly Dictionary<Skill, (Label Note, Button Toggle)> _skillRows = [];
    private Panel _skillsPanel = null!;

    /// <summary>What each skill does, in the Skills window.</summary>
    private static readonly Dictionary<Skill, string> SkillHelp = new()
    {
        [Skill.Search] = "Od Hand: search the room for secrets",
        [Skill.Steal] = "Od Hand: steal from someone beside you",
        [Skill.PickLock] = "Od Hand: pick locks on doors, chests, gates",
        [Skill.Disarm] = "Od Hand: disarm traps and lit bombs",
        [Skill.Aid] = "Od Hand: rouse the fallen, cure what ails",
        [Skill.Parry] = "Od Hand: a held die for your next defense",
        [Skill.Shove] = "Rumble: shove someone a tile",
        [Skill.BullRush] = "Rumble: charge in a line, attack, push 2",
        [Skill.Guard] = "Rumble: a held die for your or an ally's defense",
        [Skill.HighAlert] = "Rumble: a Rumble die on your next search",
    };
    private readonly List<(Label Caption, Button Previous, ColorRect Swatch, Label Value, Button Next)> _styleRows = [];

    private StatAllocation _allocation = StatAllocation.None;
    private StatAllocation _floor = StatAllocation.None;

    /// <summary>The stat whose row the mouse is over: its explanation shows in the preview panel.</summary>
    private StatKind? _explaining;

    /// <summary>What each stat does, for the hover help.</summary>
    private static readonly Dictionary<StatKind, string> StatHelp = new()
    {
        [StatKind.Hearts] = "HEARTS\nHow much punishment you can take: each hit that gets through costs one. With none left you Keel Over and roll the Keel Die each turn until you stand back up, or die. Each point buys another heart.",
        [StatKind.MoveDice] = "WEND DICE\nHow far you move. Each move rolls one or more Wend dice for that many steps. Each one bought costs more after the third: 3, 3, 3, then 4, 5, 6.",
        [StatKind.Speed] = "WEND BONUS\nAdded to one move a turn, on top of your Wend dice. Each point adds +1.",
        [StatKind.AttackDice] = "RUMBLE\nYour attack dice. Each attack or shove uses one or more, and your weapon adds to one attack a turn. Beat the defender's roll to hit. Dice cost 3, 3, 3, then 4, 5, 6; the first brings Shove free (or Bull Rush, if you know Shove).",
        [StatKind.DefenseDice] = "DEFENSE\nYour base die and armor defend against every attack. Each die bought rolls in only when a defense is losing, then it's spent until your next turn. Dice cost 3, 3, 3, then 4, 5, 6.",
        [StatKind.OdHandDice] = "OD HAND\nDice for the Od Hand skills you know: Search, Steal, Pick Lock, Disarm, Aid, Parry. Without a skill, a tool can stand in, rolling only its own dice. The first die costs 1 and brings a skill free; then 2, 2, 3, 4, 5. More skills: 1 point each.",
        [StatKind.Luck] = "LUCK\nRerolls of a single die, refilled on each new floor: after any roll, click a die to roll it again. Each point buys another reroll.",
        [StatKind.Magic] = "MAGIC\nEach rank teaches a basic spell (you pick it) and holds another banked Power Die. Spells cost Power to cast, take your Rumble dice, and add your rank to their roll. Magic is dangerous, and people don't like it.",
    };

    /// <summary>The basic spells picked for the ranks of Magic bought here, in order.</summary>
    private readonly List<string> _newSpells = [];

    private SpellPicker _spellPicker = null!;
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

        Caption(new Vector2(24, 150), "STATS");
        _points = Add(UiTheme.Label(new Vector2(84, 150), new Vector2(150, 12), color: Palette.TrimBright));
        Button skills = Add(UiTheme.CompactButton(new Vector2(234, 147), new Vector2(110, 16), "Skills..."));
        skills.Name = "Skills";
        skills.Pressed += () =>
        {
            _skillsPanel.Visible = !_skillsPanel.Visible;
            Refresh();
        };
        for (int i = 0; i < StatRows.Length; i++)
        {
            (StatKind stat, string name, string icon) = StatRows[i];
            float y = 164 + i * 17;
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
            _costLabels[stat] = Add(UiTheme.Label(new Vector2(84, y + 3), new Vector2(64, 12), "", color: Palette.TextDim));
            Button minus = Add(UiTheme.Button(new Vector2(150, y), new Vector2(20, 17), "-"));
            Label value = Add(UiTheme.Label(new Vector2(172, y + 3), new Vector2(28, 12)));
            value.HorizontalAlignment = HorizontalAlignment.Center;
            Button plus = Add(UiTheme.Button(new Vector2(202, y), new Vector2(20, 17), "+"));
            minus.Pressed += () => Step(stat, -1);
            plus.Pressed += () => Step(stat, +1);
            _statControls[stat] = (minus, value, plus);
        }

        Add(new Panel { Position = new Vector2(356, 46), Size = new Vector2(262, 262), MouseFilter = MouseFilterEnum.Ignore });
        _figure = Add(new FigureView { Position = new Vector2(364, 50), Size = new Vector2(48, 48) });
        _preview = Add(new PortraitView { Position = new Vector2(418, 58), Size = new Vector2(24, 29) });
        _previewName = Add(UiTheme.Label(new Vector2(450, 56), new Vector2(162, 22), fontSize: UiTheme.HeadingSize, color: Palette.TrimBright));
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

        // A new rank of Magic asks which basic spell it teaches.
        _spellPicker = Add(new SpellPicker { Position = new Vector2(196, 80) });
        _spellPicker.Picked += spell =>
        {
            _newSpells.Add(spell);
            _allocation = _allocation.With(StatKind.Magic, _allocation.Magic + 1);
            Refresh();
        };

        BuildSkillsPanel();

        _error = Add(UiTheme.Label(new Vector2(24, 302), new Vector2(320, 14), color: Palette.Danger));
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

    /// <summary>The Skills window: each skill, what it does and costs, and a button to learn it (or forget one learned here).</summary>
    private void BuildSkillsPanel()
    {
        _skillsPanel = Add(new Panel { Position = new Vector2(150, 44), Size = new Vector2(340, 254), Visible = false });
        _skillsPanel.AddChild(UiTheme.Heading(UiTheme.Label(new Vector2(10, 6), new Vector2(200, 16), "SKILLS", 9, Palette.TrimBright), 1));
        _skillsPanel.AddChild(UiTheme.Label(new Vector2(70, 8), new Vector2(200, 12), "1 point each, some free", color: Palette.TextDim));
        Button close = UiTheme.CompactButton(new Vector2(284, 6), new Vector2(48, 16), "Done");
        close.Pressed += () =>
        {
            _skillsPanel.Visible = false;
            Refresh();
        };
        _skillsPanel.AddChild(close);
        for (int i = 0; i < Skills.Each.Count; i++)
        {
            Skill skill = Skills.Each[i];
            float y = 28 + i * 22;
            _skillsPanel.AddChild(UiTheme.Label(new Vector2(10, y + 3), new Vector2(68, 12), skill.Name(), color: Skills.OdHand.HasFlag(skill) ? Palette.DieOdHand : Palette.DieRumble));
            Label note = UiTheme.Label(new Vector2(78, y + 3), new Vector2(190, 12), SkillHelp[skill], color: Palette.TextDim);
            _skillsPanel.AddChild(note);
            Button toggle = UiTheme.CompactButton(new Vector2(270, y), new Vector2(62, 18), "");
            toggle.Name = $"Skill{skill}";
            toggle.Pressed += () => ToggleSkill(skill);
            _skillsPanel.AddChild(toggle);
            _skillRows[skill] = (note, toggle);
        }
    }

    /// <summary>For the click tests: a skill's Learn button in the Skills window.</summary>
    internal Button SkillButton(Skill skill) => _skillRows[skill].Toggle;

    internal Panel SkillsPanel => _skillsPanel;

    internal StatAllocation Allocation => _allocation;

    private void ToggleSkill(Skill skill)
    {
        if (_allocation.Skills.HasFlag(skill))
        {
            StatAllocation forgotten = _allocation.WithSkill(skill, false);
            if (!_floor.Skills.HasFlag(skill) && PointBuy.Check(forgotten, _level, _budget) is null)
            {
                _allocation = forgotten;
            }
        }
        else if (PointBuy.CanLearn(_allocation, skill, _level, _budget))
        {
            _allocation = _allocation.WithSkill(skill, true);
        }

        Refresh();
    }

    private void Step(StatKind stat, int delta)
    {
        StatAllocation next = delta > 0 ? PointBuy.Raise(_allocation, stat) : Lower(_allocation, stat);
        if (next[stat] < _floor[stat] || PointBuy.Check(next, _level, _budget) is not null)
        {
            return;
        }

        // The first Od Hand die brings a skill of your choice, free: the Skills window opens to pick it.
        if (stat == StatKind.OdHandDice && delta > 0 && next.OdHandDice == 1 && (next.Skills & Skills.OdHand) == 0)
        {
            _skillsPanel.Visible = true;
        }

        if (stat == StatKind.Magic && delta > 0)
        {
            _spellPicker.Open("PICK A SPELL", Spellbook.Learnable([.. Editing?.Spells ?? [], .. _newSpells], Content)
                .Select(spell => (spell, spell.Name, (string?)null)));
            return;
        }

        if (stat == StatKind.Magic)
        {
            _newSpells.RemoveAt(_newSpells.Count - 1);
        }

        _allocation = next;
        Refresh();
    }

    /// <summary>
    /// One step less of a stat, taking back what came free with it: the last Rumble die bought takes its
    /// free Shove (or Bull Rush) with it, and the last Od Hand die the Od Hand skills learned here.
    /// </summary>
    private StatAllocation Lower(StatAllocation allocation, StatKind stat)
    {
        StatAllocation lowered = allocation.With(stat, allocation[stat] - 1);
        if (stat == StatKind.AttackDice && lowered.AttackDice == 0)
        {
            Skill free = lowered.Skills.HasFlag(Skill.BullRush) ? Skill.BullRush : Skill.Shove;
            lowered = _floor.Skills.HasFlag(free) ? lowered : lowered.WithSkill(free, false);
        }

        if (stat == StatKind.OdHandDice && lowered.OdHandDice == 0)
        {
            lowered = lowered with { Skills = lowered.Skills & ~(Skills.OdHand & ~_floor.Skills) };
        }

        return lowered;
    }

    /// <summary>For the click tests: the window that asks which spell a new rank of Magic teaches.</summary>
    internal SpellPicker SpellPicker => _spellPicker;

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
            int cost = PointBuy.Cost(PointBuy.Raise(_allocation, stat)) - PointBuy.Cost(_allocation);
            _costLabels[stat].Text = $"next: {cost} pt";
        }

        foreach ((Skill skill, (Label note, Button toggle)) in _skillRows)
        {
            bool known = _allocation.Skills.HasFlag(skill);
            int cost = PointBuy.Cost(_allocation.WithSkill(skill, true)) - PointBuy.Cost(_allocation);
            toggle.Text = known ? (_floor.Skills.HasFlag(skill) ? "Known" : "Forget") : cost == 0 ? "Learn free" : $"Learn {cost}";
            toggle.Disabled = known ? _floor.Skills.HasFlag(skill) : !PointBuy.CanLearn(_allocation, skill, _level, _budget);
            toggle.TooltipText = known || PointBuy.Check(_allocation.WithSkill(skill, true), _level, _budget) is not { } why ? "" : why;
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
            $"Skills: {(_allocation.Skills == Skill.None ? "none yet" : string.Join(", ", Skills.In(_allocation.Skills).Select(skill => skill.Name())))}\n" +
            (stats.Magic > 0 ? $"Magic {stats.Magic}: {string.Join(", ", (Editing?.Spells ?? []).Concat(_newSpells).Select(id => Content.GetSpell(id).Name))}\n" : "") +
            (_isCompanion ? "A companion: joins a player's party and follows their orders.\n" : "") +
            gear;
        _points.TooltipText = _level <= 1
            ? $"At level 1 you can buy at most {PointBuy.LevelOneDiceLimit} extra Rumble, Wend, Defense and Od Hand dice each. Unspent points are kept."
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
                character = Character.Create(NameEdit.Text, _portrait, _allocation, Content, DateTimeOffset.UtcNow, _isCompanion, Appearance, _newSpells);
            }
            else
            {
                character = Editing;
                character.Rename(NameEdit.Text);
                character.ChangePortrait(_portrait);
                character.ChangeAppearance(Appearance);
                character.Allocate(_allocation, _newSpells, Content);
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
