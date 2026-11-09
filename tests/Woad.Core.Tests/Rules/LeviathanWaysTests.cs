using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Leviathan Jr.'s ways: Elektrik absorbed and let go in a shockwave through its water, crawling on land, barrages that recharge.</summary>
public sealed class LeviathanWaysTests
{
    private const string Pool = """
        .........
        .........
        .,,,,,,,.
        .,,,,,,,.
        .,,,1,,,.
        .........
        .........
        ......§..
        .........
        """;

    private static GameSession Start(ScriptedDice dice, string[]? cards = null) =>
        TestRun.Start(Pool, dice, cards: cards, stats: TestRun.HeroStats with { DefenseDice = 0, Armor = 0 });

    [Fact]
    public void ElektrikDiceThatAttackItAreDrunkInAndStored()
    {
        GameSession session = Start(new ScriptedDice(4), ["bolt_elektrik"]);
        Hero hero = session.HeroInSeat();
        Monster absorber = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new UseCard(hero.Id, hero.Card("bolt_elektrik").Id, absorber.Position));
        while (session.State.Turn!.PendingRoll is not null)
        {
            events = [.. events, .. session.Do(new ConfirmAttack(hero.Id))];
        }

        Assert.Equal(new ElektrikAbsorbed(absorber.Id, 1, 1), events.SingleOf<ElektrikAbsorbed>());
        Assert.Equal((1, absorber.MaxHearts), (absorber.StoredElektrik, absorber.Hearts));
    }

    [Fact]
    public void InWaterItsStoredElektrikGoesOffThroughItAtTheStartOfItsTurn()
    {
        GameSession session = Start(new ScriptedDice(4, 3, 1, 1, 1, 1));
        Hero hero = session.HeroInSeat();
        Monster absorber = session.OnlyMonster();
        absorber.Position = new GridPosition(6, 3);
        absorber.StoredElektrik = 2;
        hero.Position = new GridPosition(3, 3);

        IReadOnlyList<GameEvent> events = session.EndRound();

        ShockwaveReleased shock = events.SingleOf<ShockwaveReleased>();
        Assert.Equal([hero.Id], shock.Struck);
        Assert.Equal(0, absorber.StoredElektrik);
    }

    [Fact]
    public void OnLandItsStoredElektrikFizzlesAndItCrawlsATileTowardTheWater()
    {
        GameSession session = Start(new ScriptedDice(1, 1, 1, 1));
        Monster absorber = session.OnlyMonster();
        absorber.StoredElektrik = 2;
        GridPosition from = absorber.Position;

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal(new ShockwaveFizzled(absorber.Id, 2), events.SingleOf<ShockwaveFizzled>());
        Assert.Equal(1, absorber.Position.ManhattanDistanceTo(from));
        Assert.True(absorber.Position.Y < from.Y, "it crawled toward the pool");
    }

    [Fact]
    public void ABarrageLoosedIsSpentTillItRechargesOnItsDie()
    {
        GameSession session = TestRun.Start(Pool.Replace('§', '¶'), new ScriptedDice(2, 1, 1, 1, 1, 1, 6, 2, 1, 1, 1, 1), stats: TestRun.HeroStats with { DefenseDice = 0, Armor = 20 });
        Monster barrager = session.OnlyMonster();

        Assert.Equal("Bolt", session.EndRound().SingleOf<BarrageLoosed>().Name);
        Assert.Contains("bolt", barrager.SpentBarrages);

        // A 1 on its recharge die: still spent, so its claws can't reach and it does nothing more.
        IReadOnlyList<GameEvent> second = session.EndRound();
        Assert.DoesNotContain(second, gameEvent => gameEvent is BarrageLoosed);
    }
}
