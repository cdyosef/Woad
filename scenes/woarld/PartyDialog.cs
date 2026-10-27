using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;
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

    public IReadOnlyList<Character> Party { get; set; } = [];

    public ContentCatalog Content { get; init; } = null!;

    /// <summary>Hearts now and at most, where the raid keeps track; null shows full hearts.</summary>
    public Func<Character, (int Now, int Max)>? Hearts { get; init; }

    /// <summary>Something about the party changed (a card moved, a switch flipped): save them.</summary>
    public event Action<IReadOnlyList<Character>>? Changed;

    /// <summary>A character with points to spend: open their points.</summary>
    public event Action<Character>? LevelUpRequested;

    internal Button? Find(string name) => FindChild(name, recursive: true, owned: false) as Button;

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
    }

    /// <summary>Opens the menu on the party as it is now.</summary>
    public void Open()
    {
        _selected = null;
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
            _giving.Text = "Click a card to give it to someone else.";
            return;
        }

        _giving.Text = $"Give {selected.From.Name}'s {Content.GetCard(selected.CardId).Name} to:";
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
        row.AddChild(UiTheme.Label(new Vector2(0, 0), new Vector2(400, 14), $"{character.Name}   Level {character.Level}   {now}/{max} hearts   {character.Gold} gold{role}", color: Palette.TrimBright));

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
            card.TooltipText = $"{name} ({where}). Click to give it to someone else.";
            card.ToggleMode = true;
            card.SetPressedNoSignal(_selected is { } chosen && chosen.From == character && chosen.CardId == cardId);
            card.Pressed += () =>
            {
                _selected = _selected is { } was && was.From == character && was.CardId == cardId ? null : (character, cardId);
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
