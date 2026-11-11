namespace Woad.Core.Model;

/// <summary>
/// How a character looks: which figure (<paramref name="Look"/>) and its colors. Purely cosmetic; the
/// game maps the ids to art and treats unknown or empty ones as that look's defaults.
/// </summary>
/// <param name="BeltLook">The belt art worn on the belt square: a belt card's id the hero owns, or empty for the belt worn.</param>
/// <param name="BuckleLook">Likewise for the buckle.</param>
/// <param name="HiddenGear">The slots whose gear isn't shown on the hero's figure on the map, by name, comma-separated.</param>
/// <param name="CrestField">The crest's field pattern (empty: plain).</param>
/// <param name="CrestCharge">The crest's charge (empty: the portrait's own emblem).</param>
/// <param name="CrestColor">The crest field's color (empty: follows the character's main color).</param>
/// <param name="CrestInk">The charge's color (empty: follows the character's main color).</param>
/// <param name="Languages">The languages they speak besides Common, by id, comma-separated (see <see cref="Content.Languages"/>).</param>
public sealed record Appearance(string Look = "", string Main = "", string Trim = "", string Skin = "", string Hair = "", string BeltLook = "", string BuckleLook = "", string HiddenGear = "",
    string CrestField = "", string CrestCharge = "", string CrestColor = "", string CrestInk = "", string Languages = "", string Persona = "")
{
    public static Appearance Default { get; } = new();

    /// <summary>The languages they speak: Common, and the rest they know.</summary>
    public IReadOnlyList<Content.Language> Speaks => Content.Languages.Known(Languages);

    /// <summary>Whether the gear in <paramref name="slot"/> shows on the hero's figure.</summary>
    public bool Shows(Content.EquipSlot slot) => !HiddenGear.Split(',').Contains(slot.ToString());

    /// <summary>This look with the gear in <paramref name="slot"/> shown on the figure, or hidden.</summary>
    public Appearance Showing(Content.EquipSlot slot, bool show)
    {
        IEnumerable<string> hidden = HiddenGear.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(name => name != slot.ToString());
        return this with { HiddenGear = string.Join(",", show ? hidden : hidden.Append(slot.ToString())) };
    }
}
