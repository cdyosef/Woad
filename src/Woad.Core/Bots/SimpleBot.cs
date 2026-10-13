using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;

namespace Woad.Core.Bots;

/// <summary>
/// A simple automatic player for smoke tests and the game's <c>--autoplay</c> mode. It fights
/// whatever is in its room, heals when hurt and loots what it passes, then explores: unexplored
/// rooms first, then the stairs, then the exit. Every command it returns should be accepted;
/// a refusal means a bug in the bot or the rules.
/// </summary>
public static class SimpleBot
{
    public static GameCommand NextCommand(GameSession session)
    {
        GameState state = session.State;
        Hero hero = state.ActiveHero ?? throw new InvalidOperationException("No hero is taking a turn.");
        TurnState turn = state.Turn!;
        Room room = state.RoomOf(hero);
        TileKind standingOn = room.Layout[hero.Position];
        bool fighting = room.Monsters.Count > 0;

        // Sixes: add the Power Die to an attack, bank the ones from moves.
        if (turn.UnsettledPowerDice > 0)
        {
            return turn.PendingAttack is not null ? new AddPowerDie(hero.Id) : new BankPowerDie(hero.Id);
        }

        if (turn.PendingAttack is not null)
        {
            return new ConfirmAttack(hero.Id);
        }

        if (hero.Hearts <= hero.MaxHearts - 2 && UsableBeltCard(session, hero, CardEffectKind.Heal) is { } potion)
        {
            return new UseCard(hero.Id, potion.Id);
        }

        if (fighting && UsableBeltCard(session, hero, CardEffectKind.Attack) is { } bomb)
        {
            return new UseCard(hero.Id, bomb.Id, session.GetCardTargets(bomb.Id)[0]);
        }

        // One all-out attack: every Attack die left plus the weapon.
        if (session.GetAttackableMonsters().FirstOrDefault() is { } target)
        {
            return new AttackMonster(hero.Id, target.Id);
        }

        if (!turn.HasActed && ClosedChestBeside(room, hero) is { } chest)
        {
            return new OpenChest(hero.Id, chest);
        }

        bool backpackHasRoom = hero.Backpack.Count < GameRules.BackpackCapacity;
        if (!turn.HasActed && room.LootAt(hero.Position).Count > 0 && backpackHasRoom)
        {
            return new PickUpLoot(hero.Id);
        }

        if (!turn.HasActed && !fighting && Beside(room, hero, TileKind.Pot) is { } pot)
        {
            return new SmashPot(hero.Id, pot);
        }

        // Already acted but standing on loot or beside a pot: wait here for next turn's action.
        if (turn.HasActed && !fighting
            && ((room.LootAt(hero.Position).Count > 0 && backpackHasRoom) || Beside(room, hero, TileKind.Pot) is not null))
        {
            return new EndTurn(hero.Id);
        }

        if (standingOn == TileKind.Exit && !room.Monsters.Any(monster => monster.Definition.Boss))
        {
            return new Escape(hero.Id);
        }

        bool canStep = turn.MovementPoints > 0;
        Direction? door = fighting ? null : NextDoor(state, room);
        if (door is { } side && hero.Position == side.DoorTile() && canStep && session.GetUsableDoors().Contains(side))
        {
            return new GoThroughDoor(hero.Id, side);
        }

        if (!fighting && door is null && standingOn == TileKind.StairsDown && canStep)
        {
            return new TakeStairs(hero.Id);
        }

        // One move with every Move die left plus Speed.
        if (turn.MovementRoll is null && session.MoveDiceLeft > 0)
        {
            return new RollMovement(hero.Id);
        }

        GridPosition? goal = fighting ? BesideNearestMonster(room, hero)
            : (backpackHasRoom ? Nearest(room, hero, room.LootPositions) : null)
              ?? Nearest(room, hero, room.TilesOfKind(TileKind.Pot).SelectMany(tile => tile.OrthogonalNeighbors()))
              ?? (door is { } exitSide ? exitSide.DoorTile() : WayOut(room));

        if (goal is { } tile && StepAlongPath(session, hero, room, tile) is { } destination)
        {
            return new MoveHero(hero.Id, destination);
        }

        if (fighting && StepToward(session, hero, tile => room.Monsters.Min(monster => monster.Position.ManhattanDistanceTo(tile))) is { } closer)
        {
            return new MoveHero(hero.Id, closer);
        }

        return new EndTurn(hero.Id);
    }

