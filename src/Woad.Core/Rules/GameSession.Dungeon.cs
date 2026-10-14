using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;

namespace Woad.Core.Rules;

/// <summary>
/// The dungeon's hazards and secrets: locked doors and the Boss Door, trap tiles, trap doors,
/// cracked walls broken by bombs, and keys that monsters and cleared rooms leave behind.
/// </summary>
public sealed partial class GameSession
{
    private string? Handle(UnlockDoor command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (room.PassageOn(command.Door) is not { IsHidden: false } passage)
        {
            return "There's no door on that side.";
        }

        if (hero.Position != command.Door.DoorTile())
        {
            return "Stand in the doorway first.";
        }

        if (!passage.IsLocked)
        {
            return "That door isn't locked.";
        }

        if (passage.NeedsBossKey)
        {
            if (!hero.HasBossKey)
            {
                return "The Boss Door opens only with the Boss Key.";
            }

            hero.HasBossKey = false;
        }
        else
        {
            if (hero.Keys < 1)
            {
                return $"You have no keys. Pick the lock with Od Hand ({passage.Difficulty}+).";
            }

            hero.Keys--;
            events.Add(new KeyUsed(hero.Id, hero.Keys));
        }

        passage.IsLocked = false;
        events.Add(new DoorUnlocked(hero.Id, room.Id, command.Door, Picked: false, passage.NeedsBossKey));
        return null;
    }

    private string? Handle(JumpDown command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (room.TrapAt(command.TrapDoor) is not { Kind: TrapKind.TrapDoor, Revealed: true })
        {
            return "There's no trap door you know of there.";
        }

        if (hero.Position.ManhattanDistanceTo(command.TrapDoor) != 1)
        {
            return "Stand next to the trap door to jump down it.";
        }

        if (CheckCanMove(1) is { } error)
        {
            return error;
        }

        Fall(hero, room, jumped: true, events);
        return null;
    }

    /// <summary>Hidden traps go off under heroes; revealed ones are walked around (see HeroPathSearch).</summary>
    private void SpringTrap(Hero hero, Room room, Trap trap, GridPosition position, List<GameEvent> events)
    {
        trap.Revealed = true;
        events.Add(new TrapSprung(hero.Id, trap.Kind, position));
        HurtHero(room, hero, 1, events);
        if (trap.Kind == TrapKind.TrapDoor && hero.IsActive)
        {
            Fall(hero, room, jumped: false, events);
        }
    }

    private void HurtHero(Room room, Hero hero, int amount, List<GameEvent> events)
    {
        hero.Hearts = Math.Max(0, hero.Hearts - amount);
        events.Add(new Damaged(hero.Id, amount, hero.Hearts));
        if (hero.Hearts == 0)
        {
            DownHero(room, hero, events);
        }
    }

    /// <summary>
    /// Drops a hero into a random room on the floor below (not the boss room), or another room on
    /// this floor when it's the last. Ends their move.
    /// </summary>
    private void Fall(Hero hero, Room from, bool jumped, List<GameEvent> events)
    {
        List<Room> below = State.Rooms.Values.Where(room => room.Floor == from.Floor + 1 && room.Role != RoomRole.Boss).ToList();
        List<Room> candidates = below.Count > 0
            ? below
            : State.Rooms.Values.Where(room => room.Floor == from.Floor && room.Role != RoomRole.Boss && room != from).ToList();
        candidates.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        if (candidates.Count == 0)
        {
            return;
        }

        Room target = _random.Behavior.Pick(candidates);
        if (Landing(target) is not { } landing)
        {
            return;
        }

        Turn.EndMove();
        hero.RoomId = target.Id;
        hero.Position = landing;
        target.Explored = true;
        bool firstTime = target.Floor > hero.DeepestFloor;
        events.Add(new HeroFell(hero.Id, from.Id, target.Id, target.Floor, landing, firstTime, jumped));
        if (firstTime)
        {
            ReachNewFloor(hero, target.Floor, events);
        }
    }

    /// <summary>The first time a hero reaches a floor: Luck and charges come back, and they earn XP.</summary>
    private static void ReachNewFloor(Hero hero, int floor, List<GameEvent> events)
    {
        hero.DeepestFloor = floor;
        hero.LuckRemaining = Math.Max(0, hero.EffectiveStats.Luck);
        foreach (CardInstance card in hero.AllCards.Where(card => card.Definition.Usage == CardUsage.Charges))
        {
            card.ChargesRemaining = card.Definition.Charges;
        }

        GrantXp(hero, GameRules.XpPerNewFloor, events);
    }

