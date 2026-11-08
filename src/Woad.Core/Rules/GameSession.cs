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
        _monstersFirst = setup.MonstersFirst;
        _uniquesClaimed = [.. setup.UniquesClaimed];
    }

    /// <summary>Dungeon uniques claimed this raid before the run: their <c>UniqueElse</c> drops in their place.</summary>
    private readonly HashSet<string> _uniquesClaimed;

    /// <summary>The monsters take a turn before the heroes' first (see <see cref="RunSetup.MonstersFirst"/>).</summary>
    private readonly bool _monstersFirst;

    public ContentCatalog Content { get; }

    public GameState State { get; }

    /// <summary>Attack dice the active hero hasn't used this turn.</summary>
    public int AttackDiceLeft => State.Turn is { } turn && !ActiveHero.IsCarrying ? Math.Max(0, ActiveHero.EffectiveStats.AttackDice - turn.AttackDiceUsed) : 0;

    /// <summary>Why there are no Rumble dice to roll: they're used up, or the hero's arms are full.</summary>
    private string NoRumbleDice => ActiveHero.IsCarrying ? ArmsFull("Rumble") : "You've used all your Rumble dice this turn.";

    /// <summary>Why there are no Od Hand dice to roll: they're used up, or the hero's arms are full.</summary>
    private string NoOdHandDice => ActiveHero.IsCarrying ? ArmsFull("Od Hand") : "You've used all your Od Hand dice this turn.";

    private string ArmsFull(string pool) =>
        $"Your arms are full ({string.Join(" and ", ActiveHero.Carried.Select(card => card.Definition.Name))}): no {pool} while you carry. Drop it, or put it away.";

    /// <summary>Move dice the active hero hasn't used this turn.</summary>
    public int MoveDiceLeft => State.Turn is { } turn && ActiveHero.Shackled is null && !ActiveHero.Conditions.Has(Condition.Buried) ? Math.Max(0, ActiveHero.EffectiveStats.MoveDice + turn.ExtraMoveDice - turn.MoveDiceUsed) : 0;

    /// <summary>The tools' own dice the active hero can still add to an Od Hand check this turn (a lockpick's d6), for the dice bar.</summary>
    public int ToolDiceLeft => State.Turn is { ToolsUsed: false } ? BestToolHelp(ActiveHero).Dice : 0;

    /// <summary>A versatile main-hand weapon's extra, held in both hands (nothing in the off hand).</summary>
    private static int Versatile(Hero hero) =>
        hero.MainWeapon?.Definition is { Versatile: > 0 } main && !hero.Equipment.ContainsKey(EquipSlot.OffHand) ? main.Versatile : 0;

    /// <summary>The Speed the active hero can still add to a move this turn (0 once used; may be negative).</summary>
    public int SpeedBonusLeft => State.Turn is { SpeedUsed: false } ? ActiveHero.EffectiveStats.Speed : 0;

    /// <summary>The Rumble Bonus the active hero has yet to put on an attack this turn.</summary>
    public int RumbleBonusLeft => State.Turn is { RumbleBonusUsed: false } ? ActiveHero.RumbleBonus : 0;

    /// <summary>
    /// The Rumble Bonus for an attack of <paramref name="dice"/> Rumble dice, spent: on the first attack, or one
    /// it was held back from until the attack that rolls the last of the Rumble dice. Call before spending the dice.
    /// </summary>
    private int TakeRumbleBonus(bool hold, int dice)
    {
        int bonus = RumbleBonusLeft;
        if (bonus == 0 || (hold && dice < AttackDiceLeft))
        {
            return 0;
        }

        Turn.RumbleBonusUsed = true;
        return bonus;
    }

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
        if (_monstersFirst)
        {
            RunEnemyPhase(events);
        }

        if (State.Phase == RunPhase.InProgress)
        {
            StartRound(events);
        }

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
            CastSpell c => Handle(c, events),
            LearnSpell c => Handle(c, events),
            LayIce c => Handle(c, events),
            AddPowerDie c => Handle(c, events),
            BankPowerDie c => Handle(c, events),
            UnlockDoor c => Handle(c, events),
            PickLock c => Handle(c, events),
            Search c => Handle(c, events),
            Disarm c => Handle(c, events),
            Steal c => Handle(c, events),
            JumpDown c => Handle(c, events),
            OpenGate c => Handle(c, events),
            GoThroughBreach c => Handle(c, events),
            UseCard c => Handle(c, events),
            EquipCard c => Handle(c, events),
            ReloadWeapon c => Handle(c, events),
            BullRush c => Handle(c, events),
            Parry c => Handle(c, events),
            GuardHero c => Handle(c, events),
            HighAlert c => Handle(c, events),
            FreeMerchant c => Handle(c, events),
            Unshackle c => Handle(c, events),
            Retreat c => Handle(c, events),
            MoveOn c => Handle(c, events),
            NudgeAlly c => Handle(c, events),
            PickUpAlly c => Handle(c, events),
            LightPortal c => Handle(c, events),
            Smash c => Handle(c, events),
            LiftBar c => Handle(c, events),
            TakePortal c => Handle(c, events),
            PutDownAlly c => Handle(c, events),
            SpendPoint c => Handle(c, events),
            SetCompanionPolicy c => Handle(c, events),
            BuyFromMerchant c => Handle(c, events),
            SellToMerchant c => Handle(c, events),
            AttackMerchant c => Handle(c, events),
            BeltCard c => Handle(c, events),
            StowCard c => Handle(c, events),
            CarryCard c => Handle(c, events),
            UnbuckleBelt c => Handle(c, events),
            ChooseBeltLook c => Handle(c, events),
            ShowGear c => Handle(c, events),
            DropCard c => Handle(c, events),
            OpenChest c => Handle(c, events),
            PickUpLoot c => Handle(c, events),
            TakeLoot c => Handle(c, events),
            GiveCard c => Handle(c, events),
            TakeCard c => Handle(c, events),
            GiveGold c => Handle(c, events),
            GiveKey c => Handle(c, events),
            TakeKey c => Handle(c, events),
            TakeGold c => Handle(c, events),
            OutfitCompanion c => Handle(c, events),
            PutLoot c => Handle(c, events),
            EndTurn c => Handle(c, events),
            TakeStairs c => Handle(c, events),
            SmashPot c => Handle(c, events),
            Escape c => Handle(c, events),
            OrderCompanion c => Handle(c, events),
            SwitchControl c => Handle(c, events),
            SetLuckAsks c => Handle(c),
            BoostFallSave c => Handle(c, events),
            LuckOnFallSave c => Handle(c, events),
            LetSaveStand c => Handle(c, events),
            AddOdHandDie c => Handle(c, events),
            HurlHeldFire c => Handle(c, events),
            OffHandAttack c => Handle(c, events),
            PushThroughDoor c => Handle(c, events),
            LightStick c => Handle(c, events),
            Shove c => Handle(c, events),
            AttackAlly c => Handle(c, events),
            Leap c => Handle(c, events),
            Aid c => Handle(c, events),
            SoakUp c => Handle(c, events),
            LearnSkill c => Handle(c, events),
            RollKeel c => Handle(c, events),
            GoProne c => Handle(c, events),
            BeatWings c => Handle(c, events),
            Fly c => Handle(c, events),
            Land c => Handle(c, events),
            GetUp c => Handle(c, events),
            StopDropRoll c => Handle(c, events),
            DigOut c => Handle(c, events),
            AttackBomb c => Handle(c, events),
            _ => $"Unknown command {command.GetType().Name}.",
        };

        if (error is not null)
        {
            return CommandResult.Reject(error);
        }

        FlushCounters(events);
        CreditDeeds(events);

        // A hero who keels over on their own turn (by a trap) hands the turn on.
        if (State.Phase == RunPhase.InProgress && State.Turn is { } turn && State.GetHero(turn.HeroId) is var current
            && (!current.IsInPlay || (current.IsKeelingOver && !turn.KeelingOver)))
        {
            State.Turn.PendingRoll = null;
            State.Turn.UnsettledPowerDice = 0;
            events.Add(new TurnEnded(turn.HeroId));
            AdvanceTurn(State.GetHero(turn.HeroId).Seat, events);
        }

        if (State.Phase == RunPhase.InProgress)
        {
            DouseEveryoneInWater(events);
            CatchFireFromFloor(events);
            LightBraziers(events);
        }

        foreach (Hero hero in State.Heroes.Where(hero => hero.IsActive && State.RoomOf(hero).Layout[hero.Position].IsWalkable()))
        {
            hero.Footing = hero.Position;
        }

        if (State.Phase == RunPhase.InProgress)
        {
            MerchantsFollow(events);
        }

        return CommandResult.Accept(events);
    }

    private string? CheckActor(GameCommand command)
    {
        if (State.Phase != RunPhase.InProgress)
        {
            return "The run isn't in progress.";
        }

        // A save after a fall is the faller's to settle, whoever's turn it is; nothing else of theirs till it is.
        if (command is BoostFallSave or LuckOnFallSave or LetSaveStand)
        {
            return null;
        }

        if (State.Heroes.FirstOrDefault(hero => hero.Id == command.HeroId) is { PendingSave: not null })
        {
            return "Settle the fall first: add Od Hand dice, try your Luck, or let it stand.";
        }

        if (command.HeroId != Turn.HeroId)
        {
            return "It isn't that hero's turn.";
        }

        if (command is OrderCompanion or SwitchControl or SetLuckAsks)
        {
            return null;
        }

        if (Turn.KeelingOver && command is not RollKeel)
        {
            return "You're Keeling Over: all you can do is roll the Keel Die.";
        }

        if (ActiveHero.Conditions.Has(Condition.Buried) && NeedsToBeUnburied(command))
        {
            return "You're buried: dig yourself out with your Rumble dice first.";
        }

        if (Turn.UnsettledPowerDice > 0 && command is not (AddPowerDie or BankPowerDie or RerollDie))
        {
            return "You rolled a 6: add your Power Die to the roll or bank it first.";
        }

        if (Turn.PendingRoll is not null && command is not (RerollDie or ConfirmAttack or AddPowerDie or BankPowerDie or AddOdHandDie))
        {
            return "Finish the roll first: reroll a die with Luck or confirm it.";
        }

        return null;
    }

    private string? Handle(EndTurn command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        if (hero.Aloft)
        {
            Touchdown(hero, events);
        }

        KeepAfloat(hero, events);
        DropCarriedFire(hero, events);
        if (State.Phase != RunPhase.InProgress || !hero.IsActive)
        {
            return null;
        }

        events.Add(new TurnEnded(command.HeroId));
        AdvanceTurn(hero.Seat, events);
        return null;
    }

    /// <summary>Starts the next seated hero's turn, or the enemy phase and a new round after the last seat.</summary>
    private void AdvanceTurn(int fromSeat, List<GameEvent> events)
    {
        if (State.Turn is { } ended)
        {
            // Od Hand dice spent this turn stay spent till the hero's next turn.
            State.GetHero(ended.HeroId).OdHandSpent = ended.OdHandDiceUsed;
            WearOffStun(State.GetHero(ended.HeroId), events);
            if (!ended.KeelingOver)
            {
                ConditionsAtTurnEnd(State.GetHero(ended.HeroId), events);
            }

            if (State.Phase != RunPhase.InProgress)
            {
                return;
            }

            BurnFuses(ended.HeroId, heroTurn: true, events);
            if (State.Phase != RunPhase.InProgress)
            {
                return;
            }
        }

        if (UsesInitiative)
        {
            NextInitiative(events);
            return;
        }

        Hero? next = State.Heroes.FirstOrDefault(hero => hero.IsInPlay && hero.Seat > fromSeat);
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
        BurnDownFloors(events);
        WearOffMagic(events);
        TickRitual(events);
        if (State.Phase != RunPhase.InProgress)
        {
            return;
        }

        State.Round++;
        foreach (Hero hero in State.Heroes)
        {
            hero.RollsThisRound = 0;
        }

        events.Add(new RoundStarted(State.Round));
        if (UsesInitiative)
        {
            RollInitiative(events);
            NextInitiative(events);
            return;
        }

        StartTurn(State.Heroes.First(hero => hero.IsInPlay), events);
    }

    private void StartTurn(Hero hero, List<GameEvent> events)
    {
        // A save still waiting from a fall settles as it is; spent dice come back now.
        SettleFallSave(hero, events);
        hero.OdHandSpent = 0;
        State.Turn = new TurnState(hero.Id) { KeelingOver = hero.IsKeelingOver };
        events.Add(new TurnStarted(hero.Id));
        ChargeMagic(hero, events);
        RechargeDefense(hero);
        DropHeldDefense(hero);
        ApplyStun(hero);

        // A burning hero burns first; if that brings them down, or they're frozen solid, play moves on.
        bool burned = BurnAtTurnStart(hero, events);
        if (State.Phase == RunPhase.InProgress && (!burned || (!State.Turn.KeelingOver && !ConditionsAtTurnStart(hero, events))))
        {
            State.Turn.PendingRoll = null;
            events.Add(new TurnEnded(hero.Id));
            AdvanceTurn(hero.Seat, events);
        }
    }

    /// <summary>
    /// Ends the run once no player is left in the dungeon (companions don't keep it going). Companions
    /// still inside are left behind and lose what they found. The richest escaped player wins; kills
    /// break ties.
    /// </summary>
    private void EndRunIfOver(List<GameEvent> events)
    {
        if (State.Heroes.Any(hero => hero.IsInPlay && !hero.IsCompanion))
        {
            return;
        }

        foreach (Hero companion in State.Heroes.Where(hero => hero.IsInPlay))
        {
            TakeFound(companion);

            companion.Coins = 0;
        }

        List<Hero> escaped = State.Heroes.Where(hero => hero.Status == HeroStatus.Escaped && !hero.IsCompanion && !State.RetreatedIds.Contains(hero.Id)).ToList();
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

    /// <summary>Marks the turn's other action as used. Steps left in the move stay usable.</summary>
    private void SpendAction() => Turn.HasActed = true;

    private bool IsOccupied(Room room, GridPosition position) => IsOccupied(State, room, position);

    private static bool IsOccupied(GameState state, Room room, GridPosition position) =>
        room.MonsterAt(position) is not null || room.MerchantAt(position) is not null || state.HeroesIn(room).Any(hero => hero.Position == position);

    private static GameState CreateState(ContentCatalog content, RunSetup setup, GameRandom random)
    {
        if (setup.Heroes.Count is < 1 or > GameRules.MaxHeroes)
        {
            throw new ArgumentException($"A run needs 1 to {GameRules.MaxHeroes} heroes.", nameof(setup));
        }

        ThemeDefinition theme = content.GetTheme(setup.ThemeId);
        var state = new GameState(setup.Seed, theme.Id, new LootDeck(theme, content, random.Loot)) { FriendlyFire = setup.FriendlyFire, Skirmish = setup.Skirmish };

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
                roomSetup.Template.HeroStarts.OrderBy(start => start.Key).Select(start => start.Value).ToList(),
                roomSetup.DoorTiles)
            {
                Dark = roomSetup.Dark,
                Level = roomSetup.Level,
                StairsUpTo = roomSetup.StairsUpTo,
                StairsDownTo = roomSetup.StairsDownTo,
            };
            foreach (MonsterSpawn spawn in roomSetup.Template.Monsters)
            {
                // A graveyard's dead lie still until a hero comes near.
                Monster monster = CreateMonster(state.NextId(), spawn.Monster, spawn.Position, GameRules.PartyStrength(setup.Heroes));
                monster.Asleep = theme.Graveyard is not null;
                room.AddMonster(monster);
            }

            AddFeatures(room, roomSetup.Features ?? RoomFeatures.None);
            if (!setup.Skirmish && theme.Finds is { } finds)
            {
                HideFinds(room, finds, roomSetup.DoorTiles, random.Finds, content, state);
            }

            state.AddRoom(room);
        }

        AddMerchants(state, content, setup, random);
        ValidateDoors(state);
        AddPassages(state, setup.Passages);
        AddBreaches(state, setup.Breaches);
        foreach (Room room in state.Rooms.Values)
        {
            // A tower's rooms drop to nothing below (but where they fall somewhere of their own, a roof's open air).
            room.BelowRoomId = setup.Rooms.First(other => other.Id == room.Id).FallsTo
                ?? (room.Level > 0 ? null : state.Rooms.Values
                    .FirstOrDefault(below => below.Level == 0 && below.Floor == room.Floor + 1 && below.MapPosition == room.MapPosition && below.Role is not (RoomRole.Boss or RoomRole.Secret))?.Id);
        }

        // Some holes go down deep: two floors, straight through the room underneath.
        foreach (Room room in state.Rooms.Values.OrderBy(room => room.Id, StringComparer.Ordinal))
        {
            if (room.Level > 0 || room.BelowRoomId is not { } belowId || state.Rooms[belowId].BelowRoomId is not { } deepId)
            {
                continue;
            }

            foreach (GridPosition hole in room.TilesOfKind(TileKind.Pit).Concat(room.Traps.Where(trap => trap.Value.Kind == TrapKind.TrapDoor).Select(trap => trap.Key)))
            {
                if (random.Holes.NextInt(0, 100) < GameRules.DeepHoleChance)
                {
                    room.MakeDeep(hole, deepId);
                }
            }
        }

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

            // In a skirmish, heroes past the marked starts gather by the first, on the party's side of the field.
            GridPosition start = startTemplate.HeroStarts.TryGetValue(seat, out GridPosition marked)
                ? marked
                : setup.Skirmish && startTemplate.HeroStarts.TryGetValue(0, out GridPosition first) ? FreeTileNear(state, startRoom, first)
                : FreeTileNearCenter(state, startRoom);
            if (!startRoom.Layout[start].IsWalkable() || IsOccupied(state, startRoom, start))
            {
                throw new ArgumentException($"Seat {seat + 1} starts on a blocked tile {start}.", nameof(setup));
            }

            int? ownerId = null;
            if (heroSetup.OwnerSeat is int ownerSeat)
            {
                if (ownerSeat < 0 || ownerSeat >= seat || setup.Heroes[ownerSeat].OwnerSeat is not null)
                {
                    throw new ArgumentException($"The companion in seat {seat + 1} must belong to a player seated before it.", nameof(setup));
                }

                ownerId = state.Heroes[ownerSeat].Id;
            }

            var hero = new Hero(state.NextId(), seat, heroSetup.Name, heroSetup.BaseStats, startRoom.Id, start, heroSetup.CharacterId, heroSetup.Portrait, heroSetup.Appearance)
            {
                OwnerId = ownerId,
                PlayerNumber = ownerId is null ? state.Heroes.Count(other => !other.IsCompanion) + 1 : 0,
                GuildStanding = heroSetup.GuildStanding,
                StartAllocation = heroSetup.Allocation,
                Allocation = heroSetup.Allocation,
                StartXp = heroSetup.Xp,
                StartUnspentPoints = heroSetup.UnspentPoints,
                Policy = heroSetup.Policy ?? CompanionPolicy.Default,
                LuckAsks = heroSetup.LuckAsks,
                CloverLuck = Math.Clamp(heroSetup.CloverLuck, 0, GameRules.MaxCloverLuck),
                StartCloverLuck = Math.Clamp(heroSetup.CloverLuck, 0, GameRules.MaxCloverLuck),
                Skills = heroSetup.Skills,
            };
            state.AddHero(hero);

            foreach (string cardId in heroSetup.CardIds)
            {
                CardInstance card = CardInstance.FromKey(cardId, content, state.NextId, hero.Id);
                if (!hero.TryPlaceStartingCard(card))
                {
                    throw new ArgumentException($"{hero.Name} is carrying more cards than fit.", nameof(setup));
                }
            }

            foreach (string spellId in heroSetup.Spells ?? [])
            {
                hero.Learn(content.GetSpell(spellId));
            }

            // Black Hearts and disease come along from the last run, until they're cured.
            foreach ((Condition condition, int stacks) in heroSetup.Lingering ?? new Dictionary<Condition, int>())
            {
                hero.Conditions.Add(condition, stacks);
            }

            hero.Power.Banked = Math.Clamp(heroSetup.PowerDice, 0, hero.Power.Capacity);
            hero.Hearts = Math.Clamp(Math.Min(heroSetup.Hearts ?? hero.MaxHearts, hero.HealableTo), 1, hero.MaxHearts);
            hero.LuckRemaining = hero.EffectiveStats.Luck;
        }

        startRoom.Explored = true;

        // The ritual under way: its celebrants are wide awake. A cursed graveyard: everyone is.
        state.RitualRoundsLeft = setup.RitualRounds;
        foreach (Room room in state.Rooms.Values)
        {
            foreach (Monster monster in room.Monsters.Where(monster => monster.Definition.Ritualist || setup.Cursed))
            {
                monster.Asleep = false;
            }

            if (setup.Cursed)
            {
                room.Cursed = true;
                room.HordeAlert = 3;
                state.PortalOpen = true;
            }
        }

        return state;
    }

    private static void ValidateDoors(GameState state)
    {
        foreach (Room room in state.Rooms.Values)
        {
            foreach ((Direction side, string targetId) in room.Doors)
            {
                if (!room.Layout[room.DoorTile(side)].IsWalkable())
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

    /// <summary>
    /// Hides what lies about (a Rock, a Stick) on a few of a room's open tiles, for a Search to find: none
    /// where heroes arrive, monsters stand, by a door, or where something's hidden already.
    /// </summary>
    private static void HideFinds(Room room, FindsDefinition finds, IReadOnlyDictionary<Direction, GridPosition>? doorTiles, IRandomSource random, ContentCatalog content, GameState state)
    {
        HashSet<GridPosition> taken = [.. room.ArrivalTiles, .. room.Monsters.Select(monster => monster.Position), .. room.Traps.Keys, .. room.Caches.Keys,
            .. (doorTiles?.Values ?? []).SelectMany(door => door.SurroundingTiles().Append(door))];
        List<GridPosition> open = room.TilesOfKind(TileKind.Floor).Where(tile => !taken.Contains(tile)).ToList();
        for (int count = random.NextInt(0, finds.Hidden + 1); count > 0 && open.Count > 0; count--)
        {
            GridPosition tile = random.Pick(open);
            open.Remove(tile);
            room.HideCache(tile, new Cache(GameRules.FindDifficulty(room.Floor), 0, 0, false, [random.Pick(finds.Cards)]));
        }

        // Now and then a Lucky Clover, lying in plain sight.
        if (finds.Clover is { } clover && finds.CloverChance > 0 && random.NextInt(0, 100) < finds.CloverChance && open.Count > 0)
        {
            room.AddLoot(random.Pick(open), [new CardInstance(state.NextId(), content.GetCard(clover), broughtInByHeroId: null)]);
        }
    }

    private static void AddFeatures(Room room, RoomFeatures features)
    {
        foreach ((GridPosition tile, TrapSetup trap) in features.Traps)
        {
            // An unstable portal is in plain sight.
            room.SetTrap(tile, new Trap(trap.Kind, trap.Difficulty) { Revealed = trap.Kind == TrapKind.Rift });
        }

        foreach ((GridPosition chest, int difficulty) in features.LockedChests)
        {
            room.LockChest(chest, difficulty);
        }

        foreach ((GridPosition chest, TrapSetup trap) in features.ChestTraps)
        {
            room.SetChestTrap(chest, new Trap(trap.Kind, trap.Difficulty));
        }

        foreach (GridPosition chest in features.GrandChests)
        {
            room.MakeGrandChest(chest);
        }

        foreach ((GridPosition chest, int difficulty) in features.Mimics)
        {
            room.HideMimic(chest, difficulty);
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

        foreach ((GridPosition tile, int difficulty) in features.Gates)
        {
            room.LockGate(tile, difficulty);
        }

        foreach ((GridPosition tile, int difficulty) in features.HiddenGates)
        {
            room.HideGate(tile, difficulty);
        }

        foreach ((GridPosition chest, ChestLook look) in features.ChestLooks)
        {
            room.SetChestLook(chest, look);
        }

        foreach (GridPosition chest in features.EmptyChests)
        {
            room.EmptyChest(chest);
        }

        foreach ((GridPosition grave, string monsterId) in features.Risers)
        {
            room.LayRiser(grave, monsterId);
        }

        room.RelicChest = features.RelicChest;
        room.RitualCircle = features.RitualCircle;
        foreach (GridPosition brazier in features.LitBraziers)
        {
            room.LightBrazier(brazier);
        }
    }

    /// <summary>Each breach is a cracked wall on both rooms' facing edge tiles, leading into each other.</summary>
    private static void AddBreaches(GameState state, IReadOnlyList<BreachSetup> breaches)
    {
        foreach (BreachSetup breach in breaches)
        {
            if (!state.Rooms.TryGetValue(breach.RoomA, out Room? a) || !state.Rooms.TryGetValue(breach.RoomB, out Room? b))
            {
                throw new ArgumentException($"A breach joins rooms that aren't in the run: {breach}.", nameof(breaches));
            }

            a.Layout.Set(breach.TileA, TileKind.CrackedWall);
            b.Layout.Set(breach.TileB, TileKind.CrackedWall);
            a.AddBreach(breach.TileA, b.Id, breach.TileB);
            b.AddBreach(breach.TileB, a.Id, breach.TileA);
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
                var passage = new Passage(room.Id, side, target.Id, setup?.Kind ?? PassageKind.Open, setup?.Difficulty ?? 0, trap)
                {
                    Locks = setup?.Locks ?? 1,
                    BarredFrom = setup?.BarredFrom,
                    TrapsArmedFrom = setup is { Trap: not null } armed ? (armed.RoomId == target.Id ? target.Id : room.Id) : null,
                };
                foreach (TrapSetup more in setup?.MoreTraps ?? [])
                {
                    passage.AddTrap(new Trap(more.Kind, more.Difficulty));
                }

                room.AddPassage(side, passage);
                target.AddPassage(side.Opposite(), passage);
            }
        }
    }

    /// <summary>A new monster; a boss is toughened for a party bigger than <see cref="GameRules.FullParty"/>.</summary>
    private static Monster CreateMonster(int id, MonsterDefinition definition, GridPosition position, int partySize) =>
        new(id, definition, position, GameRules.ExtraMonsterHearts(partySize, definition.Boss)) { Luck = Math.Max(definition.Luck, definition.Hexes ? 1 : 0) };

    private static GridPosition FreeTileNear(GameState state, Room room, GridPosition near) =>
        room.Positions
            .Where(position => room.Layout[position].IsWalkable() && !IsOccupied(state, room, position))
            .OrderBy(position => position.ManhattanDistanceTo(near))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .First();

    private static GridPosition FreeTileNearCenter(GameState state, Room room)
    {
        GridPosition center = room.Layout.Center;
        return room.Positions
            .Where(position => room.Layout[position].IsWalkable() && !IsOccupied(state, room, position))
            .OrderBy(position => position.ManhattanDistanceTo(center))
            .ThenBy(position => position.Y)
            .ThenBy(position => position.X)
            .First();
    }
}
