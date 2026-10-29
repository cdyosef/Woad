using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Woarld;

/// <summary>
/// The Party menu on the Woarld Map: every character in the party with their level, hearts, gold and
/// everything they carry. Click a card, then who to give it to, to move it between them; Level up
/// opens a character's points to spend; and each companion's switches say how it manages its things.
/// </summary>
public partial class PartyDialog : Control
{
    private const float RowHeight = 66;

    private VBoxContainer _rows = null!;
    private Label _giving = null!;
    private HBoxContainer _givers = null!;
    private (Character From, string CardId)? _selected;

    /// <summary>A character's sheet, open over the menu, and whose it is.</summary>
    private Control _sheetView = null!;
    private Game.HeroPanel _sheet = null!;
    private Character? _sheetOf;
    private IReadOnlyDictionary<int, string> _sheetKeys = new Dictionary<int, string>();

    public IReadOnlyList<Character> Party { get; set; } = [];

    public ContentCatalog Content { get; init; } = null!;

    /// <summary>Hearts now and at most, where the raid keeps track; null shows full hearts.</summary>
    public Func<Character, (int Now, int Max)>? Hearts { get; init; }

    /// <summary>A hero loses a heart (a scroll went off among the party), where the raid keeps track.</summary>
    public Action<Character>? Wound { get; init; }

    /// <summary>The dice for studying scrolls.</summary>
    public IRandomSource Random { get; init; } = new Pcg32((ulong)DateTime.UtcNow.Ticks, sequence: 11);

    /// <summary>What came of the last scroll studied, shown until something else is picked.</summary>
    private string? _news;

    /// <summary>Something about the party changed (a card moved, a switch flipped): save them.</summary>
    public event Action<IReadOnlyList<Character>>? Changed;

    /// <summary>A character with points to spend: open their points.</summary>
    public event Action<Character>? LevelUpRequested;

    internal Button? Find(string name) => FindChild(name, recursive: true, owned: false) as Button;

    /// <summary>The line under the party: what to do with the card picked, or what came of the last scroll studied.</summary>
    internal string Note => _giving.Text;

