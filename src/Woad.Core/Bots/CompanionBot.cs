using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;

namespace Woad.Core.Bots;

/// <summary>
/// Plays a companion's turn by its standing order (see <see cref="CompanionOrder"/>). Under any
/// order it rolls its Keel Die when Keeling Over, rouses allies Keeling Over beside it, settles its
/// rolls, drinks a potion when badly hurt and attacks what it can reach (staying
/// back, only what's next to it). It follows its owner, or the first player still in the dungeon if
/// the owner is gone: through open doors and up or down the stairs. It loots, unlocks and searches only
/// when told to: Find me (through locked doors and down pits to reach the owner), Burgle (the room's
/// locks and chests), Loot (the room's loot) and Explore (search each room once).
/// </summary>
public static class CompanionBot
{
    /// <summary>How close (in steps, diagonals counting 1) a following companion stays to its leader.</summary>
    private const int FollowDistance = 2;

    /// <summary>A following companion goes after monsters this close (diagonals counting 1) to its leader.</summary>
    private const int Guarding = 2;

    public static GameCommand NextCommand(GameSession session)
    {
        GameState state = session.State;
        Hero hero = state.ActiveHero ?? throw new InvalidOperationException("No hero is taking a turn.");
        TurnState turn = state.Turn!;
        Room room = state.RoomOf(hero);
        bool fighting = SimpleBot.Fighting(session, room, hero);
        if (turn.KeelingOver)
        {
            return new RollKeel(hero.Id, hero.Power.Banked);
        }

        // Badly on fire with the turn still fresh: stop, drop and roll.
        if ((hero.FireDice >= 2 || hero.Hearts <= 1) && session.CheckStopDropRoll() is null)
        {
            return new StopDropRoll(hero.Id);
        }

        if (turn.UnsettledPowerDice > 0)
        {
            return turn.PendingRoll is not null ? new AddPowerDie(hero.Id) : new BankPowerDie(hero.Id);
        }

        if (turn.PendingRoll is not null)
        {
            return new ConfirmAttack(hero.Id);
        }

        if (session.GetRousableAllies().FirstOrDefault() is { } fallen)
        {
            return new Rouse(hero.Id, fallen.Id);
        }

        // The Boss Key is for the players: it hands it to its leader as soon as they're side by side.
        if (hero.HasBossKey && Leader(state, hero) is { } keeper && session.GetTradePartners().Contains(keeper))
        {
            return new GiveKey(hero.Id, keeper.Id, BossKey: true);
        }

        if (SimpleBot.FleeBlast(session, hero, room) is { } flee)
        {
            return flee;
        }

        if (hero.Hearts <= hero.MaxHearts - 2 && SimpleBot.UsableBeltCard(session, hero, CardEffectKind.Heal) is { } potion)
        {
            return new UseCard(hero.Id, potion.Id);
        }

        if (session.GetAttackableMonsters()
                .FirstOrDefault(monster => hero.Order != CompanionOrder.StayBack || monster.Position.ChebyshevDistanceTo(hero.Position) == 1)
            is { } target)
        {
            return new AttackMonster(hero.Id, target.Id);
        }

        if (hero.Prone)
        {
            if (session.CheckGetUp() is null)
            {
                return new GetUp(hero.Id);
            }

            if (turn.MovementRoll is null && session.MoveDiceLeft > 0)
            {
                return new RollMovement(hero.Id);
            }
        }

        // Its order's work within reach: a lock, a chest, loot or a search.
        if (!fighting && Errand(session, hero, room) is { } errand)
        {
            return errand;
        }

        Plan plan = MakePlan(session, hero, room, fighting);
        bool canStep = turn.MovementPoints > 0;

        // Finding its owner: a locked door on the way is unlocked (a key) or picked.
        if (plan.Door is { } locked && hero.Position == room.DoorTile(locked) && room.PassageOn(locked) is { IsLocked: true })
        {
            return Unlock(session, hero, room, locked) ?? new EndTurn(hero.Id);
        }

        if (plan.Door is { } side && hero.Position == room.DoorTile(side) && canStep && session.GetUsableDoors().Contains(side))
        {
            return new GoThroughDoor(hero.Id, side);
        }

        // Finding its owner on the floor below: into the pit beside it.
        if (plan.Pit is { } pit && hero.Position.ManhattanDistanceTo(pit) == 1)
        {
            if (canStep && session.GetReachableTiles().ContainsKey(pit))
            {
                return new MoveHero(hero.Id, pit);
            }

            if (!canStep && session.MoveDiceLeft > 0)
            {
                return new RollMovement(hero.Id);
            }
        }

        if (plan.Stairs && room.Layout[hero.Position] is TileKind.StairsDown or TileKind.StairsUp && canStep)
        {
            return new TakeStairs(hero.Id);
        }

        if (!plan.WantsToMove(hero))
        {
            return LeaveTheWay(session, hero, room, plan) ?? new EndTurn(hero.Id);
        }

        if (!canStep && session.MoveDiceLeft > 0)
        {
            return new RollMovement(hero.Id);
        }

        GridPosition? destination = plan.Tile is { } tile
            ? SimpleBot.StepAlongPath(session, hero, room, tile)
            : plan.Score is { } score ? SimpleBot.StepToward(session, hero, score) : null;
        if (destination is { } step)
        {
            return new MoveHero(hero.Id, step);
        }

        return LeaveTheWay(session, hero, room, plan) ?? new EndTurn(hero.Id);
    }

