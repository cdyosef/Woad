using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;

namespace Woad.Core.Rules;

/// <summary>
/// The tokens typed dice leave behind (see <see cref="Condition"/>), and how they come and go. Poison
/// takes a point off all their rolls and is rolled against at the end of their turn; Freeze slows them,
/// then takes their Wend, then freezes them solid, melting a stack a turn (faster by fire); Acid eats
/// armor, and with none left a token that burns at the end of their turn; Shine and Poison make them
/// easier to hit; Blind takes a point off their attacks and keeps them from seeing past the tiles beside
/// them until an Od Hand Tend (10 + their tokens) or Eye Drops cure it.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Taken off every roll they make: a point for each Poison token.</summary>
    private static int RollPenalty(Conditions conditions) => conditions[Condition.Poison];

    /// <summary>Taken off their attacks: their poison, and a point for each Blind token.</summary>
    private static int AttackPenalty(Conditions conditions) => conditions[Condition.Poison] + conditions[Condition.Blind];

    /// <summary>Added to every attack on them: their poison (it weakens their defense) and their glow.</summary>
    private static int Exposure(Conditions conditions) => conditions[Condition.Poison] + conditions[Condition.Shine];

    /// <summary>Wend dice their Freeze takes: none for the first stack (it only slows them), one for each after.</summary>
    private static int WendFrozen(Conditions conditions) => Math.Max(0, conditions[Condition.Freeze] - 1);

    /// <summary>Frozen solid: Freeze has taken every Wend die they have.</summary>
    private static bool IsFrozenSolid(Conditions conditions, int wendDice) =>
        conditions.Has(Condition.Freeze) && WendFrozen(conditions) >= Math.Max(1, wendDice);

    /// <summary>Whether a hero is frozen solid now.</summary>
    public static bool IsFrozenSolid(Hero hero) => IsFrozenSolid(hero.Conditions, hero.EffectiveStats.MoveDice);

    /// <summary>Whether a monster is frozen solid now.</summary>
    public static bool IsFrozenSolid(Monster monster) => IsFrozenSolid(monster.Conditions, monster.Definition.WendDice);

    /// <summary>Each step a hero takes costs this many: double lying down, double again chilled by Freeze.</summary>
    private static int CrawlOf(Hero hero) => (hero.Prone ? GameRules.CrawlFactor : 1) * (hero.Conditions.Has(Condition.Freeze) ? 2 : 1);

    /// <summary>Blind: whoever it is sees only the tiles beside them.</summary>
    private static bool IsBlind(Conditions conditions) => conditions.Has(Condition.Blind);

    /// <summary>Notes the hero who laid a token on a monster: they get the kill if it finishes it off.</summary>
    private static void Afflict(Monster monster, Hero? by)
    {
        if (by is not null)
        {
            monster.AfflictedBy = by.Id;
        }
    }

    /// <summary>The hero whose tokens are on a monster, while they're still in the dungeon.</summary>
    private Hero? AfflictorOf(Monster monster) =>
        monster.AfflictedBy is int heroId && State.GetHero(heroId) is { IsInPlay: true } hero ? hero : null;

    /// <summary>A hero's most worn card, equipped first (what an Autosmith Hammer mends), or null.</summary>
    private static CardInstance? MostWorn(Hero hero) =>
        hero.EquippedCards.Concat(hero.Belt).Concat(hero.Backpack).Where(card => card.Wear > 0).OrderByDescending(card => card.Wear).FirstOrDefault();

    /// <summary>
    /// Acid on a hero eats their gear: a point off the equipped piece giving the most armor for each 6,
    /// and it stays eaten until mended. With no armor left, an Acid token instead.
    /// </summary>
    private static void Dissolve(Hero hero, int stacks, int byId, List<GameEvent> events)
    {
        for (int i = 0; i < stacks; i++)
        {
            if (hero.EquippedCards.Where(card => card.Modifiers.Armor > 0).OrderByDescending(card => card.Modifiers.Armor).FirstOrDefault() is { } armor)
            {
                armor.Wear++;
                events.Add(new GearCorroded(hero.Id, armor.Id, armor.Wear, armor.Modifiers.Armor));
            }
            else
            {
                AddCondition(hero.Conditions, hero.Id, Condition.Acid, 1, byId, events);
            }
        }
    }

    /// <summary>Acid: a point of armor eaten away for each 6, or with no armor left, an Acid token.</summary>
    private static void Dissolve(Conditions conditions, int entityId, int armor, int stacks, int byId, List<GameEvent> events)
    {
        for (int i = 0; i < stacks; i++)
        {
            bool armored = armor - conditions[Condition.Corroded] > 0;
            AddCondition(conditions, entityId, armored ? Condition.Corroded : Condition.Acid, 1, byId, events);
        }
    }

    /// <summary>Fire dice that attack someone frozen melt a stack each, there and then.</summary>
    private static void Thaw(Conditions conditions, int entityId, int fireDice, List<GameEvent> events)
    {
        if (fireDice > 0 && conditions.Has(Condition.Freeze))
        {
            AddCondition(conditions, entityId, Condition.Freeze, -fireDice, 0, events);
        }
    }

    /// <summary>Fire beside someone (or under them) as their turn starts: burning creatures, burning floor, lit torches in hand.</summary>
    private int FiresBeside(Room room, int entityId, IEnumerable<GridPosition> tiles)
    {
        List<GridPosition> near = [.. tiles];
        bool Beside(GridPosition tile) => near.Any(own => own.ChebyshevDistanceTo(tile) <= 1);

        int burningFloor = room.Positions.Count(tile => room.IsBurning(tile) && Beside(tile));
        int burning = room.Monsters.Count(monster => monster.Id != entityId && monster.FireDice > 0 && monster.Tiles.Any(Beside))
                      + State.HeroesIn(room).Count(hero => hero.Id != entityId && hero.FireDice > 0 && Beside(hero.Position));
        int torches = State.HeroesIn(room).Where(hero => Beside(hero.Position)).Sum(hero => hero.TorchesHeld);
        return burningFloor + burning + torches;
    }

    /// <summary>
    /// The start of a hero's turn, after any burning: fire beside them melts their Freeze; then frozen
    /// solid they can do nothing, and chilled they lose a Wend die for each stack after the first.
    /// Returns false when that ends the turn.
    /// </summary>
    private bool ConditionsAtTurnStart(Hero hero, List<GameEvent> events)
    {
        Room room = State.RoomOf(hero);
        if (hero.Conditions.Has(Condition.Freeze) && FiresBeside(room, hero.Id, [hero.Position]) is > 0 and var fires)
        {
            AddCondition(hero.Conditions, hero.Id, Condition.Freeze, -fires, 0, events);
        }

        if (IsFrozenSolid(hero))
        {
            events.Add(new FrozenSolid(hero.Id));
            return false;
        }

        Turn.MoveDiceUsed += WendFrozen(hero.Conditions);
        if (hero.IsActive && hero.Conditions.Has(Condition.Confused))
        {
            Stagger(room, hero, events);
            return false;
        }

        return true;
    }

    /// <summary>The start of a monster's turn, after any burning: the same for it. Returns false when it can't act.</summary>
    private bool ConditionsAtTurnStart(Room room, Monster monster, List<GameEvent> events)
    {
        if (monster.Conditions.Has(Condition.Freeze) && FiresBeside(room, monster.Id, monster.Tiles) is > 0 and var fires)
        {
            AddCondition(monster.Conditions, monster.Id, Condition.Freeze, -fires, 0, events);
        }

        if (IsFrozenSolid(monster))
        {
            events.Add(new FrozenSolid(monster.Id));
            return false;
        }

        if (monster.Conditions.Has(Condition.Freeze))
        {
            monster.MovesThisTurn = Math.Max(0, monster.MovesThisTurn / 2 - 2 * WendFrozen(monster.Conditions));
        }

        monster.MovesThisTurn = Math.Max(0, monster.MovesThisTurn - RollPenalty(monster.Conditions));
        return true;
    }

    /// <summary>
    /// The end of a hero's turn: poison is rolled against, acid burns, and a stack of Freeze melts, a
    /// Shine fades and a Confuse wears off.
    /// </summary>
    private void ConditionsAtTurnEnd(Hero hero, List<GameEvent> events)
    {
        if (!hero.IsInPlay)
        {
            return;
        }

        Room room = State.RoomOf(hero);
        if (hero.IsActive && hero.Conditions[Condition.Poison] is > 0 and var poison)
        {
            int face = RollOneDie();
            bool hurt = face <= poison;
            if (face == GameRules.PowerDieFace)
            {
                hero.Conditions.Add(Condition.Poison, -1);
            }

            events.Add(new PoisonRolled(hero.Id, face, hurt, hero.Conditions[Condition.Poison]));
            if (hurt)
            {
                LoseHeart(room, hero, "Poisoned", events);
            }
        }

        if (hero.IsActive && hero.Conditions[Condition.Acid] is > 0 and var acid)
        {
            hero.Conditions.Clear(Condition.Acid);
            events.Add(new ConditionChanged(hero.Id, Condition.Acid, 0, 0));
            (int[] faces, _) = RollBonusDice(acid);
            var roll = new DiceRoll(faces);
            events.Add(new AcidBurned(hero.Id, roll));
            if (DamageHero(room, hero, roll.Total, "Dissolved by acid", 0, events) > 0)
            {
                LayTypedDice(room, hero, TypedSixes(roll, [.. faces.Select(_ => DieType.Acid)]), 0, hero.Position, events);
            }
        }

        WearOff(hero.Conditions, hero.Id, events);
    }

    /// <summary>The end of a monster's turn: the same for it. A hero whose tokens finish it off gets the kill.</summary>
    private void ConditionsAtTurnEnd(Room room, Monster monster, List<GameEvent> events)
    {
        if (room.FindMonster(monster.Id) is null || State.Phase != RunPhase.InProgress)
        {
            return;
        }

        if (monster.Conditions[Condition.Poison] is > 0 and var poison)
        {
            int face = RollOneDie();
            bool hurt = face <= poison;
            if (face == GameRules.PowerDieFace)
            {
                monster.Conditions.Add(Condition.Poison, -1);
            }

            events.Add(new PoisonRolled(monster.Id, face, hurt, monster.Conditions[Condition.Poison]));
            if (hurt)
            {
                monster.Hearts = Math.Max(0, monster.Hearts - 1);
                events.Add(new Damaged(monster.Id, 1, monster.Hearts));
                if (monster.Hearts == 0)
                {
                    DefeatMonster(AfflictorOf(monster), room, monster, DamageSource.Fire, events);
                    return;
                }
            }
        }

        if (monster.Conditions[Condition.Acid] is > 0 and var acid)
        {
            monster.Conditions.Clear(Condition.Acid);
            events.Add(new ConditionChanged(monster.Id, Condition.Acid, 0, 0));
            (int[] faces, _) = RollBonusDice(acid);
            var roll = new DiceRoll(faces);
            events.Add(new AcidBurned(monster.Id, roll));
            Hero? credit = AfflictorOf(monster);
            if (DamageMonster(credit, room, monster, roll.Total, DamageSource.Fire, events) > 0)
            {
                LayTypedDice(room, monster, TypedSixes(roll, [.. faces.Select(_ => DieType.Acid)]), 0, monster.Position, credit, events);
            }

            if (room.FindMonster(monster.Id) is null)
            {
                return;
            }
        }

        WearOff(monster.Conditions, monster.Id, events);
    }

    /// <summary>What fades by itself at the end of a turn: a stack of Freeze, a Shine and a Confuse.</summary>
    private static void WearOff(Conditions conditions, int entityId, List<GameEvent> events)
    {
        foreach (Condition fading in new[] { Condition.Freeze, Condition.Shine, Condition.Confused })
        {
            AddCondition(conditions, entityId, fading, -1, 0, events);
        }
    }

    /// <summary>A heart lost to poison, with no defense: the last one leaves them Keeling Over.</summary>
    private void LoseHeart(Room room, Hero hero, string bane, List<GameEvent> events)
    {
        hero.Hearts = Math.Max(0, hero.Hearts - 1);
        events.Add(new Damaged(hero.Id, 1, hero.Hearts));
        if (hero.Hearts == 0)
        {
            KeelOver(room, hero, bane, events);
        }
    }

    /// <summary>Why the active hero can't tend <paramref name="targetId"/>'s eyes (themselves, or a blind ally beside them), or null.</summary>
    public string? CheckTend(int targetId) => CheckOdHandStart() ?? FindBlind(targetId, out _);

    private string? Handle(Tend command, List<GameEvent> events)
    {
        OdHandCheck? check = null;
        if ((CheckOdHandCommand(command, out int dice) ?? FindBlind(command.TargetId, out check)) is { } error)
        {
            return error;
        }

        RollOdHand(command, dice, check!, events);
        return null;
    }

    private string? FindBlind(int targetId, out OdHandCheck? check)
    {
        check = null;
        Hero hero = ActiveHero;
        if (State.HeroesIn(ActiveRoom).FirstOrDefault(other => other.Id == targetId && other.IsActive) is not { } patient)
        {
            return "There's nobody there to tend.";
        }

        if (!IsBlind(patient.Conditions))
        {
            return $"{(patient == hero ? "You aren't" : $"{patient.Name} isn't")} blind.";
        }

        if (patient.Position.ChebyshevDistanceTo(hero.Position) > 1)
        {
            return $"Stand next to {patient.Name} to tend their eyes.";
        }

        check = new OdHandCheck(OdHandCheckKind.Tend, GameRules.TendDifficulty + patient.Conditions[Condition.Blind], patient.Position, patient.Id);
        return null;
    }

    /// <summary>A Tend that reaches its difficulty clears the patient's eyes.</summary>
    private static void ResolveTend(Hero hero, Hero patient, bool success, List<GameEvent> events)
    {
        if (success)
        {
            patient.Conditions.Clear(Condition.Blind);
            events.Add(new ConditionChanged(patient.Id, Condition.Blind, 0, hero.Id));
        }
    }

    /// <summary>A cure (Eye Drops, Holy Water) clears what it cures from whoever uses it.</summary>
    private static void Cure(Hero hero, CardEffect effect, List<GameEvent> events)
    {
        if (effect.Cures is { } condition && hero.Conditions.Has(condition))
        {
            hero.Conditions.Clear(condition);
            events.Add(new ConditionChanged(hero.Id, condition, 0, hero.Id));
        }
    }

    /// <summary>
    /// Confused, a hero lashes out with every Rumble die at someone beside them (anyone, friend or foe),
    /// then staggers off in a random direction as far as their Wend takes them; that's their turn.
    /// </summary>
    private void Stagger(Room room, Hero hero, List<GameEvent> events)
    {
        List<int> beside = [.. room.Monsters.Where(monster => monster.NearestTile(hero.Position).ChebyshevDistanceTo(hero.Position) == 1).Select(monster => monster.Id),
            .. State.ActiveHeroesIn(room).Where(other => other != hero && other.Position.ChebyshevDistanceTo(hero.Position) == 1).Select(other => other.Id)];
        events.Add(new Staggered(hero.Id));
        if (beside.Count > 0 && AttackDiceLeft > 0)
        {
            int victimId = _random.Behavior.Pick(beside.Order().ToList());
            DiceRoll roll = _dice.Roll(AttackDiceLeft);
            Turn.AttackDiceUsed += roll.Faces.Count;
            int bonus = -AttackPenalty(hero.Conditions);
            events.Add(new AttackRolled(hero.Id, roll, bonus, roll.Total + bonus, [victimId]));
            if (room.FindMonster(victimId) is { } monster)
            {
                DamageMonster(hero, room, monster, roll.Total + bonus, DamageSource.Weapon, events);
            }
            else
            {
                DamageHero(room, State.GetHero(victimId), roll.Total + bonus, $"Struck down by a confused {hero.Name}", hero.Id, events);
            }
        }

        if (!hero.IsActive || State.Phase != RunPhase.InProgress || MoveDiceLeft < 1)
        {
            return;
        }

        int dice = MoveDiceLeft;
        Turn.MoveDiceUsed += dice;
        int steps = (hero.FixedWendFace is int face ? face * dice : _dice.Roll(dice).Total) - RollPenalty(hero.Conditions);
        GridPosition direction = _random.Behavior.Pick(new GridPosition(0, 0).SurroundingTiles().ToList());
        List<GridPosition> path = [];
        GridPosition at = hero.Position;
        while (true)
        {
            var next = new GridPosition(at.X + direction.X, at.Y + direction.Y);
            int cost = room.Layout.Contains(next) && room.Layout[next].IsWalkable() && !IsOccupied(room, next) ? HeroStepCost(hero, room, next) * CrawlOf(hero) : int.MaxValue;
            if (cost > steps)
            {
                break;
            }

            steps -= cost;
            path.Add(next);
            at = next;
            if (room.TrapAt(next) is not null)
            {
                break;
            }
        }

        if (path.Count == 0)
        {
            return;
        }

        hero.Position = at;
        events.Add(new HeroMoved(hero.Id, path, 0));
        if (room.TrapAt(at) is { } trap)
        {
            SpringTrap(hero, room, trap, at, events);
        }
    }

    /// <summary>Confused, a monster does the same with its Attack dice and its moves.</summary>
    private void Stagger(Room room, Monster monster, List<GameEvent> events)
    {
        List<int> beside = [.. room.Monsters.Where(other => other != monster && other.Tiles.Any(tile => monster.Tiles.Any(own => own.ChebyshevDistanceTo(tile) == 1))).Select(other => other.Id),
            .. State.ActiveHeroesIn(room).Where(hero => monster.NearestTile(hero.Position).ChebyshevDistanceTo(hero.Position) == 1).Select(hero => hero.Id)];
        events.Add(new Staggered(monster.Id));
        if (beside.Count > 0 && monster.AttackDiceThisTurn > 0)
        {
            int victimId = _random.Behavior.Pick(beside.Order().ToList());
            (DiceRoll roll, IReadOnlyList<DieType> types, int powerDice, int banked) = RollMonsterDice(monster, monster.AttackDiceThisTurn);
            int bonus = monster.Definition.Weapon - AttackPenalty(monster.Conditions);
            events.Add(new AttackRolled(monster.Id, roll, bonus, roll.Total + bonus, [victimId], powerDice) { DieTypes = types });
            NoteBanked(monster.Id, monster.Power, banked, events);
            if (room.FindMonster(victimId) is { } other)
            {
                DamageMonster(null, room, other, roll.Total + bonus, DamageSource.Monster, events);
            }
            else
            {
                Hero hero = State.GetHero(victimId);
                if (DamageHero(room, hero, roll.Total + bonus, MonsterBane(monster), monster.Id, events) > 0)
                {
                    LayTypedDice(room, hero, TypedSixes(roll, types), monster.Id, monster.NearestTile(hero.Position), events);
                }
            }
        }

        if (room.FindMonster(monster.Id) is null || State.Phase != RunPhase.InProgress)
        {
            return;
        }

        GridPosition direction = _random.Behavior.Pick(new GridPosition(0, 0).SurroundingTiles().ToList());
        List<GridPosition> path = [];
        GridPosition at = monster.Position;
        for (int moves = monster.MovesThisTurn; ;)
        {
            var next = new GridPosition(at.X + direction.X, at.Y + direction.Y);
            if (!room.Layout.Contains(next) || !CanMonsterEnter(room, monster, next) || StepCost(room, monster, next) > moves)
            {
                break;
            }

            moves -= StepCost(room, monster, next);
            path.Add(next);
            at = next;
        }

        if (path.Count > 0)
        {
            monster.Position = at;
            events.Add(new MonsterMoved(monster.Id, path));
        }
    }
}
