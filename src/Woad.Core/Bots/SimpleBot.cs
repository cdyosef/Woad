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

        if (turn.PendingRoll is null && session.CheckDigOut() is null)
        {
            return new DigOut(hero.Id);
        }

        if (turn.PendingRoll is not null)
        {
            return new ConfirmAttack(hero.Id);
        }

        if (session.GetRousableAllies().FirstOrDefault() is { } fallen)
        {
            return new Rouse(hero.Id, fallen.Id);
        }

        if (FreeShackles(session, hero, room) is { } unshackle)
        {
            return unshackle;
        }

        // A spent crossbow is reloaded straight away, while there are dice for it.
        if (session.CheckReload() is null)
        {
            return new ReloadWeapon(hero.Id);
        }

        if (FleeBlast(session, hero, room) is { } flee)
        {
            return flee;
        }

        if (hero.Hearts <= hero.MaxHearts - 2 && UsableBeltCard(session, hero, CardEffectKind.Heal) is { } potion)
        {
            return new UseCard(hero.Id, potion.Id);
        }

        // Attack cards that aren't bombs (a bot never throws a bomb: it would only blow up its own party).
        if (fighting && UsableBeltCard(session, hero, CardEffectKind.Attack) is { Definition.Type: not CardType.Bomb } scroll && SafeAim(session, room, scroll) is { } aim)
        {
            return new UseCard(hero.Id, scroll.Id, aim);
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
        // Loot on its tile or in reach (a chest it just opened, a pile beside it).
        if (new[] { hero.Position }.Concat(hero.Position.SurroundingTiles()).FirstOrDefault(tile => room.Layout.Contains(tile) && room.HasLootAt(tile) && session.CheckPickUp(tile) is null) is var reach
            && room.Layout.Contains(reach) && room.HasLootAt(reach) && session.CheckPickUp(reach) is null)
        {
            return new PickUpLoot(hero.Id, reach);
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

        IEnumerable<GridPosition> lootTiles = room.LootPositions.Where(tile => backpackHasRoom && room.Layout[tile].IsWalkable()).Concat(room.Keys.Keys).Concat(room.Gold.Keys)
            .Concat(room.Bombs.Where(bomb => !bomb.Lit && backpackHasRoom).Select(bomb => bomb.Position));
        if (room.BossKeyAt is { } bossKey)
        {
            lootTiles = lootTiles.Append(bossKey);
        }

        GridPosition? goal = fighting ? ShootingSpot(state, room, hero) ?? BesideNearestMonster(state, room, hero)
            : Nearest(state, room, hero, lootTiles)
              ?? Nearest(state, room, hero, OpenableChests(room, hero).SelectMany(tile => tile.OrthogonalNeighbors()))
              ?? Nearest(state, room, hero, room.TilesOfKind(TileKind.Pot).SelectMany(tile => tile.OrthogonalNeighbors()))
              ?? (door is { } exitSide ? room.DoorTile(exitSide) : WayOut(room));

        if (goal is { } tile && StepAlongPath(session, hero, room, tile) is { } destination)
        {
            return new MoveHero(hero.Id, destination);
        }

        // A locked gate in the way: of where it's going, or of a monster it can't otherwise reach.
        GridPosition? gateGoal = goal ?? (fighting ? room.Monsters.MinBy(monster => monster.NearestTile(hero.Position).ManhattanDistanceTo(hero.Position))!.NearestTile(hero.Position) : null);
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

        if (fighting && StepToward(session, hero, tile => room.Monsters.Min(monster => monster.NearestTile(tile).ManhattanDistanceTo(tile))) is { } closer)
        {
            return new MoveHero(hero.Id, closer);
        }

        return new EndTurn(hero.Id);
    }

    /// <summary>The closest walkable tile a monster can be hit from in melee (diagonals too), if any can be reached.</summary>
    /// <summary>Shackles, the hero's own or an ally's beside them: unlocked with a key, or the lock picked. Null if there are none, or no way to.</summary>
    internal static GameCommand? FreeShackles(GameSession session, Hero hero, Room room)
    {
        if (session.State.HeroesIn(room).FirstOrDefault(other => other.Shackled is not null && other.IsInPlay && other.Position.ChebyshevDistanceTo(hero.Position) <= 1) is not { } prisoner)
        {
            return null;
        }

        return session.CheckUnshackle(prisoner.Id) is null ? new Unshackle(hero.Id, prisoner.Id)
            : session.CheckPickLock(prisoner.Position) is null ? new PickLock(hero.Id, prisoner.Position)
            : null;
    }

    /// <summary>The nearest free tile the hero can reach to strike a monster from (one an ally or merchant stands on won't do), or null.</summary>
    /// <summary>
    /// Where a hero whose weapon reaches past the tiles beside them (a bow, a thrown Boomerang, a Spear)
    /// can hit one of <paramref name="targets"/> (any monster in the room by default) from: the nearest such
    /// tile they can get to, keeping clear of every monster's side if they can. Null for a melee fighter,
    /// or with nowhere to shoot from.
    /// </summary>
    internal static GridPosition? ShootingSpot(GameState state, Room room, Hero hero, IEnumerable<Monster>? targets = null)
    {
        if (hero.AttackRange < 2)
        {
            return null;
        }

        List<Monster> aims = [.. targets ?? room.Monsters];
        PathSearch search = PathSearch.Run(hero.Position, tile => Walkable(room, tile));
        bool Exposed(GridPosition tile) => room.Monsters.Any(monster => monster.NearestTile(tile).ChebyshevDistanceTo(tile) <= 1);
        return search.Distances.Keys
            .Where(tile => tile == hero.Position || FreeFor(state, room, hero, tile))
            .Where(tile => aims.Any(monster => monster.Tiles.Any(part => GameSession.CanHeroReach(hero, room.Layout, tile, part))))
            .OrderBy(Exposed)
            .ThenBy(tile => search.Distances[tile])
            .ThenBy(tile => tile.Y)
            .ThenBy(tile => tile.X)
            .Select(tile => (GridPosition?)tile)
            .FirstOrDefault();
    }

    internal static GridPosition? BesideNearestMonster(GameState state, Room room, Hero hero)
    {
        PathSearch search = PathSearch.Run(hero.Position, tile => Walkable(room, tile));
        return room.Monsters
            .SelectMany(monster => monster.Tiles.SelectMany(part => part.SurroundingTiles()
                .Where(tile => room.Layout.Contains(tile) && !monster.Occupies(tile) && Targeting.CanHeroAttack(room.Layout, 1, tile, part))))
            .Where(tile => FreeFor(state, room, hero, tile))
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
        return room.Monsters.Any(monster => monster.Tiles.Any(part => part.SurroundingTiles()
            .Any(tile => room.Layout.Contains(tile) && !monster.Occupies(tile) && search.CanReach(tile) && Targeting.CanHeroAttack(room.Layout, 1, tile, part))));
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
    /// <summary>Nobody else (an ally or a merchant) stands on the tile: the hero could end a move there.</summary>
    internal static bool FreeFor(GameState state, Room room, Hero hero, GridPosition tile) =>
        room.MerchantAt(tile) is null && !state.HeroesIn(room).Any(other => other != hero && other.Position == tile);

    internal static bool Walkable(Room room, GridPosition tile) =>
        room.Layout[tile].IsWalkable() && room.MonsterAt(tile) is null && room.TrapAt(tile) is not { Revealed: true } && !InBlast(room, tile);

    /// <summary>Within a tile of a lit bomb: where it would catch whoever stands there.</summary>
    internal static bool InBlast(Room room, GridPosition tile) => room.Bombs.Any(bomb => bomb.Lit && bomb.Position.ChebyshevDistanceTo(tile) <= 1);

    /// <summary>Standing where a lit bomb would catch them: roll to move if need be, and step out of the blast.</summary>
    internal static GameCommand? FleeBlast(GameSession session, Hero hero, Room room)
    {
        TurnState turn = session.State.Turn!;
        if (!InBlast(room, hero.Position) || turn.PendingRoll is not null)
        {
            return null;
        }

        if (turn.MovementPoints == 0)
        {
            return session.MoveDiceLeft > 0 ? new RollMovement(hero.Id) : null;
        }

        return StepToward(session, hero, tile => InBlast(room, tile) ? 1 : 0) is { } safe ? new MoveHero(hero.Id, safe) : null;
    }

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
    internal static GridPosition? Nearest(GameState state, Room room, Hero hero, IEnumerable<GridPosition> tiles)
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
