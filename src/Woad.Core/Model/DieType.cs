namespace Woad.Core.Model;

/// <summary>
/// What a die is made of. A Plain die's 6 earns a Power Die, to add to the roll or bank. A Star die's 6
/// surges: one Power Die rolls straight in, another goes to the bank or, with the bank full, rolls in
/// too. Every other type earns no Power Die: its 6 rolls another die of the same type straight in, and
/// on a hit lays that type's effect on the target, a stack for each 6.
/// </summary>
public enum DieType
{
    /// <summary>An ordinary die: see <see cref="DieType"/>.</summary>
    Plain,

    /// <summary>Power Surge: see <see cref="DieType"/>.</summary>
    Star,

    /// <summary>Burn: a fire die on the target, attacking them at the start of their turn.</summary>
    Fire,

    /// <summary>Poisoned: a Poison token (see <see cref="Condition.Poison"/>).</summary>
    Poison,

    /// <summary>Freeze: a stack of Freeze (see <see cref="Condition.Freeze"/>).</summary>
    Ice,

    /// <summary>Dissolve: a point of armor eaten away, or with none left an Acid token.</summary>
    Acid,

    /// <summary>Necrosis: a Black Heart (see <see cref="Condition.BlackHeart"/>).</summary>
    Necro,

    /// <summary>A gust: pushed a tile away, hurt for every tile something stops them.</summary>
    Squall,

    /// <summary>Shine: a Shine token (see <see cref="Condition.Shine"/>).</summary>
    Radiate,

    /// <summary>Plants grow where the target stands; where they've grown already, a Sentient Shrub springs up.</summary>
    Life,

    /// <summary>Quake: knocked prone, or already prone, buried.</summary>
    Earth,

    /// <summary>Electroshock: a Stun.</summary>
    Elektrik,

    /// <summary>Confuse: a Confuse token (see <see cref="Condition.Confused"/>).</summary>
    Psycho,

    /// <summary>Blind: a Blind token (see <see cref="Condition.Blind"/>).</summary>
    Shadow,
}

/// <summary>Tokens the typed dice leave on a hero or monster (fire and stun keep their own).</summary>
public enum Condition
{
    /// <summary>
    /// -1 to all their rolls for each. At the end of their turn they roll a die: not higher than their
    /// tokens loses a heart, and a 6 throws one off.
    /// </summary>
    Poison,

    /// <summary>
    /// The first halves their moves; each one after takes a Wend die (2 of a monster's moves). With no Wend
    /// left they're Frozen and can't act. One melts at the end of each of their turns, one more for each
    /// fire beside them as their turn starts, and one for every fire die that attacks them.
    /// </summary>
    Freeze,

    /// <summary>A monster's armor eaten away by acid, for the rest of the fight (a hero's gear wears instead: see <see cref="CardInstance.Wear"/>).</summary>
    Corroded,

    /// <summary>Each attacks them with an Acid die at the end of their next turn, and is used up.</summary>
    Acid,

    /// <summary>A lost heart that can't be healed until cured. A last heart lost this way kills outright, and the dead rise.</summary>
    BlackHeart,

    /// <summary>They glow: -1 to their defense for each. One fades at the end of each of their turns.</summary>
    Shine,

    /// <summary>Under the earth: no moving or attacking, only digging out, 3 Rumble for each.</summary>
    Buried,

    /// <summary>On their turn they lash out at someone beside them and stagger off; one wears off at the end of it.</summary>
    Confused,

    /// <summary>-1 to their attacks for each, and they can't see past the tiles beside them, until cured.</summary>
    Blind,
}

/// <summary>The tokens on one hero or monster.</summary>
public sealed class Conditions
{
    private readonly Dictionary<Condition, int> _counts = [];

    public int this[Condition condition] => _counts.GetValueOrDefault(condition);

    /// <summary>Every condition they have, with its count.</summary>
    public IReadOnlyDictionary<Condition, int> All => _counts;

    public bool Has(Condition condition) => this[condition] > 0;

    /// <summary>Changes a count by <paramref name="change"/> (never below 0), returning the new count.</summary>
    internal int Add(Condition condition, int change)
    {
        int count = Math.Max(0, this[condition] + change);
        if (count == 0)
        {
            _counts.Remove(condition);
        }
        else
        {
            _counts[condition] = count;
        }

        return count;
    }

    internal void Clear(Condition condition) => _counts.Remove(condition);
}
