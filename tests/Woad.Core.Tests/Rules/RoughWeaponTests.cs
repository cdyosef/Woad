using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Weapons that don't last: the Club breaks on a 1, a Stick or a Rock with the blow; Rocks are thrown or slung, and land whole on a 6.</summary>
public sealed class RoughWeaponTests
{
    private static GameSession Duel(string row, string[] cards, ScriptedDice dice, int attackDice = 1) =>
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
            """, dice, stats: TestRun.HeroStats with { AttackDice = attackDice }, cards: cards);

    [Fact]
    public void AClubBreaksOnAOne()
    {
        GameSession session = Duel("....1d...", ["t_club"], new ScriptedDice(3, 1), attackDice: 2);
        Hero hero = session.HeroInSeat();
        int dummy = session.OnlyMonster().Id;
        int club = hero.Card("t_club").Id;

        IReadOnlyList<GameEvent> first = session.Do(new AttackMonster(hero.Id, dummy, Dice: 1));
        Assert.Equal(1, first.SingleOf<AttackRolled>().Bonus);
        Assert.Empty(first.OfType<WeaponBroke>());

        // A 1 in the next blow: the club's gone.
        session.State.Turn!.StrikesUsed = 0;
        IReadOnlyList<GameEvent> second = session.Do(new AttackMonster(hero.Id, dummy, Dice: 1));
        Assert.Equal(club, second.SingleOf<WeaponBroke>().CardId);
        Assert.False(hero.Equipment.ContainsKey(EquipSlot.MainHand));
    }

    [Fact]
    public void AStickBreaksWithTheBlow()
    {
        GameSession session = Duel("....1d...", ["t_stick", "t_stick"], new ScriptedDice(4));
        Hero hero = session.HeroInSeat();
        CardInstance sticks = hero.Equipment[EquipSlot.MainHand];
        Assert.Equal(2, sticks.Count);

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id));

        Assert.Equal(1, events.SingleOf<AttackRolled>().Bonus);
        Assert.Equal(sticks.Id, events.SingleOf<WeaponBroke>().CardId);
        Assert.Equal(1, sticks.Count);
    }

    [Fact]
    public void AThrownRockIsGoneUnlessASixBringsItDownWhole()
    {
        GameSession session = Duel("..1.d....", ["t_rock", "t_rock"], new ScriptedDice(3, 6), attackDice: 2);
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();
        CardInstance rocks = hero.Equipment[EquipSlot.OffHand];

        IReadOnlyList<GameEvent> missed = session.Do(new AttackMonster(hero.Id, dummy.Id, Dice: 1, WeaponId: rocks.Id));
        Assert.Equal(1, missed.SingleOf<AttackRolled>().Bonus);
        Assert.Equal(rocks.Id, missed.SingleOf<WeaponBroke>().CardId);
        Assert.Empty(session.RoomById().LootAt(dummy.Position));

        session.State.Turn!.StrikesUsed = 0;
        // The 6 earns a Power Die: banked, the roll lands.
        IReadOnlyList<GameEvent> landed = [.. session.Do(new AttackMonster(hero.Id, dummy.Id, Dice: 1, WeaponId: hero.Equipment[EquipSlot.OffHand].Id)), .. session.Do(new BankPowerDie(hero.Id))];
        Assert.Equal(dummy.Position, landed.SingleOf<StoneLanded>().LandedAt);
        Assert.Single(session.RoomById().LootAt(dummy.Position), card => card.Definition.Id == "t_rock");
        Assert.Null(hero.FindCard(rocks.Id));
    }

    [Fact]
    public void ASlingShootsARockFromTheOffHand()
    {
        GameSession empty = Duel("..1...d..", ["t_sling"], new ScriptedDice(4));
        Hero unarmed = empty.HeroInSeat();
        empty.Refuse(new AttackMonster(unarmed.Id, empty.OnlyMonster().Id));

        GameSession session = Duel("..1...d..", ["t_sling", "t_rock", "t_rock"], new ScriptedDice(4));
        Hero hero = session.HeroInSeat();
        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id));

        // The Sling's 1, and the Rock's.
        Assert.Equal(2, events.SingleOf<AttackRolled>().Bonus);
        Assert.Single(events.OfType<WeaponBroke>());
        Assert.Equal(1, hero.Card("t_rock").Count);
    }

    [Fact]
    public void ARockBagLetsTheSlingLoadFromTheBelt()
    {
        GameSession bare = Duel("..1...d..", ["t_sling", "shield", "t_rock"], new ScriptedDice(4));
        Hero without = bare.HeroInSeat();
        Assert.Contains(without.Card("t_rock"), without.Belt);
        bare.Refuse(new AttackMonster(without.Id, bare.OnlyMonster().Id));

        GameSession session = Duel("..1...d..", ["t_sling", "shield", "t_rockbag", "t_rock"], new ScriptedDice(4));
        Hero hero = session.HeroInSeat();
        CardInstance rock = hero.Card("t_rock");
        Assert.Equal(CardLocation.Bag, hero.FindCard(rock.Id)!.Value.Location);

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id));

        Assert.Equal(2, events.SingleOf<AttackRolled>().Bonus);
        Assert.Null(hero.FindCard(rock.Id));
    }

    [Fact]
    public void RocksAndSticksStackToTenAndARockBagHoldsAHundred()
    {
        string[] tenRocks = [.. Enumerable.Repeat("t_rock", 10)];
        GameSession session = Duel("....1d...", ["shield", .. tenRocks], new ScriptedDice());
        Hero hero = session.HeroInSeat();
        var rock = TestContent.Catalog.GetCard("t_rock");
        Assert.Equal(10, hero.Card("t_rock").Count);
        Assert.False(hero.CanCarry(rock));

        // Two more lie on the hero's tile: they stay there. With room for one, only one is picked up.
        Room room = session.RoomById();
        room.AddLoot(hero.Position, [new CardInstance(session.State.NextId(), rock, null, count: 2)]);
        Assert.Contains("any more", session.Refuse(new PickUpLoot(hero.Id)));
        session.Do(new DropCard(hero.Id, hero.Card("t_rock").Id, Count: 1));
        session.Do(new PickUpLoot(hero.Id));
        Assert.Equal(10, hero.Card("t_rock").Count);
        Assert.Equal(2, room.LootAt(hero.Position).Sum(card => card.Count));

        GameSession bagged = Duel("....1d...", ["shield", "t_rockbag", .. tenRocks], new ScriptedDice());
        Hero bagger = bagged.HeroInSeat();
        Assert.Equal(100, bagger.StackLimit(rock));
        // The ten start in a hand; the bag, empty, has room for a hundred more.
        Assert.Equal(10, bagger.Equipment[EquipSlot.MainHand].Count);
        Assert.Equal(100, bagger.RoomFor(rock));
    }

    [Fact]
    public void RocksPickedUpGoIntoTheRockBagAHandfulComesOutFreeAndAnEmptiedHandFillsAgain()
    {
        // The Rock in hand, thrown (a 2: it's gone); the hand fills again from the bag.
        GameSession session = Duel(".1..d....", ["t_rockbag", "t_rock"], new ScriptedDice(2));
        Hero hero = session.HeroInSeat();
        CardDefinition rock = hero.Card("t_rock").Definition;
        Room room = session.RoomById();

        // Picked up, Rocks go straight into the bag on the belt, not the hand.
        room.AddLoot(hero.Position, [new CardInstance(session.State.NextId(), rock, null, count: 14)]);
        session.Do(new PickUpLoot(hero.Id));
        CardInstance bag = hero.BagStack(rock)!;
        Assert.Equal(14, bag.Count);

        // A handful (ten at most) drawn into the main hand: free, even in a fight.
        session.Do(new EquipCard(hero.Id, bag.Id, EquipSlot.MainHand));
        Assert.Equal((10, 4, false), (hero.Equipment[EquipSlot.MainHand].Count, hero.BagStack(rock)!.Count, session.State.Turn!.HasActed));
        Assert.True(session.State.Turn.BagDrawn);

        // The hand of ten thrown down to its last, then that one thrown: the hand fills from the bag again.
        CardInstance hand = hero.Equipment[EquipSlot.MainHand];
        hand.Count = 1;
        session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id, WeaponId: hand.Id));
        Assert.Equal(4, hero.Equipment[EquipSlot.MainHand].Count);
        Assert.Null(hero.BagStack(rock));
    }
}
