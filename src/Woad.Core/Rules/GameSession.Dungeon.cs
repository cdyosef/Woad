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

        if (hero.Position != room.DoorTile(command.Door))
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

    private string? Handle(OpenGate command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (!room.Gates.ContainsKey(command.Gate))
        {
            return "There's no locked gate there.";
        }

        if (hero.Position.ManhattanDistanceTo(command.Gate) != 1)
        {
            return "Stand next to the gate first.";
        }

        if (hero.Keys < 1)
        {
            return $"You have no keys. Pick the lock with Od Hand ({room.Gates[command.Gate]}+).";
        }

        hero.Keys--;
        events.Add(new KeyUsed(hero.Id, hero.Keys));
        room.OpenGate(command.Gate);
        events.Add(new GateOpened(hero.Id, room.Id, command.Gate, Picked: false));
        return null;
    }

    private string? Handle(GoThroughBreach command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (!room.Breaches.TryGetValue(hero.Position, out (string RoomId, GridPosition Tile) way) || room.Layout[hero.Position] == TileKind.CrackedWall)
        {
            return "Stand in a broken-open breach first.";
        }

        if (room.IsSealed)
        {
            return "The way out is sealed until every monster here is defeated.";
        }

        if (CheckCanMove(Crawl) is { } error)
        {
            return error;
        }

        Room target = State.Rooms[way.RoomId];
        GridPosition? arrival = IsOccupied(target, way.Tile) ? LandingNear(target, way.Tile) : way.Tile;
        if (arrival is not { } landing)
        {
            return "There's no room on the other side.";
        }

        hero.RoomId = target.Id;
        hero.Position = landing;
        Turn.MovementPoints -= Crawl;
        Turn.HasMoved = true;
        bool firstVisit = !target.Explored;
        target.Explored = true;
        events.Add(new HeroEnteredRoom(hero.Id, room.Id, target.Id, landing, firstVisit));
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

        if (CheckCanMove(Crawl) is { } error)
        {
            return error;
        }

        Fall(hero, room, command.TrapDoor, jumped: true, events);
        return null;
    }

    /// <summary>A trap goes off under a hero: hidden, or found and walked onto anyway (see HeroPathSearch).</summary>
    private void SpringTrap(Hero hero, Room room, Trap trap, GridPosition position, List<GameEvent> events)
    {
        trap.Revealed = true;
        events.Add(new TrapSprung(hero.Id, trap.Kind, position));
        if (trap.Kind == TrapKind.BombTrap)
        {
            // No harm itself: it pops out a lit bomb, whose fuse burns on the hero's turns.
            room.SetTrap(position, null);
            PlaceBomb(room, position, lit: true, hero.Id, events);
            return;
        }

        HurtHero(room, hero, 1, TrapBane(trap.Kind), events);
        if (trap.Kind == TrapKind.TrapDoor && hero.IsActive)
        {
            Fall(hero, room, position, jumped: false, events);
        }
    }

    /// <summary>Damage from the dungeon itself (traps, falls, drowning); <paramref name="bane"/> is for the tombstone.</summary>
    private void HurtHero(Room room, Hero hero, int amount, string bane, List<GameEvent> events)
    {
        hero.Hearts = Math.Max(0, hero.Hearts - amount);
        events.Add(new Damaged(hero.Id, amount, hero.Hearts));
        if (hero.Hearts == 0)
        {
            KeelOver(room, hero, bane, events);
        }
    }

    /// <summary>
    /// Drops a hero through the hole (a pit or trap door) at <paramref name="hole"/> into the room
    /// underneath, landing on the tile below it or the nearest free one. Ends their movement. A trap
    /// door with no room under it (only in hand-built runs) drops them into a random room on the next
    /// floor instead, or another room on this floor when it's the last.
    /// </summary>
    private void Fall(Hero hero, Room from, GridPosition hole, bool jumped, List<GameEvent> events)
    {
        Room? target = from.BelowRoomId is { } belowId ? State.Rooms[belowId] : null;
        if (target is null)
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

            target = _random.Behavior.Pick(candidates);
        }

        if (LandingNear(target, Beneath(from, hole, target)) is not { } landing)
        {
            return;
        }

        if (hero.Id == Turn.HeroId)
        {
            Turn.EndMove();
        }
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
        room.Positions
            .Where(tile => tile.ChebyshevDistanceTo(center) <= radius && room.Layout[tile] == TileKind.CrackedWall)
            .Select(tile => (tile, (Direction?)null))
            .Concat(room.Passages
                .Where(door => door.Value is { IsBlocked: true, IsHidden: false } && room.DoorTile(door.Key).ChebyshevDistanceTo(center) <= radius)
                .Select(door => (room.DoorTile(door.Key), (Direction?)door.Key)))
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

            // A breach breaks through to the facing tile of the room next door.
            if (door is null && room.Breaches.TryGetValue(tile, out (string RoomId, GridPosition Tile) breach)
                && State.Rooms[breach.RoomId] is { } beyond && beyond.Layout[breach.Tile] == TileKind.CrackedWall)
            {
                beyond.Layout.Set(breach.Tile, TileKind.Floor);
                events.Add(new WallBroken(beyond.Id, breach.Tile, null));
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
            DropLoot(room, tile, card, events);
        }
    }

    /// <summary>A defeated monster may leave a key where it fell.</summary>
    private void DropKey(Room room, GridPosition fallenAt, List<GameEvent> events)
    {
        if (Chance(Content.GetTheme(State.ThemeId).Keys.Monster))
        {
            GridPosition spot = SafeTile(room, fallenAt);
            room.AddKeys(spot, 1);
            events.Add(new KeysAppeared(room.Id, spot, 1));
        }
    }

    /// <summary>A cleared room may turn up a key on a free tile.</summary>
    private void RoomClearedKey(Room room, List<GameEvent> events)
    {
        if (Chance(Content.GetTheme(State.ThemeId).Keys.RoomCleared))
        {
            List<GridPosition> free = room.Positions
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
    /// monster or hero standing on one) or it's a pit; then the nearest walkable tile without a trap,
    /// so loot is never left where nobody would step.
    /// </summary>
    private static GridPosition SafeTile(Room room, GridPosition near)
    {
        if (room.TrapAt(near) is null && room.Layout[near].IsWalkable())
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
