using Godot;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The sheet of whatever was clicked on the board, in the right panel, shaped like a hero's: its name
/// and hearts, its picture on a card between what it hits with and what makes it special, its dice
/// (Rumble, Defense, Wend), and the state it's in. A monster's card is black with a red trim; an
/// ally's in their color. The x puts it away.
/// </summary>
public partial class TargetSheet : Panel
{
    private Label _name = null!;
    private HeartsView _hearts = null!;
    private Label _kind = null!;
    private Button _close = null!;
    private RichTextLabel _dice = null!;
    private RichTextLabel _notes = null!;
    private Portrait _portrait = null!;
    private SheetCard _attack = null!;
    private SheetCard _trait = null!;

    public event Action? CloseRequested;

    /// <summary>The theme, for the monsters' pictures.</summary>
    public string ThemeId { get; set; } = "";

    public override void _Ready()
    {
        Size = new Vector2(168, 150);
        _name = Add(UiTheme.Label(new(5, 1), new(110, 16), fontSize: UiTheme.HeadingSize));
        _hearts = Add(new HeartsView { Position = new(5, 17), Size = new(140, 9) });
        _kind = Add(UiTheme.Label(new(5, 26), new(158, 12), color: Palette.TextDim));
        _close = Add(UiTheme.CompactButton(new(152, 3), new(12, 12), "x"));
        _close.Pressed += () => CloseRequested?.Invoke();
        _attack = Add(new SheetCard { Position = new(6, 42), Size = new(44, 50), Back = "fist", ToggleMode = false, MouseFilter = MouseFilterEnum.Pass });
        _portrait = Add(new Portrait { Position = new(60, 40), Size = new(48, 54) });
        _trait = Add(new SheetCard { Position = new(118, 42), Size = new(44, 50), Back = "back_accessory", ToggleMode = false, MouseFilter = MouseFilterEnum.Pass });
        _dice = Add(UiTheme.RichLabel(new(5, 96), new(160, 13)));
        _notes = Add(UiTheme.RichLabel(new(5, 109), new(160, 40)));
        _notes.AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }

    public void ShowMonster(Monster monster)
    {
        MonsterDefinition definition = monster.Definition;
        _name.Text = definition.Name.ToUpperInvariant();
        _name.AddThemeColorOverride("font_color", Palette.Danger);
        _hearts.ShowHearts(monster.Hearts, monster.MaxHearts);
        _kind.Text = (definition.Boss ? "Boss" : definition.Miniboss ? "Champion" : "Monster") + $"  {definition.Xp} XP";
        _portrait.Show(GameArt.Monster(ThemeId, definition.Id), null, monster: true);

        string reach = definition.AttackRange > 1 ? $"Range {definition.AttackRange}" : "Melee";
        _attack.ShowFace(definition.AttackRange > 1 ? "short_bow" : "fist", definition.Weapon > 0 ? $"{reach} +{definition.Weapon}" : reach, Palette.DieRumble, true, $"Attacks: {definition.AttackDice}d6{(definition.Weapon > 0 ? $" +{definition.Weapon}" : "")}, {reach.ToLowerInvariant()}.");
        List<string> traits = Traits(monster);
        if (traits.Count > 0)
        {
            _trait.ShowFace("back_accessory", traits[0], Palette.TrimBright, true, string.Join("\n", traits));
        }
        else
        {
            _trait.Show(null, "", selected: false);
        }

        _dice.Text = $"{UiTheme.Icon("icon_rumble", "Rumble")} {UiTheme.Dice(definition.AttackDice, definition.Weapon)}  "
                     + $"{UiTheme.Icon("icon_defense", "Defense")} {UiTheme.Dice(definition.DefenseDice + definition.ExtraDefenseDice, definition.Armor)}  "
                     + $"{UiTheme.Icon("icon_wend", "Wend")} {definition.Move}";
        _notes.Text = string.Join("  ", traits.Skip(1).Concat(State(monster)));
    }

