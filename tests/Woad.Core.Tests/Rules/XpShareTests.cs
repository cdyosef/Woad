using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>A monster's XP shared by the damage done to it, the killing blow's bonus, and the end-of-run awards.</summary>
public class XpShareTests
{
    /// <summary>Seat 0 beside a Lurker (4 XP), seat 1 behind them; seat 1 has already taken 3 hearts off it, and it has 1 left.</summary>
    private static (GameSession Session, Hero Killer, Hero Helper, Monster Lurker) Wounded()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ...21x...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 5, 5, 5), heroes: 2);
        Hero killer = session.HeroInSeat(0), helper = session.HeroInSeat(1);
        Monster lurker = session.OnlyMonster();
        lurker.Hearts = 1;
        lurker.DamageBy[helper.Id] = 3;
        return (session, killer, helper, lurker);
    }

    [Fact]
    public void AMonstersXpIsSharedByTheDamageDoneAndTheKillingBlowEarnsMore()
    {
        (GameSession session, Hero killer, Hero helper, Monster lurker) = Wounded();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(killer.Id, lurker.Id));

        // 4 XP: 3 for the helper's 3 hearts, 1 for the killer's 1, and the killing blow's 2.
        Assert.Contains(events, gameEvent => gameEvent is MonsterDefeated);
        Assert.Equal((1 + GameRules.KillingBlowXp, 3), (killer.Xp, helper.Xp));
        Assert.Equal(1, killer.DamageDealt);
    }

    [Fact]
    public void KeelingOverStillSharesButTheDeadDont()
    {
        (GameSession session, Hero killer, Hero helper, Monster lurker) = Wounded();
        helper.Status = HeroStatus.Dead;

        session.Do(new AttackMonster(killer.Id, lurker.Id));

        Assert.Equal((4 + GameRules.KillingBlowXp, 0), (killer.Xp, helper.Xp));

        (session, killer, helper, lurker) = Wounded();
        helper.Status = HeroStatus.KeelingOver;
        session.Do(new AttackMonster(killer.Id, lurker.Id));
        Assert.Equal(3, helper.Xp);
    }

    [Fact]
    public void ABigMonstersKillingBlowIsAQuarterOfItsXp()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1W...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 5, 5, 5));
        Hero hero = session.HeroInSeat();
        Monster boss = session.OnlyMonster();
        boss.Hearts = 1;

        session.Do(new AttackMonster(hero.Id, boss.Id));

        // 25 XP, and a quarter of it (rounded up) for the blow.
        Assert.Equal(25 + 7, hero.Xp);
    }

    /// <summary>Two heroes by an exit, with no boss about (or one still standing, in a room of its own).</summary>
    private static GameSession OnTheWayOut(bool boss = false)
    {
        string Rows(char middle) => string.Join('\n', [.. Enumerable.Repeat(".........", 4), $"...12*{middle}..", .. Enumerable.Repeat(".........", 4)]);
        List<RoomSetup> rooms = [new RoomSetup("A", TestRun.Room(Rows('.')))];
        if (boss)
        {
            rooms.Add(new RoomSetup("B", TestRun.Room(Rows('W').Replace("12*", "..."))));
        }

        return TestRun.Start(rooms, new ScriptedDice(), heroes: 2);
    }

    private static IReadOnlyList<HeroRunResult> Escape(GameSession session)
    {
        GridPosition exit = session.RoomById().TilesOfKind(TileKind.Exit).Single();
        foreach (Hero hero in session.State.Heroes)
        {
            hero.Position = exit;
            session.Do(new Escape(hero.Id));
        }

        return RunResults.For(session.State);
    }

    [Fact]
    public void ACompletedRunsAwardsGoToWhoeverDidTheMostSharedOnATie()
    {
        GameSession session = OnTheWayOut();
        Hero ash = session.HeroInSeat(0), wren = session.HeroInSeat(1);
        ash.KeysUsed = wren.KeysUsed = 2;
        wren.TrapsSprung = 3;
        ash.Kills = 1;

        IReadOnlyList<HeroRunResult> results = Escape(session);

        // One floor: 5 XP an award, "Check for what?!" half that, rounded up.
        Assert.Equal([new RunAward("Keymaster", 5), new RunAward("Slayer", 5)], results.Single(result => result.HeroId == ash.Id).Awards);
        Assert.Equal([new RunAward("Keymaster", 5), new RunAward("Check for what?!", 3)], results.Single(result => result.HeroId == wren.Id).Awards);
        Assert.Equal(GameRules.XpForEscaping + 10, results.Single(result => result.HeroId == ash.Id).Xp);
    }

    [Fact]
    public void ARunWithTheBossStillStandingWinsNoAwardsButTheDeadCan()
    {
        GameSession standing = OnTheWayOut(boss: true);
        standing.HeroInSeat(0).KeysUsed = 1;
        Assert.All(Escape(standing), result => Assert.Empty(result.Awards));

        GameSession session = OnTheWayOut();
        Hero dead = session.HeroInSeat(1);
        dead.Kills = 4;
        dead.Status = HeroStatus.Dead;
        Hero ash = session.HeroInSeat(0);
        ash.Position = session.RoomById().TilesOfKind(TileKind.Exit).Single();
        session.Do(new Escape(ash.Id));

        Assert.Contains(new RunAward("Slayer", 5), RunResults.For(session.State).Single(result => result.HeroId == dead.Id).Awards);
    }
}