    /// <summary>Where the order sends the companion this turn.</summary>
    private static Plan MakePlan(GameSession session, Hero hero, Room room, bool fighting)
    {
        GameState state = session.State;

        // Allies Keeling Over: tending companions go to them even mid-fight, the rest once it's quiet
        // (holding ones stay put).
        if ((hero.Order == CompanionOrder.Tend || (!fighting && hero.Order != CompanionOrder.Hold))
            && SimpleBot.BesideFallen(state, room, hero) is { } kneel)
        {
            return kneel == hero.Position ? Plan.Stay : new Plan { Tile = kneel };
        }

        switch (hero.Order)
        {
            case CompanionOrder.Hold when hero.HoldAt is { } post && post.RoomId == room.Id:
                return new Plan { Tile = post.Position };

            case CompanionOrder.Fight when fighting && SimpleBot.BesideNearestMonster(room, hero) is { } beside:
                return new Plan { Tile = beside };

            case CompanionOrder.StayBack when fighting:
                // Further from the nearest monster (up to 4 steps away) is better.
                int Danger(GridPosition tile) => -Math.Min(4, room.Monsters.Min(monster => monster.Position.ManhattanDistanceTo(tile)));
                return Danger(hero.Position) > -4 ? new Plan { Score = Danger } : Plan.Stay;
        }

        // Burgling and looting: go to the next thing to work on in the room.
        if (!fighting && Work(session, hero, room) is { } work)
        {
            return new Plan { Tile = work };
        }

        if (Leader(state, hero) is not { } leader)
        {
            return Plan.Stay;
        }

        // A companion with its leader guards them: it goes after a monster closing in on them.
        if (hero.Order is CompanionOrder.Follow or CompanionOrder.FindMe or CompanionOrder.Burgle or CompanionOrder.Loot or CompanionOrder.Explore
            && fighting && leader.RoomId == room.Id
            && room.Monsters.Where(monster => monster.Position.ChebyshevDistanceTo(leader.Position) <= Guarding)
                .OrderBy(monster => monster.Position.ManhattanDistanceTo(leader.Position))
                .Select(monster => Beside(room, hero, monster))
                .FirstOrDefault(tile => tile is not null) is { } guard)
        {
            return new Plan { Tile = guard };
        }

        if (leader.RoomId == room.Id)
        {
            // Carrying the Boss Key, it comes right up beside its leader to hand it over.
            if (hero.HasBossKey && hero.Position.ChebyshevDistanceTo(leader.Position) > 1)
            {
                GridPosition? beside = SimpleBot.Nearest(state, room, hero, leader.Position.SurroundingTiles().Where(tile => room.Layout.Contains(tile) && SimpleBot.Walkable(room, tile)));
                return beside is { } spot ? new Plan { Tile = spot } : new Plan { Tile = leader.Position };
            }

            return hero.Position.ChebyshevDistanceTo(leader.Position) > FollowDistance ? new Plan { Tile = leader.Position } : Plan.Stay;
        }

        // Finding its owner, locked doors are no obstacle while it has a key or Od Hand dice to pick them.
        bool through = hero.Order == CompanionOrder.FindMe && (hero.Keys > 0 || hero.EffectiveStats.OdHandDice > 0);
        Room leaderRoom = state.RoomOf(leader);
        if (leaderRoom.Floor == room.Floor)
        {
            return RouteTo(state, room, next => next.Id == leaderRoom.Id, through) is { } door ? new Plan { Door = door, Tile = room.DoorTile(door) } : Plan.Stay;
        }

        // The leader is on another floor: head for the stairs down, or back up.
        TileKind way = leaderRoom.Floor > room.Floor ? TileKind.StairsDown : TileKind.StairsUp;
        if (room.TilesOfKind(way).Select(tile => (GridPosition?)tile).FirstOrDefault() is { } stairs)
        {
            return new Plan { Stairs = true, Tile = stairs };
        }

        if (RouteTo(state, room, next => next.TilesOfKind(way).Any(), through) is { } toStairs)
        {
            return new Plan { Door = toStairs, Tile = room.DoorTile(toStairs) };
        }

        // No way down by the stairs: one finding its owner drops down a pit to the floor below.
        if (hero.Order == CompanionOrder.FindMe && way == TileKind.StairsDown && room.BelowRoomId is not null)
        {
            List<GridPosition> brinks = room.TilesOfKind(TileKind.Pit).SelectMany(pit => pit.OrthogonalNeighbors())
                .Where(tile => room.Layout.Contains(tile) && (tile == hero.Position || SimpleBot.Walkable(room, tile))).Distinct().ToList();
            GridPosition? brink = brinks.Contains(hero.Position) ? hero.Position : SimpleBot.Nearest(state, room, hero, brinks);
            if (brink is { } edge)
            {
                GridPosition pit = edge.OrthogonalNeighbors().First(next => room.Layout.Contains(next) && room.Layout[next] == TileKind.Pit);
                return new Plan { Tile = edge, Pit = pit };
            }
        }

        return Plan.Stay;
    }

