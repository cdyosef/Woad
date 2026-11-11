using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The Party menu in a dungeon, over the board: everyone in the party, what level they are, their
/// hearts and everything they carry. The player whose turn it is can spend the points their levels
/// bring (theirs and their companions'), and set how each of their companions manages its things.
/// </summary>
public partial class PartyPanel : Panel
{
    private const float RowGap = 4;

    /// <summary>Level-up buttons to a line, under their label.</summary>
    private const int PerLine = 8;

    private static Vector2 Wrapped(int index, float y) => new(4 + index % PerLine * 32, y + 14 + index / PerLine * 17);

    /// <summary>The stats a point can go into, with their short labels.</summary>
    private static readonly (StatKind Stat, string Label)[] Stats =
    [
        (StatKind.Hearts, "Hrt"), (StatKind.Speed, "Wnd+"), (StatKind.MoveDice, "Wnd"), (StatKind.AttackDice, "Rmb"),
        (StatKind.DefenseDice, "Def"), (StatKind.OdHandDice, "Od"), (StatKind.Luck, "Lck"), (StatKind.Magic, "Mag"),
    ];

    /// <summary>The skills a point can buy, with their short labels.</summary>
    private static readonly (Skill Skill, string Label)[] SkillButtons =
    [
        (Skill.Search, "Srch"), (Skill.Steal, "Steal"), (Skill.PickLock, "Lock"), (Skill.Disarm, "Dsrm"), (Skill.Aid, "Aid"),
        (Skill.Parry, "Parry"), (Skill.Shove, "Shove"), (Skill.BullRush, "Rush"), (Skill.Guard, "Guard"), (Skill.HighAlert, "Alert"),
        (Skill.Smash, "Smash"),
    ];

    private VBoxContainer _rows = null!;
    private ScrollContainer _scroll = null!;

    /// <summary>Scrolls the list to show <paramref name="control"/> (for the click tests).</summary>
    internal void Reveal(Control control) => _scroll.EnsureControlVisible(control);
    private SpellPicker _spellPicker = null!;
    private int _learnerId;

    /// <summary>A point for a hero (themselves or a companion) on a stat, and for Magic the basic spell it teaches.</summary>
    public event Action<int, StatKind, string?>? SpendRequested;

    /// <summary>A point for a hero (themselves or a companion) on a skill.</summary>
    public event Action<int, Skill>? LearnRequested;

    /// <summary>For the click tests: the window that asks which spell a new rank of Magic teaches.</summary>
    internal SpellPicker SpellPicker => _spellPicker;

    /// <summary>A new policy for a companion.</summary>
    public event Action<int, CompanionPolicy>? PolicyRequested;

    /// <summary>A hero handed over: a companion made a hero (null), or the player's hero made a companion of another player.</summary>
    public event Action<int, int?>? SwitchRequested;

    public event Action? CloseRequested;

    /// <summary>A hero's block was clicked: put up their sheet.</summary>
    public event Action<int>? HeroClicked;

    /// <summary>A card in an opened block's inventory was clicked: it's picked up, for its buttons.</summary>
    public event Action<int>? CardSelected;

    /// <summary>The heroes whose blocks are open: their inventory and level-up menu under them.</summary>
    private readonly HashSet<int> _open = [];

    /// <summary>The hero whose sheet is up, as the game screen last said.</summary>
    private int? _sheetId;

    /// <summary>Opened afresh: the viewer's own block and their companions' are open.</summary>
    public void Opened(GameSession session, Hero? viewer)
    {
        _open.Clear();
        foreach (Hero hero in session.State.Heroes.Where(hero => viewer is not null && (hero == viewer || hero.OwnerId == viewer.Id)))
        {
            _open.Add(hero.Id);
        }
    }

    internal bool IsOpen(int heroId) => _open.Contains(heroId);

    /// <summary>
    /// A block clicked: closed, it opens (their sheet goes up, their inventory and level-up menu open under it); open,
    /// it puts their sheet up, or, with it up already, closes.
    /// </summary>
    private void ClickBlock(int heroId)
    {
        if (!_open.Add(heroId) && _sheetId == heroId)
        {
            _open.Remove(heroId);
        }

        _sheetId = heroId;
        HeroClicked?.Invoke(heroId);
    }

