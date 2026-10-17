namespace Woad.Core.Content;

public enum CardType
{
    Weapon,
    Shield,
    Armor,
    Boots,
    Spell,
    Tool,
    Bomb,
    Potion,
    Treasure,
}

/// <summary>Body slots a card can be equipped into. The belt is separate: it holds single-use cards.</summary>
public enum EquipSlot
{
    Head,
    Body,
    MainHand,
    OffHand,
    Feet,
}

public enum CardUsage
{
    /// <summary>Kept after use. Gear with an effect (e.g. a boomerang) must be equipped to use it.</summary>
    Persistent,

    /// <summary>Discarded after one use. Usable from the belt or the backpack.</summary>
    SingleUse,

    /// <summary>Kept, but each use spends a charge. Must be equipped to use.</summary>
    Charges,
}

public enum CardEffectKind
{
    /// <summary>Restore <see cref="CardEffect.Amount"/> hearts to yourself.</summary>
    Heal,

    /// <summary>Roll <see cref="CardEffect.Amount"/> attack dice against every monster in the area.</summary>
    Attack,

    /// <summary>Freeze: the target monster skips its next <see cref="CardEffect.Amount"/> turns.</summary>
    Freeze,
}

/// <summary>What happens when a card is used.</summary>
public sealed record CardEffect
{
    public required CardEffectKind Kind { get; init; }

    public int Amount { get; init; }

    /// <summary>Maximum Manhattan distance to the target tile; 0 means your own tile.</summary>
    public int Range { get; init; }

    /// <summary>Attack only: monsters within this many tiles (diagonals count as 1) of the target are hit.</summary>
    public int AreaRadius { get; init; }

    /// <summary>Attack only: the dice are fire. Any 6 among them sets the target alight, hit or miss.</summary>
    public bool Fire { get; init; }
}

public sealed record CardDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required CardType Type { get; init; }

    /// <summary>The slot this card is equipped into, or null if it can't be equipped.</summary>
    public EquipSlot? Slot { get; init; }

    /// <summary>Another slot it can go in instead (the Boomerang, main hand or off hand): used when <see cref="Slot"/> is taken and this one isn't.</summary>
    public EquipSlot? AltSlot { get; init; }

    /// <summary>Two-handed cards fill both the main hand and the off hand.</summary>
    public bool TwoHanded { get; init; }

    public CardUsage Usage { get; init; } = CardUsage.Persistent;

    /// <summary>Uses per floor when <see cref="Usage"/> is <see cref="CardUsage.Charges"/>.</summary>
    public int Charges { get; init; }

    /// <summary>Bonuses granted while equipped.</summary>
    public Stats Modifiers { get; init; } = Stats.None;

    /// <summary>
    /// Weapons: reach of an attack with it. 1 is melee; more is ranged and needs line of sight. A thrown
    /// weapon (one with an <see cref="AttackBonus"/>) reaches this far from the off hand too.
    /// </summary>
    public int AttackRange { get; init; } = 1;

    /// <summary>A thrown weapon's own bonus, added to attacks made with it (in place of the main-hand weapon's).</summary>
    public int AttackBonus { get; init; }

    /// <summary>Every 6 rolled attacking with it adds a stack of Stun to the target, hit or miss.</summary>
    public bool StunOnSix { get; init; }

    /// <summary>Weapons: strikes submerged swimmers anywhere in its reach (others only reach them from beside them), adding this bonus against any swimmer.</summary>
    public int SwimmerBonus { get; init; }

    /// <summary>Gear: water costs the wearer 1 step a tile, and deep water needs no Rumble die to stay afloat.</summary>
    public bool Swims { get; init; }

    /// <summary>Weapons: this much more on attacks while nothing is in the off hand (wielded two-handed).</summary>
    public int Versatile { get; init; }

    /// <summary>Copies gather into one card with a count, filling one slot (lockpicks, bombs, potions, scrolls).</summary>
    public bool Stacks { get; init; }

    /// <summary>Whether copies stack: those marked so, and every treasure but coins.</summary>
    public bool IsStackable => Stacks || (Type == CardType.Treasure && !Coins);

    public CardEffect? Effect { get; init; }

    /// <summary>Treasure only: coins go straight into the finder's purse as gold instead of taking a backpack slot.</summary>
    public bool Coins { get; init; }

    /// <summary>Gold value. Treasure cards count toward the run's score.</summary>
    public int Value { get; init; }

    /// <summary>Rules text shown on the card.</summary>
    public string Text { get; init; } = "";
}
