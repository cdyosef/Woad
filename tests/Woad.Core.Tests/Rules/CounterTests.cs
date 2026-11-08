using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Hitting back: the Emerald Knight at whoever attacks him, and a hero with the Emerald Axe on a Power Die off their defense.</summary>
public class CounterTests
{
    private static GameSession Duel(string row, ScriptedDice dice, string[]? cards = null) => TestRun.Start($"""
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

    [Fact]
    public void AttackedHitOrMissTheRiposterHitsBackWithADieItsWeaponAndALifeDie()
    {
        GameSession session = Duel("....1r...", new ScriptedDice(1, 1, 1, 3, 3, 3, 3, 3));
        Hero hero = session.HeroInSeat();
        Monster riposter = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, riposter.Id));

        Assert.Equal(new Countered(riposter.Id, hero.Id), events.SingleOf<Countered>());
        AttackRolled counter = events.OfType<AttackRolled>().Single(attack => attack.AttackerId == riposter.Id);
        Assert.Equal((2, 2), (counter.Roll.Faces.Count, counter.Bonus));
        Assert.Equal([DieType.Plain, DieType.Life], counter.DieTypes);
    }

    [Fact]
    public void StunnedOrOutOfReachItDoesntHitBack()
    {
        GameSession session = Duel("....1r...", new ScriptedDice(1, 1, 1, 3, 3, 3));
        Monster riposter = session.OnlyMonster();
        riposter.Stun.Add(powerful: false);
        Assert.Empty(session.Do(new AttackMonster(session.HeroInSeat().Id, riposter.Id)).OfType<Countered>());
    }

    [Fact]
    public void WithTheEmeraldAxeAPowerDieOnTheDefenseHitsBackOnceARound()
    {
        // The brute's two dice (5, 5); the hero's defense comes up a 6, its Power Die rolled in; then the axe hits back.
        GameSession session = Duel("....1B...", new ScriptedDice(5, 5, 6, 2, 2, 4, 1, 3, 3, 3, 3, 3, 3, 3, 3, 3), cards: ["t_emerald_axe"]);
        Hero hero = session.HeroInSeat();
        Monster brute = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Countered countered = Assert.Single(events.OfType<Countered>());
        Assert.Equal((hero.Id, brute.Id), (countered.EntityId, countered.TargetId));
        AttackRolled blow = events.OfType<AttackRolled>().Single(attack => attack.AttackerId == hero.Id);
        Assert.Contains(DieType.Life, blow.DieTypes);
        Assert.True(blow.PowerDice >= 1, "the defense's Power Dice roll into the blow");
        Assert.Equal(1, hero.CounteredRound);
    }
}
