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
    /// <param name="weaponId">The weapon in hand the hero picked to attack with, or null for whichever reaches.</param>
    public IReadOnlyList<Monster> GetAttackableMonsters(int? weaponId = null)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 } || AttackDiceLeft < 1)
        {
            return [];
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        CardInstance? only = HandWeapon(hero, weaponId);
        return room.Monsters.Where(monster => CanHeroAttack(hero, room, monster, only)).ToList();
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

        CardInstance? only = HandWeapon(hero, command.WeaponId);
        if (command.WeaponId is not null && only is null)
        {
            return "You aren't holding that weapon.";
        }

        if (!CanHeroAttack(hero, room, monster, only))
        {
            return only is null ? "That monster is out of reach." : $"Your {only.Definition.Name} doesn't reach it.";
        }

        if (CheckBankedPowerDice(hero, command.PowerDice) is { } powerError)
        {
            return powerError;
        }

        Reveal(hero, events);
        StrikeMonster(hero, room, monster, only, dice, command.UseWeapon, command.PowerDice, 0, events);
        return null;
    }

    /// <summary>A Rumble attack on a monster, with the weapon in hand (or <paramref name="only"/>) and <paramref name="extra"/> on top (a bull rush's tiles to spare).</summary>
    private void StrikeMonster(Hero hero, Room room, Monster monster, CardInstance? only, int dice, bool useWeapon, int powerDice, int extra, List<GameEvent> events, bool bullRush = false)
    {
        Wielding wielding = Wield(hero, room, monster.Position, monster, only);
        // A Shovel strikes earth creatures with a die more and +1.
        int earth = wielding.HasStrike && wielding.Weapon?.Shovel == true && monster.Definition.DieType == DieType.Earth ? 1 : 0;
        int weaponBonus = wielding.Bonus + earth + (monster.Definition.Swimmer && wielding.HasStrike && !wielding.OffHand ? wielding.Weapon?.SwimmerBonus ?? 0 : 0);
        bool armed = useWeapon && wielding.HasStrike && (weaponBonus > 0 || wielding.Dice > 0);
        int bonus = (armed ? weaponBonus : 0) + extra;
        Turn.AttackDiceUsed += dice;
        UseWeapon(hero, room, wielding, armed, monster.Position, events);
        SpendBankedPowerDice(hero, powerDice, events);
        (int star, int weaponDice, IReadOnlyList<(DieType, int)> typed) = Arm(hero, dice + ProneBonus(monster, hero.Position), wielding.Weapon, armed ? wielding.Dice + earth : 0, hero.TorchesHeld);
        RollHeroAttack(hero, star, bonus, [monster.Id], DamageSource.Weapon, events, powerDice,
            stunOnSix: wielding.Weapon?.StunOnSix ?? false, weaponDice: weaponDice, weapon: wielding.Weapon, typed: typed, torches: hero.TorchesHeld, bullRush: bullRush);
    }

    /// <summary>With friendly fire on, the other heroes and companions the active hero could attack right now.</summary>
    /// <param name="weaponId">The weapon in hand the hero picked to attack with, or null for whichever reaches.</param>
    public IReadOnlyList<Hero> GetAttackableAllies(int? weaponId = null)
    {
        if (!State.FriendlyFire || State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 } || AttackDiceLeft < 1)
        {
            return [];
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        return State.ActiveHeroesIn(room)
            .Where(ally => ally != hero && CanHeroReach(hero, room.Layout, hero.Position, ally.Position, HandWeapon(hero, weaponId)))
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
        CardInstance? only = HandWeapon(hero, command.WeaponId);
        if (command.WeaponId is not null && only is null)
        {
            return "You aren't holding that weapon.";
        }

        if (GetAttackableAllies(command.WeaponId).FirstOrDefault(ally => ally.Id == command.TargetId) is not { } target)
        {
            return "They're out of reach.";
        }

        if (CheckBankedPowerDice(hero, command.PowerDice) is { } powerError)
        {
            return powerError;
        }

        Reveal(hero, events);
        Wielding wielding = Wield(hero, ActiveRoom, target.Position, only: only);
        bool armed = command.UseWeapon && wielding.HasStrike && (wielding.Bonus > 0 || wielding.Dice > 0);
        int bonus = armed ? wielding.Bonus : 0;
        Turn.AttackDiceUsed += dice;
        UseWeapon(hero, ActiveRoom, wielding, armed, target.Position, events);
        SpendBankedPowerDice(hero, command.PowerDice, events);
        (int star, int weaponDice, IReadOnlyList<(DieType, int)> typed) = Arm(hero, dice + ProneBonus(target, hero.Position), wielding.Weapon, armed ? wielding.Dice : 0, torches: 0);
        RollHeroAttack(hero, star, bonus, [target.Id], DamageSource.Weapon, events, command.PowerDice,
            stunOnSix: wielding.Weapon?.StunOnSix ?? false, weaponDice: weaponDice, weapon: wielding.Weapon, typed: typed);
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

    /// <summary>
    /// What a Rumble attack rolls: Rumble dice (made of something else in gear like the Storm Mail), the
    /// weapon's own dice (typed ones roll as their type), and a held torch's fire die each.
    /// </summary>
    private static (int Plain, int WeaponDice, IReadOnlyList<(DieType Type, int Dice)> Typed) Arm(Hero hero, int dice, CardDefinition? weapon, int weaponDice, int torches)
    {
        List<(DieType, int)> typed = [];
        if (hero.EquippedCards.Select(card => card.Definition.RumbleType).FirstOrDefault(type => type is not null) is { } rumble)
        {
            typed.Add((rumble, dice));
            dice = 0;
        }

        if (weapon?.DieType is { } own && weaponDice > 0)
        {
            typed.Add((own, weaponDice));
            weaponDice = 0;
        }

        typed.Add((DieType.Fire, torches));
        return (dice, weaponDice, typed);
    }

    private static bool CanHeroAttack(Hero hero, Room room, Monster monster, CardInstance? only = null) =>
        monster.Tiles.Any(tile => CanHeroReach(hero, room.Layout, hero.Position, tile, only) && (!IsBlind(hero.Conditions) || tile.ChebyshevDistanceTo(hero.Position) <= 1))
        && (!Submerged(room, monster) || monster.NearestTile(hero.Position).ChebyshevDistanceTo(hero.Position) <= 1 || (hero.MainWeapon?.Definition.SwimmerBonus ?? 0) > 0);

    /// <summary>
    /// Rolls a hero's attack. It waits while there's something to decide: Luck rerolls (until
    /// confirmed) or Power Dice earned by plain sixes (Star sixes surge at once). Otherwise it resolves now.
    /// </summary>
    /// <param name="bonus">The weapon's fixed value added to the roll, or 0.</param>
    /// <param name="powerDice">Banked Power Dice rolled in with the dice (already taken from the bank).</param>
    /// <param name="stunOnSix">A stunning weapon (the Boomerang): on a hit, every 6 stuns the target.</param>
    /// <param name="weaponDice">The weapon's own dice (the Longsword's d6), whose sixes roll straight in.</param>
    /// <param name="weapon">The weapon swung or shot, for what it does as the attack lands (see <see cref="WeaponAftermath"/>).</param>
    /// <param name="typed">Typed dice rolled in (a spell's, a torch's fire die): see <see cref="RollTypedDice"/>.</param>
    /// <param name="torches">Torches lighting the attack (their fire dice are among <paramref name="typed"/>): a 1 anywhere burns one out.</param>
    /// <param name="chill">Stacks of Freeze laid on every target, hit or miss.</param>
    /// <param name="casting">The spell this casts, if it's a spell.</param>
    /// <param name="bullRush">The blow at the end of a bull rush (see <see cref="BullRushAftermath"/>).</param>
    private void RollHeroAttack(Hero hero, int dice, int bonus, IReadOnlyList<int> targetIds, DamageSource source, List<GameEvent> events, int powerDice = 0, bool stunOnSix = false, int weaponDice = 0, CardDefinition? weapon = null, IReadOnlyList<(DieType Type, int Dice)>? typed = null, int torches = 0, int chill = 0, SpellCasting? casting = null, bool bullRush = false)
    {
        dice = Math.Max(0, dice);
        bonus -= AttackPenalty(hero.Conditions);
        DiceRoll rolled = _dice.Roll(dice + powerDice);
        (int[] weaponFaces, int crits) = RollBonusDice(weaponDice);
        (int[] typedFaces, DieType[] types) = RollTypedDice(typed ?? []);
        var roll = new DiceRoll([.. rolled.Faces.Take(dice), .. weaponFaces[..weaponDice], .. typedFaces, .. rolled.Faces.Skip(dice), .. weaponFaces[weaponDice..]]);
        DieType[] dieTypes = [.. Enumerable.Repeat(DieType.Plain, dice + weaponDice), .. types];
        events.Add(new AttackRolled(hero.Id, roll, bonus, roll.Total + bonus, targetIds, powerDice + crits, source == DamageSource.Shove) { WeaponDice = weaponDice, DieTypes = dieTypes, WeaponId = casting is null ? weapon?.Id : null });
        if (crits > 0)
        {
            events.Add(new CritRolled(hero.Id, crits));
        }

        Turn.PendingRoll = new PendingRoll(hero.Id, roll, bonus, targetIds, source, stunOnSix: stunOnSix)
        {
            NonPowerDice = dice + weaponDice + typedFaces.Length,
            Weapon = weapon,
            Torches = torches,
            DieTypes = dieTypes,
            Chill = chill,
            Casting = casting,
            BullRush = bullRush,
        };
        SurgePowerDice(hero, StarSixes(roll, dieTypes), events);
        EarnPowerDice(hero, Sixes(rolled.Faces), events);
    }

    /// <summary>
    /// Each target defends against the attack's total separately. With friendly fire on, targets may
    /// be heroes; one brought down this way is Keeling Over as if by a monster, with no kill for the attacker.
    /// </summary>
    /// <returns>The targets it hit.</returns>
    private List<int> ResolveHeroAttack(PendingRoll attack, List<GameEvent> events)
    {
        Hero hero = State.GetHero(attack.HeroId);
        Room room = State.RoomOf(hero);
        Dictionary<DieType, int> sixes = TypedSixes(attack.Roll, attack.DieTypes);
        int stuns = attack.StunOnSix ? Sixes(attack.Roll.Faces) : 0;
        int fireDice = attack.DieTypes.Count(type => type == DieType.Fire);
        List<int> hit = [];
        foreach (int targetId in attack.TargetIds)
        {
            // A spell's extra dice against this target (Quake's on rubble) add to its total and sixes.
            int total = attack.Total;
            Dictionary<DieType, int> laid = sixes;
            if (attack.Casting?.Extra.GetValueOrDefault(targetId) is { Total: > 0 } extra)
            {
                total += extra.Total;
                laid = new Dictionary<DieType, int>(sixes) { [attack.Casting.Spell.DieType] = sixes.GetValueOrDefault(attack.Casting.Spell.DieType) + extra.Sixes };
            }

            if (room.FindMonster(targetId) is { } monster)
            {
                // Shooting into the dark is harder.
                if (attack.Source != DamageSource.Shove && monster.NearestTile(hero.Position).ChebyshevDistanceTo(hero.Position) > 1 && InDarkness(room, monster.NearestTile(hero.Position)))
                {
                    total -= 1;
                }

                Thaw(monster.Conditions, monster.Id, fireDice, events);
                if (attack.Chill > 0)
                {
                    Afflict(monster, hero);
                    AddCondition(monster.Conditions, monster.Id, Condition.Freeze, attack.Chill, hero.Id, events);
                }

                // On a hit, the typed dice take hold, and a stunning weapon's sixes stun.
                if (DamageMonster(hero, room, monster, total, attack.Source, events, ranged: monster.NearestTile(hero.Position).ChebyshevDistanceTo(hero.Position) > 1, crush: attack.Weapon?.Crush ?? false,
                        purifying: attack.DieTypes.Any(type => type is DieType.Fire or DieType.Radiate)) > 0)
                {
                    hit.Add(targetId);
                    LayTypedDice(room, monster, laid, hero.Id, hero.Position, hero, events);
                    Stun(room, monster, powerful: false, stuns, hero, events);
                    NecroKill(room, monster, laid, hero, events);
                }
            }
            else if (State.ActiveHeroesIn(room).FirstOrDefault(ally => ally.Id == targetId) is { } ally)
            {
                Thaw(ally.Conditions, ally.Id, fireDice, events);
                AddCondition(ally.Conditions, ally.Id, Condition.Freeze, attack.Chill, hero.Id, events);
                if (DamageHero(room, ally, total, AllyBane(hero, attack.Source), hero.Id, events, ranged: ally.Position.ChebyshevDistanceTo(hero.Position) > 1) > 0)
                {
                    hit.Add(targetId);
                    LayTypedDice(room, ally, laid, hero.Id, hero.Position, events);
                    Stun(ally, powerful: false, stuns, events);
                }
            }
            else if (room.FindMerchant(targetId) is { } merchant)
            {
                DamageMerchant(hero.Id, hero, room, merchant, total, events);
            }
        }

        WeaponAftermath(hero, room, attack, events);
        SpellAftermath(hero, room, attack, hit, events);

        // Any 1 in a torch-lit attack burns one of the torches out.
        if (attack.Torches > 0 && attack.Roll.Faces.Contains(1) && hero.EquippedCards.LastOrDefault(card => card.Definition.Torch) is { } torch)
        {
            hero.Remove(torch);
            events.Add(new TorchBurnedOut(hero.Id, torch.Id));
        }

        return hit;
    }

    /// <summary>An attack hits, for 1 heart, when its total beats the defense total; ties go to the defender.</summary>
    private static int DamageFor(int attackTotal, int defenseTotal) =>
        attackTotal > defenseTotal ? GameRules.DamagePerHit : 0;

    /// <param name="ranged">From a distance: a prone monster rolls an extra defense die.</param>
    /// <param name="crush">A crushing blow (the War Hammer): a hit knocks a shelled monster onto its back, and bones it breaks stay broken.</param>
    /// <returns>The damage it did: 1 for a hit, 0 for a miss.</returns>
    /// <param name="purifying">Fire or Radiate went into it: no grave dust puffs off.</param>
    private int DamageMonster(Hero? attacker, Room room, Monster monster, int attackTotal, DamageSource source, List<GameEvent> events, bool ranged = false, bool crush = false, bool purifying = false)
    {
        // A stunned, poisoned or glowing monster is easier to hit; a collapsed heap of bones has no defense at all.
        attackTotal += monster.Stun.AttackBonus + Exposure(monster.Conditions) - (room.PlantsFor(monster.Position, heroes: false) ? 1 : 0);
        bool collapsed = monster.Collapsed;
        int armor = collapsed ? 0 : ArmorOf(monster);
        int dice = collapsed ? 0 : DefenseDiceOf(monster) + (ranged && monster.Prone ? 1 : 0);
        Guard? extra = !collapsed && monster.Definition.ExtraDefenseDice > 0 ? new Guard { Dice = monster.Definition.ExtraDefenseDice } : null;
        DiceRoll defense = RollDefense(monster.Id, monster.Power, dice, armor, attackTotal, canSpend: !collapsed, events, extra);

        int damage = DamageFor(attackTotal, defense.Total + armor);
        Wake(monster, events);
        monster.Hearts = Math.Max(0, monster.Hearts - damage);
        events.Add(new Damaged(monster.Id, damage, monster.Hearts));
        if (damage > 0)
        {
            LetGo(monster);
        }

        if (damage > 0 && monster.Definition.GraveDust && !purifying && source != DamageSource.Fire)
        {
            GraveDust(room, monster, destroyed: monster.Hearts == 0, events);
        }

        if (damage > 0 && monster.Hearts == 0)
        {
            DefeatMonster(attacker, room, monster, source, events, crushed: crush);
        }
        else if (damage > 0 && crush && monster.Definition.Shell && !monster.Prone && !monster.Collapsed)
        {
            monster.Prone = true;
            events.Add(new Flipped(monster.Id));
        }

        return damage;
    }

    /// <param name="crushed">Broken by a crushing blow: bones don't reassemble.</param>
    private void DefeatMonster(Hero? killer, Room room, Monster monster, DamageSource source, List<GameEvent> events, bool crushed = false)
    {
        MonsterDefinition definition = monster.Definition;
        if (definition.Reassembles && !monster.HasReassembled && source == DamageSource.Weapon && !crushed)
        {
            monster.Collapsed = true;
            monster.HasReassembled = true;
            monster.FrozenTurns = 0;
            events.Add(new MonsterCollapsed(monster.Id));
            return;
        }

        room.RemoveMonster(monster);
        LetGo(monster);
        DropStolen(room, monster, SafeTile(room, monster.Position), events);
        if (killer is not null && monster.RaisedBy is null)
        {
            killer.Kills++;
        }

        events.Add(new MonsterDefeated(monster.Id, killer?.Id ?? 0));
        CrumbleRaised(room, monster, events);

        // The raised dead are worth nothing: no XP, no loot, no key.
        if (monster.RaisedBy is not null)
        {
            ClearRoomIfEmpty(room, events);
            return;
        }

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

        foreach (RareDrop carried in definition.Carries.Where(carried => _random.Loot.NextInt(0, 100) < carried.Chance))
        {
            DropLoot(room, SafeTile(room, monster.Position), new CardInstance(State.NextId(), Content.GetCard(carried.Card), broughtInByHeroId: null), events);
        }

        if (definition.RareDrop is { } rare && _random.Loot.NextInt(0, 100) < rare.Chance)
        {
            DropLoot(room, SafeTile(room, monster.Position), new CardInstance(State.NextId(), Content.GetCard(rare.Card), broughtInByHeroId: null), events);
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

        // Out on the Woarld Map, the fight's won: the party gathers itself and walks on.
        if (State.Skirmish)
        {
            events.Add(new RoomCleared(room.Id));
            foreach (Hero hero in State.Heroes.Where(hero => hero.IsInPlay))
            {
                hero.Status = HeroStatus.Escaped;
                events.Add(new HeroEscaped(hero.Id, hero.TreasureValue));
            }

            EndRunIfOver(events);
            return;
        }

        RoomClearedKey(room, events);
        events.Add(new RoomCleared(room.Id));
        foreach (Hero hero in State.ActiveHeroesIn(room))
        {
            int healed = Math.Min(GameRules.HeartsOnRoomClear, hero.HeartsToHeal);
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
        if (definition.Arc)
        {
            SwingArc(room, monster, target, events);
            return;
        }

        IReadOnlyList<int> attacks = definition.SeparateAttacks
            ? Enumerable.Repeat(1, monster.AttackDiceThisTurn).ToList()
            : PlanAttacks(monster.AttackDiceThisTurn, definition.Weapon, target.EffectiveStats);
        int prone = ProneBonus(target, monster.Position);
        for (int i = 0; i < attacks.Count && target.IsActive; i++)
        {
            RollMonsterAttack(room, monster, attacks[i] + (i == 0 ? prone : 0), i == 0 ? definition.Weapon : 0, [target], events, weaponDice: i == 0 ? definition.WeaponDice : 0);
        }
    }

    /// <summary>One attack roll that every target defends against separately (a sweep hits them all with the same roll).</summary>
    /// <param name="weaponDice">Its weapon's own dice (a Captain's mace), rolled in, a 6 rolling another straight in.</param>
    private void RollMonsterAttack(Room room, Monster monster, int dice, int bonus, IReadOnlyList<Hero> targets, List<GameEvent> events, int weaponDice = 0)
    {
        if (dice < 1)
        {
            return;
        }

        (DiceRoll roll, IReadOnlyList<DieType> types, int powerDice) = RollMonsterDice(monster, dice);
        (int[] weaponFaces, int crits) = RollBonusDice(weaponDice);
        foreach (int face in weaponFaces)
        {
            roll = roll.With(face);
        }

        powerDice += crits;
        int weaponSixes = weaponFaces.Count(face => face == GameRules.PowerDieFace);

        // Banked Power Dice go in, one at a time, while the roll looks short of getting past the
        // best-defended target (their armor and a middling defense die); a 6 rolls another straight in.
        int spent = 0;
        int needed = targets.Where(hero => hero.IsActive).Select(hero => hero.EffectiveStats.Armor + 4).DefaultIfEmpty(0).Max();
        while (monster.Power.Banked > 0 && roll.Total + bonus <= needed)
        {
            monster.Power.Banked--;
            spent++;
            (DiceRoll extra, _, int more) = RollMonsterDice(monster, 1, plain: true);
            foreach (int face in extra.Faces)
            {
                roll = roll.With(face);
            }

            powerDice += 1 + more;
        }

        bonus -= AttackPenalty(monster.Conditions);
        int total = roll.Total + bonus;
        events.Add(new AttackRolled(monster.Id, roll, bonus, total, targets.Select(hero => hero.Id).ToList(), powerDice) { DieTypes = types });
        if (spent > 0)
        {
            events.Add(new PowerDiceSpent(monster.Id, spent, monster.Power.Banked));
        }

        Dictionary<DieType, int> sixes = TypedSixes(roll, types);
        int stuns = (monster.Definition.StunOnSix ? Sixes(roll.Faces) : 0) + (monster.Definition.StunOnHit ? 1 + weaponSixes : 0);
        foreach (Hero hero in targets.Where(hero => hero.IsActive))
        {
            Thaw(hero.Conditions, hero.Id, types.Count(type => type == DieType.Fire), events);
            // Thieves' Oil turns Necro blows aside.
            int against = total - (hero.ThievesOilRounds > 0 && types.Contains(DieType.Necro) ? GameRules.OilNecroDefense : 0);
            int dealt = DamageHero(room, hero, against, MonsterBane(monster), monster.Id, events, ranged: hero.Position.ChebyshevDistanceTo(monster.NearestTile(hero.Position)) > 1);
            if (dealt > 0 && monster.Definition.Mugger)
            {
                Mug(monster, hero, events);
            }

            if (dealt > 0 && monster.Definition.Pulls && hero.IsActive)
            {
                Pull(room, monster, hero, events);
            }

            if (dealt > 0 && hero.IsActive)
            {
                AfterMonsterHit(room, monster, hero, events);
            }

            // On a hit, its typed dice take hold, and a stunning blow or sting stuns.
            if (dealt > 0 && hero.IsInPlay)
            {
                LayTypedDice(room, hero, sixes, monster.Id, monster.NearestTile(hero.Position), events);
                Stun(hero, powerful: false, stuns, events);
                if (monster.Definition.Infects is { } infection && roll.Faces.Contains(GameRules.PowerDieFace))
                {
                    Infect(hero, infection, monster.Id, events);
                }
            }

            // A heavy blow knocks the hero back a tile, and with a mighty one every 6 knocks them a tile further, hit or miss.
            int knockback = (dealt > 0 && monster.Definition.KnocksBack ? 1 : 0)
                            + (monster.Definition.KnockbackOnSix ? roll.Faces.Count(face => face == GameRules.PowerDieFace) : 0);
            if (knockback > 0 && hero.IsActive)
            {
                KnockBack(room, monster, hero, knockback, events);
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
        attackTotal += hero.Stun.AttackBonus + Exposure(hero.Conditions) - (room.PlantsFor(hero.Position, heroes: true) ? 1 : 0);
        int lyingLow = ranged && hero.IsProne ? 1 : 0;
        int defense = HeroDefense(hero, attackTotal, events, lyingLow);

        int damage = DamageFor(attackTotal, defense);
        if (hero.IsKeelingOver)
        {
            events.Add(new Damaged(hero.Id, damage, 0));
            if (damage > 0)
            {
                MarkForDeath(room, hero, bane, attackerId, events);
            }

            return damage;
        }

        LoseHearts(hero, damage, events);

        if (hero.Hearts == 0)
        {
            KeelOver(room, hero, bane, events);
        }

        return damage;
    }
}
