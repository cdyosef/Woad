using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Skills that ready a die for later. Parry rolls an Od Hand die and Guard a Rumble die (a 6 rolls
/// another), held for the next defense of the hero, or for Guard an ally beside them, until the start of
/// the roller's next turn. High Alert spends a Rumble die on the turn's next search: it rolls in with
/// the Od Hand dice, or makes the search on its own. None of them takes the turn's action.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Why the active hero can't Parry now, or null.</summary>
    public string? CheckParry()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        if (!ActiveHero.Knows(Skill.Parry))
        {
            return "You haven't learned to Parry.";
        }

        return OdHandDiceLeft < 1 ? "You've used all your Od Hand dice this turn." : null;
    }

    /// <summary>Why the active hero can't Guard the hero (themselves, or an ally beside them) now, or null.</summary>
    public string? CheckGuard(int targetId)
    {
        if (CheckRumbleSkill(Skill.Guard, "Guard") is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        if (targetId == hero.Id)
        {
            return null;
        }

        Hero? ally = State.ActiveHeroesIn(ActiveRoom).FirstOrDefault(other => other.Id == targetId);
        return ally is null ? "There's no one there to guard."
            : ally.Position.ChebyshevDistanceTo(hero.Position) > 1 ? $"Stand next to {ally.Name} to guard them."
            : null;
    }

    /// <summary>The heroes the active hero could Guard now: themselves and the allies beside them.</summary>
    public IReadOnlyList<Hero> GetGuardTargets() =>
        State.Turn is null ? [] : [.. State.ActiveHeroesIn(ActiveRoom).Where(other => CheckGuard(other.Id) is null)];

    /// <summary>Why the active hero can't go on High Alert now, or null.</summary>
    public string? CheckHighAlert()
    {
        return State.Turn is { AlertDice: > 0 } ? "You're already on High Alert: search the room." : CheckRumbleSkill(Skill.HighAlert, "go on High Alert");
    }

    private string? CheckRumbleSkill(Skill skill, string doing)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        if (!ActiveHero.Knows(skill))
        {
            return $"You haven't learned to {doing}.";
        }

        return AttackDiceLeft < 1 ? "You've used all your Rumble dice this turn." : null;
    }

    private string? Handle(Parry command, List<GameEvent> events)
    {
        if (CheckParry() is { } error)
        {
            return error;
        }

        Turn.OdHandDiceUsed++;
        HoldDefense(ActiveHero, ActiveHero, Skill.Parry, events);
        return null;
    }

    private string? Handle(GuardHero command, List<GameEvent> events)
    {
        if (CheckGuard(command.TargetId) is { } error)
        {
            return error;
        }

        Turn.AttackDiceUsed++;
        HoldDefense(ActiveHero, State.Heroes.First(other => other.Id == command.TargetId), Skill.Guard, events);
        return null;
    }

    private string? Handle(HighAlert command, List<GameEvent> events)
    {
        if (CheckHighAlert() is { } error)
        {
            return error;
        }

        Turn.AttackDiceUsed++;
        Turn.AlertDice++;
        events.Add(new WentOnHighAlert(ActiveHero.Id));
        return null;
    }

    /// <summary>Rolls a die (a 6 rolls another) and holds it for the defender's next defense.</summary>
    private void HoldDefense(Hero by, Hero defender, Skill skill, List<GameEvent> events)
    {
        (int[] faces, _) = RollBonusDice(1);
        defender.HeldDefense.Add(new HeldDie(by.Id, skill, faces));
        events.Add(new DefenseHeld(by.Id, defender.Id, skill, new DiceRoll(faces)));
    }

    /// <summary>Dice the hero held for anyone's defense run out at the start of their turn.</summary>
    private void DropHeldDefense(Hero hero)
    {
        foreach (Hero other in State.Heroes)
        {
            other.HeldDefense.RemoveAll(held => held.ByHeroId == hero.Id);
        }
    }
}
