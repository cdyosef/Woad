using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>The Big Club: an ogre's weapon (or a hero's, two-handed): 2 weapon dice, reach 2, a shove for each 6, broken by a double 1.</summary>
public sealed class BigClubTests
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

    [Fact]
    public void AMonsterWieldingABigClubReachesTwoAndRollsItsTwoDiceIn()
    {
        GameSession session = Duel("....1.q..", [], new ScriptedDice(3, 2, 4, 1, 1, 1, 1));
        Monster clubber = session.OnlyMonster();
        Assert.Equal((AttackPattern.Reach, 2, 2), (clubber.AttackPattern, clubber.AttackRange, clubber.WeaponDice));

        AttackRolled attack = session.EndRound().OfType<AttackRolled>().First(rolled => rolled.AttackerId == clubber.Id);

        Assert.Equal([3, 2, 4], attack.Roll.Faces);
        Assert.False(clubber.Disarmed);
    }

    [Fact]
    public void ADoubleOneOnItsDiceBreaksAMonstersClubAndItFightsOnWithoutIt()
    {
        GameSession session = Duel("....1q...", [], new ScriptedDice(3, 1, 1, 1, 1, 1, 1, 1, 1));
        Monster clubber = session.OnlyMonster();

        Assert.Equal(new MonsterWeaponBroke(clubber.Id, "t_big_club"), session.EndRound().SingleOf<MonsterWeaponBroke>());
        Assert.True(clubber.Disarmed);
        Assert.Equal((AttackPattern.Melee, 1, 0), (clubber.AttackPattern, clubber.AttackRange, clubber.WeaponDice));
    }

    [Fact]
    public void AMonsterDropsItsClubWhenItFallsWithItWhole()
    {
        GameSession session = Duel("....1q...", [], new ScriptedDice(5));
        Hero hero = session.HeroInSeat();
        Monster clubber = session.OnlyMonster();

        session.Do(new AttackMonster(hero.Id, clubber.Id));

        Room room = session.RoomById();
        Assert.Contains(room.LootPositions.SelectMany(room.LootAt), card => card.Definition.Id == "t_big_club");
    }

    [Fact]
    public void ABrokenClubIsNotDropped()
    {
        GameSession session = Duel("....1q...", [], new ScriptedDice(5));
        Hero hero = session.HeroInSeat();
        Monster clubber = session.OnlyMonster();
        clubber.Disarmed = true;

        session.Do(new AttackMonster(hero.Id, clubber.Id));

        Room room = session.RoomById();
        Assert.DoesNotContain(room.LootPositions.SelectMany(room.LootAt), card => card.Definition.Id == "t_big_club");
    }

    [Fact]
    public void AHeroSwingsItTwoHandedEachSixOnItsDiceShovingTheTargetATileBack()
    {
        GameSession session = Duel("....1d...", ["t_big_club"], new ScriptedDice(1, 6, 2, 3));
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();
        Assert.True(hero.MainWeapon!.Definition.TwoHanded);

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, dummy.Id));

        Assert.Equal(1, events.OfType<Shoved>().Count());
        Assert.Equal(new GridPosition(6, 4), dummy.Position);
    }

    [Fact]
    public void TwoSixesShoveTwice()
    {
        GameSession session = Duel("....1d...", ["t_big_club"], new ScriptedDice(1, 6, 6, 1, 1));
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, dummy.Id));

        Assert.Equal(2, events.OfType<Shoved>().Count());
        Assert.Equal(new GridPosition(7, 4), dummy.Position);
    }

    [Fact]
    public void AHerosClubBreaksOnADoubleOneButNotOnASingleOne()
    {
        GameSession session = Duel("....1d...", ["t_big_club"], new ScriptedDice(4, 1, 3, 4, 1, 1));
        Hero hero = session.HeroInSeat();
        Monster dummy = session.OnlyMonster();

        Assert.DoesNotContain(session.Do(new AttackMonster(hero.Id, dummy.Id)), gameEvent => gameEvent is WeaponBroke);
        Assert.NotNull(hero.MainWeapon);

        session.EndRound();
        Assert.Contains(session.Do(new AttackMonster(hero.Id, dummy.Id)), gameEvent => gameEvent is WeaponBroke);
        Assert.Null(hero.MainWeapon);
    }
}