    public override void _Ready()
    {
        Size = new Vector2(MenuScreen.Width, MenuScreen.Height);
        Visible = false;
        AddChild(new ColorRect { Color = new Color(Palette.Background, 0.85f), Size = Size });
        var panel = new Panel { Position = new Vector2(40, 20), Size = new Vector2(560, 320) };
        AddChild(panel);
        panel.AddChild(UiTheme.Heading(UiTheme.Label(new Vector2(10, 6), new Vector2(300, 18), "THE PARTY", 16, Palette.Trim), 2));
        Button close = UiTheme.Button(new Vector2(500, 6), new Vector2(52, 18), "Close");
        close.Name = "Close";
        close.Pressed += Hide;
        panel.AddChild(close);

        var scroll = new ScrollContainer { Position = new Vector2(8, 28), Size = new Vector2(544, 252), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        _rows = new VBoxContainer { CustomMinimumSize = new Vector2(530, 0) };
        scroll.AddChild(_rows);

        _giving = UiTheme.Label(new Vector2(10, 286), new Vector2(240, 14), "Click a card to give it to someone else.", color: Palette.TextDim);
        panel.AddChild(_giving);
        _givers = new HBoxContainer { Position = new Vector2(250, 284) };
        _givers.AddThemeConstantOverride("separation", 4);
        panel.AddChild(_givers);

        // A character's sheet, over the menu: click a card on it to give it on; click outside to close it.
        _sheetView = new Control { Size = Size, Visible = false, Name = "SheetView" };
        var dim = new ColorRect { Size = Size, Color = new Color(Palette.Background, 0.8f) };
        dim.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true })
            {
                CloseSheet();
            }
        };
        _sheetView.AddChild(dim);
        _sheet = new Game.HeroPanel { Position = new Vector2((Size.X - 176) / 2, 0), Name = "Sheet" };
        _sheet.CardSelected += cardId =>
        {
            if (_sheetOf is { } owner && _sheetKeys.TryGetValue(cardId, out string? key))
            {
                CloseSheet();
                _selected = (owner, key);
                _news = null;
                Refresh();
            }
        };
        _sheetView.AddChild(_sheet);
        _sheetView.AddChild(_sheet.BagView);
        AddChild(_sheetView);
    }

    /// <summary>Whose sheet is open, if any (for the click tests).</summary>
    internal Character? SheetOf => _sheetView.Visible ? _sheetOf : null;

    /// <summary>Opens a character's sheet over the menu.</summary>
    internal void OpenSheet(Character character)
    {
        (int now, _) = Hearts?.Invoke(character) ?? (character.MaxHearts(Content), 0);
        Hero preview = Hero.Preview(character, Content, now, out _sheetKeys);
        _sheetOf = character;
        _sheet.CloseBag();
        _sheet.ShowHero(preview, null, null);
        _sheet.ShowMagic(preview.EffectiveStats.Magic > 0 || preview.Spells.Count > 0, false, $"{character.Name} knows: {string.Join(", ", preview.Spells.Select(spell => spell.Name))}");
        _sheetView.Visible = true;
    }

    private void CloseSheet()
    {
        _sheet.CloseBag();
        _sheetView.Visible = false;
        _sheetOf = null;
    }

    /// <summary>Opens the menu on the party as it is now.</summary>
    public void Open()
    {
        _selected = null;
        _news = null;
        Refresh();
        Show();
    }

    private void Refresh()
    {
        foreach (Node old in _rows.GetChildren())
        {
            _rows.RemoveChild(old);
            old.QueueFree();
        }

        for (int i = 0; i < Party.Count; i++)
        {
            _rows.AddChild(Row(Party[i], i));
        }

        foreach (Node old in _givers.GetChildren())
        {
            _givers.RemoveChild(old);
            old.QueueFree();
        }

        if (_selected is not { } selected)
        {
            _giving.Text = _news ?? "Click a card to give it to someone else.";
            return;
        }

        _giving.Text = $"Give {selected.From.Name}'s {Content.GetCard(selected.CardId).Name} to:";

        // A scroll with a spell on it can be studied, here and now: Od Hand and Magic, or it goes off.
        if (Content.GetCard(selected.CardId).Teaches is { } spellId && ScrollStudy.Check(selected.From, selected.CardId, Content) is null)
        {
            SpellDefinition spell = Content.GetSpell(spellId);
            Button learn = UiTheme.Accent(UiTheme.CompactButton(Vector2.Zero, new Vector2(60, 16), "Learn"), Palette.Power);
            learn.CustomMinimumSize = new Vector2(60, 16);
            learn.Name = "Learn";
            learn.TooltipText = $"{selected.From.Name} studies it to learn {spell.Name}: Od Hand plus Magic, {GameSession.LearnDifficulty(spell)} or more. The scroll is used up either way; fail and it goes off among the party!";
            learn.Pressed += () => Study(selected.From, selected.CardId);
            _givers.AddChild(learn);
        }
        foreach (Character other in Party.Where(other => other != selected.From))
        {
            Button give = UiTheme.CompactButton(Vector2.Zero, new Vector2(60, 16), other.Name);
            give.CustomMinimumSize = new Vector2(60, 16);
            give.Name = $"GiveTo{other.Name}";
            give.Pressed += () => Give(selected.From, selected.CardId, other);
            _givers.AddChild(give);
        }
    }

    private Control Row(Character character, int index)
    {
        var row = new Control { CustomMinimumSize = new Vector2(530, RowHeight), Name = $"Row{index}" };
        (int now, int max) = Hearts?.Invoke(character) ?? (character.MaxHearts(Content), character.MaxHearts(Content));
        string role = character.IsCompanion ? "  (companion)" : "";
        // Their name and level: click it for their character sheet.
        Button header = UiTheme.CompactButton(new Vector2(0, 0), new Vector2(400, 15), $"{character.Name}   Level {character.Level}   {now}/{max} hearts   {character.Gold} gold{role}");
        header.Name = $"Sheet{index}";
        header.Flat = true;
        header.Alignment = HorizontalAlignment.Left;
        header.AddThemeColorOverride("font_color", Palette.TrimBright);
        header.TooltipText = $"{character.Name}'s character sheet.";
        header.Pressed += () => OpenSheet(character);
        row.AddChild(header);

        if (character.UnspentPoints > 0)
        {
            Button level = UiTheme.CompactButton(new Vector2(430, 0), new Vector2(96, 15), $"Level up ({character.UnspentPoints})");
            level.Name = $"LevelUp{index}";
            level.Pressed += () =>
            {
                Hide();
                LevelUpRequested?.Invoke(character);
            };
            row.AddChild(level);
        }

        // Every card they carry, as a button to pick it up and hand it on.
        float x = 0, y = 16;
        foreach ((string cardId, string where) in character.Equipped.Select(id => (id, "worn")).Concat(character.Belt.Select(id => (id, "belt"))).Concat(character.Backpack.Select(id => (id, "pack"))))
        {
            string name = Content.GetCard(cardId).Name + (CardKey.Wear(cardId) is > 0 and var wear ? $" (worn -{wear})" : "");
            float width = Math.Max(48, name.Length * 5 + 10);
            if (x + width > 530)
            {
                x = 0;
                y += 17;
            }

            Button card = UiTheme.CompactButton(new Vector2(x, y), new Vector2(width, 15), name);
            card.Name = $"Card{index}_{CardKey.Id(cardId)}";
            card.TooltipText = $"{name} ({where}). Click to give it to someone else.";
            card.ToggleMode = true;
            card.SetPressedNoSignal(_selected is { } chosen && chosen.From == character && chosen.CardId == cardId);
            card.Pressed += () =>
            {
                _selected = _selected is { } was && was.From == character && was.CardId == cardId ? null : (character, cardId);
                _news = null;
                Refresh();
            };
            row.AddChild(card);
            x += width + 3;
        }

        if (character.IsCompanion)
        {
            CompanionPolicy policy = character.Policy;
            (string Label, bool On, Func<CompanionPolicy, CompanionPolicy> Flip)[] switches =
            [
                ("Drinks potions", policy.DrinksPotions, p => p with { DrinksPotions = !p.DrinksPotions }),
                ("Picks up loot", policy.PicksUpLoot, p => p with { PicksUpLoot = !p.PicksUpLoot }),
                ("Wears better gear", policy.EquipsBetterGear, p => p with { EquipsBetterGear = !p.EquipsBetterGear }),
            ];
            y += 18;
            for (int i = 0; i < switches.Length; i++)
            {
                (string label, bool on, Func<CompanionPolicy, CompanionPolicy> flip) = switches[i];
                Button toggle = UiTheme.CompactButton(new Vector2(i * 104, y), new Vector2(100, 15), label);
                toggle.Name = $"Policy{index}_{i}";
                toggle.ToggleMode = true;
                toggle.SetPressedNoSignal(on);
                toggle.Pressed += () =>
                {
                    character.SetPolicy(flip(character.Policy));
                    Changed?.Invoke(Party);
                    Refresh();
                };
                row.AddChild(toggle);
            }
        }

        row.CustomMinimumSize = new Vector2(530, Math.Max(RowHeight, y + 20));
        return row;
    }

    private void Study(Character learner, string cardId)
    {
        ScrollStudy.Result result = ScrollStudy.Attempt(learner, cardId, Party, Content, Random);
        foreach (Character hurt in result.Wounded)
        {
            Wound?.Invoke(hurt);
        }

        _news = result.Message;
        _selected = null;
        Changed?.Invoke(Party);
        Refresh();
    }

    private void Give(Character from, string cardId, Character to)
    {
        if (from.TakeCard(cardId))
        {
            to.GiveCard(cardId, Content);
            Changed?.Invoke(Party);
        }

        _selected = null;
        Refresh();
    }
}
