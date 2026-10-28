using Woad.Core.Content;

namespace Woad.Core.Characters;

/// <summary>
/// The spells a character knows: a basic spell for each rank of Magic (picked as the rank is bought),
/// and any learned from scrolls.
/// </summary>
public static class Spellbook
{
    /// <summary>Why a character with Magic <paramref name="magic"/> can't know <paramref name="spells"/>, or null if they can.</summary>
    public static string? Check(int magic, IReadOnlyCollection<string> spells, ContentCatalog content)
    {
        if (spells.FirstOrDefault(id => content.FindSpell(id) is null) is { } unknown)
        {
            return $"There's no spell '{unknown}'.";
        }

        if (spells.Distinct().Count() != spells.Count)
        {
            return "A spell can only be learned once.";
        }

        int basic = spells.Count(id => content.GetSpell(id).Basic);
        return basic == magic ? null
            : basic < magic ? "Pick a basic spell for each rank of Magic."
            : "Each rank of Magic teaches only one basic spell.";
    }

    /// <summary>The basic spells someone who knows <paramref name="known"/> could pick with a new rank of Magic.</summary>
    public static IEnumerable<SpellDefinition> Learnable(IEnumerable<string> known, ContentCatalog content)
    {
        HashSet<string> knows = [.. known];
        return content.BasicSpells.Where(spell => !knows.Contains(spell.Id));
    }
}