    /// <summary>A bomb's blast breaks the cracked walls in it, and cracked walls standing in doorways.</summary>
    private static List<(GridPosition Tile, Direction? Door)> CrackedWallsIn(Room room, GridPosition center, int radius) =>
        RoomGrid.AllPositions()
            .Where(tile => tile.ChebyshevDistanceTo(center) <= radius && room.Layout[tile] == TileKind.CrackedWall)
            .Select(tile => (tile, (Direction?)null))
            .Concat(room.Passages
                .Where(door => door.Value is { IsBlocked: true, IsHidden: false } && door.Key.DoorTile().ChebyshevDistanceTo(center) <= radius)
                .Select(door => (door.Key.DoorTile(), (Direction?)door.Key)))
            .ToList();

    private void BreakWalls(Room room, IEnumerable<(GridPosition Tile, Direction? Door)> walls, List<GameEvent> events)
    {
        foreach ((GridPosition tile, Direction? door) in walls)
        {
            if (door is { } side)
            {
                room.PassageOn(side)!.IsBlocked = false;
            }
            else
            {
                room.Layout.Set(tile, TileKind.Floor);
            }

            events.Add(new WallBroken(room.Id, tile, door));
            if (door is null && room.Caches.TryGetValue(tile, out Cache? cache))
            {
                RevealCache(room, tile, cache, events);
            }
        }
    }

    /// <summary>A found stash spills its keys and cards onto its tile.</summary>
    private void RevealCache(Room room, GridPosition tile, Cache cache, List<GameEvent> events)
    {
        room.RemoveCache(tile);
        if (cache.Keys > 0)
        {
            room.AddKeys(tile, cache.Keys);
            events.Add(new KeysAppeared(room.Id, tile, cache.Keys));
        }

        for (int i = 0; i < cache.Cards; i++)
        {
            var card = new CardInstance(State.NextId(), State.LootDeck.Draw(room.Floor), broughtInByHeroId: null);
            room.AddLoot(tile, [card]);
            events.Add(new LootDropped(room.Id, tile, card.Id));
        }
    }

    /// <summary>After a monster falls: perhaps a key where it fell, and perhaps one more when the room is cleared.</summary>
    private void DropKeys(Room room, GridPosition fallenAt, List<GameEvent> events)
    {
        KeyChances chances = Content.GetTheme(State.ThemeId).Keys;
        if (Chance(chances.Monster))
        {
            GridPosition spot = SafeTile(room, fallenAt);
            room.AddKeys(spot, 1);
            events.Add(new KeysAppeared(room.Id, spot, 1));
        }

        if (room.Monsters.Count == 0 && Chance(chances.RoomCleared))
        {
            List<GridPosition> free = RoomGrid.AllPositions()
                .Where(tile => room.Layout[tile] == TileKind.Floor && !IsOccupied(room, tile) && room.TrapAt(tile) is null && !room.HasLootAt(tile))
                .ToList();
            if (free.Count > 0)
            {
                GridPosition tile = _random.Loot.Pick(free);
                room.AddKeys(tile, 1);
                events.Add(new KeysAppeared(room.Id, tile, 1));
            }
        }
    }

    /// <summary>A downed hero drops their keys and the Boss Key where they fell.</summary>
    private static void DropHeroKeys(Room room, Hero hero, List<GameEvent> events)
    {
        GridPosition spot = SafeTile(room, hero.Position);
        if (hero.Keys > 0)
        {
            room.AddKeys(spot, hero.Keys);
            events.Add(new KeysAppeared(room.Id, spot, hero.Keys));
            hero.Keys = 0;
        }

        if (hero.HasBossKey)
        {
            hero.HasBossKey = false;
            room.BossKeyAt = spot;
            events.Add(new BossKeyDropped(room.Id, spot));
        }
    }

    /// <summary>
    /// Where something dropped at <paramref name="near"/> lands: there, unless a trap is on it (a
    /// monster or hero standing on one); then the nearest walkable tile without a trap, so loot is
    /// never left where nobody may step.
    /// </summary>
    private static GridPosition SafeTile(Room room, GridPosition near)
    {
        if (room.TrapAt(near) is null)
        {
            return near;
        }

        PathSearch search = PathSearch.Run(near, tile => room.Layout[tile].IsWalkable());
        return search.Distances
            .Where(entry => room.TrapAt(entry.Key) is null)
            .OrderBy(entry => entry.Value)
            .ThenBy(entry => entry.Key.Y)
            .ThenBy(entry => entry.Key.X)
            .Select(entry => entry.Key)
            .DefaultIfEmpty(near)
            .First();
    }
}
