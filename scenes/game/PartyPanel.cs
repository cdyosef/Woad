using Godot;
using Woad.Core.Characters;
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

    /// <summary>The stats a point can go into, with their short labels.</summary>
    private static readonly (StatKind Stat, string Label)[] Stats =
    [
        (StatKind.Hearts, "Heart"), (StatKind.Speed, "Wend"), (StatKind.AttackDice, "Rmbl"),
        (StatKind.DefenseDice, "Def"), (StatKind.OdHandDice, "Od"), (StatKind.Luck, "Luck"),
    ];

    private VBoxContainer _rows = null!;

    /// <summary>A point for a hero (themselves or a companion) on a stat.</summary>
    public event Action<int, StatKind>? SpendRequested;

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
                Button spend = UiTheme.CompactButton(new Vector2(86 + i * 32, y), new Vector2(31, 15), $"{label} {PointBuy.CostPerStep(stat)}");
                spend.Name = $"Spend{hero.Id}{stat}";
                string? blocked = session.CheckSpendPoint(hero.Id, stat);
                spend.Disabled = blocked is not null;
                spend.TooltipText = blocked ?? $"+1 {stat} for {PointBuy.CostPerStep(stat)} point{(PointBuy.CostPerStep(stat) == 1 ? "" : "s")}.";
                int heroId = hero.Id;
                spend.Pressed += () => SpendRequested?.Invoke(heroId, stat);
                row.AddChild(spend);
            }

            y += 17;
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