    /// <summary>What the companion's order has it do right where it stands, or null.</summary>
    private static GameCommand? Errand(GameSession session, Hero hero, Room room)
    {
        TurnState turn = session.State.Turn!;
        switch (hero.Order)
        {
            case CompanionOrder.Explore when !hero.SearchedRoomIds.Contains(room.Id) && session.CheckSearch() is null:
                return new Search(hero.Id);

            case CompanionOrder.Burgle:
                foreach ((Direction side, Passage passage) in room.Passages)
                {
                    if (passage is { IsLocked: true, NeedsBossKey: false, IsHidden: false } && hero.Position == room.DoorTile(side) && Unlock(session, hero, room, side) is { } unlock)
                    {
                        return unlock;
                    }
                }

                foreach (GridPosition chest in Beside(room, hero.Position, TileKind.Chest).Where(chest => !room.IsChestOpened(chest)))
                {
                    if (room.ChestLockAt(chest) is not null && hero.Keys < 1)
                    {
                        if (session.CheckPickLock(chest) is null)
                        {
                            return new PickLock(hero.Id, chest);
                        }
                    }
                    else if (session.CheckOpenChest(chest) is null)
                    {
                        return new OpenChest(hero.Id, chest);
                    }
                }

                foreach (GridPosition gate in room.Gates.Keys.Where(gate => gate.ManhattanDistanceTo(hero.Position) == 1))
                {
                    if (hero.Keys > 0)
                    {
                        return new OpenGate(hero.Id, gate);
                    }

                    if (session.CheckPickLock(gate) is null)
                    {
                        return new PickLock(hero.Id, gate);
                    }
                }

                break;

            case CompanionOrder.Loot:
                foreach (GridPosition tile in new[] { hero.Position }.Concat(hero.Position.SurroundingTiles()).Where(room.Layout.Contains))
                {
                    if (room.HasLootAt(tile) && session.CheckPickUp(tile) is null && WorthTaking(room, hero, tile))
                    {
                        return new PickUpLoot(hero.Id, tile);
                    }
                }

                foreach (GridPosition chest in Beside(room, hero.Position, TileKind.Chest).Where(chest => !room.IsChestOpened(chest) && room.ChestLockAt(chest) is null))
                {
                    if (session.CheckOpenChest(chest) is null)
                    {
                        return new OpenChest(hero.Id, chest);
                    }
                }

                if (!turn.HasActed && Beside(room, hero.Position, TileKind.Pot).Select(pot => (GridPosition?)pot).FirstOrDefault() is { } pot)
                {
                    return new SmashPot(hero.Id, pot);
                }

                break;
        }

        return null;
    }

