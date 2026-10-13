using Woad.Core.Dice;

namespace Woad.Core.Model;

public enum DamageSource
{
    Weapon,
    Spell,
    Bomb,
    Monster,
}

/// <summary>A hero's attack whose dice are rolled but not yet resolved, waiting for Luck rerolls.</summary>
public sealed class PendingAttack
{
    internal PendingAttack(int attackerId, DiceRoll roll, int bonus, IReadOnlyList<int> targetIds, DamageSource source)
    {
        AttackerId = attackerId;
        Roll = roll;
        Bonus = bonus;
        TargetIds = targetIds;
        Source = source;
    }

    public int AttackerId { get; }

    public DiceRoll Roll { get; internal set; }

    /// <summary>The weapon's fixed value added to the roll, or 0.</summary>
    public int Bonus { get; }

    public int Total => Roll.Total + Bonus;

    public IReadOnlyList<int> TargetIds { get; }

    public DamageSource Source { get; }
}

/// <summary>
/// What the active hero has spent so far this turn: Attack and Move dice (each attack or move
/// uses one or more), the weapon and Speed (once each), the one other action and the free belt item.
/// </summary>
public sealed class TurnState
{
    internal TurnState(int heroId) => HeroId = heroId;

    public int HeroId { get; }

    public int AttackDiceUsed { get; internal set; }

    /// <summary>The weapon's value has been added to an attack this turn.</summary>
    public bool WeaponUsed { get; internal set; }

    public int MoveDiceUsed { get; internal set; }

    /// <summary>Speed has been added to a move this turn.</summary>
    public bool SpeedUsed { get; internal set; }

    /// <summary>The roll of the move in progress; null when no move is under way.</summary>
    public DiceRoll? MovementRoll { get; internal set; }

    /// <summary>Speed added to the move in progress.</summary>
    public int MovementBonus { get; internal set; }

    /// <summary>Steps left in the move in progress.</summary>
    public int MovementPoints { get; internal set; }

    /// <summary>At least one step has been taken in the move in progress (its dice can't be rerolled now).</summary>
    public bool HasMoved { get; internal set; }

    /// <summary>The turn's one other action (opening a chest, equipping, using a backpack card...) is used.</summary>
    public bool HasActed { get; internal set; }

    /// <summary>The once-per-turn free belt item has been used.</summary>
    public bool UsedBeltItem { get; internal set; }

    public PendingAttack? PendingAttack { get; internal set; }

    /// <summary>
    /// Power Dice just earned by sixes on the pending attack or the move in progress, waiting for
    /// the hero to add each to that roll or bank it. Nothing else can happen until they're settled.
    /// </summary>
    public int UnsettledPowerDice { get; internal set; }

    /// <summary>Ends the move in progress; its unused steps are lost.</summary>
    internal void EndMove()
    {
        MovementRoll = null;
        MovementBonus = 0;
        MovementPoints = 0;
        HasMoved = false;
    }
}
