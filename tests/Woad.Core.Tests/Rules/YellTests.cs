using Woad.Core.Events;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;
using Xunit;

namespace Woad.Core.Tests.Rules;

public sealed class YellTests
{
    private const string Yard = """
        .........
        .........
        ....y....
        .........
        ....1....
        .........
        """;

    [Fact]
    public void AYellerYellsAtTheStartOfEveryTurnItTakes()
    {
        GameSession session = TestRun.Start(Yard);
        Monster yeller = session.OnlyMonster();

        Assert.Equal(new MonsterYelled(yeller.Id, "GRA!!"), session.EndRound().SingleOf<MonsterYelled>());
        Assert.Single(session.EndRound().OfType<MonsterYelled>());
    }

    [Fact]
    public void AFrozenYellerHasNoBreathToYell()
    {
        GameSession session = TestRun.Start(Yard);
        session.OnlyMonster().FrozenTurns = 1;

        Assert.DoesNotContain(session.EndRound(), gameEvent => gameEvent is MonsterYelled);
    }
}
