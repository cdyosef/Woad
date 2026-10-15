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
}
