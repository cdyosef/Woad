using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Elektrik through water: the same roll strikes every other creature in that body of water within 8, friend or foe.</summary>
public sealed class ConductionTests
{
    // A long pool across the room; the Zapper on its bank, a dummy in the water.
    private const string Pool = """
        .........
        .........
        .........
        ....¤....
        ,,,,,,,,,
        ,,,,,,,d,
        .........
        .........
        .1.....2.
        """;

    private static GameSession Start(ScriptedDice dice) =>
        TestRun.Start(Pool, dice, heroes: 2, stats: TestRun.HeroStats with { DefenseDice = 0, Armor = 0 });

    [Fact]
    public void AnElektrikBlowOnSomeoneInWaterStrikesEveryoneElseInIt()
    {
        GameSession session = Start(new ScriptedDice(5, 1, 1, 1, 1, 1, 1, 1, 1, 1));
        Hero struck = session.HeroInSeat(0), ally = session.HeroInSeat(1);
        Monster dummy = session.RoomById().Monsters.Single(monster => monster.Definition.Id == "dummy");
        struck.Position = new GridPosition(4, 4);
        ally.Position = new GridPosition(1, 5);
        dummy.Position = new GridPosition(7, 4);

        IReadOnlyList<GameEvent> events = session.EndRound();

        CurrentConducted current = events.SingleOf<CurrentConducted>();
        Assert.Equal(new GridPosition(4, 4), current.At);
        Assert.Equal([ally.Id, dummy.Id], current.Struck);
        Assert.Contains(events, gameEvent => gameEvent is Damaged { Amount: > 0 } damaged && damaged.TargetId == ally.Id);
        Assert.Contains(events, gameEvent => gameEvent is Damaged { Amount: > 0 } damaged && damaged.TargetId == dummy.Id);
    }

    [Fact]
    public void ThoseOnDryLandAreSpared()
    {
        GameSession session = Start(new ScriptedDice(5, 1, 1, 1, 1, 1, 1, 1));
        Hero struck = session.HeroInSeat(0), ally = session.HeroInSeat(1);
        struck.Position = new GridPosition(4, 4);

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.DoesNotContain(events, gameEvent => gameEvent is CurrentConducted current && current.Struck.Contains(ally.Id));
    }

    [Fact]
    public void OnDryLandItRunsNowhere()
    {
        GameSession session = Start(new ScriptedDice(5, 1, 1, 1, 1, 1, 1, 1));
        session.HeroInSeat(0).Position = new GridPosition(4, 2);

        Assert.DoesNotContain(session.EndRound(), gameEvent => gameEvent is CurrentConducted);
    }
}
