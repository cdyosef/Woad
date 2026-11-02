using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

public sealed partial class GameSession
{
    /// <summary>The active companions belonging to <paramref name="owner"/>, in seat order.</summary>
    public IReadOnlyList<Hero> CompanionsOf(Hero owner) =>
        State.Heroes.Where(hero => hero.IsInPlay && hero.OwnerId == owner.Id).ToList();

    private string? Handle(OrderCompanion command, List<GameEvent> events)
    {
        Hero owner = ActiveHero;
        if (State.Heroes.FirstOrDefault(hero => hero.Id == command.CompanionId) is not { IsInPlay: true } companion
            || companion.OwnerId != owner.Id)
        {
            return "That isn't one of your companions.";
        }

        companion.Order = command.Order;
        companion.HoldAt = command.Order == CompanionOrder.Hold ? (companion.RoomId, companion.Position) : null;
        events.Add(new CompanionOrdered(owner.Id, companion.Id, command.Order));
        return null;
    }

    /// <summary>
    /// Why the active player can't hand <paramref name="targetId"/> over (to <paramref name="newOwnerId"/>, or
    /// as a hero of their own when null), or null if they can: one of their companions becomes a hero, or
    /// their own hero a companion of another player still in the run. Never mid-roll, and never the last
    /// hero; the dead raised as companions stay companions.
    /// </summary>
    public string? CheckSwitchControl(int targetId, int? newOwnerId)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Hero player = ActiveHero;
        if (player.IsCompanion)
        {
            return "Only a player can hand a hero over.";
        }

        if (State.Heroes.FirstOrDefault(hero => hero.Id == targetId) is not { IsInPlay: true } target || (target != player && target.OwnerId != player.Id))
        {
            return "That's not your hero or one of your companions.";
        }

        if (target.IsCompanion)
        {
            return newOwnerId is not null ? "Make a companion a hero, not someone else's companion."
                : target.Summoned ? $"{target.Name} answers to whoever raised them: they can't be a hero."
                : null;
        }

        if (newOwnerId is not int ownerId)
        {
            return $"{target.Name} is a hero already.";
        }

        return State.Heroes.FirstOrDefault(hero => hero.Id == ownerId) is not { IsInPlay: true, IsCompanion: false } owner || owner == target
            ? "A companion has to follow another player still in the run."
            : null;
    }

    private string? Handle(SwitchControl command, List<GameEvent> events)
    {
        if (CheckSwitchControl(command.TargetId, command.NewOwnerId) is { } error)
        {
            return error;
        }

        Hero target = State.GetHero(command.TargetId);
        if (command.NewOwnerId is int ownerId)
        {
            // A hero handed to another player, with any companions of its own; the game plays its turns from now on.
            foreach (Hero follower in State.Heroes.Where(hero => hero.OwnerId == target.Id))
            {
                follower.OwnerId = ownerId;
            }

            target.OwnerId = ownerId;
            target.PlayerNumber = 0;
            target.Order = CompanionOrder.Follow;
            target.HoldAt = null;
        }
        else
        {
            // A companion taken in hand: a player's hero, numbered after the others.
            target.OwnerId = null;
            target.PlayerNumber = State.Heroes.Max(hero => hero.PlayerNumber) + 1;
        }

        events.Add(new ControlSwitched(command.HeroId, target.Id, command.NewOwnerId));
        return null;
    }
}