    /// <summary>Where a burgling or looting companion goes next in the room: beside the nearest thing left to do, or null.</summary>
    private static GridPosition? Work(GameSession session, Hero hero, Room room)
    {
        GameState state = session.State;
        IEnumerable<GridPosition> spots = hero.Order switch
        {
            CompanionOrder.Burgle =>
                room.Passages.Where(door => door.Value is { IsLocked: true, NeedsBossKey: false, IsHidden: false } && (hero.Keys > 0 || hero.EffectiveStats.OdHandDice > 0))
                    .Select(door => room.DoorTile(door.Key))
                    .Concat(room.TilesOfKind(TileKind.Chest).Where(chest => !room.IsChestOpened(chest)).SelectMany(chest => chest.OrthogonalNeighbors()))
                    .Concat(room.Gates.Keys.SelectMany(gate => gate.OrthogonalNeighbors())),
            CompanionOrder.Loot =>
                room.LootPositions.Concat(room.Gold.Keys).Concat(room.Keys.Keys)
                    .Where(tile => WorthTaking(room, hero, tile))
                    .SelectMany(tile => new[] { tile }.Concat(tile.SurroundingTiles()))
                    .Concat(room.TilesOfKind(TileKind.Chest).Where(chest => !room.IsChestOpened(chest) && room.ChestLockAt(chest) is null).SelectMany(chest => chest.OrthogonalNeighbors()))
                    .Concat(room.TilesOfKind(TileKind.Pot).SelectMany(pot => pot.OrthogonalNeighbors())),
            _ => [],
        };
        List<GridPosition> open = spots.Where(tile => room.Layout.Contains(tile) && SimpleBot.Walkable(room, tile)).Distinct().ToList();
        return open.Contains(hero.Position) ? null : SimpleBot.Nearest(state, room, hero, open);
    }

    /// <summary>Loot worth a looter's while: gold, keys, or a card it can carry.</summary>
    private static bool WorthTaking(Room room, Hero hero, GridPosition tile) =>
        room.GoldAt(tile) > 0 || room.KeysAt(tile) > 0 || room.BossKeyAt == tile || room.LootAt(tile).Any(card => hero.CanCarry(card.Definition));

    private static IEnumerable<GridPosition> Beside(Room room, GridPosition at, TileKind kind) =>
        at.OrthogonalNeighbors().Where(tile => room.Layout.Contains(tile) && room.Layout[tile] == kind);

    /// <summary>Opens a locked door it stands in: with a key, or by picking the lock.</summary>
    private static GameCommand? Unlock(GameSession session, Hero hero, Room room, Direction side) =>
        hero.Keys > 0 ? new UnlockDoor(hero.Id, side)
        : session.CheckPickLock(hero.Position) is null ? new PickLock(hero.Id, hero.Position)
        : null;

    /// <summary>The closest tile the companion can reach to strike <paramref name="monster"/> from, if any.</summary>
    private static GridPosition? Beside(Room room, Hero hero, Monster monster)
    {
        PathSearch search = PathSearch.Run(hero.Position, tile => SimpleBot.Walkable(room, tile));
        return monster.Position.SurroundingTiles()
            .Where(tile => room.Layout.Contains(tile) && search.CanReach(tile) && Targeting.CanHeroAttack(room.Layout, 1, tile, monster.Position))
            .OrderBy(tile => search.Distances[tile])
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();
    }

