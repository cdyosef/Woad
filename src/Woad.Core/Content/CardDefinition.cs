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

    /// <summary>Trinkets worn in the accessory slot (the Lucky Jackalope's Foot, the Icarus Wings).</summary>
    Accessory,
}

/// <summary>Body slots a card can be equipped into. The belt is separate: it holds single-use cards.</summary>
public enum EquipSlot
{
    Head,
    Body,
    MainHand,
    OffHand,
    Feet,

    /// <summary>A trinket: a lucky charm, cufflinks, a pair of wings.</summary>
    Accessory,
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

    /// <summary>
    /// The Coward's Cloak: monsters beside you move at half speed on their next turn, and your Rumble and
    /// Od Hand dice left this turn become Wend dice. Used on yourself.
    /// </summary>
    CowardsFlight,

    /// <summary>Clears <see cref="CardEffect.Cures"/> from yourself (Eye Drops, Holy Water).</summary>
    Cure,

    /// <summary>Mends your most worn card whole (the Autosmith Hammer).</summary>
    Repair,
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

    /// <summary>Attack only: stacks of Freeze laid on every target, hit or miss (the Frost Wand's).</summary>
    public int Chill { get; init; }

    /// <summary>Cure only: the tokens it clears.</summary>
    public Model.Condition? Cures { get; init; }

    /// <summary>Attack only: what the dice are made of (fire, ice...), or null for plain Star dice.</summary>
    public Model.DieType? DieType { get; init; }

    /// <summary>Fire dice: aimed at bare floor they set it burning, and they set off a bomb they touch.</summary>
    public bool Fire => DieType == Model.DieType.Fire;
}

public sealed record CardDefinition
{
    /// <summary>A scroll's spell: the one it can be learned as (see <see cref="SpellDefinition"/>), or null.</summary>
    public string? Teaches { get; init; }

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

    /// <summary>
    /// Worn, it's the last line of defense: when a defense is still losing after everything else, it
    /// rolls one more die in, and then breaks (the Lucky Helmet).
    /// </summary>
    public bool LastChanceDie { get; init; }

    /// <summary>Weapons: its reach is a straight line, up and down or left and right (the Spear), rather than any direction in sight.</summary>
    public bool Reach { get; init; }

    /// <summary>
    /// Weapons: its bonus goes on two attacks a turn rather than one, while the off hand is empty or holds
    /// something <see cref="Light"/> (the Rapier).
    /// </summary>
    public bool TwinStrike { get; init; }

    /// <summary>Light enough not to get in the way of a <see cref="TwinStrike"/> blade (the Buckler, a Dagger).</summary>
    public bool Light { get; init; }

    /// <summary>Weapons: held in the off hand, it adds its bonus to a second attack of its own each turn (a Dagger).</summary>
    public bool OffHandStrike { get; init; }

    /// <summary>
    /// A torch: held (one to a hand), it's lit: +1d6 to Search rolls, light, and a fire die on Rumble
    /// attacks (a 6 rolls another, and on a hit sets the target alight); any 1 in the attack burns it out.
    /// </summary>
    public bool Torch { get; init; }

    /// <summary>
    /// Weapons: thrown at a target this far off (in sight), it leaves the hand and lands at the target's feet
    /// (a Dagger, one from the stack). 0 for weapons that can't be thrown away.
    /// </summary>
    public int ThrownRange { get; init; }

    /// <summary>Weapons: its bonus doubles against a monster that's stunned or lying prone (a Dagger).</summary>
    public bool Ambush { get; init; }

    /// <summary>Weapons: each 6 on its attack also strikes one more monster beside the attacker, with the same total (the Great Axe).</summary>
    public bool Cleave { get; init; }

    /// <summary>Weapons: any 6 on its attack shoves the target a tile back, hit or miss (the Maul).</summary>
    public bool ShoveOnSix { get; init; }

    /// <summary>Weapons: what its own dice (<see cref="Stats.WeaponDice"/>) are made of (the Frost Brand's ice), or null for plain ones.</summary>
    public Model.DieType? DieType { get; init; }

    /// <summary>Worn: the wearer's Rumble dice are made of this (the Storm Mail's lightning). They earn no Power Dice, but their 6s roll more in.</summary>
    public Model.DieType? RumbleType { get; init; }

    /// <summary>Weapons: its hits crush: a shelled monster is knocked onto its back, and bones it breaks stay broken (the War Hammer).</summary>
    public bool Crush { get; init; }

    /// <summary>Weapons: after a shot it's unloaded until reloaded with these dice (a crossbow); null for weapons that need none.</summary>
    public ReloadCost? Reload { get; init; }

    /// <summary>
    /// Worn, they fly (the Icarus Wings): beat them for a d6 of flight, as often as you dare. A 6 rolls a
    /// Power Die straight in. A 1 breaks them: you drop where you are, prone and stunned, Wend gone.
    /// </summary>
    public bool Wings { get; init; }

    /// <summary>Worn gear with a use that destroys it (the Coward's Cloak): equip it, use it once, and it's gone.</summary>
    public bool BreaksOnUse { get; init; }

    /// <summary>A set bonus: more modifiers while worn together with its partner card (the Knight's Sword and Shield).</summary>
    public CardSet? Set { get; init; }

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

/// <summary>Extra modifiers a card grants while its partner card is equipped too.</summary>
public sealed record CardSet
{
    /// <summary>The partner card's id.</summary>
    public required string With { get; init; }

    public Stats Modifiers { get; init; } = Stats.None;
}

/// <summary>The dice a crossbow takes to reload: Od Hand dice, and Wend dice for a heavy one.</summary>
public sealed record ReloadCost
{
    public int OdHand { get; init; }

    public int Wend { get; init; }
}
