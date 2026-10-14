using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;

namespace Woad.Core.Rules;

/// <summary>
/// The rules engine for one run. Play by executing commands; draw the game from <see cref="State"/>
/// and animate the returned events. A refused command never changes the state.
/// </summary>
public sealed partial class GameSession
{
    private readonly GameRandom _random;
    private readonly DiceRoller _dice;

    /// <param name="random">Overrides the random streams derived from the seed (tests script the dice this way).</param>
    public GameSession(ContentCatalog content, RunSetup setup, GameRandom? random = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(setup);

        Content = content;
        _random = random ?? new GameRandom(setup.Seed);
        _dice = new DiceRoller(_random.Dice);
        State = CreateState(content, setup, _random);
    }

    public ContentCatalog Content { get; }

    public GameState State { get; }

    /// <summary>Attack dice the active hero hasn't used this turn.</summary>
    public int AttackDiceLeft => State.Turn is { } turn ? Math.Max(0, ActiveHero.EffectiveStats.AttackDice - turn.AttackDiceUsed) : 0;

    /// <summary>Move dice the active hero hasn't used this turn.</summary>
    public int MoveDiceLeft => State.Turn is { } turn ? Math.Max(0, ActiveHero.EffectiveStats.MoveDice - turn.MoveDiceUsed) : 0;

    /// <summary>The weapon value the active hero can still add to an attack this turn (0 once used).</summary>
    public int WeaponBonusLeft => State.Turn is { WeaponUsed: false } ? Math.Max(0, ActiveHero.EffectiveStats.Weapon) : 0;

    /// <summary>The Speed the active hero can still add to a move this turn (0 once used; may be negative).</summary>
    public int SpeedBonusLeft => State.Turn is { SpeedUsed: false } ? ActiveHero.EffectiveStats.Speed : 0;

    private Hero ActiveHero => State.ActiveHero!;

    private TurnState Turn => State.Turn!;

    private Room ActiveRoom => State.RoomOf(ActiveHero);

    /// <summary>Starts round 1 and the first hero's turn.</summary>
    public IReadOnlyList<GameEvent> Begin()
    {
        if (State.Phase != RunPhase.NotStarted)
        {
            throw new InvalidOperationException("The run has already begun.");
        }

        State.Phase = RunPhase.InProgress;
        var events = new List<GameEvent>();
        StartRound(events);
        return events;
    }

    public CommandResult Execute(GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (CheckActor(command) is { } refusal)
        {
            return CommandResult.Reject(refusal);
        }

        var events = new List<GameEvent>();
        string? error = command switch
        {
            RollMovement c => Handle(c, events),
            MoveHero c => Handle(c, events),
            GoThroughDoor c => Handle(c, events),
            AttackMonster c => Handle(c, events),
            RerollDie c => Handle(c, events),
            ConfirmAttack c => Handle(c, events),
            AddPowerDie c => Handle(c, events),
            BankPowerDie c => Handle(c, events),
            UnlockDoor c => Handle(c, events),
            PickLock c => Handle(c, events),
            Search c => Handle(c, events),
            Disarm c => Handle(c, events),
            Steal c => Handle(c, events),
            JumpDown c => Handle(c, events),
            UseCard c => Handle(c, events),
            EquipCard c => Handle(c, events),
            BeltCard c => Handle(c, events),
            StowCard c => Handle(c, events),
            DropCard c => Handle(c, events),
            OpenChest c => Handle(c, events),
            PickUpLoot c => Handle(c, events),
            EndTurn c => Handle(c, events),
            TakeStairs c => Handle(c, events),
            SmashPot c => Handle(c, events),
            Escape c => Handle(c, events),
            _ => $"Unknown command {command.GetType().Name}.",
        };

        if (error is not null)
        {
            return CommandResult.Reject(error);
        }

        // A hero downed on their own turn (by a trap) hands the turn on.
        if (State.Phase == RunPhase.InProgress && State.Turn is { } turn && !State.GetHero(turn.HeroId).IsActive)
        {
            State.Turn.PendingRoll = null;
            State.Turn.UnsettledPowerDice = 0;
            events.Add(new TurnEnded(turn.HeroId));
            AdvanceTurn(State.GetHero(turn.HeroId).Seat, events);
        }

        return CommandResult.Accept(events);
    }

    private string? CheckActor(GameCommand command)
    {
        if (State.Phase != RunPhase.InProgress)
        {
            return "The run isn't in progress.";
        }

        if (command.HeroId != Turn.HeroId)
        {
            return "It isn't that hero's turn.";
        }

        if (Turn.UnsettledPowerDice > 0 && command is not (AddPowerDie or BankPowerDie or RerollDie))
        {
            return "You rolled a 6: add your Power Die to the roll or bank it first.";
        }

        if (Turn.PendingRoll is not null && command is not (RerollDie or ConfirmAttack or AddPowerDie or BankPowerDie))
        {
            return "Finish the roll first: reroll a die with Luck or confirm it.";
        }

        return null;
    }

