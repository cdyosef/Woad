using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;
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

    /// <summary>What mending a worn card (by its key) whole costs, in copper.</summary>
    public static int RepairPrice(string key) => Money.Gold(GoldPerWear * CardKey.Wear(key));

    /// <summary>Mends one of the hero's worn cards whole, for gold.</summary>
    public static ServiceResult Repair(ContentCatalog content, Character hero, string key) => Repair(content, hero, key, hero);

    /// <summary>Mends one of the hero's worn cards whole, paid from <paramref name="purse"/> (theirs, or the Party Chest).</summary>
    public static ServiceResult Repair(ContentCatalog content, Character hero, string key, IPurse purse)
    {
        if (CardKey.Wear(key) == 0 || !hero.WornGear.Contains(key))
        {
            return ServiceResult.Refused($"{hero.Name} hasn't got that to mend.");
        }

        int price = RepairPrice(key);
        if (!purse.Spend(price))
        {
            return ServiceResult.Refused($"Mending that costs {Money.Long(price)}; {purse.Name} has {Money.Long(purse.Coins)}.");
        }

        hero.Mend(key);
        return new ServiceResult(true, $"The smith mends {hero.Name}'s {content.GetCard(key).Name} good as new for {Money.Long(price)}.");
    }

    /// <summary>Buys one of the forge's wares (it never runs out), at the merchants' price for the hero's Guild standing.</summary>
    public static ServiceResult Buy(WoarldPlace smithy, ContentCatalog content, Character buyer, string cardId) =>
        Buy(smithy, content, Shopper.Of(buyer, content), cardId);

    public static ServiceResult Buy(WoarldPlace smithy, ContentCatalog content, Shopper buyer, string cardId)
    {
        if (!smithy.Stock.Contains(cardId) || content.FindCard(cardId) is not { } card)
        {
            return ServiceResult.Refused("The smith doesn't make those.");
        }

        int price = TavernServices.BuyPrice(card, buyer.Standing);
        if (!buyer.Purse.Spend(price))
        {
            return ServiceResult.Refused($"That costs {Money.Long(price)}; {buyer.Purse.Name} has {Money.Long(buyer.Purse.Coins)}.");
        }

        buyer.Receive(card.Id);
        return new ServiceResult(true, $"{Capital(buyer.Purse.Name)} buys {("AEIOU".Contains(card.Name[0]) ? "an" : "a")} {card.Name} from the smith for {Money.Long(price)}.");
    }

    private static string Capital(string name) => name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name;
}
