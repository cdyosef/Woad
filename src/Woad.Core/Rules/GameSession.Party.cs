using Woad.Core.Characters;
using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// The Party menu's rules in a run: a player spends the points their levels bring (their own, or a
/// companion's) without waiting for the run to end, and sets how their companions manage their things.
/// Neither takes any of the turn.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Why the active player can't spend a point on <paramref name="stat"/> for <paramref name="targetId"/> (themselves or a companion of theirs) now, or null.</summary>
    public string? CheckSpendPoint(int targetId, StatKind stat)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        if (State.Heroes.FirstOrDefault(other => other.Id == targetId) is not { } target || (target != hero && target.OwnerId != hero.Id))
        {
            return "You can only level up yourself and your own companions.";
        }

        if (!target.IsInPlay)
        {
            return $"{target.Name} isn't in the dungeon any more.";
        }

        if (target.StartAllocation is not { } start || target.Allocation is not { } now)
        {
            return $"{target.Name} isn't a saved character, so they can't level up.";
        }

        int cost = PointBuy.CostPerStep(stat);
        if (target.PointsToSpend < cost)
        {
            return target.PointsToSpend == 0 ? $"{target.Name} has no points to spend." : $"That costs {cost} points; {target.Name} has {target.PointsToSpend}.";
        }

        int budget = PointBuy.Cost(start) + target.StartUnspentPoints + (target.Level - Progression.LevelForXp(target.StartXp)) * Progression.PointsPerLevel;
        return PointBuy.Check(now.With(stat, now[stat] + 1), target.Level, budget);
    }

    private string? Handle(SpendPoint command, List<GameEvent> events)
    {
        if (CheckSpendPoint(command.TargetId, command.Stat) is { } error)
        {
            return error;
        }

        Hero target = State.GetHero(command.TargetId);
        StatAllocation now = target.Allocation!;
        target.Allocation = now.With(command.Stat, now[command.Stat] + 1);
        target.BaseStats = target.BaseStats + PointBuy.StepOf(command.Stat);
        if (command.Stat == StatKind.Hearts)
        {
            // A new heart comes full.
            target.Hearts++;
        }

        events.Add(new PointSpent(target.Id, command.Stat, target.Level, target.PointsToSpend));
        return null;
    }

    /// <summary>Why the active player can't change how <paramref name="companionId"/> manages its things, or null.</summary>
    public string? CheckSetPolicy(int companionId) =>
        State.Turn is null ? "It's nobody's turn."
        : State.Heroes.FirstOrDefault(other => other.Id == companionId) is not { } companion || companion.OwnerId != ActiveHero.Id ? "That's not one of your companions."
        : null;

    private string? Handle(SetCompanionPolicy command, List<GameEvent> events)
    {
        if (CheckSetPolicy(command.CompanionId) is { } error)
        {
            return error;
        }

        State.GetHero(command.CompanionId).Policy = command.Policy;
        events.Add(new CompanionPolicySet(command.HeroId, command.CompanionId, command.Policy));
        return null;
    }
}
