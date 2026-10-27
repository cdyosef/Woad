using Woad.Core.Commands;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Retreating from a fight out on the Woarld Map: the hero's Od Hand looks for a way out, against the
/// monsters' chase (two dice, one more for each monster past the first, and 3: harder than fleeing a
/// band on the map). Made it, and the hero (with their companions beside them) is off the field; the
/// monsters stay where they were. Failed, and they bolt in a panic as far from the monsters as their
/// Wend dice take them, and fall flat.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>What the monsters' chase adds on top of its dice: retreating is harder than fleeing.</summary>
    public const int RetreatChaseBonus = 3;

    /// <summary>Why the active hero can't retreat now, or null.</summary>
    public string? CheckRetreat()
    {
        if (!State.Skirmish)
        {
            return "There's no retreating in a dungeon: find the way out.";
        }

        if (State.Turn is null)
        {
            return "It isn't anyone's turn.";
        }

        if (ActiveRoom.Monsters.Count == 0)
        {
            return "There's nothing left to run from.";
        }

        return CheckOdHandStart();
    }

    private string? Handle(Retreat command, List<GameEvent> events)
    {
        if ((CheckRetreat() ?? CheckOdHandCommand(command, out _)) is { } error)
        {
            return error;
        }

        CheckOdHandCommand(command, out int dice);
        RollOdHand(command, dice, new OdHandCheck(OdHandCheckKind.Retreat, 0, ActiveHero.Position), events);
        return null;
    }

    /// <summary>The chase: rolled once the hero's Od Hand roll is final.</summary>
    private void ResolveRetreat(Hero hero, Room room, int total, List<GameEvent> events)
    {
        DiceRoll chase = _dice.Roll(1 + Math.Max(1, room.Monsters.Count));
        Monster chaser = room.Monsters.OrderBy(monster => monster.Id).First();
        events.Add(new DefenseRolled(chaser.Id, chase, RetreatChaseBonus, chase.Total + RetreatChaseBonus));
        if (total >= chase.Total + RetreatChaseBonus)
        {
            List<Hero> leaving = [.. State.ActiveHeroesIn(room).Where(other => other == hero || other.OwnerId == hero.Id)];
            foreach (Hero runner in leaving)
            {
                runner.Status = HeroStatus.Escaped;
                State.RetreatedIds.Add(runner.Id);
                events.Add(new HeroRetreated(runner.Id));
            }

            EndRunIfOver(events);
            if (State.Phase == RunPhase.InProgress)
            {
                AdvanceTurn(hero.Seat, events);
            }

            return;
        }

        events.Add(new CheckFailed(hero.Id, OdHandCheckKind.Retreat));
        Rout(hero, room, events);
    }

    /// <summary>A failed retreat: the hero runs as far from the monsters as their Wend dice take them, and falls flat.</summary>
    private void Rout(Hero hero, Room room, List<GameEvent> events)
    {
        int dice = MoveDiceLeft;
        int points = 0;
        if (dice > 0 && !hero.Prone && HeldBy(hero) is null)
        {
            DiceRoll wend = _dice.Roll(dice);
            points = wend.Total + SpeedBonusLeft;
            Turn.MoveDiceUsed += dice;
            Turn.SpeedUsed = true;
            events.Add(new Routed(hero.Id, wend, points));
        }

        PathSearch search = PathSearch.Run(hero.Position, tile => room.Layout.Contains(tile) && room.Layout[tile].IsWalkable() && !IsOccupied(room, tile) && room.TrapAt(tile) is not { Revealed: true },
            points, stepCost: tile => HeroStepCost(hero, room, tile));
        int Danger(GridPosition tile) => room.Monsters.Min(monster => monster.NearestTile(tile).ManhattanDistanceTo(tile));
        GridPosition safest = search.Distances.Keys
            .OrderByDescending(Danger)
            .ThenBy(tile => search.Distances[tile])
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .First();
        if (safest != hero.Position)
        {
            IReadOnlyList<GridPosition> path = search.PathTo(safest);
            hero.Position = safest;
            hero.Footing = safest;
            Turn.HasMoved = true;
            events.Add(new HeroMoved(hero.Id, path, Turn.MovementPoints));
        }

        if (!hero.Prone)
        {
            hero.Prone = true;
            events.Add(new WentProne(hero.Id, Diving(room, hero)));
        }
    }
}