    internal Button? Find(string name) => FindChild(name, recursive: true, owned: false) as Button;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(UiTheme.Heading(UiTheme.Label(new Vector2(8, 4), new Vector2(200, 16), "PARTY", 14, Palette.Trim), 1));
        Button close = UiTheme.Button(new Vector2(Size.X - 50, 4), new Vector2(44, 15), "Close");
        close.Name = "Close";
        close.Pressed += () => CloseRequested?.Invoke();
        AddChild(close);
        var scroll = _scroll = new ScrollContainer
        {
            Position = new Vector2(6, 24),
            Size = new Vector2(Size.X - 12, Size.Y - 30),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        AddChild(scroll);
        _rows = new VBoxContainer { CustomMinimumSize = new Vector2(Size.X - 24, 0) };
        _rows.AddThemeConstantOverride("separation", (int)RowGap);
        scroll.AddChild(_rows);
        _spellPicker = new SpellPicker { Position = new Vector2((Size.X - 248) / 2, 30) };
        _spellPicker.Picked += spell => SpendRequested?.Invoke(_learnerId, StatKind.Magic, spell);
        AddChild(_spellPicker);
    }

    /// <summary>Lists the party; <paramref name="viewer"/> (the hero whose turn it is) gets the buttons for their own.</summary>
    public void ShowParty(GameSession session, Hero? viewer, Hero? sheet = null)
    {
        _sheetId = sheet?.Id ?? viewer?.Id;
        foreach (Node old in _rows.GetChildren())
        {
            _rows.RemoveChild(old);
            old.QueueFree();
        }

        foreach (Hero hero in session.State.Heroes)
        {
            _rows.AddChild(Row(session, hero, viewer));
        }
    }

    /// <summary>
    /// The buttons that hand a hero over: <b>Make hero</b> on the viewer's companions, and on the viewer's own
    /// hero a <b>Follow</b> button for each other player still in the run, making it their companion.
    /// </summary>
    private float HandOver(GameSession session, Control row, Hero hero, Hero viewer, float y)
    {
        int heroId = hero.Id;
        if (hero.IsCompanion)
        {
            Button make = UiTheme.CompactButton(new Vector2(86, y), new Vector2(70, 15), "Make hero");
            make.Name = $"MakeHero{heroId}";
            string? blocked = session.CheckSwitchControl(heroId, null);
            make.Disabled = blocked is not null;
            make.TooltipText = blocked ?? $"{hero.Name} becomes a hero, played by a player, for the rest of the run.";
            make.Pressed += () => SwitchRequested?.Invoke(heroId, null);
            row.AddChild(make);
            row.AddChild(UiTheme.Label(new Vector2(4, y + 2), new Vector2(80, 12), "Take over:", color: Palette.TextDim));
            return y + 17;
        }

        if (hero != viewer)
        {
            return y;
        }

        row.AddChild(UiTheme.Label(new Vector2(4, y + 2), new Vector2(80, 12), "Make companion:", color: Palette.TextDim));
        List<Hero> players = [.. session.State.Heroes.Where(other => other != hero && other.IsInPlay && !other.IsCompanion)];
        if (players.Count == 0)
        {
            Button none = UiTheme.CompactButton(new Vector2(86, y), new Vector2(70, 15), "Follow...");
            none.Disabled = true;
            none.TooltipText = "There's no other player in the run for it to follow.";
            row.AddChild(none);
            return y + 17;
        }

        for (int i = 0; i < players.Count; i++)
        {
            Hero owner = players[i];
            Button follow = UiTheme.CompactButton(new Vector2(86 + i % 3 * 74, y + i / 3 * 17), new Vector2(72, 15), $"Follow {owner.Name}");
            follow.Name = $"Follow{heroId}_{owner.Id}";
            string? blocked = session.CheckSwitchControl(heroId, owner.Id);
            follow.Disabled = blocked is not null;
            follow.TooltipText = blocked ?? $"{hero.Name} becomes {owner.Name}'s companion for the rest of the run, with any companions of its own. The game plays it from now on.";
            int ownerId = owner.Id;
            follow.Pressed += () => SwitchRequested?.Invoke(heroId, ownerId);
            row.AddChild(follow);
        }

        return y + 17 * ((players.Count + 2) / 3);
    }