    public void ShowHero(Hero hero)
    {
        _name.Text = hero.Name.ToUpperInvariant();
        _name.AddThemeColorOverride("font_color", Palette.Seat(hero.Seat));
        _hearts.ShowHearts(hero.Hearts, hero.MaxHearts);
        _kind.Text = $"Lv {hero.Level}" + (hero.IsCompanion ? " companion" : hero.PlayerNumber > 0 ? $"  Player {hero.PlayerNumber}" : "");
        _portrait.Show(null, Looks.Resolve(hero.Appearance, hero.Portrait), monster: false, Palette.Seat(hero.Seat));
        if (hero.MainWeapon is { } weapon)
        {
            _attack.Show(weapon, "", selected: false);
        }
        else
        {
            _attack.ShowFace("fist", "Fists", Palette.DieRumble, true, "Bare-handed.");
        }

        if (hero.Equipment.GetValueOrDefault(EquipSlot.OffHand) is { } off && off != hero.MainWeapon)
        {
            _trait.Show(off, "", selected: false);
        }
        else
        {
            _trait.Show(null, "", selected: false);
        }

        Stats stats = hero.EffectiveStats;
        _dice.Text = $"{UiTheme.Icon("icon_rumble", "Rumble")} {UiTheme.Dice(stats.AttackDice, stats.Weapon)}  "
                     + $"{UiTheme.Icon("icon_defense", "Defense")} {UiTheme.Dice(Math.Min(1, stats.DefenseDice), stats.Armor)}  "
                     + $"{UiTheme.Icon("icon_wend", "Wend")} {UiTheme.Dice(stats.MoveDice, stats.Speed)}";
        var notes = new List<string>();
        if (hero.IsKeelingOver)
        {
            notes.Add($"Keeling Over (life {hero.LifeMarks}/3, death {hero.DeathMarks}/3)");
        }

        if (hero.Stun.IsStunned)
        {
            notes.Add("Stunned");
        }

        if (hero.FireDice > 0)
        {
            notes.Add($"On fire ({hero.FireDice})");
        }

        if (hero.Prone)
        {
            notes.Add("Prone");
        }

        if (hero.Aloft)
        {
            notes.Add("Flying");
        }

        notes.AddRange(hero.Conditions.All.Where(entry => entry.Value > 0).Select(entry => $"{entry.Key} {entry.Value}"));
        _notes.Text = string.Join("  ", notes);
    }

    /// <summary>What makes it special: how it moves, what it does when it hits, what it's made of.</summary>
    private static List<string> Traits(Monster monster)
    {
        MonsterDefinition d = monster.Definition;
        var traits = new List<string>();
        void Add(bool has, string trait)
        {
            if (has)
            {
                traits.Add(trait);
            }
        }

        Add(d.DieType is { } type && type != DieType.Plain, $"{d.DieType} dice");
        Add(d.SplitsInto is not null, "Splits");
        Add(d.Flying, "Flies");
        Add(d.Swimmer, "Swims");
        Add(d.Thief || d.Mugger, "Steals");
        Add(d.Shell, "Shelled");
        Add(d.Reassembles, "Reassembles");
        Add(d.Regenerates, "Regenerates");
        Add(d.Grabs, "Grabs");
        Add(d.Pulls, "Pulls");
        Add(d.KnocksBack || d.KnockbackOnSix, "Knocks back");
        Add(d.StunOnHit || d.StunOnSix, "Stuns");
        Add(d.Hexes, "Hexes");
        Add(d.Engulfs, "Engulfs");
        Add(d.Raises is not null, "Raises the dead");
        Add(d.Erratic, "Erratic");
        Add(d.KeepsDistance, "Keeps away");
        Add(d.FireImmune, "Fireproof");
        Add(d.StunImmune, "Unstunnable");
        return traits;
    }

    /// <summary>The state it's in right now.</summary>
    private static IEnumerable<string> State(Monster monster)
    {
        if (monster.Stun.IsStunned)
        {
            yield return "Stunned";
        }

        if (monster.FireDice > 0)
        {
            yield return $"On fire ({monster.FireDice})";
        }

        if (monster.Prone)
        {
            yield return "Prone";
        }

        if (monster.FrozenTurns > 0)
        {
            yield return "Frozen";
        }

        foreach ((Condition condition, int stacks) in monster.Conditions.All.Where(entry => entry.Value > 0))
        {
            yield return $"{condition} {stacks}";
        }

        if (monster.HasLoot)
        {
            yield return "Carrying loot";
        }
    }

    private T Add<T>(T node) where T : Node
    {
        AddChild(node);
        return node;
    }

    /// <summary>The picture card in the middle: a monster's on black with a red trim, a hero's in their color.</summary>
    private sealed partial class Portrait : Control
    {
        private Texture2D? _sprite;
        private LookColors? _look;
        private bool _monster;
        private Color _trim;

        public void Show(Texture2D? sprite, LookColors? look, bool monster, Color? trim = null)
        {
            _sprite = sprite;
            _look = look;
            _monster = monster;
            _trim = trim ?? new Color("c0242c");
            QueueRedraw();
        }

        public override void _Draw()
        {
            var rect = new Rect2(Vector2.Zero, Size);
            DrawRect(rect, _monster ? Colors.Black : Palette.PanelDark);
            DrawRect(rect, _trim, filled: false, width: 2);
            DrawRect(rect.Grow(-3), _monster ? new Color("6a0e14") : _trim.Darkened(0.5f), filled: false, width: 1);
            var picture = new Rect2(4, 5, Size.X - 8, Size.X - 8);
            if (_sprite is not null)
            {
                DrawTextureRect(_sprite, picture, tile: false);
            }
            else if (_look is { } look)
            {
                Looks.Draw(this, picture, look);
            }
        }
    }
}
