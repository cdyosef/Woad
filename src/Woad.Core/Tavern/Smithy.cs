using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Woarld;

namespace Woad.Core.Tavern;

/// <summary>
/// The blacksmith's shack on the Woarld Map: mends worn gear (armor acid ate, and weapons once
/// something wears them) for gold, and sells arms, armor and Autosmith Hammers from the forge.
/// </summary>
public static class Smithy
{
    /// <summary>What mending costs for each point of wear.</summary>
    public const int GoldPerWear = 2;

    /// <summary>What mending a worn card (by its key) whole costs.</summary>
    public static int RepairPrice(string key) => GoldPerWear * CardKey.Wear(key);

    /// <summary>Mends one of the hero's worn cards whole, for gold.</summary>
    public static ServiceResult Repair(ContentCatalog content, Character hero, string key)
    {
        if (CardKey.Wear(key) == 0 || !hero.WornGear.Contains(key))
        {
            return ServiceResult.Refused($"{hero.Name} hasn't got that to mend.");
        }

        int price = RepairPrice(key);
        if (!hero.SpendGold(price))
        {
            return ServiceResult.Refused($"Mending that costs {price} gold; {hero.Name} has {hero.Gold}.");
        }

        hero.Mend(key);
        return new ServiceResult(true, $"The smith mends {hero.Name}'s {content.GetCard(key).Name} good as new for {price} gold.");
    }

    /// <summary>Buys one of the forge's wares (it never runs out), at the merchants' price for the hero's Guild standing.</summary>
    public static ServiceResult Buy(WoarldPlace smithy, ContentCatalog content, Character buyer, string cardId)
    {
        if (!smithy.Stock.Contains(cardId) || content.FindCard(cardId) is not { } card)
        {
            return ServiceResult.Refused("The smith doesn't make those.");
        }

        int price = TavernServices.BuyPrice(card, buyer.GuildStanding);
        if (!buyer.SpendGold(price))
        {
            return ServiceResult.Refused($"That costs {price} gold; {buyer.Name} has {buyer.Gold}.");
        }

        buyer.GiveCard(card, content);
        return new ServiceResult(true, $"{buyer.Name} buys {("AEIOU".Contains(card.Name[0]) ? "an" : "a")} {card.Name} from the smith for {price} gold.");
    }
}
