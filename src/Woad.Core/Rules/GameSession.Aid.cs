using Woad.Core.Content;
using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Aid: an Od Hand roll to look after yourself or an ally beside you. On a hero Keeling Over it rouses
/// them (see <see cref="ResolveRouse"/>); otherwise it cures one ailment, the patient's choice when they
/// have several: reach 10 plus its stacks and it's all gone. Od Hand alone cures Poison, Acid, Blind,
/// Shine, Confused and Buried; a Medicine Bag (on the belt or in a hand) adds +1 and 1d6 to one Aid
/// roll a turn, lets you roll with no Od Hand dice left, and cures Freeze, Black Hearts, Burning and Stun
/// too. Bandages add a die to a Rouse (one used up each time), a Shovel in hand a die against Buried.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The active hero has a Medicine Bag to hand that hasn't helped an Aid roll yet this turn.</summary>
    private bool MedicineBagReady => ActiveHero.MedicineBag is not null && State.Turn is { MedicineBagUsed: false };

    /// <summary>
    /// Why the active hero can't aid <paramref name="targetId"/> (themselves, or an ally beside them), or
    /// null. <paramref name="cure"/> names the ailment, when they have more than one.
    /// </summary>
    public string? CheckAid(int targetId, Ailment? cure = null) => CheckAidStart() ?? FindAid(targetId, cure, MedicineBagReady, out _);

    /// <summary>What Aid could cure on <paramref name="targetId"/> right now, and the total each needs.</summary>
    public IReadOnlyList<(Ailment Ailment, int Difficulty)> GetAidCures(int targetId) =>
        State.Turn is null || State.HeroesIn(ActiveRoom).FirstOrDefault(other => other.Id == targetId && other.IsActive) is not { } patient
            ? []
            : Curable(patient, MedicineBagReady);

    /// <summary>The heroes the active hero could aid right now: the fallen to rouse, and themselves or allies with something to cure.</summary>
    public IReadOnlyList<Hero> GetAidTargets() =>
        State.Turn is null || State.ActiveHero is not { IsActive: true } || CheckAidStart() is not null
            ? []
            : State.HeroesIn(ActiveRoom).Where(other => other.IsKeelingOver ? CheckAid(other.Id) is null : CanTreat(other)).ToList();

    private bool CanTreat(Hero patient) =>
        patient.IsActive && patient.Position.ChebyshevDistanceTo(ActiveHero.Position) <= 1 && Curable(patient, MedicineBagReady).Count > 0;

    /// <summary>
    /// An Aid roll needs a turn with nothing pending, and Od Hand dice left to roll on it (with the Aid
    /// skill), or a Medicine Bag ready, or Bandages to rouse with.
    /// </summary>
    private string? CheckAidStart() => CheckOdHandStart(Skill.Aid);

    private string? Handle(Aid command, List<GameEvent> events)
    {
        if (CheckAidStart() is { } error)
        {
            return error;
        }

        bool bag = command.UseTools && MedicineBagReady;
        OdHandCheck? check = null;
        if ((CheckBankedPowerDice(ActiveHero, command.PowerDice) ?? FindAid(command.TargetId, command.Cure, bag, out check)) is { } targetError)
        {
            return targetError;
        }

        // Rolled with no Od Hand dice, it takes a Medicine Bag, or Bandages on a Rouse.
        bool bandaged = check!.Kind == OdHandCheckKind.Rouse && command.UseTools && ActiveHero.Bandages is not null;
        int usable = OdHandDiceFor(Skill.Aid);
        int least = bag || bandaged ? 0 : 1;
        int dice = command.Dice ?? usable;
        if (dice < least || dice > usable)
        {
            return usable == 0
                ? ActiveHero.Knows(Skill.Aid) ? "With no Od Hand dice left, only a Medicine Bag (or Bandages, to rouse) can aid." : Unlearned(Skill.Aid)
                : $"Choose {least} to {usable} Od Hand dice.";
        }

        RollOdHand(command, dice, check, events);
        return null;
    }

    /// <summary>The fallen hero to rouse, or the ailment to cure and the total it needs.</summary>
    private string? FindAid(int targetId, Ailment? cure, bool bag, out OdHandCheck? check)
    {
        check = null;
        Hero hero = ActiveHero;
        if (State.HeroesIn(ActiveRoom).FirstOrDefault(other => other.Id == targetId) is { IsKeelingOver: true })
        {
            return FindKeeler(targetId, out check);
        }

        if (State.HeroesIn(ActiveRoom).FirstOrDefault(other => other.Id == targetId && other.IsActive) is not { } patient)
        {
            return "There's nobody there to aid.";
        }

        if (patient.Position.ChebyshevDistanceTo(hero.Position) > 1)
        {
            return $"Stand next to {patient.Name} to aid them.";
        }

        IReadOnlyList<(Ailment Ailment, int Difficulty)> cures = Curable(patient, bag);
        string who = patient == hero ? "You" : patient.Name;
        if (cures.Count == 0)
        {
            return Curable(patient, bag: true).Count > 0
                ? $"Only a Medicine Bag can help with what ails {(patient == hero ? "you" : patient.Name)}."
                : $"{who} {(patient == hero ? "aren't" : "isn't")} ailing.";
        }

        if (cure is null && cures.Count > 1)
        {
            return $"Choose what to cure: {string.Join(", ", cures.Select(option => option.Ailment.Name()))}.";
        }

        Ailment chosen = cure ?? cures[0].Ailment;
        if (cures.FirstOrDefault(option => option.Ailment == chosen) is not { Difficulty: > 0 } found)
        {
            return Ailments.MedicineBagCures.Contains(chosen) && chosen.Stacks(patient.Conditions, patient.FireDice, patient.Stun) > 0
                ? $"Curing {chosen.Name()} takes a Medicine Bag."
                : $"{who} {(patient == hero ? "have" : "has")} no {chosen.Name()} to cure.";
        }

        check = new OdHandCheck(OdHandCheckKind.Cure, found.Difficulty, patient.Position, patient.Id, Cure: chosen);
        return null;
    }

    /// <summary>The ailments on a hero Aid can cure (with a Medicine Bag or not), and the total each needs: 10 plus its stacks.</summary>
    private static IReadOnlyList<(Ailment Ailment, int Difficulty)> Curable(Hero patient, bool bag) =>
        [
            .. Ailments.AidCures.Concat(bag ? Ailments.MedicineBagCures : [])
                .Select(ailment => (Ailment: ailment, Stacks: ailment.Stacks(patient.Conditions, patient.FireDice, patient.Stun)))
                .Where(found => found.Stacks > 0)
                .Select(found => (found.Ailment, GameRules.AidDifficulty + found.Stacks)),
        ];

    /// <summary>
    /// What helps an Aid roll: a Medicine Bag (+1 and a die, once a turn), Bandages on a Rouse (a die,
    /// one used up), a Shovel in hand against Buried (a die). Returns whether the bag helped and the dice they add.
    /// </summary>
    private (bool Bag, int Dice) AidKit(Hero hero, OdHandCommand command, OdHandCheck check, List<GameEvent> events)
    {
        if (check.Kind is not (OdHandCheckKind.Rouse or OdHandCheckKind.Cure))
        {
            return (false, 0);
        }

        bool bag = command.UseTools && MedicineBagReady;
        Turn.MedicineBagUsed |= bag;
        int dice = bag ? 1 : 0;
        if (check.Kind == OdHandCheckKind.Rouse && command.UseTools && hero.Bandages is { } bandages)
        {
            UseUp(hero, bandages, events);
            dice++;
        }

        if (check.Cure == Ailment.Buried && hero.HoldsShovel)
        {
            dice++;
        }

        return (bag, dice);
    }

    /// <summary>One off a stack of a single-use card, or the card gone when it was the last.</summary>
    private static void UseUp(Hero hero, CardInstance card, List<GameEvent> events)
    {
        if (card.Count > 1)
        {
            card.Count--;
            card.BroughtInCount = Math.Min(card.BroughtInCount, card.Count);
            events.Add(new CardConsumed(hero.Id, card.Id, card.Count));
        }
        else
        {
            hero.Remove(card);
            events.Add(new CardConsumed(hero.Id, card.Id));
        }
    }

    /// <summary>An Aid that reaches its total clears the whole ailment.</summary>
    private void ResolveCure(Hero hero, PendingRoll roll, OdHandCheck check, bool success, List<GameEvent> events)
    {
        SpillMedicineBag(hero, roll, success, events);
        Hero patient = State.GetHero(check.TargetId);
        if (!success)
        {
            events.Add(new CheckFailed(hero.Id, check.Kind));
            return;
        }

        RemoveAilment(patient, check.Cure!.Value, int.MaxValue, hero.Id, events);
    }

    /// <summary>Takes up to <paramref name="stacks"/> of an ailment off a hero; returns how many came off.</summary>
    private static int RemoveAilment(Hero patient, Ailment ailment, int stacks, int byId, List<GameEvent> events)
    {
        int had = ailment.Stacks(patient.Conditions, patient.FireDice, patient.Stun);
        int taken = Math.Min(had, stacks);
        if (taken <= 0)
        {
            return 0;
        }

        switch (ailment)
        {
            case Ailment.Burning:
                patient.FireDice -= taken;
                if (patient.FireDice == 0)
                {
                    events.Add(new FireOut(patient.Id, FireOutCause.Aided));
                }

                break;

            case Ailment.Stun when taken == had:
                patient.Stun.Clear();
                events.Add(new StunWoreOff(patient.Id));
                break;

            case Ailment.Stun:
                patient.Stun.Remove(taken);
                break;

            default:
                AddCondition(patient.Conditions, patient.Id, ailment.AsCondition()!.Value, -taken, byId, events);
                break;
        }

        return taken;
    }

    /// <summary>A failed Aid roll a Medicine Bag helped, with a 1 among its dice, uses a bag up.</summary>
    private static void SpillMedicineBag(Hero hero, PendingRoll roll, bool success, List<GameEvent> events)
    {
        if (success || !roll.UsedMedicineBag || !roll.Roll.Faces.Take(roll.NonPowerDice).Contains(1) || hero.MedicineBag is not { } bag)
        {
            return;
        }

        UseUp(hero, bag, events);
    }
}
