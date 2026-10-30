using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Grid;

namespace Woad.Core.Model;

public enum DamageSource
{
    Weapon,
    Spell,
    Bomb,
    Monster,

    /// <summary>A shove: the roll pushes the monster instead of hurting it.</summary>
    Shove,

    /// <summary>A fall or a trap a monster was shoved into. Bones broken this way stay down.</summary>
    Fall,

    /// <summary>Burning, at the start of a turn. Bones burned this way stay down.</summary>
    Fire,
}

public enum OdHandCheckKind
{
    /// <summary>Pick the lock of the door the hero stands in.</summary>
    PickDoor,

    /// <summary>Pick the lock of an adjacent chest.</summary>
    PickChest,

    /// <summary>Pick the lock of an adjacent gate in a wall dividing the room.</summary>
    PickGate,

    /// <summary>Search the whole room for secrets.</summary>
    Search,

    /// <summary>Disarm a revealed trap on an adjacent tile, an adjacent chest or the door the hero stands in.</summary>
    Disarm,

    /// <summary>Steal from an adjacent monster or hero (a contest against their roll).</summary>
    Steal,

    /// <summary>Aid an adjacent hero Keeling Over: every 6 of the total is a mark of life for them.</summary>
    Rouse,

    /// <summary>Put out a lit bomb next to you (or under you): 10 or more, or it goes off in your hands.</summary>
    DefuseBomb,

    /// <summary>Pick the lock of the chains holding a merchant beside you.</summary>
    PickChains,

    /// <summary>Pick the lock of the shackles on yourself or an ally beside you.</summary>
    PickShackles,

    /// <summary>Find a way out of a fight on the Woarld Map (a contest against the monsters' chase).</summary>
    Retreat,

    /// <summary>Aid that cures an ailment, your own or an ally's beside you: 10 plus its stacks.</summary>
    Cure,

    /// <summary>Learn the spell on a scroll (Magic adds to the roll): 6 plus the spell's level, or it backfires.</summary>
    Learn,
}

/// <summary>
/// What an Od Hand roll is for. <see cref="Difficulty"/> is the total it must reach (0 for a steal,
/// which is a contest). <see cref="Target"/> is the tile involved; <see cref="TargetId"/> who is robbed;
/// <see cref="SpellId"/> the spell being learned.
/// </summary>
public sealed record OdHandCheck(OdHandCheckKind Kind, int Difficulty, GridPosition Target = default, int TargetId = 0, string? SpellId = null, Ailment? Cure = null);

/// <summary>
/// A hero's roll whose dice are rolled but not yet resolved, waiting for Luck rerolls or Power Dice
/// choices: an attack, or an Od Hand <see cref="Check"/>.
/// </summary>
/// <summary>
/// What a spell's roll carries besides its dice: the spell, the tile it was aimed at, extra dice
/// rolled against some targets (by id: their total and their sixes), the Rumble dice for Squall Wave's
/// shove and where Swirly Eyes moves the target.
/// </summary>
public sealed record SpellCasting(SpellDefinition Spell, GridPosition? Aim, IReadOnlyDictionary<int, (int Total, int Sixes)> Extra, int ShoveDice, GridPosition? MoveTo);

public sealed class PendingRoll
{
    internal PendingRoll(int heroId, DiceRoll roll, int bonus, IReadOnlyList<int> targetIds, DamageSource source, OdHandCheck? check = null, bool stunOnSix = false)
    {
        HeroId = heroId;
        Roll = roll;
        Bonus = bonus;
        TargetIds = targetIds;
        Source = source;
        Check = check;
        StunOnSix = stunOnSix;
    }

    public int HeroId { get; }

    public DiceRoll Roll { get; internal set; }

    /// <summary>The weapon's or tools' fixed value added to the roll, or 0.</summary>
    public int Bonus { get; }

    public int Total => Roll.Total + Bonus;

    /// <summary>Monsters an attack is aimed at (empty for Od Hand checks).</summary>
    public IReadOnlyList<int> TargetIds { get; }

    public DamageSource Source { get; }

    /// <summary>The Od Hand check this roll is for, or null for an attack.</summary>
    public OdHandCheck? Check { get; }

    /// <summary>A stunning weapon (the Boomerang): on a hit, every 6 among the dice stuns the target.</summary>
    public bool StunOnSix { get; }

    /// <summary>How many of the dice, from the first, aren't Power Dice (the rest are, and those added later).</summary>
    public int NonPowerDice { get; internal init; } = int.MaxValue;

    /// <summary>The weapon the attack was made with, for what it does as the attack lands (cleave, shove, crush); null bare-handed.</summary>
    public CardDefinition? Weapon { get; internal init; }

    /// <summary>The tools (lockpicks) went into this roll: if it fails with a 1 among its non-Power dice, a pick snaps.</summary>
    public bool UsedTools { get; internal init; }

    /// <summary>An Aid roll a Medicine Bag helped: if it fails with a 1 among its dice, a bag is used up.</summary>
    public bool UsedMedicineBag { get; internal init; }

