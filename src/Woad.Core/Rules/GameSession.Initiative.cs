using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Initiative (in a graveyard, for now): at the start of each round every hero rolls all their Wend dice
/// and adds their Wend bonus. The monsters' turn stands at 5 and the undead's at 1; everyone goes from
/// highest to lowest. A hero who ties an enemy turn goes right after it. Heroes tied with each other
/// roll off: a die each (a 6 rolls another), highest first; tied again, a die more each time.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Where the monsters' turn stands in the initiative order.</summary>
    public const int MonstersInitiative = 5;

    /// <summary>Where the undead's turn stands in the initiative order.</summary>
    public const int UndeadInitiative = 1;

    /// <summary>Whether turns go by initiative rolled each round, rather than seat order then the enemies.</summary>
    private bool UsesInitiative => Graveyard is not null;

    /// <summary>Rolls the round's initiative and sets its order.</summary>
    private void RollInitiative(List<GameEvent> events)
    {
        List<(Hero Hero, IReadOnlyList<int> Faces, int Total)> rolls = [];
        foreach (Hero hero in State.Heroes.Where(hero => hero.IsInPlay).OrderBy(hero => hero.Seat))
        {
            IReadOnlyList<int> faces = _dice.Roll(Math.Max(0, hero.EffectiveStats.MoveDice)).Faces;
            rolls.Add((hero, faces, faces.Sum() + hero.EffectiveStats.Speed));
        }

        List<TurnSlot> order = [];
        foreach (int score in rolls.Select(roll => roll.Total).Append(MonstersInitiative).Append(UndeadInitiative).Distinct().OrderDescending())
        {
            // An enemy turn first, then the heroes who tied it.
            if (score == MonstersInitiative)
            {
                order.Add(new TurnSlot(null, Faction.Monsters, score));
            }

            if (score == UndeadInitiative)
            {
                order.Add(new TurnSlot(null, Faction.Undead, score));
            }

            List<Hero> tied = [.. rolls.Where(roll => roll.Total == score).Select(roll => roll.Hero)];
            order.AddRange(RollOff(tied, 1, events).Select(hero => new TurnSlot(hero.Id, null, score)));
        }

        State.Initiative = order;
        State.InitiativeIndex = 0;
        events.Add(new InitiativeRolled(State.Round, [.. rolls.Select(roll => new InitiativeRoll(roll.Hero.Id, roll.Faces, roll.Total))], order));
    }

    /// <summary>Heroes tied on initiative, in the order a roll-off puts them: <paramref name="dice"/> each, highest first, tied again a die more.</summary>
    private List<Hero> RollOff(List<Hero> tied, int dice, List<GameEvent> events)
    {
        if (tied.Count < 2)
        {
            return tied;
        }

        List<(Hero Hero, int Total)> rolled = [.. tied.Select(hero => (hero, RollBonusDice(dice).Faces.Sum()))];
        events.Add(new InitiativeRollOff([.. rolled.Select(roll => roll.Hero.Id)], dice, [.. rolled.Select(roll => roll.Total)]));
        return [.. rolled.GroupBy(roll => roll.Total).OrderByDescending(group => group.Key)
            .SelectMany(group => RollOff([.. group.Select(roll => roll.Hero)], dice + 1, events))];
    }

    /// <summary>Plays on down the round's initiative order: the next hero still in play takes their turn, or an enemy turn plays out. Past the end, a new round.</summary>
    private void NextInitiative(List<GameEvent> events)
    {
        while (State.Initiative is { } order && State.InitiativeIndex < order.Count)
        {
            TurnSlot slot = order[State.InitiativeIndex++];
            if (slot.HeroId is int heroId)
            {
                if (State.GetHero(heroId).IsInPlay)
                {
                    StartTurn(State.GetHero(heroId), events);
                    return;
                }

                continue;
            }

            RunEnemyPhase(slot.Enemy!.Value, events);
            if (State.Phase != RunPhase.InProgress)
            {
                return;
            }
        }

        StartRound(events);
    }
}
