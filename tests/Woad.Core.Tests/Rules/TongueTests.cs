using Woad.Core.Events;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>A tongue (Leviathan Jr.'s): with nobody in reach of its blows, it lashes out at a hero up to 5 tiles off, in sight.</summary>
public sealed class TongueTests
{
    private static GameSession Yard(string row, ScriptedDice dice) =>
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
            """, dice, stats: TestRun.HeroStats with { DefenseDice = 0, Armor = 20 });

    [Fact]
    public void BeyondItsClawsItsTongueLashesAHeroWithinFiveWithoutItsWeapon()
    {
        GameSession session = Yard("..1...$..", new ScriptedDice(3, 1, 1, 1));
        Hero hero = session.HeroInSeat();
        Monster licker = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal(new TongueLashed(licker.Id, hero.Id), events.SingleOf<TongueLashed>());
        Assert.Equal(0, events.OfType<AttackRolled>().Single(attack => attack.AttackerId == licker.Id).Bonus);
    }

    [Fact]
    public void FurtherThanFiveItsTongueDoesntReach()
    {
        GameSession session = Yard("1.......$", new ScriptedDice(3, 1, 1, 1));

        Assert.DoesNotContain(session.EndRound(), gameEvent => gameEvent is TongueLashed);
    }
}