    /// <summary>The closest walkable tile next to a monster (for a melee attack), if any can be reached.</summary>
    private static GridPosition? BesideNearestMonster(Room room, Hero hero)
    {
        PathSearch search = PathSearch.Run(hero.Position, tile => room.Layout[tile].IsWalkable() && room.MonsterAt(tile) is null);
        return room.Monsters
            .SelectMany(monster => monster.Position.OrthogonalNeighbors())
            .Where(search.CanReach)
            .OrderBy(tile => search.Distances[tile])
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();
    }

    /// <summary>Walks the shortest route to the goal as far as this turn's movement allows.</summary>
    private static GridPosition? StepAlongPath(GameSession session, Hero hero, Room room, GridPosition goal)
    {
        IReadOnlyDictionary<GridPosition, int> reachable = session.GetReachableTiles();
        PathSearch search = PathSearch.Run(hero.Position, tile => room.Layout[tile].IsWalkable() && room.MonsterAt(tile) is null);
        if (reachable.Count == 0 || goal == hero.Position || !search.CanReach(goal))
        {
            return null;
        }

        IReadOnlyList<GridPosition> path = search.PathTo(goal);
        for (int i = path.Count - 1; i >= 0; i--)
        {
            if (reachable.ContainsKey(path[i]))
            {
                return path[i];
            }
        }

        return null;
    }

    private static CardInstance? UsableBeltCard(GameSession session, Hero hero, CardEffectKind kind) =>
        hero.Belt.FirstOrDefault(card => card.Definition.Effect?.Kind == kind && session.GetCardTargets(card.Id).Count > 0);

    private static GridPosition? ClosedChestBeside(Room room, Hero hero) =>
        hero.Position.OrthogonalNeighbors()
            .Where(tile => room.Layout[tile] == TileKind.Chest && !room.IsChestOpened(tile))
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();

    private static GridPosition? Beside(Room room, Hero hero, TileKind kind) =>
        hero.Position.OrthogonalNeighbors()
            .Where(tile => room.Layout[tile] == kind)
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();

    /// <summary>The closest of the given tiles the hero could walk to (ignoring this turn's movement), if any.</summary>
    private static GridPosition? Nearest(Room room, Hero hero, IEnumerable<GridPosition> tiles)
    {
        PathSearch search = PathSearch.Run(hero.Position, tile => room.Layout[tile].IsWalkable() && room.MonsterAt(tile) is null);
        return tiles
            .Where(tile => tile != hero.Position && search.CanReach(tile))
            .OrderBy(tile => search.Distances[tile])
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();
    }

    private static GridPosition? WayOut(Room room) =>
        room.TilesOfKind(TileKind.Exit).Concat(room.TilesOfKind(TileKind.StairsDown)).Select(tile => (GridPosition?)tile).FirstOrDefault();

    /// <summary>The reachable tile that gets closest to the goal, if it gets any closer than where the hero stands.</summary>
    private static GridPosition? StepToward(GameSession session, Hero hero, Func<GridPosition, int> distanceToGoal)
    {
        IReadOnlyDictionary<GridPosition, int> reachable = session.GetReachableTiles();
        if (reachable.Count == 0)
        {
            return null;
        }

        GridPosition best = reachable.Keys
            .OrderBy(distanceToGoal)
            .ThenBy(tile => reachable[tile])
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .First();
        return distanceToGoal(best) < distanceToGoal(hero.Position) ? best : null;
    }

    /// <summary>
    /// The door to head for: toward the nearest unexplored room on this floor, or once everything
    /// is explored, toward the room with the stairs or the exit. Null when this room is the goal.
    /// </summary>
    private static Direction? NextDoor(GameState state, Room start)
    {
        if (start.IsSealed)
        {
            return null;
        }

        var firstStep = new Dictionary<string, Direction?> { [start.Id] = null };
        var frontier = new Queue<Room>([start]);
        Room? unexplored = null;
        Room? wayDown = null;
        while (frontier.Count > 0)
        {
            Room room = frontier.Dequeue();
            if (!room.Explored && unexplored is null)
            {
                unexplored = room;
            }

            if (room.Role is RoomRole.Stairs or RoomRole.Boss && wayDown is null)
            {
                wayDown = room;
            }

            foreach ((Direction side, string nextId) in room.Doors.OrderBy(door => door.Key))
            {
                if (!firstStep.ContainsKey(nextId))
                {
                    firstStep[nextId] = firstStep[room.Id] ?? side;
                    frontier.Enqueue(state.Rooms[nextId]);
                }
            }
        }

        Room? goal = unexplored ?? wayDown;
        return goal is null || goal == start ? null : firstStep[goal.Id];
    }
}
