using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;

namespace Woad.Core.Bots;

/// <summary>
/// A simple automatic player for smoke tests and the game's <c>--autoplay</c> mode. It fights
/// whatever is in its room, heals when hurt, loots what it passes and opens the room's chests (with
/// a key if locked), then explores: unexplored rooms first, then the stairs, then the exit. Locked
/// doors on its way take a key or a picked lock; the Boss Door waits until it has the Boss Key.
/// It never searches, steals or bombs walls. Companions' turns go to <see cref="CompanionBot"/>.
/// Every command it returns should be accepted; a refusal means a bug in the bot or the rules.
/// </summary>
public static class SimpleBot
{
    public static GameCommand NextCommand(GameSession session)
    {
        GameState state = session.State;
        Hero hero = state.ActiveHero ?? throw new InvalidOperationException("No hero is taking a turn.");
        if (hero.IsCompanion)
        {
            return CompanionBot.NextCommand(session);
        }

        TurnState turn = state.Turn!;
        if (turn.KeelingOver)
        {
            return new RollKeel(hero.Id, hero.Power.Banked);
        }

        // Badly on fire with the turn still fresh: stop, drop and roll.
        if ((hero.FireDice >= 2 || hero.Hearts <= 1) && session.CheckStopDropRoll() is null)
        {
            return new StopDropRoll(hero.Id);
        }

        Room room = state.RoomOf(hero);
        TileKind standingOn = room.Layout[hero.Position];
        bool fighting = Fighting(session, room, hero);

        // Sixes: add the Power Die to an attack, bank the ones from moves.
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

        if (hero.Hearts <= hero.MaxHearts - 2 && UsableBeltCard(session, hero, CardEffectKind.Heal) is { } potion)
        {
            return new UseCard(hero.Id, potion.Id);
        }

        if (fighting && UsableBeltCard(session, hero, CardEffectKind.Attack) is { } bomb && SafeAim(session, room, bomb) is { } aim)
        {
            return new UseCard(hero.Id, bomb.Id, aim);
        }

        // One all-out attack: every Attack die left plus the weapon.
        if (session.GetAttackableMonsters().FirstOrDefault() is { } target)
        {
            return new AttackMonster(hero.Id, target.Id);
        }

        // Prone (after stopping, dropping and rolling): roll to move and get up.
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

        if (ClosedChestBeside(room, hero) is { } chest && session.CheckOpenChest(chest) is null)
        {
            return new OpenChest(hero.Id, chest);
        }

        bool backpackHasRoom = hero.Backpack.Count < GameRules.BackpackCapacity;
        if (session.CheckPickUp() is null)
        {
            return new PickUpLoot(hero.Id);
        }

        if (!turn.HasActed && !fighting && Beside(room, hero, TileKind.Pot) is { } pot)
        {
            return new SmashPot(hero.Id, pot);
        }

        // Out of free loot and action, but standing on loot or beside a chest or pot: wait for next turn.
        bool lootedOut = turn.UsedFreeLoot && turn.HasActed;
        if (!fighting && ((lootedOut && (WorthPickingUp(room, hero) || ClosedChestBeside(room, hero) is not null))
                          || (turn.HasActed && Beside(room, hero, TileKind.Pot) is not null)))
        {
            return new EndTurn(hero.Id);
        }

        // Nobody gets left Keeling Over once the room is clear: kneel beside them and wait to rouse them.
        if (!fighting && BesideFallen(state, room, hero) is { } kneel)
        {
            if (kneel == hero.Position)
            {
                return new EndTurn(hero.Id);
            }

            if (turn.MovementPoints == 0 && session.MoveDiceLeft > 0)
            {
                return new RollMovement(hero.Id);
            }

            if (StepAlongPath(session, hero, room, kneel) is { } toFallen)
            {
                return new MoveHero(hero.Id, toFallen);
            }
        }

        if (standingOn == TileKind.Exit && !room.Monsters.Any(monster => monster.Definition.Boss))
        {
            return new Escape(hero.Id);
        }

        bool canStep = turn.MovementPoints > 0;
        Direction? door = fighting ? null : NextDoor(state, room, hero);
        if (door is { } lockedSide && hero.Position == room.DoorTile(lockedSide) && room.PassageOn(lockedSide) is { IsLocked: true } locked)
        {
            if (locked.NeedsBossKey || hero.Keys > 0)
            {
                return new UnlockDoor(hero.Id, lockedSide);
            }

            return session.CheckPickLock(hero.Position) is null ? new PickLock(hero.Id, hero.Position) : new EndTurn(hero.Id);
        }

        if (door is { } side && hero.Position == room.DoorTile(side) && canStep && session.GetUsableDoors().Contains(side))
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

        IEnumerable<GridPosition> lootTiles = room.LootPositions.Where(_ => backpackHasRoom).Concat(room.Keys.Keys).Concat(room.Gold.Keys);
        if (room.BossKeyAt is { } bossKey)
        {
            lootTiles = lootTiles.Append(bossKey);
        }

        GridPosition? goal = fighting ? BesideNearestMonster(room, hero)
            : Nearest(state, room, hero, lootTiles)
              ?? Nearest(state, room, hero, OpenableChests(room, hero).SelectMany(tile => tile.OrthogonalNeighbors()))
              ?? Nearest(state, room, hero, room.TilesOfKind(TileKind.Pot).SelectMany(tile => tile.OrthogonalNeighbors()))
              ?? (door is { } exitSide ? room.DoorTile(exitSide) : WayOut(room));

        if (goal is { } tile && StepAlongPath(session, hero, room, tile) is { } destination)
        {
            return new MoveHero(hero.Id, destination);
        }

        // A locked gate in the way: of where it's going, or of a monster it can't otherwise reach.
        GridPosition? gateGoal = goal ?? (fighting ? room.Monsters.MinBy(monster => monster.Position.ManhattanDistanceTo(hero.Position))!.Position : null);
        if (gateGoal is { } beyond && ThroughGate(session, hero, room, beyond) is { } gateCommand)
        {
            return gateCommand;
        }

        // Not using this doorway (say, a teammate holds the Boss Key): at least keep out of it.
        bool InDoorway(GridPosition at) => room.Doors.Keys.Any(side => room.DoorTile(side) == at);
        if (!fighting && (door is not { } heading || room.DoorTile(heading) != hero.Position) && InDoorway(hero.Position)
            && StepToward(session, hero, at => InDoorway(at) ? 1 : 0) is { } aside)
        {
            return new MoveHero(hero.Id, aside);
        }

        if (fighting && StepToward(session, hero, tile => room.Monsters.Min(monster => monster.Position.ManhattanDistanceTo(tile))) is { } closer)
        {
            return new MoveHero(hero.Id, closer);
        }

        return new EndTurn(hero.Id);
    }

