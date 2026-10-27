using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Stuns (see <see cref="StunState"/>): the Boomerang's sixes and Elektrik dice add Stun on a hit, bomb blasts Powerful Stun.
/// A hero's stun takes its dice when their next turn starts; a monster's when its next turn does. Either
/// way it wears off at the end of that turn. Stunned flyers drop out of the air, into whatever is under
/// them. Bosses can't be stunned. Also here: which weapon an attack is made with, a thrown weapon from
/// the off hand reaching where the main hand can't.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>A melee attack (from right beside it) on a prone monster rolls an extra die.</summary>
    private static int ProneBonus(Monster target, GridPosition from) =>
        target.Prone && target.Position.ChebyshevDistanceTo(from) <= 1 ? 1 : 0;

    /// <summary>Stuns a monster (not a boss). A flyer stunned over a pit falls; over deep water, it flounders out.</summary>
    private void Stun(Room room, Monster monster, bool powerful, int stacks, Hero? by, List<GameEvent> events)
    {
        if (monster.Definition.StunImmune || stacks < 1 || room.FindMonster(monster.Id) is null)
        {
            return;
        }

        bool flew = monster.Flies;
        monster.Stun.Add(powerful, stacks);
        events.Add(new Stunned(monster.Id, monster.Stun.Stacks, monster.Stun.PowerfulStacks, powerful));
        if (!flew || monster.Flies || (monster.Definition.Swimmer && InWater(room, monster.Position)))
        {
            return;
        }

        TileKind under = room.Layout[monster.Position];
        if (under == TileKind.Pit)
        {
            MonsterFalls(by, room, monster, monster.Position, monster.Position, events);
        }
        else if (under == TileKind.DeepWater)
        {
            HurtMonster(by, room, monster, events);
            if (room.FindMonster(monster.Id) is not null && LandingNear(room, monster.Position) is { } shore)
            {
                monster.Position = shore;
                events.Add(new ClimbedOut(monster.Id, shore));
            }
        }
    }

    private static void Stun(Hero hero, bool powerful, int stacks, List<GameEvent> events)
    {
        if (!hero.IsInPlay || stacks < 1)
        {
            return;
        }

        hero.Stun.Add(powerful, stacks);
        events.Add(new Stunned(hero.Id, hero.Stun.Stacks, hero.Stun.PowerfulStacks, powerful));
    }

    /// <summary>
    /// A stunned hero's turn starts short: no Od Hand action and no free loot or belt item (or a Wend die
    /// if they have no Od Hand), and the dice the stun takes, Wend first, then Rumble.
    /// </summary>
    private void ApplyStun(Hero hero)
    {
        if (!hero.Stun.IsStunned)
        {
            return;
        }

        hero.Stun.WearsOffThisTurn = true;
        Stats stats = hero.EffectiveStats;
        bool odHand = stats.OdHandDice > 0;
        (int wend, int rumble, _) = hero.Stun.Lost(odHand, stats.MoveDice, stats.AttackDice, stats.DefenseDice);
        if (odHand)
        {
            Turn.OdHandDiceUsed = stats.OdHandDice;
        }

        Turn.MoveDiceUsed += wend;
        Turn.AttackDiceUsed += rumble;
        Turn.UsedFreeLoot = true;
        Turn.UsedBeltItem = true;
    }

    /// <summary>The end of a hero's turn: a stun that was on them all turn wears off.</summary>
    private static void WearOffStun(Hero hero, List<GameEvent> events)
    {
        if (hero.Stun.WearsOffThisTurn)
        {
            hero.Stun.Clear();
            events.Add(new StunWoreOff(hero.Id));
        }
    }

    /// <summary>Defense dice a hero rolls now: theirs, less what a stun took.</summary>
    private static int DefenseDiceOf(Hero hero)
    {
        Stats stats = hero.EffectiveStats;
        int dice = Math.Max(0, stats.DefenseDice);
        return dice - hero.Stun.Lost(stats.OdHandDice > 0, stats.MoveDice, stats.AttackDice, dice).Defense;
    }

    /// <summary>Defense dice a monster rolls now: its own, less what a stun took.</summary>
    private static int DefenseDiceOf(Monster monster)
    {
        MonsterDefinition definition = monster.Definition;
        return definition.DefenseDice - monster.Stun.Lost(definition.OdHandDice > 0, definition.WendDice, definition.AttackDice, definition.DefenseDice).Defense;
    }
}