    private string? Handle(EndTurn command, List<GameEvent> events)
    {
        events.Add(new TurnEnded(command.HeroId));
        AdvanceTurn(ActiveHero.Seat, events);
        return null;
    }

    /// <summary>Starts the next seated hero's turn, or the enemy phase and a new round after the last seat.</summary>
    private void AdvanceTurn(int fromSeat, List<GameEvent> events)
    {
        Hero? next = State.Heroes.FirstOrDefault(hero => hero.IsActive && hero.Seat > fromSeat);
        if (next is not null)
        {
            StartTurn(next, events);
            return;
        }

        RunEnemyPhase(events);
        if (State.Phase == RunPhase.InProgress)
        {
            StartRound(events);
        }
    }

    private void StartRound(List<GameEvent> events)
    {
        State.Round++;
        events.Add(new RoundStarted(State.Round));
        StartTurn(State.Heroes.First(hero => hero.IsActive), events);
    }

    private void StartTurn(Hero hero, List<GameEvent> events)
    {
        State.Turn = new TurnState(hero.Id);
        events.Add(new TurnStarted(hero.Id));
    }

    /// <summary>Ends the run once nobody is left in the dungeon. The richest escaped hero wins; kills break ties.</summary>
    private void EndRunIfOver(List<GameEvent> events)
    {
        if (State.Heroes.Any(hero => hero.IsActive))
        {
            return;
        }

        List<Hero> escaped = State.Heroes.Where(hero => hero.Status == HeroStatus.Escaped).ToList();
        List<int> winners = [];
        if (escaped.Count > 0)
        {
            int bestTreasure = escaped.Max(hero => hero.TreasureValue);
            List<Hero> richest = escaped.Where(hero => hero.TreasureValue == bestTreasure).ToList();
            int bestKills = richest.Max(hero => hero.Kills);
            winners = richest.Where(hero => hero.Kills == bestKills).Select(hero => hero.Id).ToList();
        }

        State.Phase = RunPhase.Ended;
        State.Turn = null;
        State.WinnerIds = winners;
        events.Add(new RunEnded(winners));
    }

    private string? CheckCanAct() => Turn.HasActed ? "You've already used your action this turn." : null;

    /// <summary>Marks the turn's other action as used. Acting ends the move in progress; unused steps are lost.</summary>
    private void SpendAction()
    {
        Turn.HasActed = true;
        Turn.EndMove();
    }

    private bool IsOccupied(Room room, GridPosition position) => IsOccupied(State, room, position);

    private static bool IsOccupied(GameState state, Room room, GridPosition position) =>
        room.MonsterAt(position) is not null || state.ActiveHeroesIn(room).Any(hero => hero.Position == position);

    private static GameState CreateState(ContentCatalog content, RunSetup setup, GameRandom random)
    {
        if (setup.Heroes.Count is < 1 or > GameRules.MaxHeroes)
        {
            throw new ArgumentException($"A run needs 1 to {GameRules.MaxHeroes} heroes.", nameof(setup));
        }

        ThemeDefinition theme = content.GetTheme(setup.ThemeId);
        var state = new GameState(setup.Seed, theme.Id, new LootDeck(theme, content, random.Loot));

        foreach (RoomSetup roomSetup in setup.Rooms)
        {
            var room = new Room(
                roomSetup.Id,
                roomSetup.Template.Layout.Copy(),
                new Dictionary<Direction, string>(roomSetup.Doors ?? new Dictionary<Direction, string>()),
                roomSetup.Floor,
                roomSetup.MapPosition,
                roomSetup.Role,
                roomSetup.Trap,
                roomSetup.Template.HeroStarts.OrderBy(start => start.Key).Select(start => start.Value).ToList());
            foreach (MonsterSpawn spawn in roomSetup.Template.Monsters)
            {
                room.AddMonster(new Monster(state.NextId(), spawn.Monster, spawn.Position));
            }

            AddFeatures(room, roomSetup.Features ?? RoomFeatures.None);
            state.AddRoom(room);
        }

        ValidateDoors(state);
        AddPassages(state, setup.Passages);

        if (!state.Rooms.TryGetValue(setup.StartRoomId, out Room? startRoom))
        {
            throw new ArgumentException($"Start room '{setup.StartRoomId}' isn't one of the run's rooms.", nameof(setup));
        }

        RoomTemplate startTemplate = setup.Rooms.First(room => room.Id == setup.StartRoomId).Template;
        for (int seat = 0; seat < setup.Heroes.Count; seat++)
        {
            HeroSetup heroSetup = setup.Heroes[seat];
            if (string.IsNullOrWhiteSpace(heroSetup.Name))
            {
                throw new ArgumentException($"The hero in seat {seat + 1} needs a name.", nameof(setup));
            }

            GridPosition start = startTemplate.HeroStarts.TryGetValue(seat, out GridPosition marked)
                ? marked
                : FreeTileNearCenter(state, startRoom);
            if (!startRoom.Layout[start].IsWalkable() || IsOccupied(state, startRoom, start))
            {
                throw new ArgumentException($"Seat {seat + 1} starts on a blocked tile {start}.", nameof(setup));
            }

            var hero = new Hero(state.NextId(), seat, heroSetup.Name, heroSetup.BaseStats, startRoom.Id, start, heroSetup.CharacterId, heroSetup.Portrait);
            state.AddHero(hero);

            foreach (string cardId in heroSetup.CardIds)
            {
                var card = new CardInstance(state.NextId(), content.GetCard(cardId), hero.Id);
                if (!hero.TryPlaceStartingCard(card))
                {
                    throw new ArgumentException($"{hero.Name} is carrying more cards than fit.", nameof(setup));
                }
            }

            hero.Hearts = hero.MaxHearts;
            hero.LuckRemaining = hero.EffectiveStats.Luck;
        }

        startRoom.Explored = true;
        return state;
    }

