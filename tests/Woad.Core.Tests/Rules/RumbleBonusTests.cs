using Woad.Core.Characters;
using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;
using Xunit;

namespace Woad.Core.Tests.Rules;

/// <summary>The Rumble Bonus: +1 for each Rumble rank bought, on one Rumble attack a turn.</summary>
public sealed class RumbleBonusTests
{
    private static (GameSession Session, Hero Hero, int Dummy) Duel()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1d...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(1, 1, 1), stats: TestRun.HeroStats with { AttackDice = 3 });
        Hero hero = session.HeroInSeat();
        hero.Allocation = new StatAllocation { AttackDice = 2 };
        return (session, hero, session.OnlyMonster().Id);
    }

    [Fact]
    public void TheRumbleBonusGoesOnTheFirstAttack()
    {
        (GameSession session, Hero hero, int dummy) = Duel();

        Assert.Equal(2, session.Do(new AttackMonster(hero.Id, dummy, Dice: 1)).SingleOf<AttackRolled>().Bonus);
        Assert.Equal(0, session.Do(new AttackMonster(hero.Id, dummy, Dice: 1)).SingleOf<AttackRolled>().Bonus);
    }

    [Fact]
    public void HeldBackItGoesOnTheAttackThatRollsTheLastRumbleDice()
    {
        (GameSession session, Hero hero, int dummy) = Duel();

        Assert.Equal(0, session.Do(new AttackMonster(hero.Id, dummy, Dice: 1, HoldRumbleBonus: true)).SingleOf<AttackRolled>().Bonus);
        Assert.Equal(2, session.RumbleBonusLeft);
        Assert.Equal(2, session.Do(new AttackMonster(hero.Id, dummy, Dice: 2, HoldRumbleBonus: true)).SingleOf<AttackRolled>().Bonus);
        Assert.Equal(0, session.RumbleBonusLeft);
    }
}
