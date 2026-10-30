using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Bull Rush: a Wend die (a 6 rolls another) and the Wend bonus give the charge's length, and the hero
/// charges that far in a straight line (diagonals too), at least a tile, with no stopping early. The
/// first monster in the way takes a Rumble attack, weapon and all, with the tiles left over added; a
/// hit pushes it two tiles, bosses and huge monsters too, and a miss leaves the hero prone. Running
/// into a wall or anything else that stops them leaves them prone as well. A hidden trap on the way
/// ends the charge where it goes off. It takes only the dice, never the turn's action, and the Rumble
/// dice only if the charge reaches a monster.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Tiles a bull rush shoves a monster it hits.</summary>
    public const int BullRushPush = 2;

    /// <summary>Why the active hero can't bull rush now, or null.</summary>
    public string? CheckBullRush()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        if (!hero.Knows(Skill.BullRush))
        {
            return "You haven't learned to Bull Rush.";
        }

        if (MoveDiceLeft < 1)
        {
            return "A bull rush takes a Wend die, and you've none left this turn.";
        }

        if (AttackDiceLeft < 1)
        {
            return "A bull rush takes Rumble dice for the blow, and you've none left this turn.";
        }

        if (hero.Prone)
        {
            return "You can't charge lying down: get up first.";
        }

        if (hero.Aloft)
        {
            return "You're in the air: land first.";
        }

        return CheckHeld(hero);
    }

    /// <summary>
    /// The tiles the active hero could charge toward: along each of the eight lines from them, up to a
    /// wall or anything else in the way, the first monster included. The first tile must be open ground.
    /// </summary>
    public IReadOnlyList<GridPosition> GetBullRushTargets()
    {
        if (State.Turn is null || CheckBullRush() is not null)
        {
            return [];
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        var targets = new List<GridPosition>();
        foreach ((int dx, int dy) in LeapDirections)
        {
            for (int distance = 1; ; distance++)
            {
                var tile = new GridPosition(hero.Position.X + dx * distance, hero.Position.Y + dy * distance);
                if (!room.Layout.Contains(tile))
                {
                    break;
                }

                bool monster = room.MonsterAt(tile) is not null;
                if (monster && distance > 1)
                {
                    targets.Add(tile);
                }

                if (monster || !CanCharge(room, tile))
                {
                    break;
                }

                targets.Add(tile);
            }
        }

        return targets;
    }

    /// <summary>A charge runs on over open ground nobody stands on (traps and all).</summary>
    private bool CanCharge(Room room, GridPosition tile) => room.Layout[tile].IsWalkable() && !IsOccupied(room, tile);

    private string? Handle(BullRush command, List<GameEvent> events)
    {
        if (CheckBullRush() is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        GridPosition from = hero.Position;
        int dx = Math.Sign(command.Toward.X - from.X), dy = Math.Sign(command.Toward.Y - from.Y);
        int across = Math.Abs(command.Toward.X - from.X), down = Math.Abs(command.Toward.Y - from.Y);
        var first = new GridPosition(from.X + dx, from.Y + dy);
        if ((across != down && across != 0 && down != 0) || command.Toward == from || !room.Layout.Contains(first) || !CanCharge(room, first))
        {
            return "Charge in a straight line (diagonals too), with open ground ahead of you.";
        }

        int left = AttackDiceLeft;
        int dice = command.Dice ?? left;
        if (dice < 1 || dice > left)
        {
            return $"Choose 1 to {left} Rumble dice.";
        }

        if (CheckBankedPowerDice(hero, command.PowerDice) is { } powerError)
        {
            return powerError;
        }

        // The charge's length: a Wend die (a 6 rolls another) and the Wend bonus.
        int speed = SpeedBonusLeft;
        Turn.SpeedUsed |= speed != 0;
        Turn.MoveDiceUsed++;
        (int[] faces, _) = RollBonusDice(1);
        int length = Math.Max(0, faces.Sum() + speed - RollPenalty(hero.Conditions));

        // On it goes, tile by tile, until it's run its length or something stops it.
        var path = new List<GridPosition>();
        Monster? struck = null;
        bool crashed = false;
        Trap? sprung = null;
        while (path.Count < length)
        {
            GridPosition at = path.Count > 0 ? path[^1] : from;
            var next = new GridPosition(at.X + dx, at.Y + dy);
            if (room.Layout.Contains(next) && room.MonsterAt(next) is { } monster)
            {
                struck = monster;
                break;
            }

            if (!room.Layout.Contains(next) || !CanCharge(room, next))
            {
                crashed = true;
                break;
            }

            path.Add(next);
            if (room.TrapAt(next) is { } trap)
            {
                sprung = trap;
                break;
            }
        }

        // Just as far as the monster, with nothing to spare, still reaches it.
        if (struck is null && sprung is null && !crashed && path.Count == length)
        {
            GridPosition end = path.Count > 0 ? path[^1] : from;
            var ahead = new GridPosition(end.X + dx, end.Y + dy);
            struck = room.Layout.Contains(ahead) ? room.MonsterAt(ahead) : null;
        }

        int leftover = length - path.Count;
        events.Add(new BullRushed(hero.Id, new DiceRoll(faces), speed, length, from, path.Count > 0 ? path[^1] : from, struck?.Id, leftover));
        if (path.Count > 0)
        {
            hero.Position = path[^1];
            foreach (GridPosition tile in path.Where(tile => room.Layout[tile].IsWalkable()))
            {
                hero.Footing = tile;
            }

            events.Add(new HeroMoved(hero.Id, path, Turn.MovementPoints));
            if (hero.FireDice == 0 && path.Any(tile => room.IsBurning(tile) && !InWater(room, tile)))
            {
                Ignite(hero, hero.Id, events);
            }
        }

        if (sprung is not null)
        {
            Turn.EndMove();
            SpringTrap(hero, room, sprung, hero.Position, events);
            return null;
        }

        if (crashed)
        {
            FallProne(room, hero, events);
            return null;
        }

        if (struck is null)
        {
            return null;
        }

        Reveal(hero, events);
        StrikeMonster(hero, room, struck, null, dice, command.UseWeapon, command.PowerDice, leftover, events, bullRush: true);
        return null;
    }

    /// <summary>A bull rush's blow, once its dice are final: a hit drives the monster back two tiles; a miss leaves the hero sprawled.</summary>
    private void BullRushAftermath(PendingRoll attack, IReadOnlyList<int> hit, List<GameEvent> events)
    {
        Hero hero = State.GetHero(attack.HeroId);
        Room room = State.RoomOf(hero);
        int targetId = attack.TargetIds[0];
        if (!hit.Contains(targetId))
        {
            if (hero.IsActive)
            {
                FallProne(room, hero, events);
            }

            return;
        }

        if (room.FindMonster(targetId) is not { } monster)
        {
            return;
        }

        if (monster.Definition is { Boss: true } or { Miniboss: true } || monster.Definition.Size > 1)
        {
            HeaveAside(room, monster, hero.Position, BullRushPush, hero, events, slam: false);
            return;
        }

        for (int pushed = 0; pushed < BullRushPush; pushed++)
        {
            GridPosition at = monster.Position;
            if (PushDestination(room, hero.Position, at, monster.Flies) is not { } to)
            {
                return;
            }

            // Two tiles straight on: the second keeps the first push's line, away from where the hero stands.
            PushMonster(hero, room, monster, to, events);
            if (room.FindMonster(monster.Id) is null || monster.Position != to)
            {
                return;
            }
        }
    }

    private static void FallProne(Room room, Hero hero, List<GameEvent> events)
    {
        if (!hero.Prone)
        {
            hero.Prone = true;
            events.Add(new WentProne(hero.Id, Diving(room, hero)));
        }
    }
}
