using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Keys and locks, the Boss Door, secret doors, cracked walls, traps, trap doors, searching, stealing and free looting.</summary>
public class DungeonSecretsTests
{
    private const string Empty = """
        .........
        .........
        .........
        .........
        .........
        .........
        .........
        .........
        .........
        """;

    /// <summary>The hero standing in room A's east doorway.</summary>
    private const string InEastDoorway = """
        .........
        .........
        .........
        .........
        ........1
        .........
        .........
        .........
        .........
        """;

    private const string Open = """
        .........
        .........
        .........
        .........
        ....1....
        .........
        .........
        .........
        .........
        """;

    private static RoomSetup[] TwoRooms(string roomA = InEastDoorway) =>
    [
        new RoomSetup("A", TestRun.Room(roomA), new Dictionary<Direction, string> { [Direction.East] = "B" }),
        new RoomSetup("B", TestRun.Room(Empty), new Dictionary<Direction, string> { [Direction.West] = "A" }),
    ];

    private static GameSession DoorRun(PassageKind kind, int difficulty = 5, ScriptedDice? dice = null, Stats? stats = null, string[]? cards = null) =>
        TestRun.Start(TwoRooms(), dice, stats: stats, cards: cards, passages: [new PassageSetup("A", Direction.East, kind, difficulty)]);

