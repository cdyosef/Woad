using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// A hero's defense: the base die and armor against every attack. While that's losing, the hero's
/// Block goes in (all of it, on the first defense that needs it), then their extra Defense dice roll in
/// one at a time, then banked Power Dice, and last of all a lucky die (the Lucky Helmet, which breaks).
/// Block and extra dice are spent until the start of the hero's next turn.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>What a hero can add to a defense that's losing, and what it took.</summary>
    private sealed class Guard
    {
        public int Block { get; init; }

        public int Dice { get; init; }

        public bool Lucky { get; init; }

        public int BlockUsed { get; set; }

        public int DiceUsed { get; set; }

        public bool LuckyUsed { get; set; }
    }

    /// <summary>The hero's base Defense die (none when a stun took every die, or they have none).</summary>
    public static int BaseDefenseDice(Hero hero) => Math.Min(1, DefenseDiceOf(hero));

    /// <summary>The hero's extra Defense dice, ready or spent: those beyond the base die, less any a stun took.</summary>
    public static int ExtraDefenseDice(Hero hero) => Math.Max(0, DefenseDiceOf(hero) - BaseDefenseDice(hero));

    /// <summary>Extra Defense dice the hero has ready: their Defense dice beyond the base one, less those spent since their turn and any a stun took.</summary>
    public static int ExtraDefenseDiceReady(Hero hero) => Math.Max(0, DefenseDiceOf(hero) - BaseDefenseDice(hero) - hero.DefenseDiceSpent);

    /// <summary>Block the hero has ready (0 once spent, until their next turn).</summary>
    public static int BlockReady(Hero hero) => hero.BlockSpent ? 0 : Math.Max(0, hero.EffectiveStats.Block);

    /// <summary>
    /// Rolls a hero's defense against an attack, spending what it must, and returns the total to beat
    /// (dice, armor and Block). <paramref name="extraDice"/> are rolled from the start, on top of the base
    /// die (lying low against a ranged attack).
    /// </summary>
    private int HeroDefense(Hero hero, int attackTotal, List<GameEvent> events, int extraDice = 0)
    {
        int armor = Math.Max(0, hero.EffectiveStats.Armor);
        CardInstance? lucky = hero.EquippedCards.FirstOrDefault(card => card.Definition.LastChanceDie);
        var guard = new Guard { Block = BlockReady(hero), Dice = ExtraDefenseDiceReady(hero), Lucky = lucky is not null };
        DiceRoll roll = RollDefense(hero.Id, hero.Power, BaseDefenseDice(hero) + extraDice, armor, attackTotal, canSpend: true, events, guard);

        hero.DefenseDiceSpent += guard.DiceUsed;
        hero.BlockSpent |= guard.BlockUsed > 0;
        if (guard.LuckyUsed && lucky is not null)
        {
            hero.Remove(lucky);
            events.Add(new LastChanceUsed(hero.Id, lucky.Id));
        }

        return roll.Total + armor + guard.BlockUsed;
    }

    /// <summary>A hero's Block and extra Defense dice come back at the start of their turn.</summary>
    private static void RechargeDefense(Hero hero)
    {
        hero.DefenseDiceSpent = 0;
        hero.BlockSpent = false;
    }
}
