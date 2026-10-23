using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Weapon strikes: Rapiers and Daggers, thrown Daggers, cleaving, shoving and crushing blows, reach and crossbows.</summary>
public sealed class WeaponTests
{
    private static GameSession Duel(string row, string[] cards, ScriptedDice dice, int attackDice = 1, int odHandDice = 1) =>
        TestRun.Start($"""
            .........
            .........
            .........
            .........
            {row}
            .........
            .........
            .........
            .........
            """, dice, stats: TestRun.HeroStats with { AttackDice = attackDice, OdHandDice = odHandDice }, cards: cards);

    private static Monster MonsterAt(GameSession session, int x) => session.RoomById().Monsters.Single(monster => monster.Position == new GridPosition(x, 4));

    [Fact]
    public void ARapierStrikesTwiceWithALightOffHandButOnceBehindAShield()
    {
        GameSession session = Duel("....1d...", ["t_rapier", "t_dagger"], new ScriptedDice(1, 1, 1), attackDice: 3);
        Hero hero = session.HeroInSeat();
        int dummy = session.OnlyMonster().Id;

        int[] bonuses = Enumerable.Range(0, 3)
            .Select(_ => session.Do(new AttackMonster(hero.Id, dummy, Dice: 1)).SingleOf<AttackRolled>().Bonus)
            .ToArray();

        // Two Rapier strikes, then the off-hand Dagger's own.
        Assert.Equal([2, 2, 1], bonuses);

        GameSession shielded = Duel("....1d...", ["t_rapier", "shield"], new ScriptedDice(1, 1), attackDice: 2);
        Hero knight = shielded.HeroInSeat();
        int target = shielded.OnlyMonster().Id;
        Assert.Equal(2, shielded.Do(new AttackMonster(knight.Id, target, Dice: 1)).SingleOf<AttackRolled>().Bonus);
        Assert.Equal(0, shielded.Do(new AttackMonster(knight.Id, target, Dice: 1)).SingleOf<AttackRolled>().Bonus);
    }

    [Fact]
    public void ADaggerInEachHandAddsOneMoreToEachAndDoublesOnAStunnedMonster()
    {
        GameSession session = Duel("....1d...", ["t_dagger", "t_dagger"], new ScriptedDice(1, 1), attackDice: 2);
        Hero hero = session.HeroInSeat();
        CardInstance daggers = hero.Card("t_dagger");
        Assert.Equal(2, daggers.Count);

        // Equipping the stack again sends one dagger to the other hand.
        session.Do(new EquipCard(hero.Id, daggers.Id, EquipSlot.MainHand));
        Assert.Equal((1, 1), (hero.Equipment[EquipSlot.MainHand].Count, hero.Equipment[EquipSlot.OffHand].Count));

        Monster dummy = session.OnlyMonster();
        dummy.Stun.Add(powerful: false);

        // Each Dagger: 1, +1 for the pair, and its 1 again on a stunned monster (a stun adds its own +1 to the total).
        IReadOnlyList<GameEvent> first = session.Do(new AttackMonster(hero.Id, dummy.Id, Dice: 1));
        Assert.Equal(3, first.SingleOf<AttackRolled>().Bonus);
        IReadOnlyList<GameEvent> second = session.Do(new AttackMonster(hero.Id, dummy.Id, Dice: 1));
        Assert.Equal(3, second.SingleOf<AttackRolled>().Bonus);
    }

