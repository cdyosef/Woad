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

    /// <summary>What mending a worn card (by its key) whole costs, in copper (nothing for one mended only with something else: see <see cref="RepairMaterial"/>).</summary>
    public static int RepairPrice(ContentCatalog content, string key) =>
        content.GetCard(CardKey.Id(key)).MendsWith is null ? Money.Gold(GoldPerWear * CardKey.Wear(key)) : 0;

    /// <summary>What a card mended only with something else (the Emerald Axe: emeralds) takes, one a point of wear; null for gold.</summary>
    public static (string CardId, int Count)? RepairMaterial(ContentCatalog content, string key) =>
        content.GetCard(CardKey.Id(key)).MendsWith is { } material ? (material, CardKey.Wear(key)) : null;

    /// <summary>Mends one of the hero's worn cards whole, for gold.</summary>
    public static ServiceResult Repair(ContentCatalog content, Character hero, string key) => Repair(content, hero, key, hero);

    /// <summary>Mends one of the hero's worn cards whole, paid from <paramref name="purse"/> (theirs, or the Party Chest).</summary>
    public static ServiceResult Repair(ContentCatalog content, Character hero, string key, IPurse purse)
    {
        if (CardKey.Wear(key) == 0 || !hero.WornGear.Contains(key))
        {
            return ServiceResult.Refused($"{hero.Name} hasn't got that to mend.");
        }

        // Mended only with emeralds (the Emerald Axe): one for each point of wear, from what the owner carries.
        if (RepairMaterial(content, key) is (string material, int count))
        {
            string named = content.GetCard(material).Name;
            if (hero.CountOf(material) < count)
            {
                return ServiceResult.Refused($"Only {(count == 1 ? $"an {named.ToLowerInvariant()}" : $"{count} {named.ToLowerInvariant()}s")} will mend that; {hero.Name} has {hero.CountOf(material)}.");
            }

            for (int i = 0; i < count; i++)
            {
                hero.TakeCard(material);
            }

            hero.Mend(key);
            return new ServiceResult(true, $"The smith mends {hero.Name}'s {content.GetCard(key).Name} good as new with {count} {named.ToLowerInvariant()}{(count == 1 ? "" : "s")}.");
        }

        int price = RepairPrice(content, key);
        if (!purse.Spend(price))
        {
            return ServiceResult.Refused($"Mending that costs {Money.Long(price)}; {purse.Name} has {Money.Long(purse.Coins)}.");
        }

        hero.Mend(key);
        return new ServiceResult(true, $"The smith mends {hero.Name}'s {content.GetCard(key).Name} good as new for {Money.Long(price)}.");
    }

    /// <summary>What making a card finer (the Emerald Axe into the +3) takes: so many of its material, and as many gold; null if it can't be.</summary>
    public static (string Into, string Material, int Count, int Price)? Improvement(ContentCatalog content, string key) =>
        content.GetCard(CardKey.Id(key)) is { ImprovesTo: { } into, MendsWith: { } material, ImproveCost: > 0 and var cost } ? (into, material, cost, Money.Gold(cost)) : null;

    /// <summary>Makes one of the hero's cards finer (the Emerald Axe into the +3): its material from what they carry, the gold from <paramref name="purse"/>.</summary>
    public static ServiceResult Improve(ContentCatalog content, Character hero, string key, IPurse purse)
    {
        if (Improvement(content, key) is not (string into, string material, int count, int price) || hero.CountOf(key) == 0 && !hero.WornGear.Contains(key))
        {
            return ServiceResult.Refused($"The smith can't make that any finer.");
        }

        string named = content.GetCard(material).Name.ToLowerInvariant();
        if (hero.CountOf(material) < count)
        {
            return ServiceResult.Refused($"That takes {count} {named}s; {hero.Name} has {hero.CountOf(material)}.");
        }

        if (!purse.Spend(price))
        {
            return ServiceResult.Refused($"That takes {Money.Long(price)} too; {purse.Name} has {Money.Long(purse.Coins)}.");
        }

        for (int i = 0; i < count; i++)
        {
            hero.TakeCard(material);
        }

        hero.Replace(key, CardKey.Of(into, CardKey.Wear(key), CardKey.Painted(key)));
        return new ServiceResult(true, $"The smith makes {hero.Name}'s {content.GetCard(key).Name} into the {content.GetCard(into).Name} with {count} {named}s and {Money.Long(price)}.");
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