    /// <summary>An open block's inventory: what's worn, on the belt, in each bag under it, and in the pack, face up.</summary>
    private float Inventory(Control row, Hero hero, float width, float y)
    {
        List<(string Title, IReadOnlyList<CardInstance> Cards, string Empty)> sections =
        [
            ("Worn", [.. hero.EquippedCards], "nothing"),
            ($"Belt {hero.BeltUsed}/{hero.BeltCapacity}", hero.Belt, hero.BeltCapacity == 0 ? "no belt" : "empty"),
            .. hero.Bags.Select(bag => ($"{bag.Definition.Name} {bag.Contents.Count}/{bag.Definition.Hangs!.Slots}", (IReadOnlyList<CardInstance>)[.. bag.Contents], "empty")),
            ($"Pack {hero.Backpack.Count}/{GameRules.BackpackCapacity}", hero.Backpack, "empty"),
        ];
        const float labelWidth = 64;
        int perLine = Math.Max(1, (int)((width - labelWidth - 4) / 30));
        foreach ((string title, IReadOnlyList<CardInstance> cards, string empty) in sections)
        {
            row.AddChild(UiTheme.Label(new Vector2(4, y + 2), new Vector2(labelWidth, 12), title, color: Palette.TrimBright));
            if (cards.Count == 0)
            {
                row.AddChild(UiTheme.Label(new Vector2(labelWidth, y + 2), new Vector2(120, 12), empty, color: Palette.TextDim));
                y += 16;
                continue;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                CardInstance card = cards[i];
                var face = new SheetCard { Position = new Vector2(labelWidth + i % perLine * 30, y + i / perLine * 40), Size = CardFace.Size, Back = "back_bag" };
                face.Name = $"Card{card.Id}";
                face.Show(card, "", selected: false, hero);
                int id = card.Id;
                face.Pressed += () => CardSelected?.Invoke(id);
                row.AddChild(face);
            }

            y += 40 * ((cards.Count + perLine - 1) / perLine) + 2;
        }

        return y + 2;
    }