    [Fact]
    public void AThrownDaggerLeavesYourHandAndLandsAtTheTargetsFeet()
    {
        GameSession session = Duel(".1..d....", ["t_dagger"], new ScriptedDice(1));
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, dummy.Id));

        Assert.Equal(1, events.SingleOf<AttackRolled>().Bonus);
        Assert.Equal(dummy.Position, events.SingleOf<DaggerThrown>().LandedAt);
        Assert.False(hero.Equipment.ContainsKey(EquipSlot.OffHand));
        Assert.Contains(session.RoomById().LootAt(dummy.Position), card => card.Definition.Id == "t_dagger");
    }

    [Fact]
    public void AGreatAxesSixesCleaveIntoAnotherMonsterBesideYou()
    {
        // The roll: 2, the axe's own die a 6 (rolling a 1 straight in), +2: 11, against two Dummies with no defense.
        GameSession session = Duel("...d1d...", ["t_axe"], new ScriptedDice(2, 6, 1));
        Hero hero = session.HeroInSeat();
        Monster left = MonsterAt(session, 3), right = MonsterAt(session, 5);

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, left.Id));

        Assert.Equal(new Cleaved(hero.Id, right.Id), events.SingleOf<Cleaved>());
        Assert.Equal((2, 2), (left.Hearts, right.Hearts));
    }

    [Fact]
    public void AMaulsSixShovesTheTargetBack()
    {
        GameSession session = Duel("....1d...", ["t_maul"], new ScriptedDice(1, 6, 1));
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, dummy.Id));

        Assert.Single(events.OfType<Shoved>());
        Assert.Equal(new GridPosition(6, 4), dummy.Position);
    }

    [Fact]
    public void AWarHammerFlipsShellsAndItsBrokenBonesStayBroken()
    {
        GameSession session = Duel("....1Q...", ["t_hammer"], new ScriptedDice(5));
        Hero hero = session.HeroInSeat();
        Monster crab = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, crab.Id));
        Assert.Single(events.OfType<Flipped>());
        Assert.True(crab.Prone);

        GameSession boneyard = Duel("....1k...", ["t_hammer"], new ScriptedDice(5));
        Hero smith = boneyard.HeroInSeat();
        Monster bones = boneyard.OnlyMonster();
        bones.Hearts = 1;
        IReadOnlyList<GameEvent> smashed = boneyard.Do(new AttackMonster(smith.Id, bones.Id));
        Assert.Empty(smashed.OfType<MonsterCollapsed>());
        Assert.Single(smashed.OfType<MonsterDefeated>());
    }

    [Fact]
    public void ASpearReachesTwoTilesInAStraightLineOnly()
    {
        GameSession session = Duel("..1.d....", ["t_spear"], new ScriptedDice(1));
        Hero hero = session.HeroInSeat();
        RoomLayout layout = session.RoomById().Layout;

        Assert.True(GameSession.CanHeroReach(hero, layout, hero.Position, new GridPosition(4, 4)));
        Assert.False(GameSession.CanHeroReach(hero, layout, hero.Position, new GridPosition(3, 6)));
        Assert.Equal(2, session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id)).SingleOf<AttackRolled>().Bonus);
    }

    [Fact]
    public void ACrossbowShotUnloadsItUntilReloadedWithAnOdHandDie()
    {
        GameSession session = Duel("1.....d..", ["t_xbow"], new ScriptedDice(1, 1), attackDice: 2);
        Hero hero = session.HeroInSeat();
        int dummy = session.OnlyMonster().Id;

        Assert.Equal(3, session.Do(new AttackMonster(hero.Id, dummy, Dice: 1)).SingleOf<AttackRolled>().Bonus);
        Assert.True(hero.MainWeapon!.Unloaded);
        Assert.Equal("That monster is out of reach.", session.Refuse(new AttackMonster(hero.Id, dummy, Dice: 1)));

        session.Do(new ReloadWeapon(hero.Id));
        Assert.Equal(1, session.State.Turn!.OdHandDiceUsed);
        Assert.Equal(3, session.Do(new AttackMonster(hero.Id, dummy, Dice: 1)).SingleOf<AttackRolled>().Bonus);
    }

    [Fact]
    public void AHeavyCrossbowTakesAWendDieToReloadToo()
    {
        GameSession session = Duel("1.....d..", ["t_heavy"], new ScriptedDice(1));
        Hero hero = session.HeroInSeat();

        Assert.Contains("loaded", session.Refuse(new ReloadWeapon(hero.Id)));
        Assert.Equal(4, session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id)).SingleOf<AttackRolled>().Bonus);
        session.Do(new ReloadWeapon(hero.Id));
        Assert.Equal((1, 1), (session.State.Turn!.OdHandDiceUsed, session.State.Turn.MoveDiceUsed));
        Assert.False(hero.MainWeapon!.Unloaded);
    }
}
