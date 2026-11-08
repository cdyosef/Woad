using Godot;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// What the buckle does, opened by clicking it on the belt square, over the board: the belt, buckle and
/// mods worn (unbuckle the belt whole, or take a buckle or mod off), the belts, buckles and mods carried
/// (put them on; a belt unbuckled whole buckles on just as it was), the belt square's art (from the belts
/// and buckles owned), and whether the belt shows on the hero's figure. Every button says why when it
/// can't be used.
/// </summary>
public partial class BuckleMenu : Panel
{
    private const float Width = 260;
    private const float Line = 17;

    private readonly List<Control> _rows = [];
    private Label _title = null!;

    /// <summary>A button was pressed: the command it gives, for the active hero.</summary>
    public event Action<Func<int, GameCommand>>? Commanded;

    public override void _Ready()
    {
        Size = new Vector2(Width, 60);
        Visible = false;
        _title = UiTheme.Label(new Vector2(0, 3), new Vector2(Width, 12), "THE BUCKLE", color: Palette.TrimBright);
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        AddChild(_title);
        Button close = UiTheme.CompactButton(new Vector2(Width - 18, 2), new Vector2(15, 14), "x");
        close.TooltipText = "Close the buckle.";
        close.Pressed += () => Visible = false;
        AddChild(close);
    }

    internal bool IsOpen => Visible;

    /// <summary>The first button whose text starts with <paramref name="text"/> (for the tests).</summary>
    internal Button? ButtonFor(string text) => _rows.OfType<Button>().FirstOrDefault(button => button.Text.StartsWith(text, StringComparison.Ordinal));

    /// <summary>Lays the menu out for the hero as they are now (the rules' own checks say what can't be done).</summary>
    public void ShowHero(GameSession session, Hero? hero)
    {
        foreach (Control row in _rows)
        {
            row.QueueFree();
        }

        _rows.Clear();
        if (hero is null || !Visible)
        {
            return;
        }

        float y = 18;
        void Row(string text, string? buttonText = null, string? blocked = null, Func<int, GameCommand>? command = null, string tooltip = "")
        {
            Label label = UiTheme.Label(new Vector2(8, y + 2), new Vector2(buttonText is null ? Width - 16 : Width - 84, 14), text, color: Palette.Text);
            label.ClipText = true;
            label.TooltipText = tooltip;
            label.MouseFilter = MouseFilterEnum.Pass;
            Add(label);
            if (buttonText is not null)
            {
                Button button = UiTheme.CompactButton(new Vector2(Width - 74, y), new Vector2(66, 15), buttonText);
                button.Disabled = blocked is not null;
                button.TooltipText = blocked ?? tooltip;
                button.Pressed += () => Commanded?.Invoke(command!);
                Add(button);
            }

            y += Line;
        }

        void Heading(string text)
        {
            Label label = UiTheme.Label(new Vector2(6, y + 2), new Vector2(Width - 12, 12), text, color: Palette.TextDim);
            Add(label);
            y += Line - 3;
        }

        // What's worn.
        CardInstance? belt = hero.Equipment.GetValueOrDefault(EquipSlot.Belt);
        CardInstance? buckle = hero.Equipment.GetValueOrDefault(EquipSlot.Buckle);
        if (belt is null)
        {
            Row("Beltless");
        }
        else
        {
            Row($"{belt.Definition.Name} ({hero.BeltUsed}/{hero.BeltCapacity})", "Unbuckle", session.CheckUnbuckle(), id => new UnbuckleBelt(id),
                "Take the belt off whole: its buckle, mods and cards stay on it, in your bag, to buckle on again as it was.");
        }

        if (buckle is not null)
        {
            Row(buckle.Definition.Name, "Take off", session.CheckStow(buckle.Id), id => new StowCard(id, buckle.Id), buckle.Definition.Text);
        }

        foreach (CardInstance mod in hero.Mods)
        {
            Row($"  {mod.Definition.Name}", "Take off", session.CheckStow(mod.Id), id => new StowCard(id, mod.Id), mod.Definition.Text);
        }

        // What could go on, from the bag or the arms.
        List<CardInstance> spare = [.. hero.Backpack.Concat(hero.Carried).Where(card => card.Definition.Type is CardType.Belt or CardType.Buckle or CardType.Mod)];
        if (spare.Count > 0)
        {
            Heading("In your bag");
            foreach (CardInstance card in spare)
            {
                string packed = card.Packed is { } on ? $" (packed: {on.Held.Sum(held => held.Count)} cards)" : "";
                Row($"{card.Definition.Name}{packed}", card.Packed is not null ? "Buckle on" : "Put on", session.CheckEquip(card.Id), id => new EquipCard(id, card.Id), card.Definition.Text);
            }
        }

        // The belt square's art, from the belts and buckles owned.
        Heading("Look");
        LookRow("Belt", hero, CardType.Belt, hero.Appearance.BeltLook, session, look => id => new ChooseBeltLook(id, look, hero.Appearance.BuckleLook));
        LookRow("Buckle", hero, CardType.Buckle, hero.Appearance.BuckleLook, session, look => id => new ChooseBeltLook(id, hero.Appearance.BeltLook, look));
        // What shows on the hero's figure on the map, slot by slot.
        Heading("On your figure");
        foreach ((EquipSlot slot, string name) in new[] { (EquipSlot.Belt, "Belt"), (EquipSlot.Head, "Head"), (EquipSlot.MainHand, "Main hand"), (EquipSlot.OffHand, "Off hand") })
        {
            bool shows = hero.Appearance.Shows(slot);
            Row($"{name}: {(shows ? "shown" : "hidden")}", shows ? "Hide" : "Show", null, id => new ShowGear(id, slot, !shows),
                $"Whether your {name.ToLowerInvariant()}'s gear shows on your figure on the map.");
        }

        Size = new Vector2(Width, y + 4);

        void LookRow(string what, Hero owner, CardType type, string current, GameSession check, Func<string, Func<int, GameCommand>> choose)
        {
            string? worn = owner.Equipment.GetValueOrDefault(type == CardType.Belt ? EquipSlot.Belt : EquipSlot.Buckle)?.Definition.Id;
            List<string> looks = ["", .. owner.Looks(type).Where(id => id != worn)];
            int at = Math.Max(0, looks.IndexOf(current));
            string name = looks[at].Length == 0 ? "the one worn" : check.Content.GetCard(looks[at]).Name;
            string next = looks[(at + 1) % looks.Count];
            Row($"{what}: {name}", looks.Count > 1 ? "Next look" : null, null, choose(next), $"Cycle through the {what.ToLowerInvariant()}s you own for the belt square's art.");
        }
    }

    private void Add(Control control)
    {
        AddChild(control);
        _rows.Add(control);
    }
}
