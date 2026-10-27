namespace Woad.Core.Content;

/// <summary>
/// A card as a character keeps it between runs: its id, and after a "~" the wear it has taken (acid eats
/// armor, and it stays eaten until a blacksmith or an Autosmith Hammer mends it). "chain_mail~2" is chain
/// mail worn by 2.
/// </summary>
public static class CardKey
{
    private const char Mark = '~';

    /// <summary>The card's id, without its wear.</summary>
    public static string Id(string key) => key.IndexOf(Mark) is var at and >= 0 ? key[..at] : key;

    /// <summary>The wear it has taken (0 when whole).</summary>
    public static int Wear(string key) => key.IndexOf(Mark) is var at and >= 0 && int.TryParse(key[(at + 1)..], out int wear) ? Math.Max(0, wear) : 0;

    /// <summary>The key for a card with this much wear.</summary>
    public static string Of(string id, int wear) => wear > 0 ? $"{id}{Mark}{wear}" : id;

    /// <summary>What a card adds worn: its modifiers, less its wear (off its armor, or a weapon's value).</summary>
    public static Stats Modifiers(CardDefinition card, int wear)
    {
        Stats modifiers = card.Modifiers;
        if (wear <= 0)
        {
            return modifiers;
        }

        return modifiers.Armor > 0
            ? modifiers with { Armor = Math.Max(0, modifiers.Armor - wear) }
            : modifiers with { Weapon = Math.Max(0, modifiers.Weapon - wear) };
    }

    /// <summary>The most wear a card can take: all its armor, or all of a weapon's value.</summary>
    public static int MaxWear(CardDefinition card) => card.Modifiers.Armor > 0 ? card.Modifiers.Armor : Math.Max(0, card.Modifiers.Weapon);
}
