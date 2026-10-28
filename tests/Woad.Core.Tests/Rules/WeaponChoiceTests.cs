using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Holding a Spear and a Boomerang, the hero picks which one an attack is made with.</summary>
public class WeaponChoiceTests
{
    private const string Field = """
        .........
        .........
        .........
        .........
        ....1d...
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void LeftToTheRulesTheSpearStrikesWhatItReachesAndTheBoomerangTheRest()
    {
        GameSession session = TestRun.Start(Field, new ScriptedDice(2, 2), cards: ["spear", "boomerang"]);
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        Assert.Equal(("spear", 2), (session.WeaponAgainst(dummy)!.Value.Weapon!.Definition.Id, session.WeaponAgainst(dummy)!.Value.Bonus));
        dummy.Position = new GridPosition(8, 4);
        Assert.Equal(("boomerang", 1), (session.WeaponAgainst(dummy)!.Value.Weapon!.Definition.Id, session.WeaponAgainst(dummy)!.Value.Bonus));
        Assert.Equal(1, session.Do(new AttackMonster(hero.Id, dummy.Id)).SingleOf<AttackRolled>().Bonus);
    }

    [Fact]
    public void PickingTheBoomerangThrowsItEvenAtAMonsterTheSpearReaches()
    {
        // The Boomerang's +1 and a 6 (its Power Die banked): 7 against the dummy's nothing. The 6 stuns.
        GameSession session = TestRun.Start(Field, new ScriptedDice(6, 1), cards: ["spear", "boomerang"]);
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();
        int boomerang = hero.Card("boomerang").Id;

        Assert.Equal(1, session.WeaponBonusLeft(boomerang));
        Assert.Equal(1, session.Do(new AttackMonster(hero.Id, dummy.Id, WeaponId: boomerang)).SingleOf<AttackRolled>().Bonus);
        IReadOnlyList<GameEvent> events = session.Do(new BankPowerDie(hero.Id));

        Assert.Contains(events, gameEvent => gameEvent is Stunned);
    }

    [Fact]
    public void APickedWeaponThatDoesntReachIsRefused()
    {
        GameSession session = TestRun.Start(Field, cards: ["spear", "boomerang"]);
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();
        dummy.Position = new GridPosition(8, 4);
        int spear = hero.Card("spear").Id;

        Assert.DoesNotContain(dummy, session.GetAttackableMonsters(spear));
        Assert.Contains("Spear doesn't reach", session.Refuse(new AttackMonster(hero.Id, dummy.Id, WeaponId: spear)));
        Assert.Contains("aren't holding", session.Refuse(new AttackMonster(hero.Id, dummy.Id, WeaponId: 999)));
    }
}