    private static void ValidateDoors(GameState state)
    {
        foreach (Room room in state.Rooms.Values)
        {
            foreach ((Direction side, string targetId) in room.Doors)
            {
                if (!room.Layout[side.DoorTile()].IsWalkable())
                {
                    throw new ArgumentException($"Room '{room.Id}' has a {side} door but its doorway isn't floor.");
                }

                if (!state.Rooms.TryGetValue(targetId, out Room? target)
                    || !target.Doors.TryGetValue(side.Opposite(), out string? backId)
                    || backId != room.Id)
                {
                    throw new ArgumentException($"Room '{room.Id}' {side} door must lead to a room with a matching {side.Opposite()} door back.");
                }
            }
        }
    }

    private static void AddFeatures(Room room, RoomFeatures features)
    {
        foreach ((GridPosition tile, TrapSetup trap) in features.Traps)
        {
            room.SetTrap(tile, new Trap(trap.Kind, trap.Difficulty));
        }

        foreach ((GridPosition chest, int difficulty) in features.LockedChests)
        {
            room.LockChest(chest, difficulty);
        }

        foreach ((GridPosition chest, TrapSetup trap) in features.ChestTraps)
        {
            room.SetChestTrap(chest, new Trap(trap.Kind, trap.Difficulty));
        }

        if (features.BossKeyChest is { } bossKeyChest)
        {
            room.PutBossKeyInChest(bossKeyChest);
        }

        room.BossKeyAt = features.BossKeyOnFloor;

        foreach ((GridPosition tile, CacheSetup cache) in features.Caches)
        {
            room.HideCache(tile, new Cache(cache.Difficulty, cache.Keys, cache.Cards, cache.BehindWall));
        }

        foreach ((GridPosition tile, int keys) in features.Keys)
        {
            room.AddKeys(tile, keys);
        }
    }

    /// <summary>One shared <see cref="Passage"/> per pair of doors, open unless the setup says otherwise.</summary>
    private static void AddPassages(GameState state, IReadOnlyList<PassageSetup> setups)
    {
        foreach (Room room in state.Rooms.Values)
        {
            foreach ((Direction side, string targetId) in room.Doors)
            {
                if (room.PassageOn(side) is not null)
                {
                    continue;
                }

                Room target = state.Rooms[targetId];
                PassageSetup? setup = setups.FirstOrDefault(passage =>
                    (passage.RoomId == room.Id && passage.Side == side) || (passage.RoomId == target.Id && passage.Side == side.Opposite()));
                Trap? trap = setup?.Trap is { } darts ? new Trap(darts.Kind, darts.Difficulty) : null;
                var passage = new Passage(room.Id, side, target.Id, setup?.Kind ?? PassageKind.Open, setup?.Difficulty ?? 0, trap);
                room.AddPassage(side, passage);
                target.AddPassage(side.Opposite(), passage);
            }
        }
    }

    private static GridPosition FreeTileNearCenter(GameState state, Room room)
    {
        var center = new GridPosition(RoomGrid.Center, RoomGrid.Center);
        return RoomGrid.AllPositions()
            .Where(position => room.Layout[position].IsWalkable() && !IsOccupied(state, room, position))
            .OrderBy(position => position.ManhattanDistanceTo(center))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .First();
    }
}
