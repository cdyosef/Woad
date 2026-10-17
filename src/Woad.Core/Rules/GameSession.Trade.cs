using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Trading between allies standing next to each other (diagonals too): fellow players and companions.
/// The hero taking their turn can give cards and gold, and take them back; cards go into (or join a
/// stack in) the other's backpack, and a stack can be split. A companion's owner also dresses it:
/// equipping, belting and stowing its cards. Trading is free.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The allies the active hero can trade with now: in play, in the same room and next to them.</summary>
    public IReadOnlyList<Hero> GetTradePartners()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0, KeelingOver: false })
        {
            return [];
        }

        Hero hero = ActiveHero;
        return State.HeroesIn(ActiveRoom)
            .Where(other => other != hero && other.IsInPlay && other.Position.ChebyshevDistanceTo(hero.Position) <= 1)
            .ToList();
    }

    private string? CheckPartner(int partnerId, out Hero partner)
    {
        partner = GetTradePartners().FirstOrDefault(other => other.Id == partnerId)!;
        return partner is null ? "Stand next to them to trade." : null;
    }

    private string? Handle(GiveCard command, List<GameEvent> events)
    {
        if (CheckPartner(command.ToHeroId, out Hero partner) is { } error)
        {
            return error;
        }

        return Pass(ActiveHero, partner, command.CardId, command.Count, events);
    }

    private string? Handle(TakeCard command, List<GameEvent> events)
    {
        if (CheckPartner(command.FromHeroId, out Hero partner) is { } error)
        {
            return error;
        }

        return Pass(partner, ActiveHero, command.CardId, command.Count, events);
    }

    /// <summary>Moves a card (or part of a stack) from one hero's hands to the other's backpack.</summary>
    private string? Pass(Hero from, Hero to, int cardId, int? count, List<GameEvent> events)
    {
        if (from.FindCard(cardId) is not var (card, _))
        {
            return $"{from.Name} doesn't have that card.";
        }

        int amount = count ?? card.Count;
        if (amount < 1 || amount > card.Count)
        {
            return $"Choose 1 to {card.Count}.";
        }

        if (!to.CanCarry(card.Definition))
        {
            return $"{to.Name}'s backpack is full.";
        }

        CardInstance passed = TakeFromHero(from, card, amount);
        to.AddToBackpack(passed);
        events.Add(new CardTraded(from.Id, to.Id, passed.Id, amount));
        return null;
    }

    private string? Handle(GiveGold command, List<GameEvent> events)
    {
        if (CheckPartner(command.ToHeroId, out Hero partner) is { } error)
        {
            return error;
        }

        return PassGold(ActiveHero, partner, command.Amount, events);
    }

    private string? Handle(TakeGold command, List<GameEvent> events)
    {
        if (CheckPartner(command.FromHeroId, out Hero partner) is { } error)
        {
            return error;
        }

        return PassGold(partner, ActiveHero, command.Amount, events);
    }

    private static string? PassGold(Hero from, Hero to, int amount, List<GameEvent> events)
    {
        if (amount < 1 || amount > from.Gold)
        {
            return from.Gold == 0 ? $"{from.Name} has no gold." : $"Choose 1 to {from.Gold} gold.";
        }

        from.Gold -= amount;
        to.Gold += amount;
        events.Add(new GoldTraded(from.Id, to.Id, amount));
        return null;
    }

    private string? Handle(GiveKey command, List<GameEvent> events) =>
        CheckPartner(command.ToHeroId, out Hero partner) ?? PassKey(ActiveHero, partner, command.BossKey, events);

    private string? Handle(TakeKey command, List<GameEvent> events) =>
        CheckPartner(command.FromHeroId, out Hero partner) ?? PassKey(partner, ActiveHero, command.BossKey, events);

    private static string? PassKey(Hero from, Hero to, bool bossKey, List<GameEvent> events)
    {
        if (bossKey ? !from.HasBossKey : from.Keys < 1)
        {
            return bossKey ? $"{from.Name} doesn't have the Boss Key." : $"{from.Name} has no keys.";
        }

        if (bossKey)
        {
            from.HasBossKey = false;
            to.HasBossKey = true;
        }
        else
        {
            from.Keys--;
            to.Keys++;
        }

        events.Add(new KeyTraded(from.Id, to.Id, bossKey));
        return null;
    }

    /// <summary>A companion's owner, beside it, equips, belts or stows one of its cards.</summary>
    private string? Handle(OutfitCompanion command, List<GameEvent> events)
    {
        if (CheckPartner(command.CompanionId, out Hero companion) is { } error)
        {
            return error;
        }

        if (companion.OwnerId != ActiveHero.Id)
        {
            return "Only its owner can dress a companion.";
        }

        if (companion.FindCard(command.CardId) is not var (card, location))
        {
            return $"{companion.Name} doesn't have that card.";
        }

        CardDefinition definition = card.Definition;
        switch (command.Action)
        {
            case OutfitAction.Equip:
                if (definition.Slot is null || location == CardLocation.Equipped)
                {
                    return location == CardLocation.Equipped ? "It's already equipped." : "That can't be equipped.";
                }

                IReadOnlyList<CardInstance> displaced = companion.CardsDisplacedBy(definition);
                if (companion.Backpack.Count - (location == CardLocation.Backpack ? 1 : 0) + displaced.Count > GameRules.BackpackCapacity)
                {
                    return $"There's no room in {companion.Name}'s backpack for what it would take off.";
                }

                companion.Remove(card);
                foreach (CardInstance old in displaced)
                {
                    companion.Remove(old);
                    companion.AddToBackpack(old);
                }

                companion.Equip(card);
                break;

            case OutfitAction.Belt:
                if (definition.Usage != CardUsage.SingleUse || location == CardLocation.Belt)
                {
                    return location == CardLocation.Belt ? "It's already on the belt." : "Only single-use cards go on the belt.";
                }

                if (companion.Belt.Count >= GameRules.BeltCapacity)
                {
                    return $"{companion.Name}'s belt is full.";
                }

                companion.Remove(card);
                companion.AddToBelt(card);
                break;

            default:
                if (location == CardLocation.Backpack)
                {
                    return "It's already in the backpack.";
                }

                if (companion.Backpack.Count >= GameRules.BackpackCapacity)
                {
                    return $"{companion.Name}'s backpack is full.";
                }

                companion.Remove(card);
                companion.AddToBackpack(card);
                break;
        }

        companion.ClampHearts();
        events.Add(new CompanionOutfitted(ActiveHero.Id, companion.Id, card.Id, command.Action));
        return null;
    }
}
