using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Allies who let themselves be pushed: free, no roll, no Shove skill or friendly fire.</summary>
public class AllyPushTests
{
    private const string Side = """
        .........
        .........
        .........
        .........
        ...12....
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void AnAllyWhoLetsYouIsPushedATileAwayForFree()
    {
        GameSession session = TestRun.Start(Side, heroes: 2, skills: Skill.None);
        Hero hero = session.HeroInSeat(0), ally = session.HeroInSeat(1);
        int rumble = session.AttackDiceLeft;
        Assert.NotNull(session.CheckShove(ally.Id));
        Assert.Contains(ally.Position, session.GetShoveTargets());

        IReadOnlyList<GameEvent> events = session.Do(new NudgeAlly(hero.Id, ally.Id));

        Assert.Equal(new GridPosition(5, 4), ally.Position);
        Assert.Contains(events, gameEvent => gameEvent is Shoved);
        Assert.Equal(rumble, session.AttackDiceLeft);
    }

    [Fact]
    public void AnAllyOutOfReachOrBlockedCantBePushed()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ...1.2#..
            .........
            .........
            .........
            .........
            """, heroes: 2);
        Hero hero = session.HeroInSeat(0), ally = session.HeroInSeat(1);

        Assert.Contains("next to them", session.CheckNudgeAlly(ally.Id));
        hero.Position = new GridPosition(4, 4);
        Assert.Contains("in the way", session.CheckNudgeAlly(ally.Id));
    }
}
