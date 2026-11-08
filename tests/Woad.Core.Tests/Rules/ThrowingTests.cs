using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Things thrown: an ogre's Rocks (twice as far as anyone) and Boulders, which roll on, break and burst.</summary>
public sealed class ThrowingTests
{
    private static GameSession Duel(string row, string[] cards, ScriptedDice dice) =>
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
            """, dice, cards: cards);

    private static CardInstance Rocks(GameSession session, int count) =>
        new(session.State.NextId(), session.Content.GetCard("rock"), broughtInByHeroId: null, count);

    [Fact]
    public void WithNobodyInReachAThrowerHurlsARockTwiceAsFarAsAnyoneElse()
    {
        GameSession session = Duel("1.....l..", [], new ScriptedDice(3, 1, 1, 1, 1));
        Hero hero = session.HeroInSeat();
        Monster lobber = session.OnlyMonster();
        lobber.Holding = Rocks(session, 2);
        Assert.Equal(6, GameSession.ThrowRange(lobber, lobber.Holding.Definition));

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal(hero.Id, events.SingleOf<MonsterThrew>().TargetId);
        Assert.Single(events.OfType<ThrownShattered>());
        Assert.Equal(1, lobber.Holding!.Count);
    }

    [Fact]
    public void ARockThrownWithASixLandsWholeAtTheTargetsFeet()
    {
        GameSession session = Duel("1.....l..", [], new ScriptedDice(6, 1, 1, 1, 1, 1));
        Hero hero = session.HeroInSeat();
        Monster lobber = session.OnlyMonster();
        lobber.Holding = Rocks(session, 1);

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Single(events.OfType<StoneLanded>());
        Assert.Null(lobber.Holding);
        Assert.Contains(session.RoomById().LootAt(hero.Position), card => card.Definition.Id == "rock");
    }

    [Fact]
    public void OutOfRangeItKeepsItsRocks()
    {
        GameSession session = Duel("1.......l", [], new ScriptedDice(3, 1, 1, 1));
        Monster lobber = session.OnlyMonster();
        lobber.Holding = Rocks(session, 2);

        Assert.DoesNotContain(session.EndRound(), gameEvent => gameEvent is MonsterThrew);
        Assert.Equal(2, lobber.Holding!.Count);
    }

    [Fact]
    public void WithNothingToThrowItPicksBackUpARockBesideIt()
    {
        GameSession session = Duel("1.......l", [], new ScriptedDice(3, 1, 1, 1));
        Monster lobber = session.OnlyMonster();
        lobber.Holding = null;
        session.RoomById().AddLoot(new GridPosition(7, 4), [Rocks(session, 1)]);

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal(lobber.Id, events.SingleOf<MonsterPickedUp>().MonsterId);
        Assert.Equal("rock", lobber.Holding!.Definition.Id);
        Assert.Empty(session.RoomById().LootAt(new GridPosition(7, 4)));
    }

    [Fact]
    public void AThrownBoulderRollsOnStrikingWhoeverItMeetsAndRestsWhereItStops()
    {
        GameSession session = Duel("....1d.d.", ["t_boulder"], new ScriptedDice(3, 4, 2, 4));
        Hero hero = session.HeroInSeat();
        Monster first = session.RoomById().Monsters.Single(monster => monster.Position == new GridPosition(5, 4));

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, first.Id));

        BoulderRolled rolled = events.SingleOf<BoulderRolled>();
        Assert.Equal(new GridPosition(8, 4), rolled.Rest);
        Assert.Equal(2, events.OfType<Damaged>().Count(damaged => damaged.Amount > 0));
        Assert.Contains(session.RoomById().LootAt(rolled.Rest), card => card.Definition.Id == "t_boulder");
        Assert.Null(hero.MainWeapon);
    }

    [Fact]
    public void AOneOnBothItsWeaponDieAndItsEarthDieBreaksABoulderIntoRocks()
    {
        GameSession session = Duel("....1d...", ["t_boulder"], new ScriptedDice(3, 1, 1));
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, dummy.Id));

        Assert.False(events.SingleOf<BoulderBroke>().Burst);
        Assert.DoesNotContain(events, gameEvent => gameEvent is BoulderRolled);
        Assert.Contains(session.RoomById().LootAt(dummy.Position), card => card.Definition.Id == "rock");
    }
}
