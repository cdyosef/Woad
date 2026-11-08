using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Hold Fire: Hurl Fire cast on yourself puts a flame in a hand, a card there (one to a hand, so two at most).
/// It's a lit torch in every way, and it stays in hand till it's hurled or goes out: it can move to the other
/// hand, be dropped (it sets the floor there burning, which spreads and dies down like any fire), or be handed
/// to an ally, into their arms, where it burns on as a light; they must take it into a hand by the end of their
/// turn or it falls from their arms, burning. Hurl Fire 1 (a basic spell learned on top of Hurl Fire) adds +1
/// fire to Hurl Fire and to the hurled flame.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The Hold Fire card's id.</summary>
    public const string HoldFireId = "hold_fire";

    /// <summary>The spell that improves Hurl Fire and Hold Fire: +1 fire.</summary>
    public const string HurlFireOneId = "hurl_fire_1";

    /// <summary>The fire a hero's Hurl Fire and hurled flames add: 1 with Hurl Fire 1, else 0.</summary>
    private static int FireBonus(Hero hero) => hero.Spells.Any(spell => spell.Id == HurlFireOneId) ? 1 : 0;

    /// <summary>
    /// Puts a flame in one of the hero's hands: an empty one, else the off hand (what's there goes on the belt, or
    /// into the bag, or drops). False with a flame in each hand already.
    /// </summary>
    private bool GrantHeldFire(Hero hero, List<GameEvent> events)
    {
        EquipSlot[] hands = [EquipSlot.OffHand, EquipSlot.MainHand];
        if (hands.All(hand => hero.Equipment.GetValueOrDefault(hand)?.Definition.HeldFire == true))
        {
            return false;
        }

        EquipSlot slot = hands.FirstOrDefault(hand => !hero.Equipment.ContainsKey(hand), EquipSlot.OffHand);
        if (hero.Equipment.GetValueOrDefault(slot)?.Definition.HeldFire == true)
        {
            slot = EquipSlot.MainHand;
        }

        var flame = new CardInstance(State.NextId(), Content.GetCard(HoldFireId), broughtInByHeroId: null);
        foreach (CardInstance old in hero.CardsDisplacedBy(flame.Definition, slot))
        {
            hero.Remove(old);
            if (old.Definition.Belts || HandHeld(old.Definition) && hero.Belt.Count < hero.BeltCapacity)
            {
                hero.AddToBelt(old);
                events.Add(new CardBelted(hero.Id, old.Id));
            }
            else if (hero.CanCarry(old.Definition))
            {
                hero.AddToBackpack(old);
                events.Add(new CardStowed(hero.Id, old.Id));
            }
            else
            {
                GridPosition spot = SafeTile(State.RoomOf(hero), hero.Position);
                State.RoomOf(hero).AddLoot(spot, [old]);
                events.Add(new CardDropped(hero.Id, old.Id, spot));
            }
        }

        hero.Equip(flame, slot);
        events.Add(new CardEquipped(hero.Id, flame.Id));
        return true;
    }

    /// <summary>One flame of the hero's goes out (the one in a hand first), or is hurled.</summary>
    private void QuenchHeldFire(Hero hero)
    {
        if (hero.HeldFires.OrderBy(card => hero.SlotOf(card) is null ? 1 : 0).FirstOrDefault() is { } flame)
        {
            hero.Remove(flame);
        }
    }

    /// <summary>A flame dropped, or let fall from an ally's arms: the floor it lands on burns.</summary>
    private void DropHeldFire(Hero hero, CardInstance flame, List<GameEvent> events)
    {
        hero.Remove(flame);
        Room room = State.RoomOf(hero);
        GridPosition spot = SafeTile(room, hero.Position);
        events.Add(new CardDropped(hero.Id, flame.Id, spot));
        SetFloorAlight(room, [spot], events, hero);
    }

    /// <summary>At the end of a hero's turn, a flame still in their arms (not taken into a hand) falls, burning.</summary>
    private void DropCarriedFire(Hero hero, List<GameEvent> events)
    {
        foreach (CardInstance flame in hero.Carried.Where(card => card.Definition.HeldFire).ToList())
        {
            DropHeldFire(hero, flame, events);
        }
    }

    /// <summary>Why a flame can't go where it's asked (the belt, the bag, your own arms), or null if it isn't a flame.</summary>
    private static string? HeldFireStays(CardInstance card) =>
        card.Definition.HeldFire ? "Hold Fire is a flame in your hand: hurl it, drop it, hand it to an ally, or move it to your other hand." : null;
}