    /// <summary>The closest walkable tile a monster can be hit from in melee (diagonals too), if any can be reached.</summary>
    internal static GridPosition? BesideNearestMonster(Room room, Hero hero)
    {
        PathSearch search = PathSearch.Run(hero.Position, tile => Walkable(room, tile));
        return room.Monsters
            .SelectMany(monster => monster.Position.SurroundingTiles()
                .Where(tile => room.Layout.Contains(tile) && Targeting.CanHeroAttack(room.Layout, 1, tile, monster.Position)))
            .Where(search.CanReach)
            .OrderBy(tile => search.Distances[tile])
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();
    }

    /// <summary>
    /// Whether there's a fight to be had: a monster the hero can hit now, or can walk (or open a gate)
    /// to strike. A monster sealed off where nobody can get at it (say, a slimeling split into a walled
    /// corner) doesn't hold the party up.
    /// </summary>
    internal static bool Fighting(GameSession session, Room room, Hero hero)
    {
        if (room.Monsters.Count == 0)
        {
            return false;
        }

        if (session.GetAttackableMonsters().Count > 0)
        {
            return true;
        }

        PathSearch search = PathSearch.Run(hero.Position, tile => Walkable(room, tile) || room.Layout[tile] == TileKind.Gate);
        return room.Monsters.Any(monster => monster.Position.SurroundingTiles()
            .Any(tile => room.Layout.Contains(tile) && search.CanReach(tile) && Targeting.CanHeroAttack(room.Layout, 1, tile, monster.Position)));
    }

