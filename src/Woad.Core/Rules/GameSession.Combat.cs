using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dice;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

public sealed partial class GameSession
{
    /// <summary>Monsters the active hero could attack with their weapon right now.</summary>
    public IReadOnlyList<Monster> GetAttackableMonsters()
    {
        if (State.Turn is not { PendingRoll: null } || AttackDiceLeft < 1)
        {
            return [];
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        return room.Monsters.Where(monster => CanHeroAttack(hero, room, monster)).ToList();
    }

    private string? Handle(AttackMonster command, List<GameEvent> events)
    {
        int left = AttackDiceLeft;
        if (left < 1)
        {
            return "You've used all your Attack dice this turn.";
        }

        int dice = command.Dice ?? left;
        if (dice < 1 || dice > left)
        {
            return $"Choose 1 to {left} Attack dice.";
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (room.FindMonster(command.MonsterId) is not { } monster)
        {
            return "There's no such monster in this room.";
        }

        if (!CanHeroAttack(hero, room, monster))
        {
            return "That monster is out of reach.";
        }

        if (CheckBankedPowerDice(hero, command.PowerDice) is { } powerError)
        {
            return powerError;
        }

        (int weaponBonus, bool stunOnSix) = WeaponFor(hero, room, monster.Position);
        if (monster.Definition.Swimmer)
        {
            weaponBonus += hero.MainWeapon?.Definition.SwimmerBonus ?? 0;
        }

        int bonus = command.UseWeapon && !Turn.WeaponUsed ? weaponBonus : 0;
        Turn.AttackDiceUsed += dice;
        Turn.WeaponUsed |= bonus > 0;
        SpendBankedPowerDice(hero, command.PowerDice, events);
        RollHeroAttack(hero, dice + ProneBonus(monster, hero.Position), bonus, [monster.Id], DamageSource.Weapon, events, command.PowerDice, stunOnSix: stunOnSix);
        return null;
    }

    /// <summary>With friendly fire on, the other heroes and companions the active hero could attack right now.</summary>
    public IReadOnlyList<Hero> GetAttackableAllies()
    {
        if (!State.FriendlyFire || State.Turn is not { PendingRoll: null } || AttackDiceLeft < 1)
        {
            return [];
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        return State.ActiveHeroesIn(room)
            .Where(ally => ally != hero && Targeting.CanHeroAttack(room.Layout, hero.AttackRange, hero.Position, ally.Position))
            .Where(ally => !Diving(room, ally) || ally.Position.ChebyshevDistanceTo(hero.Position) <= 1)
            .ToList();
    }

    private string? Handle(AttackAlly command, List<GameEvent> events)
    {
        if (!State.FriendlyFire)
        {
            return "Friendly fire is off: you can't attack your allies.";
        }

        int left = AttackDiceLeft;
        int dice = command.Dice ?? left;
        if (left < 1 || dice < 1 || dice > left)
        {
            return left < 1 ? "You've used all your Rumble dice this turn." : $"Choose 1 to {left} Rumble dice.";
        }

        Hero hero = ActiveHero;
        if (GetAttackableAllies().FirstOrDefault(ally => ally.Id == command.TargetId) is not { } target)
        {
            return "They're out of reach.";
        }

        if (CheckBankedPowerDice(hero, command.PowerDice) is { } powerError)
        {
            return powerError;
        }

        (int weaponBonus, bool stunOnSix) = WeaponFor(hero, ActiveRoom, target.Position);
        int bonus = command.UseWeapon && !Turn.WeaponUsed ? weaponBonus : 0;
        Turn.AttackDiceUsed += dice;
        Turn.WeaponUsed |= bonus > 0;
        SpendBankedPowerDice(hero, command.PowerDice, events);
        RollHeroAttack(hero, dice + ProneBonus(target, hero.Position), bonus, [target.Id], DamageSource.Weapon, events, command.PowerDice, stunOnSix: stunOnSix);
        return null;
    }

    private string? Handle(RerollDie command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        if (hero.LuckRemaining < 1)
        {
            return "You have no Luck left this floor.";
        }

        PendingRoll? attack = Turn.PendingRoll;
        DiceRoll? roll = attack?.Roll ?? (Turn.HasMoved ? null : Turn.MovementRoll);
        if (roll is null)
        {
            return "There's no roll to reroll.";
        }

        if (command.DieIndex < 0 || command.DieIndex >= roll.Faces.Count)
        {
            return "There's no such die.";
        }

        int[] faces = [.. roll.Faces];
        int oldFace = faces[command.DieIndex];
        faces[command.DieIndex] = RollOneDie();
        var rerolled = new DiceRoll(faces);
        hero.LuckRemaining--;

        if (attack is not null)
        {
            attack.Roll = rerolled;
            events.Add(new DieRerolled(hero.Id, command.DieIndex, oldFace, faces[command.DieIndex], rerolled, null));
        }
        else
        {
            Turn.MovementRoll = rerolled;
            Turn.CountSteps();
            events.Add(new DieRerolled(hero.Id, command.DieIndex, oldFace, faces[command.DieIndex], rerolled, Turn.MovementPoints));
        }

        // A reroll that comes up 6 earns a Power Die; spending the last Luck resolves the attack.
        EarnPowerDice(hero, faces[command.DieIndex] == GameRules.PowerDieFace ? 1 : 0, events);
        return null;
    }

    private string? Handle(ConfirmAttack command, List<GameEvent> events)
    {
        if (Turn.PendingRoll is not { } pending)
        {
            return "There's no roll waiting to be confirmed.";
        }

        Turn.PendingRoll = null;
        ResolvePendingRoll(pending, events);
        return null;
    }

    private static bool CanHeroAttack(Hero hero, Room room, Monster monster) =>
        Targeting.CanHeroAttack(room.Layout, hero.AttackRange, hero.Position, monster.Position)
        && (!Submerged(room, monster) || monster.Position.ChebyshevDistanceTo(hero.Position) <= 1 || (hero.MainWeapon?.Definition.SwimmerBonus ?? 0) > 0);

    /// <summary>
    /// Rolls a hero's attack. It waits while there's something to decide: Luck rerolls (until
    /// confirmed) or Power Dice earned by sixes. Otherwise it resolves now.
    /// </summary>
    /// <param name="bonus">The weapon's fixed value added to the roll, or 0.</param>
    /// <param name="powerDice">Banked Power Dice rolled in with the dice (already taken from the bank).</param>
    /// <param name="fire">Fire dice: any 6 sets the targets alight (see <see cref="IgniteTargets"/>).</param>
    /// <param name="stunOnSix">A stunning weapon (the Boomerang): every 6 stuns the targets.</param>
    private void RollHeroAttack(Hero hero, int dice, int bonus, IReadOnlyList<int> targetIds, DamageSource source, List<GameEvent> events, int powerDice = 0, bool fire = false, bool stunOnSix = false)
    {
        DiceRoll roll = _dice.Roll(Math.Max(0, dice) + powerDice);
        events.Add(new AttackRolled(hero.Id, roll, bonus, roll.Total + bonus, targetIds, powerDice, source == DamageSource.Shove));

        Turn.PendingRoll = new PendingRoll(hero.Id, roll, bonus, targetIds, source, fire: fire, stunOnSix: stunOnSix);
        EarnPowerDice(hero, Sixes(roll.Faces), events);
    }

    /// <summary>
    /// Each target defends against the attack's total separately. With friendly fire on, targets may
    /// be heroes; one brought down this way is Keeling Over as if by a monster, with no kill for the attacker.
    /// </summary>
    private void ResolveHeroAttack(PendingRoll attack, List<GameEvent> events)
    {
        Hero hero = State.GetHero(attack.HeroId);
        Room room = State.RoomOf(hero);
        foreach (int targetId in attack.TargetIds)
        {
            if (room.FindMonster(targetId) is { } monster)
            {
                DamageMonster(hero, room, monster, attack.Total, attack.Source, events, ranged: monster.Position.ChebyshevDistanceTo(hero.Position) > 1);
            }
            else if (State.ActiveHeroesIn(room).FirstOrDefault(ally => ally.Id == targetId) is { } ally)
            {
                DamageHero(room, ally, attack.Total, AllyBane(hero, attack.Source), hero.Id, events, ranged: ally.Position.ChebyshevDistanceTo(hero.Position) > 1);
            }
        }

        IgniteTargets(hero, room, attack, events);
        StunTargets(hero, room, attack, events);
    }

    /// <summary>An attack hits, for 1 heart, when its total beats the defense total; ties go to the defender.</summary>
    private static int DamageFor(int attackTotal, int defenseTotal) =>
        attackTotal > defenseTotal ? GameRules.DamagePerHit : 0;

    /// <param name="ranged">From a distance: a prone monster rolls an extra defense die.</param>
    private void DamageMonster(Hero? attacker, Room room, Monster monster, int attackTotal, DamageSource source, List<GameEvent> events, bool ranged = false)
    {
        // A stunned monster is easier to hit; a collapsed heap of bones has no defense at all.
        attackTotal += monster.Stun.AttackBonus;
        bool collapsed = monster.Collapsed;
        int armor = collapsed ? 0 : ArmorOf(monster);
        int dice = collapsed ? 0 : DefenseDiceOf(monster) + (ranged && monster.Prone ? 1 : 0);
        DiceRoll defense = RollDefense(monster.Id, monster.Power, dice, armor, attackTotal, canSpend: !collapsed, events);

        int damage = DamageFor(attackTotal, defense.Total + armor);
        monster.Hearts = Math.Max(0, monster.Hearts - damage);
        events.Add(new Damaged(monster.Id, damage, monster.Hearts));

        if (damage > 0 && monster.Hearts == 0)
        {
            DefeatMonster(attacker, room, monster, source, events);
        }
    }

    private void DefeatMonster(Hero? killer, Room room, Monster monster, DamageSource source, List<GameEvent> events)
    {
        MonsterDefinition definition = monster.Definition;
        if (definition.Reassembles && !monster.HasReassembled && source == DamageSource.Weapon)
        {
            monster.Collapsed = true;
            monster.HasReassembled = true;
            monster.FrozenTurns = 0;
            events.Add(new MonsterCollapsed(monster.Id));
            return;
        }

        room.RemoveMonster(monster);
        DropStolen(room, monster, SafeTile(room, monster.Position), events);
        if (killer is not null)
        {
            killer.Kills++;
        }

        events.Add(new MonsterDefeated(monster.Id, killer?.Id ?? 0));
        if (killer is not null)
        {
            GrantXp(killer, definition.Xp, events);
        }

        if (definition.SplitsInto is { } splitId)
        {
            Split(room, monster.Position, Content.GetMonster(splitId), definition.SplitCount, events);
        }

        if (definition.DropChance > 0 && _random.Loot.NextInt(0, 100) < definition.DropChance)
        {
            for (int i = 0; i < definition.Drops; i++)
            {
                var card = new CardInstance(State.NextId(), State.LootDeck.Draw(room.Floor), broughtInByHeroId: null);
                DropLoot(room, SafeTile(room, monster.Position), card, events);
            }
        }

        DropKey(room, monster.Position, events);
        ClearRoomIfEmpty(room, events);
    }

    /// <summary>Once a room's last monster is gone: perhaps a key appears, and everyone in it recovers a heart.</summary>
    private void ClearRoomIfEmpty(Room room, List<GameEvent> events)
    {
        if (room.Monsters.Count > 0)
        {
            return;
        }

        RoomClearedKey(room, events);
        events.Add(new RoomCleared(room.Id));
        foreach (Hero hero in State.ActiveHeroesIn(room))
        {
            int healed = Math.Min(GameRules.HeartsOnRoomClear, hero.MaxHearts - hero.Hearts);
            if (healed > 0)
            {
                hero.Hearts += healed;
                events.Add(new HeroHealed(hero.Id, healed, hero.Hearts));
            }
        }
    }

    private static void GrantXp(Hero hero, int amount, List<GameEvent> events)
    {
        if (amount > 0)
        {
            hero.Xp += amount;
            events.Add(new XpGained(hero.Id, amount, hero.Xp));
        }
    }

    /// <summary>Spawns the pieces of a split monster on its tile and the free tiles around it.</summary>
    private void Split(Room room, GridPosition origin, MonsterDefinition piece, int count, List<GameEvent> events)
    {
        // Only tiles it could crawl to: a diagonal neighbor can be cut off behind walls.
        PathSearch reach = PathSearch.Run(origin, tile => room.Layout[tile].IsWalkable(piece.Flying));
        List<GridPosition> tiles = new[] { origin }
            .Concat(origin.SurroundingTiles())
            .Where(position => room.Layout[position].IsWalkable(piece.Flying) && !IsOccupied(room, position) && reach.CanReach(position))
            .Take(count)
            .ToList();

        foreach (GridPosition position in tiles)
        {
            int strength = GameRules.PartyStrength(State.Heroes.Count(hero => !hero.IsCompanion), State.Heroes.Count(hero => hero.IsCompanion));
            Monster monster = CreateMonster(State.NextId(), piece, position, strength);
            room.AddMonster(monster);
            events.Add(new MonsterSpawned(monster.Id, piece.Id, room.Id, position, monster.Hearts));
        }
    }

    /// <summary>
    /// A monster's turn of attacks on one hero: its Attack dice split into the attacks likeliest to
    /// land the most hits (see <see cref="PlanAttacks"/>), its weapon value added to the first.
    /// It stops once the hero is Keeling Over.
    /// </summary>
    private void MonsterAttacks(Room room, Monster monster, Hero target, List<GameEvent> events)
    {
        MonsterDefinition definition = monster.Definition;
        IReadOnlyList<int> attacks = definition.SeparateAttacks
            ? Enumerable.Repeat(1, monster.AttackDiceThisTurn).ToList()
            : PlanAttacks(monster.AttackDiceThisTurn, definition.Weapon, target.EffectiveStats);
        int prone = ProneBonus(target, monster.Position);
        for (int i = 0; i < attacks.Count && target.IsActive; i++)
        {
            RollMonsterAttack(room, monster, attacks[i] + (i == 0 ? prone : 0), i == 0 ? definition.Weapon : 0, [target], events);
        }
    }

    /// <summary>One attack roll that every target defends against separately (a sweep hits them all with the same roll).</summary>
    private void RollMonsterAttack(Room room, Monster monster, int dice, int bonus, IReadOnlyList<Hero> targets, List<GameEvent> events)
    {
        if (dice < 1)
        {
            return;
        }

        (DiceRoll roll, int powerDice) = RollMonsterDice(dice);
        int total = roll.Total + bonus;
        events.Add(new AttackRolled(monster.Id, roll, bonus, total, targets.Select(hero => hero.Id).ToList(), powerDice));
        foreach (Hero hero in targets.Where(hero => hero.IsActive))
        {
            int dealt = DamageHero(room, hero, total, MonsterBane(monster), monster.Id, events, ranged: hero.Position.ChebyshevDistanceTo(monster.Position) > 1);
            if (dealt > 0 && monster.Definition.Mugger)
            {
                Mug(monster, hero, events);
            }

            if (dealt > 0 && monster.Definition.Pulls && hero.IsActive)
            {
                Pull(room, monster, hero, events);
            }
        }

        IgniteFromMonster(monster, targets, roll, events);
        if (monster.Definition.StunOnSix && roll.Faces.Count(face => face == GameRules.PowerDieFace) is > 0 and var sixes)
        {
            foreach (Hero hero in targets.Where(hero => hero.IsActive))
            {
                Stun(hero, powerful: false, sixes, events);
            }
        }
    }

    /// <summary>
    /// How a monster splits its Attack dice against a hero: the split with the most expected hits,
    /// counting its weapon on the biggest attack. On a tie it keeps fewer, bigger attacks.
    /// </summary>
    internal static IReadOnlyList<int> PlanAttacks(MonsterDefinition monster, Stats target) => PlanAttacks(monster.AttackDice, monster.Weapon, target);

    /// <summary>The same, for <paramref name="attackDice"/> dice (fewer when a stun took some).</summary>
    internal static IReadOnlyList<int> PlanAttacks(int attackDice, int weapon, Stats target)
    {
        int defenseDice = Math.Max(0, target.DefenseDice);
        int armor = Math.Max(0, target.Armor);
        IReadOnlyList<int> best = [];
        double bestHits = -1;
        foreach (IReadOnlyList<int> split in Splits(attackDice, attackDice))
        {
            double hits = split.Select((dice, i) => DiceOdds.ChanceToBeat(dice, i == 0 ? weapon : 0, defenseDice, armor)).Sum();
            if (hits > bestHits + 1e-9)
            {
                best = split;
                bestHits = hits;
            }
        }

        return best;
    }

    /// <summary>Ways to split <paramref name="dice"/> into attacks of at most <paramref name="largest"/> dice, biggest first, fewest attacks first.</summary>
    private static IEnumerable<IReadOnlyList<int>> Splits(int dice, int largest)
    {
        if (dice < 1)
        {
            yield break;
        }

        for (int first = Math.Min(dice, largest); first >= 1; first--)
        {
            if (first == dice)
            {
                yield return [first];
                continue;
            }

            foreach (IReadOnlyList<int> rest in Splits(dice - first, first))
            {
                yield return [first, .. rest];
            }
        }
    }

    /// <summary>
    /// The hero defends against an attack. A hit costs a heart, and the last one leaves them Keeling
    /// Over; a hit on a hero already Keeling Over is a mark of death instead.
    /// </summary>
    /// <param name="bane">What the attacker is, for the tombstone ("Slain by a Goblin").</param>
    /// <param name="ranged">From a distance: a prone hero rolls an extra defense die.</param>
    /// <returns>The damage it did: 1 for a hit (a heart, or a mark of death), 0 for a miss.</returns>
    private int DamageHero(Room room, Hero hero, int attackTotal, string bane, int attackerId, List<GameEvent> events, bool ranged = false)
    {
        attackTotal += hero.Stun.AttackBonus;
        Stats stats = hero.EffectiveStats;
        int armor = Math.Max(0, stats.Armor);
        int lyingLow = ranged && hero.IsProne ? 1 : 0;
        DiceRoll defense = RollDefense(hero.Id, hero.Power, DefenseDiceOf(hero) + lyingLow, armor, attackTotal, canSpend: true, events);

        int damage = DamageFor(attackTotal, defense.Total + armor);
        if (hero.IsKeelingOver)
        {
            events.Add(new Damaged(hero.Id, damage, 0));
            if (damage > 0)
            {
                MarkForDeath(room, hero, bane, attackerId, events);
            }

            return damage;
        }

        hero.Hearts = Math.Max(0, hero.Hearts - damage);
        events.Add(new Damaged(hero.Id, damage, hero.Hearts));

        if (hero.Hearts == 0)
        {
            KeelOver(room, hero, bane, events);
        }

        return damage;
    }
}
