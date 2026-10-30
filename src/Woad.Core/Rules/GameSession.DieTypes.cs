using Woad.Core.Commands;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Typed dice (see <see cref="DieType"/>): fire, ice, poison and the rest. They earn no Power Dice;
/// each 6 rolls another die of its type straight in, and if the attack hits, lays a stack of its
/// type's effect on the target. Spells, weapons, torches and monsters all roll them.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>
    /// Rolls typed dice, a type at a time: each 6 rolls another of its type straight in. Star dice
    /// roll just once (their sixes surge with Power instead: see <see cref="SurgePowerDice"/>).
    /// </summary>
    private (int[] Faces, DieType[] Types) RollTypedDice(IReadOnlyList<(DieType Type, int Dice)> dice)
    {
        List<int> faces = [];
        List<DieType> types = [];
        foreach ((DieType type, int count) in dice.Where(typed => typed.Dice > 0))
        {
            int[] rolled = type is DieType.Plain or DieType.Star ? [.. _dice.Roll(count).Faces] : RollBonusDice(count).Faces;
            faces.AddRange(rolled);
            types.AddRange(rolled.Select(_ => type));
        }

        return ([.. faces], [.. types]);
    }

    /// <summary>The 6s of each type among a roll's typed dice: a stack of its effect for each.</summary>
    private static Dictionary<DieType, int> TypedSixes(DiceRoll roll, IReadOnlyList<DieType> types)
    {
        var sixes = new Dictionary<DieType, int>();
        for (int i = 0; i < Math.Min(roll.Faces.Count, types.Count); i++)
        {
            if (types[i] is not (DieType.Plain or DieType.Star) && roll.Faces[i] == GameRules.PowerDieFace)
            {
                sixes[types[i]] = sixes.GetValueOrDefault(types[i]) + 1;
            }
        }

        return sixes;
    }

    /// <summary>The 6s among a roll's Star dice.</summary>
    private static int StarSixes(DiceRoll roll, IReadOnlyList<DieType> types) =>
        Enumerable.Range(0, Math.Min(roll.Faces.Count, types.Count)).Count(i => types[i] == DieType.Star && roll.Faces[i] == GameRules.PowerDieFace);

    /// <summary>Changes a token count on a hero or monster, and says so.</summary>
    private static void AddCondition(Conditions conditions, int entityId, Condition condition, int change, int byId, List<GameEvent> events)
    {
        int before = conditions[condition];
        int count = conditions.Add(condition, change);
        if (count != before)
        {
            events.Add(new ConditionChanged(entityId, condition, count, byId));
        }
    }

    /// <summary>
    /// A hit's typed sixes take hold of a monster, a stack of each type's effect for each 6.
    /// <paramref name="from"/> is where the blow came from (a gust blows them away from it).
    /// </summary>
    /// <param name="hero">The hero who struck, who gets the credit; null for a monster's blow.</param>
    private void LayTypedDice(Room room, Monster target, IReadOnlyDictionary<DieType, int> sixes, int byId, GridPosition from, Hero? hero, List<GameEvent> events)
    {
        if (sixes.Count > 0)
        {
            Afflict(target, hero);
        }

        foreach ((DieType type, int stacks) in sixes.OrderBy(entry => entry.Key))
        {
            if (room.FindMonster(target.Id) is null || State.Phase != RunPhase.InProgress)
            {
                return;
            }

            switch (type)
            {
                case DieType.Fire:
                    for (int i = 0; i < stacks; i++)
                    {
                        Ignite(room, target, byId, hero?.Id, events);
                    }

                    break;

                case DieType.Acid:
                    Dissolve(target.Conditions, target.Id, target.Definition.Armor, stacks, byId, events);
                    break;

                case DieType.Poison or DieType.Ice or DieType.Radiate or DieType.Shadow or DieType.Psycho:
                    AddCondition(target.Conditions, target.Id, TokenOf(type), stacks, byId, events);
                    break;

                case DieType.Necro:
                    Necrosis(target.Conditions, target.Id, target.MaxHearts - target.Hearts, stacks, byId, events);
                    break;

                case DieType.Life:
                    Grow(room, target.Position, stacks, hero, events);
                    break;

                case DieType.Squall:
                    Gust(room, target, from, stacks, hero, events);
                    break;

                case DieType.Earth:
                    Quake(target, stacks, byId, events);
                    break;

                case DieType.Elektrik:
                    Stun(room, target, powerful: false, stacks, hero, events);
                    break;
            }
        }
    }

    /// <summary>The token a type of die leaves, for those that only leave one.</summary>
    private static Condition TokenOf(DieType type) => type switch
    {
        DieType.Poison => Condition.Poison,
        DieType.Ice => Condition.Freeze,
        DieType.Radiate => Condition.Shine,
        DieType.Shadow => Condition.Blind,
        DieType.Psycho => Condition.Confused,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "It leaves no single token."),
    };

    /// <summary>A hero's Necro hit that killed a monster: it rises at their side.</summary>
    private void NecroKill(Room room, Monster dead, IReadOnlyDictionary<DieType, int> sixes, Hero killer, List<GameEvent> events)
    {
        if (sixes.ContainsKey(DieType.Necro) && room.FindMonster(dead.Id) is null)
        {
            RiseForHero(room, dead, killer, events);
        }
    }

    /// <summary>A hit's typed sixes take hold of a hero (see the monster's <see cref="LayTypedDice(Room, Monster, IReadOnlyDictionary{DieType, int}, int, GridPosition, Hero?, List{GameEvent})"/>).</summary>
    private void LayTypedDice(Room room, Hero target, IReadOnlyDictionary<DieType, int> sixes, int byId, GridPosition from, List<GameEvent> events)
    {
        foreach ((DieType type, int stacks) in sixes.OrderBy(entry => entry.Key))
        {
            if (!target.IsInPlay || State.RoomOf(target) != room || State.Phase != RunPhase.InProgress)
            {
                return;
            }

            switch (type)
            {
                case DieType.Fire:
                    for (int i = 0; i < stacks; i++)
                    {
                        Ignite(target, byId, events);
                    }

                    break;

                case DieType.Acid:
                    Dissolve(target, stacks, byId, events);
                    break;

                case DieType.Poison or DieType.Ice or DieType.Radiate or DieType.Shadow or DieType.Psycho:
                    AddCondition(target.Conditions, target.Id, TokenOf(type), stacks, byId, events);
                    break;

                case DieType.Necro:
                    Necrosis(target.Conditions, target.Id, target.MaxHearts - target.Hearts, stacks, byId, events);
                    if (target.IsKeelingOver && target.Hearts == 0 && target.KeelRolls == 0 && target.DeathMarks == 0)
                    {
                        NecroDeath(room, target, fromMonster: room.FindMonster(byId) is not null, events);
                        return;
                    }

                    break;

                case DieType.Life:
                    Grow(room, target.Position, stacks, State.Heroes.FirstOrDefault(hero => hero.Id == byId), events);
                    break;

                case DieType.Squall:
                    Gust(room, target, from, stacks, byId, events);
                    break;

                case DieType.Earth:
                    Quake(room, target, stacks, byId, events);
                    break;

                case DieType.Elektrik:
                    Stun(target, powerful: false, stacks, events);
                    break;
            }
        }
    }

    /// <summary>
    /// Squall: a tile straight away from <paramref name="from"/> for each 6. For every tile something
    /// stops them (a wall, or someone in the way, who's struck too) a die hits them. Bosses and huge
    /// monsters are harder to shift: a tile for every two 6s.
    /// </summary>
    private void Gust(Room room, Monster monster, GridPosition from, int tiles, Hero? by, List<GameEvent> events)
    {
        if (monster.Definition is { Boss: true } or { Miniboss: true } || monster.Definition.Size > 1)
        {
            HeaveAside(room, monster, from, tiles / 2, by, events);
            return;
        }

        for (int pushed = 0; pushed < tiles; pushed++)
        {
            GridPosition at = monster.Position;
            if (PushDestination(room, from, at, monster.Flies) is not { } to)
            {
                Slam(room, monster.Id, Beyond(from, at), tiles - pushed, by, events);
                return;
            }

            PushMonster(by, room, monster, to, events);
            if (room.FindMonster(monster.Id) is null || monster.Position != to)
            {
                return;
            }
        }
    }

    private void Gust(Room room, Hero hero, GridPosition from, int tiles, int byId, List<GameEvent> events)
    {
        for (int pushed = 0; pushed < tiles; pushed++)
        {
            GridPosition at = hero.Position;
            if (PushDestination(room, from, at, flying: false) is not { } to)
            {
                Slam(room, hero.Id, Beyond(from, at), tiles - pushed, null, events, byId);
                return;
            }

            events.Add(new Shoved(byId, hero.Id, at, to));
            PushHero(null, room, hero, to, events, announce: false);
            if (!hero.IsActive || State.RoomOf(hero) != room || hero.Position != to)
            {
                return;
            }
        }
    }

    /// <summary>
    /// A gust shifting a boss or a huge monster, its whole footprint a tile at a time straight away from
    /// <paramref name="from"/>, onto ground it could stand on. Nothing drops it into a pit or out of its water.
    /// </summary>
    private void HeaveAside(Room room, Monster monster, GridPosition from, int tiles, Hero? by, List<GameEvent> events)
    {
        GridPosition near = monster.NearestTile(from);
        int dx = Math.Sign(near.X - from.X), dy = Math.Sign(near.Y - from.Y);
        if (tiles < 1 || (dx, dy) == (0, 0))
        {
            return;
        }

        for (int pushed = 0; pushed < tiles; pushed++)
        {
            GridPosition at = monster.Position;
            var to = new GridPosition(at.X + dx, at.Y + dy);
            List<GridPosition> footprint = [.. FootprintAt(monster, to)];
            GridPosition? blocker = footprint.Where(tile => room.Layout.Contains(tile) && OtherThan(room, monster, tile)).Select(tile => (GridPosition?)tile).FirstOrDefault();
            if (blocker is not null || !footprint.All(tile => room.Layout.Contains(tile) && CanMonsterStand(room.Layout[tile], monster)))
            {
                Slam(room, monster.Id, blocker ?? to, tiles - pushed, by, events);
                return;
            }

            monster.Position = to;
            events.Add(new Shoved(by?.Id ?? 0, monster.Id, at, to));
        }
    }

    /// <summary>Someone other than <paramref name="monster"/> stands on the tile.</summary>
    private bool OtherThan(Room room, Monster monster, GridPosition tile) =>
        room.MonsterAt(tile) is { } other ? other != monster : room.MerchantAt(tile) is not null || State.HeroesIn(room).Any(hero => hero.Position == tile);

    /// <summary>The next tile on from <paramref name="at"/>, straight away from <paramref name="from"/>.</summary>
    private static GridPosition Beyond(GridPosition from, GridPosition at) =>
        new(at.X + Math.Sign(at.X - from.X), at.Y + Math.Sign(at.Y - from.Y));

    /// <summary>A gust slams someone into a wall or into whoever stands at <paramref name="into"/>: a die each tile it was stopped, at both of them.</summary>
    private void Slam(Room room, int entityId, GridPosition into, int dice, Hero? by, List<GameEvent> events, int byId = 0)
    {
        Monster? monsterInWay = room.MonsterAt(into) is { } found && found.Id != entityId ? found : null;
        Hero? heroInWay = monsterInWay is null ? State.ActiveHeroesIn(room).FirstOrDefault(other => other.Position == into) : null;
        DiceRoll roll = _dice.Roll(dice);
        events.Add(new Slammed(entityId, monsterInWay?.Id ?? heroInWay?.Id, roll));
        foreach (int id in new[] { entityId, monsterInWay?.Id ?? heroInWay?.Id ?? 0 }.Where(id => id != 0))
        {
            if (room.FindMonster(id) is { } monster)
            {
                DamageMonster(by, room, monster, roll.Total, DamageSource.Fall, events);
            }
            else if (State.ActiveHeroesIn(room).FirstOrDefault(hero => hero.Id == id) is { } hero)
            {
                DamageHero(room, hero, roll.Total, "Dashed against the stones", by?.Id ?? byId, events);
            }
        }
    }

    /// <summary>Earth: the first 6 knocks them prone; on someone prone already, each buries them deeper.</summary>
    private static void Quake(Monster monster, int stacks, int byId, List<GameEvent> events)
    {
        if (!monster.Prone)
        {
            monster.Prone = true;
            events.Add(new WentProne(monster.Id, Diving: false));
            stacks--;
        }

        if (stacks > 0)
        {
            AddCondition(monster.Conditions, monster.Id, Condition.Buried, stacks, byId, events);
        }
    }

    private static void Quake(Room room, Hero hero, int stacks, int byId, List<GameEvent> events)
    {
        if (!hero.Prone)
        {
            hero.Prone = true;
            events.Add(new WentProne(hero.Id, Diving(room, hero)));
            stacks--;
        }

        if (stacks > 0)
        {
            AddCondition(hero.Conditions, hero.Id, Condition.Buried, stacks, byId, events);
        }
    }

    /// <summary>Rumble totalling 3 digs one token of earth off; with none left they're only prone.</summary>
    private static int DugThrough(Conditions conditions, int total) => Math.Min(conditions[Condition.Buried], total / GameRules.DigPerToken);

    /// <summary>Why the active hero can't dig themselves out now, or null.</summary>
    public string? CheckDigOut()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0, KeelingOver: false })
        {
            return "Finish the roll first.";
        }

        if (!ActiveHero.Conditions.Has(Condition.Buried))
        {
            return "You're not buried.";
        }

        return AttackDiceLeft < 1 ? "You've no Rumble left to dig with this turn." : null;
    }

    private string? Handle(DigOut command, List<GameEvent> events)
    {
        if (CheckDigOut() is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        int dice = AttackDiceLeft;
        Turn.AttackDiceUsed += dice;
        // A Shovel in hand digs with a die more.
        DiceRoll roll = _dice.Roll(dice + (hero.HoldsShovel ? 1 : 0));
        int dug = DugThrough(hero.Conditions, roll.Total);
        hero.Conditions.Add(Condition.Buried, -dug);
        events.Add(new DugOut(hero.Id, roll, hero.Conditions[Condition.Buried]));
        if (dug > 0)
        {
            events.Add(new ConditionChanged(hero.Id, Condition.Buried, hero.Conditions[Condition.Buried], hero.Id));
        }

        return null;
    }

    /// <summary>A buried monster spends its turn digging with its Attack dice. True if it was buried (its turn is spent).</summary>
    private bool DigOutMonster(Monster monster, List<GameEvent> events)
    {
        if (!monster.Conditions.Has(Condition.Buried))
        {
            return false;
        }

        DiceRoll roll = _dice.Roll(Math.Max(1, monster.AttackDiceThisTurn));
        int dug = DugThrough(monster.Conditions, roll.Total);
        monster.Conditions.Add(Condition.Buried, -dug);
        events.Add(new DugOut(monster.Id, roll, monster.Conditions[Condition.Buried]));
        if (dug > 0)
        {
            events.Add(new ConditionChanged(monster.Id, Condition.Buried, monster.Conditions[Condition.Buried], monster.Id));
        }

        return true;
    }

    /// <summary>What a buried hero can't do: anything that needs them up and about.</summary>
    private static bool NeedsToBeUnburied(GameCommand command) =>
        command is RollMovement or MoveHero or Leap or GoThroughDoor or AttackMonster or AttackAlly or AttackMerchant or AttackBomb or Shove or JumpDown or TakeStairs or Escape or GetUp;
}
