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
    ];

    private VBoxContainer _rows = null!;
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

    public event Action? CloseRequested;

    internal Button? Find(string name) => FindChild(name, recursive: true, owned: false) as Button;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(UiTheme.Heading(UiTheme.Label(new Vector2(8, 4), new Vector2(200, 16), "PARTY", 14, Palette.Trim), 1));
        Button close = UiTheme.Button(new Vector2(Size.X - 50, 4), new Vector2(44, 15), "Close");
        close.Name = "Close";
        close.Pressed += () => CloseRequested?.Invoke();
        AddChild(close);
        var scroll = new ScrollContainer
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
    public void ShowParty(GameSession session, Hero? viewer)
    {
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
        string owner = hero.OwnerId is int ownerId ? $", {session.State.GetHero(ownerId).Name}'s companion" : "";
        row.AddChild(UiTheme.Label(new Vector2(0, y), new Vector2(width, 13), $"{hero.Name}   Lv {hero.Level}   {hero.Hearts}/{hero.MaxHearts} hearts{owner}{status}", color: Palette.Seat(hero.Seat)));
        y += 13;

        string Names(IEnumerable<CardInstance> cards) => string.Join(", ", cards.Select(card => card.Count > 1 ? $"{card.Definition.Name} x{card.Count}" : card.Definition.Name).DefaultIfEmpty("nothing"));
        string keys = hero.Keys > 0 ? $"   {UiTheme.Count(hero.Keys, "key", "keys")}" : "";
        Label gear = UiTheme.Label(new Vector2(4, y), new Vector2(width - 4, 36),
            $"Wearing {Names(hero.EquippedCards)}.\nBelt: {Names(hero.Belt)}.  Pack: {Names(hero.Backpack)}.{keys}", color: Palette.Text);
        gear.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        row.AddChild(gear);
        y += 26;

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

        row.CustomMinimumSize = new Vector2(width, y);
        return row;
    }
}
