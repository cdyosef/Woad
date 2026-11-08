using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// A monster's Bull Rush (an ogre's): with nobody in reach of its blows but a hero in a clear straight line (diagonals
/// too) beyond it, it charges at them, as far as a Wend die carries it (a 6 rolls another in). Falling short, it
/// just ends up nearer. Reaching them, it swings with the tiles to spare added: a hit drives them 2 tiles further
/// back; a miss leaves it sprawled, prone. That's its turn.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>How far a monster's Bull Rush drives a hero it hits.</summary>
    public const int MonsterRushPush = 2;

    private static readonly (int Dx, int Dy)[] RushLines = [(0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1)];

    private bool MonsterBullRush(Room room, Monster monster, IReadOnlyList<Hero> standing, List<GameEvent> events)
    {
        if (!monster.Definition.BullRushes || monster.Tiles.Count() > 1 || IsBlind(monster.Conditions) || monster.MovesThisTurn <= 0
            || ChooseTarget(room, monster, monster.Position, standing) is not null)
        {
            return false;
        }

        // The nearest hero (then the weakest) at the end of a clear straight line from it, two or more tiles off.
        (Hero Hero, int Dx, int Dy, int Distance)? best = null;
        foreach ((int dx, int dy) in RushLines)
        {
            for (int step = 1; ; step++)
            {
                var tile = new GridPosition(monster.Position.X + dx * step, monster.Position.Y + dy * step);
                if (!room.Layout.Contains(tile) || !room.Layout[tile].IsWalkable() || room.Monsters.Any(other => other.Tiles.Contains(tile)))
                {
                    break;
                }

                if (State.HeroesIn(room).FirstOrDefault(hero => hero.Position == tile) is { } met)
                {
                    if (step >= 2 && standing.Contains(met) && !Diving(room, met)
                        && (best is not { } current || step < current.Distance || (step == current.Distance && met.Hearts < current.Hero.Hearts)))
                    {
                        best = (met, dx, dy, step);
                    }

                    break;
                }
            }
        }

        if (best is not var (target, rushX, rushY, distance))
        {
            return false;
        }

        (int[] faces, _) = RollBonusDice(1);
        int length = faces.Sum();
        int free = distance - 1;
        List<GridPosition> path = [.. Enumerable.Range(1, Math.Min(length, free)).Select(step => new GridPosition(monster.Position.X + rushX * step, monster.Position.Y + rushY * step))];
        bool reached = length >= free;
        events.Add(new MonsterCharged(monster.Id, new DiceRoll(faces), length, reached ? target.Id : null));
        if (path.Count > 0)
        {
            monster.Position = path[^1];
            events.Add(new MonsterMoved(monster.Id, path));
        }

        if (!reached || !target.IsActive)
        {
            return true;
        }

        int mark = events.Count;
        RollMonsterAttack(room, monster, monster.AttackDiceThisTurn, monster.Weapon + (length - free), [target], events, weaponDice: monster.WeaponDice);
        bool hit = events.Skip(mark).OfType<Damaged>().Any(damaged => damaged.TargetId == target.Id && damaged.Amount > 0);
        if (hit && target.IsActive && room.FindMonster(monster.Id) is not null)
        {
            KnockBack(room, monster, target, MonsterRushPush, events);
        }
        else if (!hit && room.FindMonster(monster.Id) is not null)
        {
            monster.Prone = true;
            events.Add(new WentProne(monster.Id, Diving: false));
        }

        return true;
    }
}
