using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;

namespace Woad.Core.Rules;

/// <summary>
/// The Emerald Keep's portals are unstable: rifts crackling with lightning (a <see cref="TrapKind.Rift"/>, in plain
/// sight). Whoever walks into one, or is pushed in, is struck by a 1d6 Elektrik die and the lightning leaps on to
/// everyone beside them, and on from each of those, each struck once; everyone struck, hit or miss, is flung to a
/// random spot on floor 1, any room at all, secret or not. (A boss, or anything too big, takes the blow but stays.)
/// A rift can be disarmed, closing it: a banked Power Die buys the attempt, against <see cref="GameRules.RiftDifficulty"/>;
/// botched, it goes off on whoever tried.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>A rift goes off on whoever's on its tile (or botching its disarming beside it), and leaps on from them.</summary>
    private void RiftStrike(Room room, GridPosition rift, int firstId, List<GameEvent> events)
    {
        // Who the lightning reaches: the first, then everyone beside anyone struck, once each.
        var struck = new List<int> { firstId };
        for (int i = 0; i < struck.Count; i++)
        {
            List<GridPosition> tiles = TilesOf(room, struck[i]);
            foreach (int next in Beside(room, tiles, struck[i]).Where(id => !struck.Contains(id)).Order())
            {
                struck.Add(next);
            }
        }

        events.Add(new RiftCrackled(room.Id, rift, struck));
        foreach (int id in struck)
        {
            (int[] faces, DieType[] types) = RollTypedDice([(DieType.Elektrik, 1)]);
            var roll = new DiceRoll(faces);
            int bonus = room.Floor;
            int total = roll.Total + bonus;
            events.Add(new RiftZapped(id, roll, bonus, total));
            if (room.FindMonster(id) is { } monster)
            {
                DamageMonster(null, room, monster, total, DamageSource.Fall, events);
            }
            else if (State.Heroes.FirstOrDefault(hero => hero.Id == id) is { IsInPlay: true } hero)
            {
                DamageHero(room, hero, total, "Torn apart by an unstable portal", 0, events);
            }
        }

        foreach (int id in struck)
        {
            Fling(room, id, events);
        }
    }

    /// <summary>The tiles a figure stands on (a big monster's four), or none if it's gone.</summary>
    private List<GridPosition> TilesOf(Room room, int id) =>
        room.FindMonster(id) is { } monster ? [.. monster.Tiles]
        : State.Heroes.FirstOrDefault(hero => hero.Id == id && hero.RoomId == room.Id) is { } hero ? [hero.Position]
        : [];

    /// <summary>Flings someone the rift struck to a random free spot on floor 1: any room, secret or not.</summary>
    private void Fling(Room from, int id, List<GameEvent> events)
    {
        List<Room> rooms = [.. State.Rooms.Values.Where(room => room.Floor == 1 && room.Level == 0).OrderBy(room => room.Id, StringComparer.Ordinal)];
        if (rooms.Count == 0)
        {
            return;
        }

        Room to = _random.Behavior.Pick(rooms);
        List<GridPosition> open = [.. to.Positions.Where(tile => to.Layout[tile].IsWalkable() && to.Layout[tile] != TileKind.Exit && to.TrapAt(tile) is null && !IsOccupied(to, tile))];
        if (open.Count == 0)
        {
            return;
        }

        GridPosition spot = _random.Behavior.Pick(open);
        if (from.FindMonster(id) is { } monster)
        {
            // A boss, or anything too big, holds its ground.
            if (monster.Definition.Boss || monster.Definition.Miniboss || monster.Definition.Size > 1)
            {
                return;
            }

            from.RemoveMonster(monster);
            monster.Position = spot;
            to.AddMonster(monster);
            events.Add(new MonsterFlung(monster.Id, from.Id, to.Id, spot));
            ClearRoomIfEmpty(from, events);
        }
        else if (State.Heroes.FirstOrDefault(hero => hero.Id == id && hero.RoomId == from.Id) is { } hero && (hero.IsInPlay || hero.IsKeelingOver))
        {
            hero.RoomId = to.Id;
            hero.Position = spot;
            to.Explored = true;
            events.Add(new HeroFlung(hero.Id, from.Id, to.Id, spot));
        }
    }
}
