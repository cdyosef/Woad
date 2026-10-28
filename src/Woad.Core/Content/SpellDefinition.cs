using Woad.Core.Model;

namespace Woad.Core.Content;

/// <summary>Who a spell reaches.</summary>
public enum SpellShape
{
    /// <summary>One creature on a tile within range (and in sight, unless it ignores sight).</summary>
    Bolt,

    /// <summary>Every creature in a straight line from the caster toward the tile aimed at, up to the range (Poison Mist).</summary>
    Line,

    /// <summary>Every monster beside the caster (Quake); nothing to aim.</summary>
    Around,
}

/// <summary>Who a spell can be cast on as a boon instead of an attack (Lifeforce, Shining Light, Shadowbind).</summary>
public enum SpellWard
{
    /// <summary>It's only ever an attack.</summary>
    None,

    /// <summary>On the caster.</summary>
    Self,

    /// <summary>On the caster or an ally beside them.</summary>
    Beside,

    /// <summary>On the caster or an ally in range.</summary>
    Ally,
}

/// <summary>
/// A spell a hero can learn: a basic one from a rank of Magic, or one learned from a scroll. Casting
/// it spends banked Power Dice and the turn's Rumble dice, and rolls its typed dice plus the caster's
/// Magic rank as an attack. Each extra Power spent (up to the Magic rank) adds a die.
/// </summary>
public sealed record SpellDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>What its dice are made of.</summary>
    public required DieType DieType { get; init; }

    /// <summary>Its dice before upcasting.</summary>
    public int Dice { get; init; } = 1;

    /// <summary>Banked Power Dice it costs to cast.</summary>
    public int Cost { get; init; } = 1;

    public int Range { get; init; }

    public SpellShape Shape { get; init; }

    /// <summary>Learned with a rank of Magic (rather than from a scroll).</summary>
    public bool Basic { get; init; }

    /// <summary>How hard it is to learn from a scroll: the roll needs 6 plus this.</summary>
    public int Level { get; init; } = 1;

    /// <summary>Aims anywhere in range, walls or no (Shining Light, Shadowbind).</summary>
    public bool IgnoresSight { get; init; }

    /// <summary>The same roll splashes one random creature beside the target, friend or foe (Acid Ball).</summary>
    public bool Splash { get; init; }

    /// <summary>Each 6 jumps the same roll to another monster beside the target (Elektrobolt).</summary>
    public bool Cleave { get; init; }

    /// <summary>The caster's Rumble dice go into a shove roll against the target too (Squall Wave).</summary>
    public bool Shove { get; init; }

    /// <summary>On a hit, the caster moves the target a tile of their choice (Swirly Eyes).</summary>
    public bool Swirl { get; init; }

    /// <summary>A die more for each Poison token on the caster, and casting throws them all off (Poison Mist).</summary>
    public bool DrawsPoison { get; init; }

    /// <summary>A die more against targets standing on rubble (Quake).</summary>
    public bool RubbleDie { get; init; }

    /// <summary>A die more against targets in darkness (Shadowbind).</summary>
    public bool DarknessDie { get; init; }

    /// <summary>Who it can be cast on as a boon: its dice roll for how long and how strong, and it hurts no one.</summary>
    public SpellWard Ward { get; init; }

    /// <summary>Aimed at bare floor, it lays ice (Icicle Arrow).</summary>
    public bool Floor { get; init; }

    public string Text { get; init; } = "";
}