    /// <summary>The blow at the end of a bull rush: a hit pushes the monster two tiles, a miss leaves the hero prone.</summary>
    public bool BullRush { get; internal init; }

    /// <summary>Torches' fire dice went into this attack: a 1 anywhere in it burns one out.</summary>
    public int Torches { get; internal init; }

    /// <summary>Stacks of Freeze laid on every target as the attack lands, hit or miss (the Frost Wand).</summary>
    public int Chill { get; internal init; }

    /// <summary>What each face is made of (fire, ice...), in order; Plain past its end (Power Dice added later).</summary>
    public IReadOnlyList<DieType> DieTypes { get; internal init; } = [];

    /// <summary>The spell this roll casts, and what comes of it besides the roll (null for anything else).</summary>
    public SpellCasting? Casting { get; internal init; }
}

/// <summary>
/// What the active hero has spent so far this turn: Rumble, Wend and Od Hand dice (each attack, move
/// or check uses one or more), the weapon, Speed and tools (once each), the one other action, the free
/// belt item, the free loot and the one steal.
/// </summary>
public sealed class TurnState
{
    internal TurnState(int heroId) => HeroId = heroId;

    public int HeroId { get; }

    /// <summary>The hero began the turn Keeling Over: all they can do is roll the Keel Die.</summary>
    public bool KeelingOver { get; internal init; }

    public int AttackDiceUsed { get; internal set; }

    /// <summary>Main-hand weapon strikes used this turn: each adds the weapon's value to an attack (most weapons get one a turn, a Rapier two).</summary>
    public int StrikesUsed { get; internal set; }

    /// <summary>An off-hand Dagger has added its value to an attack this turn.</summary>
    public bool OffHandStrikeUsed { get; internal set; }

    public int MoveDiceUsed { get; internal set; }

    /// <summary>Wend dice gained this turn (the Coward's Cloak turns Rumble and Od Hand dice into them).</summary>
    public int ExtraMoveDice { get; internal set; }

    /// <summary>Speed has been added to a move this turn.</summary>
    public bool SpeedUsed { get; internal set; }

    public int OdHandDiceUsed { get; internal set; }

    /// <summary>The tools' value has been added to an Od Hand roll this turn.</summary>
    public bool ToolsUsed { get; internal set; }

    /// <summary>Rumble dice spent on High Alert, waiting to roll in with the turn's next search.</summary>
    public int AlertDice { get; internal set; }

    /// <summary>A Medicine Bag has helped an Aid roll this turn (once a turn).</summary>
    public bool MedicineBagUsed { get; internal set; }

    /// <summary>The once-per-turn free loot (picking up or opening a chest) has been used.</summary>
    public bool UsedFreeLoot { get; internal set; }

    /// <summary>The pile (or chest) looted this turn: taking more from it costs nothing more.</summary>
    public GridPosition? LootedTile { get; internal set; }

    /// <summary>The hero has tried to steal this turn (once per turn).</summary>
    public bool HasStolen { get; internal set; }

    /// <summary>The roll of the move in progress; null when no move is under way.</summary>
    public DiceRoll? MovementRoll { get; internal set; }

    /// <summary>Speed added to the move in progress.</summary>
    public int MovementBonus { get; internal set; }

    /// <summary>Steps left this turn: the move in progress plus any carried over from earlier moves.</summary>
    public int MovementPoints { get; internal set; }

    /// <summary>Steps left over from earlier moves this turn when the move in progress was rolled.</summary>
    public int CarriedSteps { get; internal set; }

    /// <summary>At least one step has been taken in the move in progress (its dice can't be rerolled now).</summary>
    public bool HasMoved { get; internal set; }

    /// <summary>The turn's one other action (opening a chest, equipping, using a backpack card...) is used.</summary>
    public bool HasActed { get; internal set; }

    /// <summary>The once-per-turn free belt item has been used.</summary>
    public bool UsedBeltItem { get; internal set; }

    public PendingRoll? PendingRoll { get; internal set; }

    /// <summary>
    /// Power Dice just earned by sixes on the pending attack or the move in progress, waiting for
    /// the hero to add each to that roll or bank it. Nothing else can happen until they're settled.
    /// </summary>
    public int UnsettledPowerDice { get; internal set; }

    /// <summary>Patches of an Icicle Arrow's ice still to lay this turn, how many rounds they last, and the spell (for its range).</summary>
    public int IceToLay { get; internal set; }

    public int IceRounds { get; internal set; }

    public string? IceSpellId { get; internal set; }

    /// <summary>Sets the steps from the move's roll (after a reroll or an added Power Die), plus the carried-over steps.</summary>
    /// <summary>Flight left from the Icarus Wings, apart from Wend.</summary>
    public int FlightPoints { get; internal set; }

    internal void CountSteps() => MovementPoints = CarriedSteps + Math.Max(0, MovementRoll!.Total + MovementBonus);

    /// <summary>Ends movement for the turn so far (a sprung trap or a fall); every unused step is lost.</summary>
    internal void EndMove()
    {
        MovementRoll = null;
        MovementBonus = 0;
        MovementPoints = 0;
        CarriedSteps = 0;
        HasMoved = false;
    }
}
