using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Bags hung under the belt (belt mods): the Rock Bag, the Medicine Bag, Burgling Tools, a Bomb Bag, Sacks. A belt hangs
/// as many as it has card slots, without using them. Each holds stacks of what it takes, to hand like the belt: drawn
/// into a hand (free, once a turn), used as the turn's free belt item, even in a fight. A bag that names what it holds
/// catches it when it's picked up; anything can be put into a bag that takes it.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Why the active hero can't put the card into the bag now, or null if they can.</summary>
    public string? CheckPack(int cardId, int bagId)
    {
        if (CheckCardQuery(cardId, out CardInstance? card, out CardLocation location) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        if (hero.Bags.FirstOrDefault(bag => bag.Id == bagId) is not { } bag)
        {
            return "There's no such bag on your belt.";
        }

        if (card == bag || hero.BagHolding(card!) == bag)
        {
            return $"The {card!.Definition.Name} is in the {bag.Definition.Name} already.";
        }

        if (!Hero.BagTakes(bag, card!.Definition))
        {
            return $"The {card.Definition.Name} doesn't go in the {bag.Definition.Name}.";
        }

        if (Hero.BagRoom(bag, card.Definition) < card.Count)
        {
            return $"The {bag.Definition.Name} is full.";
        }

        if (HeldFireStays(card) is { } flame)
        {
            return flame;
        }

        if (InCombat && location == CardLocation.Backpack)
        {
            return BagInCombat;
        }

        return location == CardLocation.Equipped ? TakingOff(card) ?? CheckGearAction() : CheckGearAction();
    }

    private string? Handle(PackCard command, List<GameEvent> events)
    {
        if (CheckPack(command.CardId, command.BagId) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        CardInstance card = hero.FindCard(command.CardId)!.Value.Card;
        CardInstance bag = hero.Bags.First(bag => bag.Id == command.BagId);
        SpendGearAction();
        hero.Remove(card);
        CardInstance holder = hero.PackInto(bag, card);
        hero.ClampHearts();
        events.Add(new CardPacked(hero.Id, card.Id, bag.Id, holder.Id));
        return null;
    }
}
