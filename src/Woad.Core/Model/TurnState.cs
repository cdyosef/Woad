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
    internal PendingAttack(int attackerId, DiceRoll roll, IReadOnlyList<int> targetIds, DamageSource source)
    {
        AttackerId = attackerId;
        Roll = roll;
        TargetIds = targetIds;
        Source = source;
    }

    public int AttackerId { get; }

    public DiceRoll Roll { get; internal set; }

    public IReadOnlyList<int> TargetIds { get; }

    public DamageSource Source { get; }
}

/// <summary>What the active hero has done so far this turn.</summary>
public sealed class TurnState
{
    internal TurnState(int heroId) => HeroId = heroId;

    public int HeroId { get; }

    /// <summary>Null until the hero rolls for movement.</summary>
    public DiceRoll? MovementRoll { get; internal set; }

    public int MovementPoints { get; internal set; }

    /// <summary>At least one step has been taken.</summary>
    public bool HasMoved { get; internal set; }

    /// <summary>The hero moved and then acted, so the move is over.</summary>
    public bool MovementLocked { get; internal set; }

    public bool HasActed { get; internal set; }

    /// <summary>The once-per-turn free belt item has been used.</summary>
    public bool UsedBeltItem { get; internal set; }

    public PendingAttack? PendingAttack { get; internal set; }
}
