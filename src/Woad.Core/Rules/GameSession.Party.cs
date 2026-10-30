using Woad.Core.Characters;
using Woad.Core.Commands;
using Woad.Core.Content;
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
    /// <summary>
    /// Why the active player can't spend a point on <paramref name="stat"/> for <paramref name="targetId"/>
    /// (themselves or a companion of theirs) now, or null. A rank of Magic needs a basic spell they don't
    /// know yet (<paramref name="spellId"/>; with none given, whether there's one left to pick).
    /// </summary>
    public string? CheckSpendPoint(int targetId, StatKind stat, string? spellId = null)
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

        int cost = PointBuy.Cost(PointBuy.Raise(now, stat)) - PointBuy.Cost(now);
        if (target.PointsToSpend < cost)
        {
            return target.PointsToSpend == 0 ? $"{target.Name} has no points to spend." : $"That costs {cost} points; {target.Name} has {target.PointsToSpend}.";
        }

        if (stat == StatKind.Magic)
        {
            List<SpellDefinition> learnable = [.. Spellbook.Learnable(target.Spells.Select(spell => spell.Id), Content)];
            if (spellId is null ? learnable.Count == 0 : learnable.All(spell => spell.Id != spellId))
            {
                return spellId is null ? $"{target.Name} knows every basic spell already." : "Pick a basic spell they don't know yet.";
            }
        }

        return PointBuy.Check(PointBuy.Raise(now, stat), target.Level, Budget(target, start));
    }

    /// <summary>All the points a hero has had to spend: what they came in with, and the levels since.</summary>
    private static int Budget(Hero target, StatAllocation start) =>
        PointBuy.Cost(start) + target.StartUnspentPoints + (target.Level - Progression.LevelForXp(target.StartXp)) * Progression.PointsPerLevel;

    /// <summary>Why <paramref name="targetId"/> (the active player's hero or companion) can't learn <paramref name="skill"/> now, or null.</summary>
    public string? CheckLearnSkill(int targetId, Skill skill)
    {
        if (State.Turn is null || State.ActiveHero is not { } player || State.Heroes.FirstOrDefault(hero => hero.Id == targetId) is not { } target)
        {
            return "It isn't anyone's turn.";
        }

        if (target != player && target.OwnerId != player.Id)
        {
            return "You can only level up yourself and your own companions.";
        }

        if (target.StartAllocation is not { } start || target.Allocation is not { } now)
        {
            return $"{target.Name} isn't a saved character, so they can't level up.";
        }

        if (now.Skills.HasFlag(skill))
        {
            return $"{target.Name} knows {skill.Name()} already.";
        }

        int cost = PointBuy.Cost(now.WithSkill(skill, true)) - PointBuy.Cost(now);
        if (target.PointsToSpend < cost)
        {
            return target.PointsToSpend == 0 ? $"{target.Name} has no points to spend." : $"That costs {cost} points; {target.Name} has {target.PointsToSpend}.";
        }

        return PointBuy.Check(now.WithSkill(skill, true), target.Level, Budget(target, start));
    }

    private string? Handle(LearnSkill command, List<GameEvent> events)
    {
        if (CheckLearnSkill(command.TargetId, command.Skill) is { } error)
        {
            return error;
        }

        Hero target = State.GetHero(command.TargetId);
        target.Allocation = target.Allocation!.WithSkill(command.Skill, true);
        target.Skills = target.Allocation.Skills;
        events.Add(new SkillLearned(target.Id, command.Skill, target.PointsToSpend));
        return null;
    }

    private string? Handle(SpendPoint command, List<GameEvent> events)
    {
        if (CheckSpendPoint(command.TargetId, command.Stat, command.SpellId) is { } error)
        {
            return error;
        }

        if (command.Stat == StatKind.Magic && command.SpellId is null)
        {
            return "Pick the basic spell this rank of Magic teaches.";
        }

        Hero target = State.GetHero(command.TargetId);
        StatAllocation now = target.Allocation!;
        target.Allocation = PointBuy.Raise(now, command.Stat);
        target.Skills = target.Allocation.Skills;
        target.BaseStats = target.BaseStats + PointBuy.StepOf(command.Stat);
        if (command.Stat == StatKind.Hearts)
        {
            // A new heart comes full.
            target.Hearts++;
        }

        events.Add(new PointSpent(target.Id, command.Stat, target.Level, target.PointsToSpend));
        if (command.Stat == StatKind.Magic)
        {
            SpellDefinition spell = Content.GetSpell(command.SpellId!);
            target.Learn(spell);
            events.Add(new SpellLearned(target.Id, spell.Id));
        }

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
