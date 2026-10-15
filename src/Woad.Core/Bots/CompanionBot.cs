using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;

namespace Woad.Core.Bots;

/// <summary>
/// Plays a companion's turn by its standing order (see <see cref="CompanionOrder"/>). Under any
/// order it settles its rolls, drinks a potion when badly hurt and attacks what it can reach (staying
/// back, only what's next to it). It follows its owner, or the first player still in the dungeon if
/// the owner is gone: through open doors and up or down the stairs. It never loots, unlocks or searches;
/// that's left to the players.
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
        bool fighting = room.Monsters.Count > 0;

        if (turn.UnsettledPowerDice > 0)
        {
            return turn.PendingRoll is not null ? new AddPowerDie(hero.Id) : new BankPowerDie(hero.Id);
        }

        if (turn.PendingRoll is not null)
        {
            return new ConfirmAttack(hero.Id);
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

        Plan plan = MakePlan(session, hero, room, fighting);
        bool canStep = turn.MovementPoints > 0;
        if (plan.Door is { } side && hero.Position == side.DoorTile() && canStep && session.GetUsableDoors().Contains(side))
        {
            return new GoThroughDoor(hero.Id, side);
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

        if (Leader(state, hero) is not { } leader)
        {
            return Plan.Stay;
        }

        // A following companion guards its leader: it goes after a monster closing in on them.
        if (hero.Order == CompanionOrder.Follow && fighting && leader.RoomId == room.Id
            && room.Monsters.Where(monster => monster.Position.ChebyshevDistanceTo(leader.Position) <= Guarding)
                .OrderBy(monster => monster.Position.ManhattanDistanceTo(leader.Position))
                .Select(monster => Beside(room, hero, monster))
                .FirstOrDefault(tile => tile is not null) is { } guard)
        {
            return new Plan { Tile = guard };
        }

        if (leader.RoomId == room.Id)
        {
            return hero.Position.ChebyshevDistanceTo(leader.Position) > FollowDistance ? new Plan { Tile = leader.Position } : Plan.Stay;
        }

        Room leaderRoom = state.RoomOf(leader);
        if (leaderRoom.Floor == room.Floor)
        {
            return RouteTo(state, room, next => next.Id == leaderRoom.Id) is { } door ? new Plan { Door = door, Tile = door.DoorTile() } : Plan.Stay;
        }

        // The leader is on another floor: head for the stairs down, or back up.
        TileKind way = leaderRoom.Floor > room.Floor ? TileKind.StairsDown : TileKind.StairsUp;
        if (room.TilesOfKind(way).Select(tile => (GridPosition?)tile).FirstOrDefault() is { } stairs)
        {
            return new Plan { Stairs = true, Tile = stairs };
        }

        return RouteTo(state, room, next => next.TilesOfKind(way).Any()) is { } toStairs
            ? new Plan { Door = toStairs, Tile = toStairs.DoorTile() }
            : Plan.Stay;
    }

    /// <summary>The closest tile the companion can reach to strike <paramref name="monster"/> from, if any.</summary>
    private static GridPosition? Beside(Room room, Hero hero, Monster monster)
    {
        PathSearch search = PathSearch.Run(hero.Position, tile => SimpleBot.Walkable(room, tile));
        return monster.Position.SurroundingTiles()
            .Where(tile => tile.IsInRoom && search.CanReach(tile) && Targeting.CanHeroAttack(room.Layout, 1, tile, monster.Position))
            .OrderBy(tile => search.Distances[tile])
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();
    }

    /// <summary>The owner while they're in the dungeon, otherwise the first player who still is.</summary>
    private static Hero? Leader(GameState state, Hero companion) =>
        state.Heroes.FirstOrDefault(hero => hero.Id == companion.OwnerId && hero.IsActive)
        ?? state.Heroes.FirstOrDefault(hero => hero.IsActive && !hero.IsCompanion);

    /// <summary>The door to take toward the nearest room matching <paramref name="isGoal"/>, through doors that are open.</summary>
    private static Direction? RouteTo(GameState state, Room start, Func<Room, bool> isGoal)
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
                if (room.PassageOn(side) is not { IsOpen: false } && firstStep.TryAdd(nextId, firstStep[room.Id] ?? side))
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
        bool BesideGate(GridPosition at) => at.OrthogonalNeighbors().Any(next => next.IsInRoom && room.Layout[next] == TileKind.Gate);
        bool InTheWay(GridPosition at) =>
            room.Layout[at] is TileKind.StairsDown or TileKind.StairsUp or TileKind.Exit
            || room.Doors.Keys.Any(side => side.DoorTile() == at)
            || room.HasLootAt(at)
            || BesideGate(at)
            || at.OrthogonalNeighbors().Any(next => next.IsInRoom && room.Layout[next] == TileKind.Chest && !room.IsChestOpened(next));
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

        public bool WantsToMove(Hero hero) => Score is not null || (Tile is { } tile && tile != hero.Position) || Door is not null || Stairs;
    }
}