    private Control Row(GameSession session, Hero hero, Hero? viewer)
    {
        float width = _rows.CustomMinimumSize.X;
        var row = new Control { Name = $"Hero{hero.Id}" };
        float y = 0;
        string status = hero.Status switch
        {
            HeroStatus.Dead => "  (dead)",
            HeroStatus.Escaped => "  (out)",
            HeroStatus.KeelingOver => "  (down)",
            _ => "",
        };
        // The hero's block: portrait, crest, name, hearts, level and gold, vital stats. Click it for their sheet.
        Crest crest = Crests.Resolve(hero.Appearance, hero.Portrait, Looks.Resolve(hero.Appearance, hero.Portrait));
        var block = new PartyCard(hero, crest, status.Trim(' ', '(', ')')) { Position = new Vector2(0, y), Size = new Vector2(width, PartyCard.HeightFor(hero, width)) };
        if (hero.OwnerId is int ownerId)
        {
            block.TooltipText = $"{hero.Name}, {session.State.GetHero(ownerId).Name}'s companion: click to put up their sheet.";
        }

        int clicked = hero.Id;
        block.Pressed += () => ClickBlock(clicked);
        row.AddChild(block);
        y += block.Size.Y + 3;
        if (!_open.Contains(hero.Id))
        {
            row.CustomMinimumSize = new Vector2(width, y);
            return row;
        }

        y = Inventory(row, hero, width, y);

        bool mine = viewer is not null && (hero == viewer || hero.OwnerId == viewer.Id) && hero.IsInPlay;
        if (mine && hero.PointsToSpend > 0)
        {
            row.AddChild(UiTheme.Label(new Vector2(4, y + 2), new Vector2(80, 12), $"Level up: {UiTheme.Count(hero.PointsToSpend, "point", "points")}", color: Palette.Heal));
            for (int i = 0; i < Stats.Length; i++)
            {
                (StatKind stat, string label) = Stats[i];
                int cost = hero.Allocation is { } now ? PointBuy.Cost(PointBuy.Raise(now, stat)) - PointBuy.Cost(now) : 0;
                Button spend = UiTheme.CompactButton(Wrapped(i, y), new Vector2(31, 15), $"{label} {cost}");
                spend.Name = $"Spend{hero.Id}{stat}";
                string? blocked = session.CheckSpendPoint(hero.Id, stat);
                spend.Disabled = blocked is not null;
                spend.TooltipText = blocked ?? $"+1 {stat} for {UiTheme.Count(cost, "point", "points")}.";
                int heroId = hero.Id;
                spend.Pressed += () =>
                {
                    if (stat != StatKind.Magic)
                    {
                        SpendRequested?.Invoke(heroId, stat, null);
                        return;
                    }

                    // A rank of Magic teaches a basic spell: which one?
                    _learnerId = heroId;
                    _spellPicker.Open($"{hero.Name.ToUpperInvariant()} LEARNS", Spellbook.Learnable(hero.Spells.Select(known => known.Id), session.Content)
                        .Select(spell => (spell, spell.Name, (string?)null)));
                };
                row.AddChild(spend);
            }

            y += 14 + 17 * ((Stats.Length + PerLine - 1) / PerLine);
            row.AddChild(UiTheme.Label(new Vector2(4, y + 2), new Vector2(160, 12), "Learn a skill (1 point each):", color: Palette.Heal));
            int shown = 0;
            foreach ((Skill skill, string label) in SkillButtons.Where(entry => !hero.Knows(entry.Skill)))
            {
                int cost = hero.Allocation is { } now ? PointBuy.Cost(now.WithSkill(skill, true)) - PointBuy.Cost(now) : 0;
                Button learn = UiTheme.CompactButton(Wrapped(shown, y), new Vector2(31, 15), label);
                learn.Name = $"Learn{hero.Id}{skill}";
                string? blocked = session.CheckLearnSkill(hero.Id, skill);
                learn.Disabled = blocked is not null;
                learn.TooltipText = blocked ?? $"{skill.Name()} for {UiTheme.Count(cost, "point", "points")}.";
                int heroId = hero.Id;
                learn.Pressed += () => LearnRequested?.Invoke(heroId, skill);
                row.AddChild(learn);
                shown++;
            }

            y += 14 + 17 * ((shown + PerLine - 1) / PerLine);
        }

        if (mine && hero.IsCompanion)
        {
            CompanionPolicy policy = hero.Policy;
            (string Label, string Help, bool On, Func<CompanionPolicy, CompanionPolicy> Flip)[] switches =
            [
                ("Potions", "Drinks a healing potion from its belt when it's badly hurt.", policy.DrinksPotions, p => p with { DrinksPotions = !p.DrinksPotions }),
                ("Loot", "Picks up loot within reach while there's no fight, whatever its order.", policy.PicksUpLoot, p => p with { PicksUpLoot = !p.PicksUpLoot }),
                ("Better gear", "Puts on gear it carries that's worth more than what it wears, while there's no fight.", policy.EquipsBetterGear, p => p with { EquipsBetterGear = !p.EquipsBetterGear }),
            ];
            row.AddChild(UiTheme.Label(new Vector2(4, y + 2), new Vector2(80, 12), "On its turns:", color: Palette.TextDim));
            for (int i = 0; i < switches.Length; i++)
            {
                (string label, string help, bool on, Func<CompanionPolicy, CompanionPolicy> flip) = switches[i];
                Button toggle = UiTheme.CompactButton(new Vector2(86 + i * 62, y), new Vector2(60, 15), label);
                toggle.Name = $"Policy{hero.Id}{label.Replace(" ", "")}";
                toggle.ToggleMode = true;
                toggle.SetPressedNoSignal(on);
                toggle.TooltipText = help;
                int companionId = hero.Id;
                toggle.Pressed += () => PolicyRequested?.Invoke(companionId, flip(policy));
                row.AddChild(toggle);
            }

            y += 17;
        }

        if (mine && viewer is not null)
        {
            y = HandOver(session, row, hero, viewer, y);
        }

        row.CustomMinimumSize = new Vector2(width, y);
        return row;
    }
}