    /// <summary>The owner while they're in the dungeon, otherwise the first player who still is.</summary>
    private static Hero? Leader(GameState state, Hero companion) =>
        state.Heroes.FirstOrDefault(hero => hero.Id == companion.OwnerId && hero.IsInPlay)
        ?? state.Heroes.FirstOrDefault(hero => hero.IsInPlay && !hero.IsCompanion);

    /// <summary>
    /// The door to take toward the nearest room matching <paramref name="isGoal"/>, through doors that are
    /// open (and, with <paramref name="throughLocks"/>, locked ones too, but never the Boss Door).
    /// </summary>
    private static Direction? RouteTo(GameState state, Room start, Func<Room, bool> isGoal, bool throughLocks = false)
    {
        if (start.IsSealed)
        {
            return null;
        }

        var firstStep = new Dictionary<string, Direction?> { [start.Id] = null };
        var frontier = new Queue<Room>([start]);
        while (frontier.Count > 0)
        {
            Room room = frontier.Dequeue();
            if (room != start && isGoal(room))
            {
                return firstStep[room.Id];
            }

            foreach ((Direction side, string nextId) in room.Doors.OrderBy(door => door.Key))
            {
                bool passable = room.PassageOn(side) is not { IsOpen: false } passage
                    || (throughLocks && passage is { IsLocked: true, NeedsBossKey: false, IsHidden: false, IsBlocked: false });
                if (passable && firstStep.TryAdd(nextId, firstStep[room.Id] ?? side))
                {
                    frontier.Enqueue(state.Rooms[nextId]);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A companion with nowhere to be steps off a doorway, the stairs, the exit, loot it won't pick up,
    /// and the spots beside locked gates and unopened chests (unless told to hold there), so it doesn't
    /// stand in its party's way.
    /// </summary>
    private static GameCommand? LeaveTheWay(GameSession session, Hero hero, Room room, Plan plan)
    {
        // Beside a locked gate matters even mid-fight: the monsters may be beyond it.
        bool BesideGate(GridPosition at) => at.OrthogonalNeighbors().Any(next => room.Layout.Contains(next) && room.Layout[next] == TileKind.Gate);
        bool InTheWay(GridPosition at) =>
            room.Layout[at] is TileKind.StairsDown or TileKind.StairsUp or TileKind.Exit
            || room.Doors.Keys.Any(side => room.DoorTile(side) == at)
            || room.HasLootAt(at)
            || BesideGate(at)
            || at.OrthogonalNeighbors().Any(next => room.Layout.Contains(next) && room.Layout[next] == TileKind.Chest && !room.IsChestOpened(next));
        if (plan.Door is not null || plan.Stairs || hero.Order == CompanionOrder.Hold || !InTheWay(hero.Position)
            || (room.Monsters.Count > 0 && !BesideGate(hero.Position)))
        {
            return null;
        }

        if (session.State.Turn!.MovementPoints == 0)
        {
            return session.MoveDiceLeft > 0 ? new RollMovement(hero.Id) : null;
        }

        return SimpleBot.StepToward(session, hero, at => InTheWay(at) ? 1 : 0) is { } aside ? new MoveHero(hero.Id, aside) : null;
    }

    private sealed record Plan
    {
        public static Plan Stay { get; } = new();

        /// <summary>A tile to walk toward.</summary>
        public GridPosition? Tile { get; init; }

        /// <summary>Or: walk to the reachable tile scoring lowest, if it beats where the companion stands.</summary>
        public Func<GridPosition, int>? Score { get; init; }

        /// <summary>A door to go through once in it.</summary>
        public Direction? Door { get; init; }

        /// <summary>Take the stairs once on them.</summary>
        public bool Stairs { get; init; }

        /// <summary>A pit to drop into once beside it (to the floor below).</summary>
        public GridPosition? Pit { get; init; }

        public bool WantsToMove(Hero hero) => Score is not null || (Tile is { } tile && tile != hero.Position) || Door is not null || Stairs || Pit is not null;
    }
}