    [Fact]
    public void ALockedDoorTakesAKey()
    {
        GameSession session = DoorRun(PassageKind.Locked, dice: new ScriptedDice(1));
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));

        Assert.Contains("locked", session.Refuse(new GoThroughDoor(hero.Id, Direction.East)));
        Assert.Empty(session.GetUsableDoors());
        Assert.Contains("no keys", session.Refuse(new UnlockDoor(hero.Id, Direction.East)));

        hero.Keys = 1;
        IReadOnlyList<GameEvent> events = session.Do(new UnlockDoor(hero.Id, Direction.East));

        Assert.Equal(new KeyUsed(hero.Id, 0), events[0]);
        Assert.Equal(new DoorUnlocked(hero.Id, "A", Direction.East, Picked: false, BossDoor: false), events[1]);
        Assert.False(session.State.Turn!.HasActed);
        session.Do(new GoThroughDoor(hero.Id, Direction.East));
        Assert.Equal("B", hero.RoomId);
        Assert.False(session.RoomById("B").PassageOn(Direction.West)!.IsLocked);
    }

    [Fact]
    public void OdHandPicksALockWhenItReachesTheDifficulty()
    {
        GameSession session = DoorRun(PassageKind.Locked, difficulty: 5, dice: new ScriptedDice(4, 5), stats: GameRules.HeroBaseStats with { OdHandDice = 2 });
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new PickLock(hero.Id, hero.Position, Dice: 1));
        Assert.Equal((OdHandCheckKind.PickDoor, 4, 5), (events.SingleOf<OdHandRolled>().Kind, events.SingleOf<OdHandRolled>().Total, events.SingleOf<OdHandRolled>().Difficulty));
        Assert.Equal(new CheckFailed(hero.Id, OdHandCheckKind.PickDoor), events[^1]);

        events = session.Do(new PickLock(hero.Id, hero.Position, Dice: 1));

        Assert.Equal(new DoorUnlocked(hero.Id, "A", Direction.East, Picked: true, BossDoor: false), events[^1]);
        Assert.Equal(0, session.OdHandDiceLeft);
        Assert.False(session.State.Turn!.HasActed);
    }

    [Fact]
    public void LockpicksAddTheirValueOnceATurn()
    {
        GameSession session = DoorRun(PassageKind.Locked, difficulty: 5, dice: new ScriptedDice(3), cards: ["picks"]);
        Hero hero = session.HeroInSeat();

        var rolled = session.Do(new PickLock(hero.Id, hero.Position)).SingleOf<OdHandRolled>();

        Assert.Equal((2, 5), (rolled.Bonus, rolled.Total));
        Assert.Equal(0, session.ToolsBonusLeft);
    }

    [Fact]
    public void OnlyTheBossKeyOpensTheBossDoor()
    {
        GameSession session = DoorRun(PassageKind.BossLocked);
        Hero hero = session.HeroInSeat();
        hero.Keys = 3;

        Assert.Contains("Boss Key", session.Refuse(new UnlockDoor(hero.Id, Direction.East)));
        Assert.Contains("can't be picked", session.Refuse(new PickLock(hero.Id, hero.Position)));

        hero.HasBossKey = true;
        IReadOnlyList<GameEvent> events = session.Do(new UnlockDoor(hero.Id, Direction.East));

        Assert.Equal(new DoorUnlocked(hero.Id, "A", Direction.East, Picked: false, BossDoor: true), Assert.Single(events));
        Assert.False(hero.HasBossKey);
        Assert.Equal(3, hero.Keys);
    }

    [Fact]
    public void SecretDoorsStayHiddenUntilASearchFindsThem()
    {
        GameSession session = DoorRun(PassageKind.Secret, difficulty: 5, dice: new ScriptedDice(1, 5, 1));
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));
        Assert.Empty(session.GetUsableDoors());
        Assert.Contains("no door", session.Refuse(new GoThroughDoor(hero.Id, Direction.East)));

        // Searching ends the move, so roll again afterwards.
        session.Do(new EndTurn(hero.Id));
        var found = session.Do(new Search(hero.Id)).SingleOf<SecretsFound>();
        Assert.Equal([Direction.East], found.Doors);

        session.Do(new RollMovement(hero.Id));
        session.Do(new GoThroughDoor(hero.Id, Direction.East));
        Assert.Equal("B", hero.RoomId);
    }

    [Fact]
    public void ABombBreaksTheCrackedWallInADoorway()
    {
        GameSession session = DoorRun(PassageKind.Cracked, cards: ["bomb"]);
        Hero hero = session.HeroInSeat();
        Assert.Contains("cracked wall", session.Refuse(new GoThroughDoor(hero.Id, Direction.East)));

        IReadOnlyList<GameEvent> events = session.Do(new UseCard(hero.Id, hero.Card("bomb").Id, new GridPosition(7, 4)));

        Assert.Equal(new WallBroken("A", new GridPosition(8, 4), Direction.East), events.SingleOf<WallBroken>());
        Assert.DoesNotContain(events, gameEvent => gameEvent is AttackRolled);
        Assert.True(session.RoomById("A").PassageOn(Direction.East)!.IsOpen);
    }

    [Fact]
    public void ABombBreaksACrackedWallAndTheStashInsideSpills()
    {
        var features = new RoomFeatures { Caches = new Dictionary<GridPosition, CacheSetup> { [new(4, 2)] = new(0, Keys: 1, BehindWall: true) } };
        GameSession session = TestRun.Start([new RoomSetup("A", TestRun.Room("""
            .........
            .........
            ....X....
            .........
            ....1....
            .........
            .........
            .........
            .........
            """), Features: features)], cards: ["bomb"]);
        Hero hero = session.HeroInSeat();
        Room room = session.RoomById();

        IReadOnlyList<GameEvent> events = session.Do(new UseCard(hero.Id, hero.Card("bomb").Id, new GridPosition(4, 3)));

        Assert.Equal(TileKind.Floor, room.Layout[new GridPosition(4, 2)]);
        Assert.Contains(new KeysAppeared("A", new GridPosition(4, 2), 1), events);
        Assert.Equal(1, room.KeysAt(new GridPosition(4, 2)));
    }

    [Fact]
    public void HiddenSpikesStopTheWalkAndHurt()
    {
        var features = new RoomFeatures { Traps = new Dictionary<GridPosition, TrapSetup> { [new(4, 2)] = new(TrapKind.Spikes, 4) } };
        GameSession session = TestRun.Start([new RoomSetup("A", TestRun.Room(Open), Features: features)], new ScriptedDice(3));
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));

        IReadOnlyList<GameEvent> events = session.Do(new MoveHero(hero.Id, new GridPosition(4, 0)));

        Assert.Equal([new GridPosition(4, 3), new GridPosition(4, 2)], events.SingleOf<HeroMoved>().Path);
        Assert.Equal(new TrapSprung(hero.Id, TrapKind.Spikes, new GridPosition(4, 2)), events.SingleOf<TrapSprung>());
        Assert.Equal(3, hero.Hearts);
        Assert.Equal(0, session.State.Turn!.MovementPoints);
        Assert.True(session.RoomById().TrapAt(new GridPosition(4, 2))!.Revealed);
    }

    [Fact]
    public void RevealedTrapsAreWalkedAround()
    {
        var features = new RoomFeatures { Traps = new Dictionary<GridPosition, TrapSetup> { [new(4, 3)] = new(TrapKind.Spikes, 4) } };
        GameSession session = TestRun.Start([new RoomSetup("A", TestRun.Room(Open), Features: features)], new ScriptedDice(4, 3));
        Hero hero = session.HeroInSeat();
        session.Do(new Search(hero.Id));
        session.Do(new RollMovement(hero.Id));

        Assert.DoesNotContain(new GridPosition(4, 3), session.GetReachableTiles().Keys);
        var moved = session.Do(new MoveHero(hero.Id, new GridPosition(4, 2))).SingleOf<HeroMoved>();
        Assert.DoesNotContain(new GridPosition(4, 3), moved.Path);
    }

    [Fact]
    public void ATrapDoorDropsYouToTheFloorBelow()
    {
        var features = new RoomFeatures { Traps = new Dictionary<GridPosition, TrapSetup> { [new(4, 3)] = new(TrapKind.TrapDoor, 4) } };
        GameSession session = TestRun.Start(
        [
            new RoomSetup("A", TestRun.Room(Open), Features: features),
            new RoomSetup("B", TestRun.Room(Empty), Floor: 2, Role: RoomRole.Arrival),
        ], new ScriptedDice(1));
        Hero hero = session.HeroInSeat();
        session.Do(new RollMovement(hero.Id));

        IReadOnlyList<GameEvent> events = session.Do(new MoveHero(hero.Id, new GridPosition(4, 2)));

        var fell = events.SingleOf<HeroFell>();
        Assert.Equal(("A", "B", 2, true, false), (fell.FromRoomId, fell.RoomId, fell.Floor, fell.FirstTimeOnFloor, fell.Jumped));
        Assert.Equal("B", hero.RoomId);
        Assert.Equal(3, hero.Hearts);
        Assert.Equal(GameRules.XpPerNewFloor, hero.Xp);
    }

    [Fact]
    public void AFoundTrapDoorCanBeJumpedDownSafely()
    {
        var features = new RoomFeatures { Traps = new Dictionary<GridPosition, TrapSetup> { [new(4, 3)] = new(TrapKind.TrapDoor, 4) } };
        GameSession session = TestRun.Start(
        [
            new RoomSetup("A", TestRun.Room(Open), Features: features),
            new RoomSetup("B", TestRun.Room(Empty), Floor: 2, Role: RoomRole.Arrival),
        ], new ScriptedDice(5, 1));
        Hero hero = session.HeroInSeat();
        session.Do(new Search(hero.Id));
        session.Do(new RollMovement(hero.Id));

        var fell = session.Do(new JumpDown(hero.Id, new GridPosition(4, 3))).SingleOf<HeroFell>();

        Assert.True(fell.Jumped);
        Assert.Equal(4, hero.Hearts);
    }

    [Fact]
    public void ASearchFindsWhatItsTotalReaches()
    {
        var features = new RoomFeatures
        {
            Traps = new Dictionary<GridPosition, TrapSetup> { [new(2, 2)] = new(TrapKind.Spikes, 4) },
            Caches = new Dictionary<GridPosition, CacheSetup> { [new(6, 6)] = new(7, Keys: 1) },
        };
        GameSession session = TestRun.Start([new RoomSetup("A", TestRun.Room(Open), Features: features)], new ScriptedDice(5, 3, 4), stats: GameRules.HeroBaseStats with { OdHandDice = 3 });
        Hero hero = session.HeroInSeat();

        var first = session.Do(new Search(hero.Id, Dice: 1)).SingleOf<SecretsFound>();
        Assert.Equal([new GridPosition(2, 2)], first.Traps);
        Assert.Empty(first.Stashes);

        IReadOnlyList<GameEvent> events = session.Do(new Search(hero.Id, Dice: 2));
        Assert.Equal([new GridPosition(6, 6)], events.SingleOf<SecretsFound>().Stashes);
        Assert.Contains(new KeysAppeared("A", new GridPosition(6, 6), 1), events);
    }

    [Fact]
    public void ARevealedTrapNextToYouCanBeDisarmed()
    {
        var features = new RoomFeatures { Traps = new Dictionary<GridPosition, TrapSetup> { [new(4, 3)] = new(TrapKind.Spikes, 4) } };
        GameSession session = TestRun.Start([new RoomSetup("A", TestRun.Room(Open), Features: features)], new ScriptedDice(5, 4), stats: GameRules.HeroBaseStats with { OdHandDice = 2 });
        Hero hero = session.HeroInSeat();
        Assert.Contains("no trap you know of", session.Refuse(new Disarm(hero.Id, new GridPosition(4, 3))));

        session.Do(new Search(hero.Id, Dice: 1));
        IReadOnlyList<GameEvent> events = session.Do(new Disarm(hero.Id, new GridPosition(4, 3)));

        Assert.Equal(new TrapDisarmed(hero.Id, new GridPosition(4, 3)), events[^1]);
        Assert.Null(session.RoomById().TrapAt(new GridPosition(4, 3)));
    }

    private static GameSession ChestRun(RoomFeatures features, ScriptedDice? dice = null) => TestRun.Start([new RoomSetup("A", TestRun.Room("""
        .........
        .........
        .........
        ....C....
        ...C1....
        .........
        .........
        .........
        .........
        """), Features: features)], dice);

    [Fact]
    public void ALockedChestTakesAKeyAndHoldsTwoCards()
    {
        GameSession session = ChestRun(new RoomFeatures { LockedChests = new Dictionary<GridPosition, int> { [new(4, 3)] = 6 } });
        Hero hero = session.HeroInSeat();
        Assert.Contains("locked", session.Refuse(new OpenChest(hero.Id, new GridPosition(4, 3))));

        hero.Keys = 1;
        IReadOnlyList<GameEvent> events = session.Do(new OpenChest(hero.Id, new GridPosition(4, 3)));

        Assert.Equal(new KeyUsed(hero.Id, 0), events[0]);
        Assert.Equal(2, events.SingleOf<ChestOpened>().CardIds.Count);
        Assert.Equal(2, hero.Backpack.Count);
    }

    [Fact]
    public void APickedChestStillHasToBeOpened()
    {
        GameSession session = ChestRun(new RoomFeatures { LockedChests = new Dictionary<GridPosition, int> { [new(4, 3)] = 5 } }, new ScriptedDice(5));
        Hero hero = session.HeroInSeat();
        Assert.Contains("isn't locked", session.CheckPickLock(new GridPosition(3, 4)));

        Assert.Equal(new ChestUnlocked(hero.Id, new GridPosition(4, 3), Picked: true), session.Do(new PickLock(hero.Id, new GridPosition(4, 3)))[^1]);
        Assert.False(session.RoomById().IsChestOpened(new GridPosition(4, 3)));

        IReadOnlyList<GameEvent> events = session.Do(new OpenChest(hero.Id, new GridPosition(4, 3)));
        Assert.DoesNotContain(events, gameEvent => gameEvent is KeyUsed);
        Assert.Equal(2, events.SingleOf<ChestOpened>().CardIds.Count);
    }

    [Fact]
    public void AChestTrapStabsWhoeverOpensIt()
    {
        GameSession session = ChestRun(new RoomFeatures { ChestTraps = new Dictionary<GridPosition, TrapSetup> { [new(4, 3)] = new(TrapKind.Needle, 4) } });
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new OpenChest(hero.Id, new GridPosition(4, 3)));

        Assert.Equal(new TrapSprung(hero.Id, TrapKind.Needle, new GridPosition(4, 3)), events.SingleOf<TrapSprung>());
        Assert.Equal(3, hero.Hearts);
        Assert.Single(events.OfType<ChestOpened>());
    }

    [Fact]
    public void TheBossKeyWaitsInItsChest()
    {
        GameSession session = ChestRun(new RoomFeatures { BossKeyChest = new GridPosition(4, 3) });
        Hero hero = session.HeroInSeat();

        Assert.Contains(new BossKeyFound(hero.Id), session.Do(new OpenChest(hero.Id, new GridPosition(4, 3))));
        Assert.True(hero.HasBossKey);
    }

    [Fact]
    public void TheFirstLootEachTurnIsFree()
    {
        var features = new RoomFeatures { Keys = new Dictionary<GridPosition, int> { [new(4, 4)] = 2 } };
        GameSession session = ChestRun(features);
        Hero hero = session.HeroInSeat();

        session.Do(new OpenChest(hero.Id, new GridPosition(4, 3)));
        Assert.False(session.State.Turn!.HasActed);

        session.Do(new OpenChest(hero.Id, new GridPosition(3, 4)));
        Assert.True(session.State.Turn.HasActed);
        Assert.Contains("free loot and your action", session.Refuse(new PickUpLoot(hero.Id)));

        session.EndRound();
        Assert.Equal(new KeysFound(hero.Id, 2, 2), session.Do(new PickUpLoot(hero.Id)).SingleOf<KeysFound>());
    }

    [Fact]
    public void StealingFromAMonsterTakesAKeyOrACardOnce()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ....d....
            ....1....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(1), stats: GameRules.HeroBaseStats with { OdHandDice = 2 }, loot: new ScriptedDice(10));
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new Steal(hero.Id, dummy.Id, Dice: 1));

        Assert.Equal(new Stole(hero.Id, dummy.Id, null, Key: true), events.SingleOf<Stole>());
        Assert.Equal(1, hero.Keys);
        Assert.Contains("already tried", session.Refuse(new Steal(hero.Id, dummy.Id)));
        session.EndRound();
        Assert.Contains("nothing left", session.Refuse(new Steal(hero.Id, dummy.Id)));
    }

    [Fact]
    public void StealingFromAHeroIsAContestOfOdHand()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....12...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 4), heroes: 2, loot: new ScriptedDice(0));
        Hero thief = session.HeroInSeat(0);
        Hero victim = session.HeroInSeat(1);
        Assert.Contains("nothing you could take", session.Refuse(new Steal(thief.Id, victim.Id)));
        victim.Keys = 2;

        IReadOnlyList<GameEvent> events = session.Do(new Steal(thief.Id, victim.Id));

        Assert.Equal(4, events.SingleOf<DefenseRolled>().Total);
        Assert.Equal(new Stole(thief.Id, victim.Id, null, Key: true), events.SingleOf<Stole>());
        Assert.Equal((1, 1), (thief.Keys, victim.Keys));
    }

    [Fact]
    public void AHeroDownedByATrapDropsTheirKeysBesideItAndTheTurnMovesOn()
    {
        // The second hero starts at (4,3), the free tile nearest the center.
        var features = new RoomFeatures { Traps = new Dictionary<GridPosition, TrapSetup> { [new(3, 4)] = new(TrapKind.Spikes, 4) } };
        GameSession session = TestRun.Start([new RoomSetup("A", TestRun.Room(Open), Features: features)], new ScriptedDice(1), heroes: 2);
        Hero hero = session.HeroInSeat();
        hero.Hearts = 1;
        hero.Keys = 2;
        hero.HasBossKey = true;
        session.Do(new RollMovement(hero.Id));

        IReadOnlyList<GameEvent> events = session.Do(new MoveHero(hero.Id, new GridPosition(3, 4)));

        Assert.Equal(HeroStatus.Downed, hero.Status);
        Room room = session.RoomById();
        Assert.Null(room.TrapAt(room.BossKeyAt!.Value));
        Assert.Equal(2, room.KeysAt(room.BossKeyAt.Value));
        Assert.Equal(new TurnStarted(session.HeroInSeat(1).Id), events[^1]);
    }

    [Theory]
    [InlineData(31UL)]
    [InlineData(32UL)]
    [InlineData(33UL)]
    [InlineData(34UL)]
    public void GeneratedDungeonsGuardTheBossRoomAndHideOneBossKey(ulong seed)
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        RunSetup setup = DungeonGenerator.CreateRun(content, "emerald_keep", [new HeroSetup("Bot", GameRules.HeroBaseStats, GameRules.StarterKit)], seed);
        var session = new GameSession(content, setup);
        GameState state = session.State;

        Room boss = state.Rooms.Values.Single(room => room.Role == RoomRole.Boss);
        Passage bossDoor = Assert.Single(boss.Passages.Values);
        Assert.Equal(PassageKind.BossLocked, bossDoor.Kind);

        int bossKeys = state.Rooms.Values.Sum(room => (room.BossKeyAt is null ? 0 : 1) + room.TilesOfKind(TileKind.Chest).Count(room.HoldsBossKey));
        Assert.Equal(1, bossKeys);
        Room keyRoom = state.Rooms.Values.Single(room => room.BossKeyAt is not null || room.TilesOfKind(TileKind.Chest).Any(room.HoldsBossKey));
        Assert.Equal((boss.Floor, RoomRole.Treasure), (keyRoom.Floor, keyRoom.Role));

        // Traps never split a room: it has as many separate areas with its traps as without.
        foreach (Room room in state.Rooms.Values)
        {
            Assert.Equal(Areas(room, tile => true), Areas(room, tile => room.TrapAt(tile) is null));
        }
    }

    [Fact]
    public void EveryRoomCanBeReachedWithoutSecretsOrGoingThroughTheBossRoom()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        for (ulong seed = 1000; seed < 1300; seed++)
        {
            RunSetup setup = DungeonGenerator.CreateRun(content, "emerald_keep", [new HeroSetup("Bot", GameRules.HeroBaseStats, GameRules.StarterKit)], seed);
            GameState state = new GameSession(content, setup).State;
            foreach (IGrouping<int, Room> floor in state.Rooms.Values.GroupBy(room => room.Floor))
            {
                Room arrival = floor.Single(room => room.Role == RoomRole.Arrival);
                var reached = new HashSet<string> { arrival.Id };
                var frontier = new Queue<Room>([arrival]);
                while (frontier.Count > 0)
                {
                    Room room = frontier.Dequeue();
                    if (room.Role == RoomRole.Boss)
                    {
                        continue;
                    }

                    foreach (Passage passage in room.Passages.Values.Where(passage => passage.Kind is not (PassageKind.Secret or PassageKind.Cracked)))
                    {
                        if (reached.Add(passage.OtherRoom(room.Id)))
                        {
                            frontier.Enqueue(state.Rooms[passage.OtherRoom(room.Id)]);
                        }
                    }
                }

                Assert.True(floor.All(room => reached.Contains(room.Id)), $"Seed {seed}, floor {floor.Key}: {string.Join(", ", floor.Where(room => !reached.Contains(room.Id)).Select(room => room.Id))} cut off.");
            }
        }
    }

    private static int Areas(Room room, Func<GridPosition, bool> allowed)
    {
        bool Open(GridPosition tile) => room.Layout[tile].IsWalkable() && allowed(tile);
        var seen = new HashSet<GridPosition>();
        int areas = 0;
        foreach (GridPosition tile in RoomGrid.AllPositions().Where(tile => Open(tile) && !seen.Contains(tile)))
        {
            areas++;
            seen.UnionWith(PathSearch.Run(tile, Open).Distances.Keys);
        }

        return areas;
    }
}
