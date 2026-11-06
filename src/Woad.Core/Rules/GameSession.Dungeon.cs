using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
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
    /// <summary>What a gate says to someone without its key.</summary>
    public const string GateLockedText = "The gate is locked fast: only its key opens it. Its guardian carries it.";

    /// <summary>Whether a hero holds a gate's key (the Emerald Key).</summary>
    public static bool HasGateKey(Hero hero) => hero.AllCards.Any(card => card.Definition.GateKey);

    private string? Handle(UnlockDoor command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (room.PassageOn(command.Door) is not { IsHidden: false } passage)
        {
            return "There's no door on that side.";
        }

        if (!AtDoor(room, command.Door, hero.Position))
        {
            return "Go up to the door first.";
        }

        if (!passage.IsLocked)
        {
            return "That door isn't locked.";
        }

        if (passage.NeedsGateKey)
        {
            if (!HasGateKey(hero))
            {
                return GateLockedText;
            }
        }
        else if (passage.NeedsBossKey)
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

    /// <summary>
    /// A trap goes off on a hero: hidden, or found and walked onto anyway (see HeroPathSearch), or botched in
    /// disarming it. It attacks (see <see cref="GameRules.TrapAttack"/>) and the hero defends as from a monster,
    /// +1 if it had been found: a hit is a heart, and a trap door's drops them through. A bomb trap pops out a
    /// lit bomb instead. <paramref name="onIt"/>: they're on the trap's tile (not beside it, disarming).
    /// </summary>
    private void SpringTrap(Hero hero, Room room, Trap trap, GridPosition position, List<GameEvent> events, bool onIt = true)
    {
        bool found = trap.Revealed;
        trap.Revealed = true;
        events.Add(new TrapSprung(hero.Id, trap.Kind, position));
        if (trap.Kind == TrapKind.BombTrap)
        {
            // No harm itself: it pops out a lit bomb, whose fuse burns on the hero's turns.
            room.SetTrap(position, null);
            PlaceBomb(room, position, lit: true, hero.Id, events);
            return;
        }

        DiceRoll roll = _dice.Roll(GameRules.TrapAttackDice);
        int bonus = GameRules.TrapAttack(room.Floor, trap.Difficulty);
        events.Add(new TrapAttacked(hero.Id, trap.Kind, roll, bonus, roll.Total + bonus, found));
        int damage = DamageHero(room, hero, roll.Total + bonus - (found ? GameRules.FoundTrapDefense : 0), TrapBane(trap.Kind), 0, events);
        if (trap.Kind == TrapKind.TrapDoor && onIt && damage > 0 && hero.IsActive)
        {
            Fall(hero, room, position, jumped: false, events);
        }
    }

    /// <summary>Damage from the dungeon itself (traps, falls, drowning); <paramref name="bane"/> is for the tombstone.</summary>
    private void HurtHero(Room room, Hero hero, int amount, string bane, List<GameEvent> events)
    {
        LoseHearts(hero, amount, events);
        if (hero.Hearts == 0)
        {
            KeelOver(room, hero, bane, events);
        }
    }

    /// <summary>
    /// Drops a hero through the hole (a pit or trap door) at <paramref name="hole"/> into the room
    /// underneath, landing on the tile below it or the nearest free one. Ends their movement. A deep hole
    /// drops them two floors (see <see cref="Room.DeepHoles"/>), and landing on another pit, they fall on
    /// through it: every floor past the first is another heart. A trap door with no room under it (only
    /// in hand-built runs) drops them into a random room on the next floor instead, or another room on
    /// this floor when it's the last.
    /// </summary>
    /// <param name="fallen">Floors already fallen on the way (falling on through a pit).</param>
    private void Fall(Hero hero, Room from, GridPosition hole, bool jumped, List<GameEvent> events, int fallen = 0)
    {
        Room? target = from.DeepHoles.TryGetValue(hole, out string? deepId) ? State.Rooms[deepId]
            : from.BelowRoomId is { } belowId ? State.Rooms[belowId]
            : null;
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

        int floors = fallen + Math.Max(0, target.Floor - from.Floor);
        GridPosition under = Beneath(from, hole, target);

        // Landing on another pit, they fall on through it.
        if (target.Layout[under] == TileKind.Pit && (target.BelowRoomId is not null || target.DeepHoles.ContainsKey(under)) && target.Floor > from.Floor)
        {
            hero.RoomId = target.Id;
            hero.Position = under;
            target.Explored = true;
            events.Add(new HeroFell(hero.Id, from.Id, target.Id, target.Floor, under, target.Floor > hero.DeepestFloor, jumped));
            if (target.Floor > hero.DeepestFloor)
            {
                ReachNewFloor(hero, target.Floor, events);
            }

            Fall(hero, target, under, jumped, events, floors);
            return;
        }

        if (LandingNear(target, under) is not { } landing)
        {
            return;
        }

        if (hero.Id == Turn.HeroId)
        {
            Turn.EndMove();
        }
        hero.RoomId = target.Id;
        hero.Position = landing;
        // Falling to the floor below, their lights and spells need a save (the stairs don't).
        if (target.Floor > from.Floor)
        {
            StartFallSave(hero, floors, events);
        }
        target.Explored = true;
        bool firstTime = target.Floor > hero.DeepestFloor;
        events.Add(new HeroFell(hero.Id, from.Id, target.Id, target.Floor, landing, firstTime, jumped) { Floors = floors });
        if (firstTime)
        {
            ReachNewFloor(hero, target.Floor, events);
        }

        // Every floor fallen past the first is another heart.
        if (floors > 1)
        {
            HurtHero(target, hero, floors - 1, "Fell a long way", events);
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

        foreach (CardInstance crossbow in hero.AllCards.Where(card => card.Definition.Reload is not null))
        {
            crossbow.Unloaded = false;
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

        foreach (string find in cache.Finds)
        {
            DropLoot(room, tile, new CardInstance(State.NextId(), Content.GetCard(find), broughtInByHeroId: null), events);
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
