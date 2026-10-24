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

    /// <summary>Rouse an adjacent hero Keeling Over: every 6 of the total is a mark of life for them.</summary>
    Rouse,

    /// <summary>Put out a lit bomb next to you (or under you): 10 or more, or it goes off in your hands.</summary>
    DefuseBomb,

    /// <summary>Pick the lock of the chains holding a merchant beside you.</summary>
    PickChains,
}

/// <summary>
/// What an Od Hand roll is for. <see cref="Difficulty"/> is the total it must reach (0 for a steal,
/// which is a contest). <see cref="Target"/> is the tile involved; <see cref="TargetId"/> who is robbed.
/// </summary>
public sealed record OdHandCheck(OdHandCheckKind Kind, int Difficulty, GridPosition Target = default, int TargetId = 0);

/// <summary>
/// A hero's roll whose dice are rolled but not yet resolved, waiting for Luck rerolls or Power Dice
/// choices: an attack, or an Od Hand <see cref="Check"/>.
/// </summary>
public sealed class PendingRoll
{
    internal PendingRoll(int heroId, DiceRoll roll, int bonus, IReadOnlyList<int> targetIds, DamageSource source, OdHandCheck? check = null, bool fire = false, bool stunOnSix = false)
    {
        HeroId = heroId;
        Roll = roll;
        Bonus = bonus;
        TargetIds = targetIds;
        Source = source;
        Check = check;
        Fire = fire;
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

    /// <summary>The dice are fire (the Flame Scroll): any 6 among them sets the targets alight, hit or miss.</summary>
    public bool Fire { get; }

    /// <summary>A stunning weapon (the Boomerang): every 6 among the dice stuns the targets, hit or miss.</summary>
    public bool StunOnSix { get; }

    /// <summary>How many of the dice, from the first, aren't Power Dice (the rest are, and those added later).</summary>
    public int NonPowerDice { get; internal init; } = int.MaxValue;

    /// <summary>The weapon the attack was made with, for what it does as the attack lands (cleave, shove, crush); null bare-handed.</summary>
    public CardDefinition? Weapon { get; internal init; }

    /// <summary>The tools (lockpicks) went into this roll: if it fails with a 1 among its non-Power dice, a pick snaps.</summary>
    public bool UsedTools { get; internal init; }
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

    /// <summary>Speed has been added to a move this turn.</summary>
    public bool SpeedUsed { get; internal set; }

    public int OdHandDiceUsed { get; internal set; }

    /// <summary>The tools' value has been added to an Od Hand roll this turn.</summary>
    public bool ToolsUsed { get; internal set; }

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

    /// <summary>Sets the steps from the move's roll (after a reroll or an added Power Die), plus the carried-over steps.</summary>
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
