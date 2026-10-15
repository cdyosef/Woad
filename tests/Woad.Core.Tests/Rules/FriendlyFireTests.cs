using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class FriendlyFireTests
{
    /// <summary>Hero 1 at (2,4) with a bomb in range of a dummy at (4,3); hero 2 beside the dummy at (4,4).</summary>
    private const string BlastRadius = """
        .........
        .........
        .........
        ....d....
        ..1.2....
        .........
        .........
        .........
        .........
        """;

    /// <summary>Hero 1 at (4,5) right behind hero 2 at (4,4), who stands at a bottomless pit's edge.</summary>
    private const string AtTheEdge = """
        .........
        .........
        .........
        ....~....
        ....2....
        ....1....
        .........
        .........
        .........
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ABlastCatchesAlliesOnlyWithFriendlyFireOn(bool friendlyFire)
    {
        GameSession session = TestRun.Start(BlastRadius, new ScriptedDice(5, 5, 1), heroes: 2, cards: ["bomb"], friendlyFire: friendlyFire);
        Hero thrower = session.HeroInSeat(0);
        Hero ally = session.HeroInSeat(1);

        IReadOnlyList<GameEvent> events = session.Do(new UseCard(thrower.Id, thrower.Card("bomb").Id, new GridPosition(4, 3)));

        int[] expected = friendlyFire ? [session.OnlyMonster().Id, ally.Id] : [session.OnlyMonster().Id];
        Assert.Equal(expected, events.SingleOf<AttackRolled>().TargetIds);
        Assert.Equal(friendlyFire ? ally.MaxHearts - 1 : ally.MaxHearts, ally.Hearts);
    }

    [Fact]
    public void WithFriendlyFireABlastCanBeAimedAtAlliesAlone()
    {
        GameSession off = TestRun.Start(AtTheEdge, heroes: 2, cards: ["bomb"]);
        GameSession on = TestRun.Start(AtTheEdge, heroes: 2, cards: ["bomb"], friendlyFire: true);

        Assert.Empty(off.GetCardTargets(off.HeroInSeat().Card("bomb").Id));
        Assert.Contains(new GridPosition(4, 4), on.GetCardTargets(on.HeroInSeat().Card("bomb").Id));
    }

    [Fact]
    public void AlliesCanBeAttackedOnlyWithFriendlyFireOnAndAFallenAllyIsNoKill()
    {
        GameSession off = TestRun.Start(AtTheEdge, heroes: 2);
        Assert.Contains("Friendly fire is off", off.Refuse(new AttackAlly(off.HeroInSeat(0).Id, off.HeroInSeat(1).Id)));
        Assert.Empty(off.GetAttackableAllies());

        GameSession session = TestRun.Start(AtTheEdge, new ScriptedDice(5, 1), heroes: 2, friendlyFire: true);
        Hero attacker = session.HeroInSeat(0);
        Hero ally = session.HeroInSeat(1);
        ally.Hearts = 1;
        Assert.Equal([ally], session.GetAttackableAllies());

        IReadOnlyList<GameEvent> events = session.Do(new AttackAlly(attacker.Id, ally.Id));

        Assert.Equal(new Damaged(ally.Id, 1, 0), events.SingleOf<Damaged>());
        Assert.Equal(HeroStatus.KeelingOver, ally.Status);
        Assert.Equal($"Struck down by {attacker.Name}", ally.Bane);
        Assert.Equal(0, attacker.Kills);
        Assert.Equal(0, attacker.Xp);
    }

    [Fact]
    public void AnAllyCanBeShovedIntoAPitOnlyWithFriendlyFireOn()
    {
        GameSession off = TestRun.Start(AtTheEdge, heroes: 2);
        Assert.Contains("nobody here to shove", off.Refuse(new Shove(off.HeroInSeat(0).Id, off.HeroInSeat(1).Id)));

        GameSession session = TestRun.Start(AtTheEdge, new ScriptedDice(3, 5, 1), heroes: 2, friendlyFire: true);
        Hero shover = session.HeroInSeat(0);
        Hero ally = session.HeroInSeat(1);
        session.Do(new RollMovement(shover.Id));

        IReadOnlyList<GameEvent> events = session.Do(new Shove(shover.Id, ally.Id));

        Assert.Equal(new Shoved(shover.Id, ally.Id, new GridPosition(4, 4), new GridPosition(4, 3)), events.SingleOf<Shoved>());
        Assert.True(events.SingleOf<FellIntoPit>().Bottomless);
        Assert.Equal(ally.MaxHearts - GameRules.FallDamage, ally.Hearts);
        Assert.NotEqual(new GridPosition(4, 3), ally.Position);
        Assert.Equal(5, session.State.Turn!.MovementPoints);
    }
}
