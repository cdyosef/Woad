using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Woarld;

/// <summary>
/// A party member at a glance, for the Party menu: their figure and crest, name and level, purse and
/// hearts, belt and bag slots in use, a little sheet of which slots hold gear, and their ability dice
/// with what's added to each. Click it to pick them.
/// </summary>
public partial class PartyMemberCard : Button
{
    public const float Width = 112;
    public const float Height = 98;

    /// <summary>The slots of the little sheet, in order.</summary>
    private static readonly EquipSlot[] Slots =
        [EquipSlot.Head, EquipSlot.Body, EquipSlot.MainHand, EquipSlot.OffHand, EquipSlot.Feet, EquipSlot.Accessory, EquipSlot.Belt, EquipSlot.Buckle];

    public Character Character { get; init; } = null!;

    public ContentCatalog Content { get; init; } = null!;

    public (int Now, int Max) Hearts { get; init; }

    public override void _Ready()
    {
        CustomMinimumSize = Size = new Vector2(Width, Height);
        ToggleMode = true;
        TooltipText = $"{Character.Name}'s things{(Character.IsCompanion ? " (a companion)" : "")}.";
    }

    public override void _Draw()
    {
        Font font = GetThemeDefaultFont();
        Color ink = ButtonPressed ? Palette.TrimBright : Palette.Text;
        int small = UiTheme.FontSize;

        // Figure and crest, then name and level, purse and hearts.
        Looks.Draw(this, new Rect2(3, 3, 24, 24), Looks.Resolve(Character.Appearance, Character.Portrait));
        PortraitView.DrawCrest(this, new Rect2(28, 4, 13, 16), Portraits.Get(Character.Portrait), Looks.Resolve(Character.Appearance, Character.Portrait), Character.Appearance);
        DrawString(font, new Vector2(44, 12), Character.Name, HorizontalAlignment.Left, Width - 46, small, ink);
        DrawString(font, new Vector2(44, 22), $"L{Character.Level}{(Character.UnspentPoints > 0 ? $" +{Character.UnspentPoints}" : "")}",
            HorizontalAlignment.Left, 30, small, Character.UnspentPoints > 0 ? Palette.Power : Palette.TextDim);
        DrawString(font, new Vector2(66, 22), Money.Short(Character.Coins), HorizontalAlignment.Left, Width - 68, small, Palette.Loot);
        DrawString(font, new Vector2(4, 37), $"{Hearts.Now}/{Hearts.Max} hearts", HorizontalAlignment.Left, 60, small, Hearts.Now < Hearts.Max ? Palette.Danger : Palette.Heal);

        // Belt and bag slots in use.
        int belt = Character.BeltCapacity(Content), beltUsed = Character.BeltUsed(Content), bag = Character.Backpack.Count;
        DrawString(font, new Vector2(4, 47), belt > 0 ? $"Belt {beltUsed}/{belt}" : "No belt", HorizontalAlignment.Left, 48, small, beltUsed > belt ? Palette.Danger : Palette.TextDim);
        DrawString(font, new Vector2(60, 47), $"Bag {bag}/{GameRules.BackpackCapacity}", HorizontalAlignment.Left, 48, small, bag > GameRules.BackpackCapacity ? Palette.Danger : Palette.TextDim);

        // The little sheet: a square for each slot, holding the picture of what's in it.
        IReadOnlyDictionary<EquipSlot, string> worn = Character.WornSlots(Content);
        for (int i = 0; i < Slots.Length; i++)
        {
            var square = new Rect2(4 + i * 12, 52, 11, 11);
            DrawRect(square, new Color(Palette.Background, 0.9f));
            if (worn.TryGetValue(Slots[i], out string? key) && GameArt.Texture($"res://assets/cards/{CardKey.Id(key)}.png") is { } picture)
            {
                DrawTextureRect(picture, square.Grow(-1), tile: false);
            }
            else if (worn.ContainsKey(Slots[i]))
            {
                DrawRect(square.Grow(-2), Palette.Trim);
            }

            DrawRect(square, worn.ContainsKey(Slots[i]) ? Palette.Trim : new Color(Palette.TextDim, 0.4f), filled: false);
        }

        // Ability dice, and what's added: Rumble, Od Hand, Wend and Defense.
        Stats stats = Character.Stats(Content);
        StatAllocation bought = Character.Allocation;
        (string Icon, Color Color, int Dice, int Plus)[] pools =
        [
            ("icon_rumble", Palette.DieRumble, stats.AttackDice, stats.Weapon + bought.AttackDice),
            ("icon_odhand", Palette.DieOdHand, stats.OdHandDice, bought.OdHandDice + stats.Tools),
            ("icon_wend", Palette.DieWend, stats.MoveDice, stats.Speed),
            ("icon_defense", Palette.DieDefense, stats.DefenseDice, stats.Armor),
        ];
        for (int i = 0; i < pools.Length; i++)
        {
            (string icon, Color color, int dice, int plus) = pools[i];
            var at = new Vector2(4 + i % 2 * 50, 67 + i / 2 * 13);
            if (GameArt.Ui(icon) is { } texture)
            {
                DrawTextureRect(texture, new Rect2(at, new Vector2(10, 10)), tile: false);
            }

            DrawString(font, at + new Vector2(13, 9), $"{dice}d6{(plus != 0 ? $"{plus:+0;-0}" : "")}", HorizontalAlignment.Left, 36, small, color);
        }
    }
}
