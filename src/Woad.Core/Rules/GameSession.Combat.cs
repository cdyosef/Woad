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
        if (State.Turn is not { PendingAttack: null } || AttackDiceLeft < 1)
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

        int bonus = command.UseWeapon ? WeaponBonusLeft : 0;
        Turn.EndMove();
        Turn.AttackDiceUsed += dice;
        Turn.WeaponUsed |= bonus > 0;
        SpendBankedPowerDice(hero, command.PowerDice, events);
        RollHeroAttack(hero, dice, bonus, [monster.Id], DamageSource.Weapon, events, command.PowerDice);
        return null;
    }

    private string? Handle(RerollDie command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        if (hero.LuckRemaining < 1)
        {
            return "You have no Luck left this floor.";
        }

        PendingAttack? attack = Turn.PendingAttack;
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
            Turn.MovementPoints = Math.Max(0, rerolled.Total + Turn.MovementBonus);
            events.Add(new DieRerolled(hero.Id, command.DieIndex, oldFace, faces[command.DieIndex], rerolled, Turn.MovementPoints));
        }

        // A reroll that comes up 6 earns a Power Die; spending the last Luck resolves the attack.
        EarnPowerDice(hero, faces[command.DieIndex] == GameRules.PowerDieFace ? 1 : 0, events);
        return null;
    }

    private string? Handle(ConfirmAttack command, List<GameEvent> events)
    {
        if (Turn.PendingAttack is not { } attack)
        {
            return "There's no attack waiting to be confirmed.";
        }

        Turn.PendingAttack = null;
        ResolveHeroAttack(attack, events);
        return null;
    }

    private static bool CanHeroAttack(Hero hero, Room room, Monster monster) =>
        Targeting.CanAttack(room.Layout, Targeting.HeroPattern(hero.AttackRange), hero.AttackRange, hero.Position, monster.Position);

    /// <summary>
    /// Rolls a hero's attack. It waits while there's something to decide: Luck rerolls (until
    /// confirmed) or Power Dice earned by sixes. Otherwise it resolves now.
    /// </summary>
    /// <param name="bonus">The weapon's fixed value added to the roll, or 0.</param>
    /// <param name="powerDice">Banked Power Dice rolled in with the dice (already taken from the bank).</param>
    private void RollHeroAttack(Hero hero, int dice, int bonus, IReadOnlyList<int> targetIds, DamageSource source, List<GameEvent> events, int powerDice = 0)
    {
        DiceRoll roll = _dice.Roll(Math.Max(0, dice) + powerDice);
        events.Add(new AttackRolled(hero.Id, roll, bonus, roll.Total + bonus, targetIds, powerDice));

        Turn.PendingAttack = new PendingAttack(hero.Id, roll, bonus, targetIds, source);
        EarnPowerDice(hero, Sixes(roll.Faces), events);
    }

    /// <summary>Each target defends against the attack's total separately.</summary>
    private void ResolveHeroAttack(PendingAttack attack, List<GameEvent> events)
    {
        Hero hero = State.GetHero(attack.AttackerId);
        Room room = State.RoomOf(hero);
        foreach (int targetId in attack.TargetIds)
        {
            if (room.FindMonster(targetId) is { } monster)
            {
                DamageMonster(hero, room, monster, attack.Total, attack.Source, events);
            }
        }
    }

    /// <summary>An attack hits, for 1 heart, when its total beats the defense total; ties go to the defender.</summary>
    private static int DamageFor(int attackTotal, int defenseTotal) =>
        attackTotal > defenseTotal ? GameRules.DamagePerHit : 0;

    private void DamageMonster(Hero attacker, Room room, Monster monster, int attackTotal, DamageSource source, List<GameEvent> events)
    {
        // A collapsed heap of bones has no defense at all.
        bool collapsed = monster.Collapsed;
        int armor = collapsed ? 0 : monster.Definition.Armor;
        DiceRoll defense = RollDefense(monster.Id, monster.Power, collapsed ? 0 : monster.Definition.DefenseDice, armor, attackTotal, canSpend: !collapsed, events);

        int damage = DamageFor(attackTotal, defense.Total + armor);
        monster.Hearts = Math.Max(0, monster.Hearts - damage);
        events.Add(new Damaged(monster.Id, damage, monster.Hearts));

        if (damage > 0 && monster.Hearts == 0)
        {
            DefeatMonster(attacker, room, monster, source, events);
        }
    }

    private void DefeatMonster(Hero killer, Room room, Monster monster, DamageSource source, List<GameEvent> events)
    {
        MonsterDefinition definition = monster.Definition;
        if (definition.Reassembles && !monster.HasReassembled && source == DamageSource.Weapon)
        {
            monster.Collapsed = true;
            monster.HasReassembled = true;
            monster.StunnedTurns = 0;
            events.Add(new MonsterCollapsed(monster.Id));
            return;
        }

        room.RemoveMonster(monster);
        killer.Kills++;
        events.Add(new MonsterDefeated(monster.Id, killer.Id));
        GrantXp(killer, definition.Xp, events);

        if (definition.SplitsInto is { } splitId)
        {
            Split(room, monster.Position, Content.GetMonster(splitId), definition.SplitCount, events);
        }

        if (definition.DropChance > 0 && _random.Loot.NextInt(0, 100) < definition.DropChance)
        {
            for (int i = 0; i < definition.Drops; i++)
            {
                var card = new CardInstance(State.NextId(), State.LootDeck.Draw(room.Floor), broughtInByHeroId: null);
                room.AddLoot(monster.Position, [card]);
                events.Add(new LootDropped(room.Id, monster.Position, card.Id));
            }
        }

        if (room.Monsters.Count == 0)
        {
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
        List<GridPosition> tiles = new[] { origin }
            .Concat(origin.SurroundingTiles())
            .Where(position => room.Layout[position].IsWalkable(piece.Flying) && !IsOccupied(room, position))
            .Take(count)
            .ToList();

        foreach (GridPosition position in tiles)
        {
            var monster = new Monster(State.NextId(), piece, position);
            room.AddMonster(monster);
            events.Add(new MonsterSpawned(monster.Id, piece.Id, room.Id, position));
        }
    }

    /// <summary>
    /// A monster's turn of attacks on one hero: its Attack dice split into the attacks likeliest to
    /// land the most hits (see <see cref="PlanAttacks"/>), its weapon value added to the first.
    /// It stops once the hero is downed.
    /// </summary>
    private void MonsterAttacks(Room room, Monster monster, Hero target, List<GameEvent> events)
    {
        MonsterDefinition definition = monster.Definition;
        IReadOnlyList<int> attacks = PlanAttacks(definition, target.EffectiveStats);
        for (int i = 0; i < attacks.Count && target.IsActive; i++)
        {
            RollMonsterAttack(room, monster, attacks[i], i == 0 ? definition.Weapon : 0, [target], events);
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
            DamageHero(room, hero, total, events);
        }
    }

    /// <summary>
    /// How a monster splits its Attack dice against a hero: the split with the most expected hits,
    /// counting its weapon on the biggest attack. On a tie it keeps fewer, bigger attacks.
    /// </summary>
    internal static IReadOnlyList<int> PlanAttacks(MonsterDefinition monster, Stats target)
    {
        int defenseDice = Math.Max(0, target.DefenseDice);
        int armor = Math.Max(0, target.Armor);
        IReadOnlyList<int> best = [];
        double bestHits = -1;
        foreach (IReadOnlyList<int> split in Splits(monster.AttackDice, monster.AttackDice))
        {
            double hits = split.Select((dice, i) => DiceOdds.ChanceToBeat(dice, i == 0 ? monster.Weapon : 0, defenseDice, armor)).Sum();
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

    private void DamageHero(Room room, Hero hero, int attackTotal, List<GameEvent> events)
    {
        Stats stats = hero.EffectiveStats;
        int armor = Math.Max(0, stats.Armor);
        DiceRoll defense = RollDefense(hero.Id, hero.Power, Math.Max(0, stats.DefenseDice), armor, attackTotal, canSpend: true, events);

        int damage = DamageFor(attackTotal, defense.Total + armor);
        hero.Hearts = Math.Max(0, hero.Hearts - damage);
        events.Add(new Damaged(hero.Id, damage, hero.Hearts));

        if (hero.Hearts == 0)
        {
            DownHero(room, hero, events);
        }
    }

    /// <summary>A downed hero is out of the run and drops everything they didn't bring in.</summary>
    private void DownHero(Room room, Hero hero, List<GameEvent> events)
    {
        hero.Status = HeroStatus.Downed;
        List<CardInstance> dropped = hero.AllCards.Where(card => card.BroughtInByHeroId != hero.Id).ToList();
        foreach (CardInstance card in dropped)
        {
            hero.Remove(card);
        }

        room.AddLoot(hero.Position, dropped);
        events.Add(new HeroDowned(hero.Id, hero.Position, dropped.Select(card => card.Id).ToList()));
        EndRunIfOver(events);
    }
}
