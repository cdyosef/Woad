using Woad.Core.Bots;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>An unpaid companion nobody took on, leaving: last offers, talking them round, a change of heart, and going.</summary>
public sealed class LeavingTests
{
    private const string Quiet = """
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

    private static (GameSession Session, Hero Leader, Hero Clod) Start(LeavingPlan plan, params int[] dice)
    {
        GameSession session = TestRun.Start(Quiet, new ScriptedDice(dice), heroes: 2, owners: [null, 0]);
        Hero leader = session.HeroInSeat(0), clod = session.HeroInSeat(1);
        clod.Wage = Money.Gold(10);
        clod.Leaving = true;
        clod.LeavingTurns = 1;
        clod.LeavingPlan = plan;
        return (session, leader, clod);
    }

    [Fact]
    public void AtTheEntranceTheyWaitForOffersThenWalkOut()
    {
        (GameSession session, Hero leader, Hero clod) = Start(LeavingPlan.Entrance, 1, 1, 1, 1);

        IReadOnlyList<GameEvent> waiting = session.Do(new EndTurn(leader.Id));
        Assert.Equal(clod.Id, waiting.SingleOf<HeroSpoke>().HeroId);
        Assert.IsType<EndTurn>(CompanionBot.NextCommand(session));
        session.Do(new EndTurn(clod.Id));

        session.Do(new EndTurn(leader.Id));
        Assert.Equal(LeavingWhy.NoOffers, clod.LeavingWhy);
        IReadOnlyList<GameEvent> gone = session.Do(CompanionBot.NextCommand(session));

        Assert.Equal(new CompanionDeparted(clod.Id), gone.SingleOf<CompanionDeparted>());
        Assert.True(clod.Departed);
        Assert.Equal(HeroStatus.Escaped, clod.Status);
        Assert.Equal(leader.Id, session.State.Turn!.HeroId);
    }

    [Fact]
    public void ABondMakesTalkingThemRoundEasier()
    {
        (GameSession session, Hero leader, Hero clod) = Start(LeavingPlan.Entrance, 5, 1);
        ConvinceRolled cold = session.Do(new Convince(leader.Id, clod.Id)).SingleOf<ConvinceRolled>();
        Assert.False(cold.Success);
        Assert.Contains("next turn", session.Refuse(new Convince(leader.Id, clod.Id)));

        (session, leader, clod) = Start(LeavingPlan.Entrance, 1);
        clod.Bonds[leader.Id] = 5;
        IReadOnlyList<GameEvent> warm = session.Do(new Convince(leader.Id, clod.Id));

        Assert.True(warm.SingleOf<ConvinceRolled>().Success);
        Assert.Equal(new CompanionStayed(clod.Id, leader.Id, StayReason.Convinced), warm.SingleOf<CompanionStayed>());
        Assert.False(clod.Leaving);
    }

    [Fact]
    public void PaidWhatIsOwedLessTheirBondTheyStayAndFollowWhoeverPaid()
    {
        (GameSession session, Hero leader, Hero clod) = Start(LeavingPlan.Entrance);
        leader.Coins = Money.Gold(20);
        clod.Bonds[leader.Id] = 3;
        int had = clod.Coins;

        Assert.Equal(Money.Gold(7), session.StayPrice(clod, leader));
        Assert.Contains("wants", session.Refuse(new OfferWage(leader.Id, clod.Id, Money.Gold(6))));
        session.Do(new OfferWage(leader.Id, clod.Id, Money.Gold(7)));

        Assert.Equal((Money.Gold(13), false, leader.Id), (leader.Coins, clod.Leaving, clod.OwnerId));
        Assert.Equal(had + Money.Gold(7), clod.Coins);
    }

    [Fact]
    public void TheyMayHaveAChangeOfHeart()
    {
        (GameSession session, Hero leader, Hero clod) = Start(LeavingPlan.Entrance, 6);

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(leader.Id));

        Assert.Equal(StayReason.ChangeOfHeart, events.SingleOf<CompanionStayed>().Reason);
        Assert.False(clod.Leaving);
    }

    [Fact]
    public void ComingAlongTheyStillGoWhenItGetsTooHot()
    {
        (GameSession session, Hero leader, Hero clod) = Start(LeavingPlan.FewRooms, 1, 1);
        session.Do(new EndTurn(leader.Id));
        Assert.Null(clod.LeavingWhy);
        Assert.False(clod.Lingering);
        session.Do(new EndTurn(clod.Id));

        clod.Hearts = 1;
        session.Do(new EndTurn(leader.Id));

        Assert.Equal(LeavingWhy.Hurt, clod.LeavingWhy);
        Assert.IsType<Depart>(CompanionBot.NextCommand(session));
    }
}
