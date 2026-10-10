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
        if (State.Turn is not { PendingAttack: null, HasActed: false })
        {
            return [];
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        return room.Monsters.Where(monster => CanHeroAttack(hero, room, monster)).ToList();
    }

    private string? Handle(AttackMonster command, List<GameEvent> events)
    {
        if (CheckCanAct() is { } error)
        {
            return error;
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

        SpendAction();
        RollHeroAttack(hero, hero.EffectiveStats.AttackDice, [monster.Id], DamageSource.Weapon, events);
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
        faces[command.DieIndex] = _dice.Roll(1).Faces[0];
        var rerolled = new DiceRoll(faces);
        hero.LuckRemaining--;

        if (attack is not null)
        {
            attack.Roll = rerolled;
            events.Add(new DieRerolled(hero.Id, command.DieIndex, oldFace, faces[command.DieIndex], rerolled, null));
            if (hero.LuckRemaining == 0)
            {
                Turn.PendingAttack = null;
                ResolveHeroAttack(attack, events);
            }
        }
        else
        {
            Turn.MovementRoll = rerolled;
            Turn.MovementPoints = MovementPointsFor(rerolled, hero.EffectiveStats);
            events.Add(new DieRerolled(hero.Id, command.DieIndex, oldFace, faces[command.DieIndex], rerolled, Turn.MovementPoints));
        }

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

    private static int Hits(DiceRoll roll) => roll.CountSuccesses(GameRules.HitFace);

    /// <summary>Rolls a hero's attack. With Luck left, it waits for rerolls or confirmation; otherwise it resolves now.</summary>
    private void RollHeroAttack(Hero hero, int dice, IReadOnlyList<int> targetIds, DamageSource source, List<GameEvent> events)
    {
        DiceRoll roll = _dice.Roll(Math.Max(0, dice));
        events.Add(new AttackRolled(hero.Id, roll, Hits(roll), targetIds));

        var attack = new PendingAttack(hero.Id, roll, targetIds, source);
        if (hero.LuckRemaining > 0 && roll.Faces.Count > 0)
        {
            Turn.PendingAttack = attack;
        }
        else
        {
            ResolveHeroAttack(attack, events);
        }
    }

    private void ResolveHeroAttack(PendingAttack attack, List<GameEvent> events)
    {
        int hits = Hits(attack.Roll);
        if (hits == 0)
        {
            return;
        }

        Hero hero = State.GetHero(attack.AttackerId);
        Room room = State.RoomOf(hero);
        foreach (int targetId in attack.TargetIds)
        {
            if (room.FindMonster(targetId) is { } monster)
            {
                DamageMonster(hero, room, monster, hits, attack.Source, events);
            }
        }
    }

    private void DamageMonster(Hero attacker, Room room, Monster monster, int hits, DamageSource source, List<GameEvent> events)
    {
        DiceRoll defense = _dice.Roll(monster.Collapsed ? 0 : monster.Definition.DefenseDice);
        int blocks = defense.CountSuccesses(GameRules.MonsterBlockFace);
        events.Add(new DefenseRolled(monster.Id, defense, blocks));

        int damage = Math.Max(0, hits - blocks);
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

    private void MonsterAttack(Room room, Monster monster, IReadOnlyList<Hero> targets, List<GameEvent> events)
    {
        DiceRoll roll = _dice.Roll(monster.Definition.AttackDice);
        int hits = Hits(roll);
        events.Add(new AttackRolled(monster.Id, roll, hits, targets.Select(hero => hero.Id).ToList()));
        if (hits == 0)
        {
            return;
        }

        foreach (Hero hero in targets)
        {
            DamageHero(room, hero, hits, events);
        }
    }

    private void DamageHero(Room room, Hero hero, int hits, List<GameEvent> events)
    {
        DiceRoll defense = _dice.Roll(Math.Max(0, hero.EffectiveStats.DefenseDice));
        int blocks = defense.CountSuccesses(GameRules.HeroBlockFace);
        events.Add(new DefenseRolled(hero.Id, defense, blocks));

        int damage = Math.Max(0, hits - blocks);
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