    /// <summary>
    /// Where to kneel to rouse an ally Keeling Over in the room: the hero's own tile if already beside
    /// one (diagonals too), else the closest free tile beside one. Null when nobody's down or reachable.
    /// </summary>
    internal static GridPosition? BesideFallen(GameState state, Room room, Hero hero)
    {
        List<Hero> fallen = state.HeroesIn(room).Where(other => other.IsKeelingOver).ToList();
        if (fallen.Any(other => other.Position.ChebyshevDistanceTo(hero.Position) == 1))
        {
            return hero.Position;
        }

        return Nearest(state, room, hero, fallen.SelectMany(other => other.Position.SurroundingTiles()).Where(tile => room.Layout.Contains(tile)));
    }

    /// <summary>Walks the shortest route to the goal as far as this turn's movement allows.</summary>
    internal static GridPosition? StepAlongPath(GameSession session, Hero hero, Room room, GridPosition goal)
    {
        IReadOnlyDictionary<GridPosition, int> reachable = session.GetReachableTiles();
        PathSearch search = PathSearch.Run(hero.Position, tile => Walkable(room, tile));
        if (!search.CanReach(goal))
        {
            // The only way is past a trap it knows of: step onto it and take the hit rather than wait forever.
            search = PathSearch.Run(hero.Position, tile => room.Layout[tile].IsWalkable() && room.MonsterAt(tile) is null);
        }

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

    /// <summary>Where the bot can walk: like the rules, around monsters and the traps it knows of.</summary>
    internal static bool Walkable(Room room, GridPosition tile) =>
        room.Layout[tile].IsWalkable() && room.MonsterAt(tile) is null && room.TrapAt(tile) is not { Revealed: true };

    /// <summary>
    /// When a locked gate stands between the hero and its goal: walk up to the gate, then open it with
    /// a key or pick its lock. Null when no gate is in the way (or there's nothing to do about it now).
    /// </summary>
    private static GameCommand? ThroughGate(GameSession session, Hero hero, Room room, GridPosition goal)
    {
        PathSearch walk = PathSearch.Run(hero.Position, tile => Walkable(room, tile));
        if (walk.CanReach(goal))
        {
            return null;
        }

        foreach (GridPosition gate in room.Gates.Keys.OrderBy(gate => gate.ManhattanDistanceTo(hero.Position)))
        {
            if (hero.Position.ManhattanDistanceTo(gate) == 1)
            {
                return hero.Keys > 0 ? new OpenGate(hero.Id, gate)
                    : session.CheckPickLock(gate) is null ? new PickLock(hero.Id, gate)
                    : null;
            }

            if (gate.OrthogonalNeighbors().Where(walk.CanReach).OrderBy(stand => walk.Distances[stand]).Select(stand => (GridPosition?)stand).FirstOrDefault() is { } spot
                && StepAlongPath(session, hero, room, spot) is { } step)
            {
                return new MoveHero(hero.Id, step);
            }
        }

        return null;
    }

    /// <summary>Where to aim an attack card: at monsters, and with friendly fire on, never where the blast would catch a hero.</summary>
    private static GridPosition? SafeAim(GameSession session, Room room, CardInstance card)
    {
        int radius = card.Definition.Effect!.AreaRadius;
        bool Catches(GridPosition aim, GridPosition at) => at.ChebyshevDistanceTo(aim) <= radius;
        return session.GetCardTargets(card.Id)
            .Where(aim => room.Monsters.Any(monster => Catches(aim, monster.Position)))
            .Where(aim => !session.State.FriendlyFire || !session.State.ActiveHeroesIn(room).Any(hero => Catches(aim, hero.Position)))
            .Select(aim => (GridPosition?)aim)
            .FirstOrDefault();
    }

    internal static CardInstance? UsableBeltCard(GameSession session, Hero hero, CardEffectKind kind) =>
        hero.Belt.FirstOrDefault(card => card.Definition.Effect?.Kind == kind && session.GetCardTargets(card.Id).Count > 0);

    /// <summary>An adjacent chest the bot means to open: unlocked, or locked with a key in hand.</summary>
    private static GridPosition? ClosedChestBeside(Room room, Hero hero) =>
        hero.Position.OrthogonalNeighbors()
            .Where(OpenableChests(room, hero).Contains)
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();

    /// <summary>Unopened chests the bot can open: locked ones only while it has a key.</summary>
    private static List<GridPosition> OpenableChests(Room room, Hero hero) =>
        room.TilesOfKind(TileKind.Chest)
            .Where(tile => !room.IsChestOpened(tile) && (room.ChestLockAt(tile) is null || hero.Keys > 0))
            .ToList();

    private static bool WorthPickingUp(Room room, Hero hero) =>
        room.KeysAt(hero.Position) > 0 || room.GoldAt(hero.Position) > 0 || room.BossKeyAt == hero.Position
        || (room.LootAt(hero.Position).Count > 0 && hero.Backpack.Count < GameRules.BackpackCapacity);

    private static GridPosition? Beside(Room room, Hero hero, TileKind kind) =>
        hero.Position.OrthogonalNeighbors()
            .Where(tile => room.Layout[tile] == kind)
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();

    /// <summary>
    /// The closest of the given tiles the hero could walk to (ignoring this turn's movement), if any.
    /// Tiles a teammate stands on are skipped: the hero couldn't stop there.
    /// </summary>
    private static GridPosition? Nearest(GameState state, Room room, Hero hero, IEnumerable<GridPosition> tiles)
    {
        PathSearch search = PathSearch.Run(hero.Position, tile => Walkable(room, tile));
        return tiles
            .Where(tile => tile != hero.Position && search.CanReach(tile))
            .Where(tile => !state.HeroesIn(room).Any(other => other.Position == tile))
            .OrderBy(tile => search.Distances[tile])
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();
    }

    private static GridPosition? WayOut(Room room) =>
        room.TilesOfKind(TileKind.Exit).Concat(room.TilesOfKind(TileKind.StairsDown)).Select(tile => (GridPosition?)tile).FirstOrDefault();

    /// <summary>The reachable tile that gets closest to the goal, if it gets any closer than where the hero stands.</summary>
    internal static GridPosition? StepToward(GameSession session, Hero hero, Func<GridPosition, int> distanceToGoal)
    {
        // Pits and found traps can be walked into, but a bot never does.
        Room room = session.State.RoomOf(hero);
        IReadOnlyDictionary<GridPosition, int> reachable = session.GetReachableTiles();
        if (!reachable.Keys.Any(tile => Walkable(room, tile)))
        {
            return null;
        }

        GridPosition best = reachable.Keys
            .Where(tile => Walkable(room, tile))
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
    private static Direction? NextDoor(GameState state, Room start, Hero hero)
    {
        if (start.IsSealed)
        {
            return null;
        }

        var firstStep = new Dictionary<string, Direction?> { [start.Id] = null };
        var frontier = new Queue<Room>([start]);
        Room? unexplored = null;
        Room? wayDown = null;
        Room? bossKey = null;
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

            // Without the Boss Key the boss room is out of reach: go where the key is (on the floor or in a chest).
            bool keyHere = room.BossKeyAt is not null || room.TilesOfKind(TileKind.Chest).Any(chest => room.HoldsBossKey(chest));
            if (!hero.HasBossKey && keyHere && bossKey is null)
            {
                bossKey = room;
            }

            foreach ((Direction side, string nextId) in room.Doors.OrderBy(door => door.Key))
            {
                // Secret doors and cracked walls are out of the bot's reach; the Boss Door needs the Boss Key.
                if (room.PassageOn(side) is { } passage
                    && (passage.IsHidden || passage.IsBlocked || (passage.IsLocked && passage.NeedsBossKey && !hero.HasBossKey)))
                {
                    continue;
                }

                if (!firstStep.ContainsKey(nextId))
                {
                    firstStep[nextId] = firstStep[room.Id] ?? side;
                    frontier.Enqueue(state.Rooms[nextId]);
                }
            }
        }

        Room? goal = unexplored ?? bossKey ?? wayDown;
        return goal is null || goal == start ? null : firstStep[goal.Id];
    }
}
