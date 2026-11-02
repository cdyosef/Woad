using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Handing a hero over mid-run: a companion made a player's hero, or a player's hero made another player's companion.</summary>
public sealed class SwitchControlTests
{
    private const string Room = """
        .........
        .........
        .........
        .........
        ....1....
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void ACompanionCanBeMadeAHero()
    {
        GameSession session = TestRun.Start(Room, heroes: 2, owners: [null, 0]);
        Hero player = session.HeroInSeat(), companion = session.HeroInSeat(1);
        Assert.True(companion.IsCompanion);

        IReadOnlyList<GameEvent> events = session.Do(new SwitchControl(player.Id, companion.Id));

        Assert.Equal(new ControlSwitched(player.Id, companion.Id, null), events.SingleOf<ControlSwitched>());
        Assert.False(companion.IsCompanion);
        Assert.Equal(2, companion.PlayerNumber);
    }

    [Fact]
    public void AHeroCanBeHandedToAnotherPlayerWithItsCompanions()
    {
        // Seat 0's hero, seat 1's, and seat 0's companion.
        GameSession session = TestRun.Start(Room, heroes: 3, owners: [null, null, 0]);
        Hero first = session.HeroInSeat(), second = session.HeroInSeat(1), follower = session.HeroInSeat(2);

        session.Do(new SwitchControl(first.Id, first.Id, second.Id));

        Assert.Equal((second.Id, second.Id), (first.OwnerId, follower.OwnerId));
        Assert.Equal(0, first.PlayerNumber);
        Assert.Equal(CompanionOrder.Follow, first.Order);
    }

    [Fact]
    public void TheLastHeroCantBeHandedOverNorAnotherPlayersHero()
    {
        GameSession pair = TestRun.Start(Room, heroes: 2, owners: [null, 0]);
        Hero player = pair.HeroInSeat(), companion = pair.HeroInSeat(1);
        Assert.Contains("another player", pair.Refuse(new SwitchControl(player.Id, player.Id, companion.Id)));
        Assert.Contains("another player", pair.Refuse(new SwitchControl(player.Id, player.Id, player.Id)));

        GameSession two = TestRun.Start(Room, heroes: 2);
        Assert.Contains("not your hero", two.Refuse(new SwitchControl(two.HeroInSeat().Id, two.HeroInSeat(1).Id)));
        Assert.Contains("hero already", two.Refuse(new SwitchControl(two.HeroInSeat().Id, two.HeroInSeat().Id)));
    }
}
