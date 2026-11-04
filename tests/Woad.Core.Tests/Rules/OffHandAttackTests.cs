using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;
using Xunit;

namespace Woad.Core.Tests.Rules;

/// <summary>The Od Hand card with something in the off hand: an attack with it, on top of the Rumble attack.</summary>
public sealed class OffHandAttackTests
{
    private static GameSession Duel(string row, string[] cards, ScriptedDice dice, int odHandRanks = 0)
    {
        GameSession session = TestRun.Start($"""
            .........
            .........
            .........
            .........
            {row}
            .........
            .........
            .........
            .........
            """, dice, stats: TestRun.HeroStats with { AttackDice = 1, OdHandDice = 2 }, cards: cards);
        Hero hero = session.HeroInSeat();
        hero.Allocation = new Woad.Core.Characters.StatAllocation { OdHandDice = odHandRanks, Magic = 3 };
        return session;
    }

    [Fact]
    public void AnOffHandDaggerFollowsUpWithOneOdHandDieTheOdHandBonusAndItsValue()
    {
        GameSession session = Duel("....1d...", ["t_rapier", "t_dagger"], new ScriptedDice(1, 1), odHandRanks: 2);
        Hero hero = session.HeroInSeat();
        int dummy = session.OnlyMonster().Id;
        Assert.Contains("Rumble first", session.Refuse(new OffHandAttack(hero.Id, dummy)));

        session.Do(new AttackMonster(hero.Id, dummy, Dice: 1));
        AttackRolled offHand = session.Do(new OffHandAttack(hero.Id, dummy)).SingleOf<AttackRolled>();

        // One die; 2 for the Od Hand ranks (the Magic ranks don't count) and the Dagger's 1.
        Assert.Single(offHand.Roll.Faces);
        Assert.Equal(3, offHand.Bonus);
        Assert.Equal(1, session.State.Turn!.OdHandDiceUsed);
        Assert.Contains("already attacked with your off hand", session.Refuse(new OffHandAttack(hero.Id, dummy)));
    }

    [Fact]
    public void ATorchInTheOffHandAttacksWithAFireDie()
    {
        // The Rumble attack: a die and the torch's fire die; then the follow-up's fire die.
        GameSession session = Duel("....1d...", ["t_torch"], new ScriptedDice(2, 2, 3));
        Hero hero = session.HeroInSeat();
        session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id, Dice: 1));

        AttackRolled attack = session.Do(new OffHandAttack(hero.Id, session.OnlyMonster().Id)).SingleOf<AttackRolled>();

        Assert.Equal([DieType.Fire], attack.DieTypes);
    }

    [Fact]
    public void AnOffHandRockIsThrownAndGoneUnlessItLandsOnASix()
    {
        // The Rumble attack throws one Rock, the follow-up the other.
        GameSession session = Duel(".1.d.....", ["t_rock", "t_rock"], new ScriptedDice(2, 2));
        Hero hero = session.HeroInSeat();
        session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id, Dice: 1));

        IReadOnlyList<GameEvent> events = session.Do(new OffHandAttack(hero.Id, session.OnlyMonster().Id));

        Assert.Single(events.OfType<AttackRolled>());
        Assert.False(hero.Equipment.ContainsKey(EquipSlot.OffHand));
    }

    [Fact]
    public void AStickHeldToAFireLightsAndBurnsOutAfterFiveRounds()
    {
        // Two Sticks in the off hand and a torch in the main: one lights; the other goes to the bag.
        GameSession session = Duel("....1....", ["t_torch", "t_stick", "t_stick"], new ScriptedDice());
        Hero hero = session.HeroInSeat();
        CardInstance sticks = hero.Card("t_stick");
        session.Do(new EquipCard(hero.Id, hero.Card("t_torch").Id, EquipSlot.MainHand));
        session.Do(new EquipCard(hero.Id, sticks.Id, EquipSlot.OffHand));
        Assert.Null(session.CheckLightStick());

        IReadOnlyList<GameEvent> events = session.Do(new LightStick(hero.Id));

        CardInstance lit = hero.Equipment[EquipSlot.OffHand];
        Assert.Equal("t_lit_stick", lit.Definition.Id);
        Assert.Equal(lit.Id, events.SingleOf<StickLit>().CardId);
        Assert.Equal(2, hero.TorchesHeld);
        Assert.Contains(hero.AllCards, card => card.Definition.Id == "t_stick" && card.Count == 1);

        for (int round = 0; round < 5; round++)
        {
            session.Do(new EndTurn(hero.Id));
        }

        Assert.DoesNotContain(hero.AllCards, card => card.Definition.Id == "t_lit_stick");
        Assert.Equal(1, hero.TorchesHeld);
    }

    [Fact]
    public void WithNoFireNearAStickWontLight()
    {
        GameSession session = Duel("....1....", ["t_stick"], new ScriptedDice());
        Hero hero = session.HeroInSeat();

        Assert.Contains("no fire beside you", session.Refuse(new LightStick(hero.Id)));
    }

    [Fact]
    public void ARockIsThrownAtRangeThreeAndAStickAtTwo()
    {
        GameSession rock = Duel("1..d.....", ["t_rock", "t_rock"], new ScriptedDice(2, 2));
        rock.Do(new AttackMonster(rock.HeroInSeat().Id, rock.OnlyMonster().Id, Dice: 1));
        Assert.Single(rock.Do(new OffHandAttack(rock.HeroInSeat().Id, rock.OnlyMonster().Id)).OfType<AttackRolled>());

        // A Stick in the off hand, the monster as good as attacked with Rumble already.
        GameSession stick = Duel("1..d.....", ["t_stick"], new ScriptedDice());
        Hero hero = stick.HeroInSeat();
        stick.Do(new EquipCard(hero.Id, hero.Card("t_stick").Id, EquipSlot.OffHand));
        stick.State.Turn!.RumbledIds.Add(stick.OnlyMonster().Id);
        Assert.Contains("out of reach", stick.Refuse(new OffHandAttack(hero.Id, stick.OnlyMonster().Id)));
    }

    [Fact]
    public void NothingInTheOffHandNoOffHandAttack()
    {
        GameSession session = Duel("....1d...", [], new ScriptedDice());
        Hero hero = session.HeroInSeat();

        Assert.Contains("nothing in your off hand", session.Refuse(new OffHandAttack(hero.Id, session.OnlyMonster().Id)));
    }
}
