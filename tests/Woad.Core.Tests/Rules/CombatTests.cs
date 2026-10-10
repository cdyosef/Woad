using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class CombatTests
{
    /// <summary>The hero at (4,4) with a monster of the given glyph directly north at (4,3).</summary>
    private static string Duel(char monster) => $"""
        .........
        .........
        .........
        ....{monster}....
        ....1....
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void HitsThatGetPastDefenseDealDamage()
    {
        GameSession session = TestRun.Start(Duel('B'), new ScriptedDice(5, 3));
        Hero hero = session.HeroInSeat();
        Monster brute = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, brute.Id));

        var attack = events.SingleOf<AttackRolled>();
        Assert.Equal(hero.Id, attack.AttackerId);
        Assert.Equal(1, attack.Hits);
        Assert.Equal([brute.Id], attack.TargetIds);
        Assert.Equal(0, events.SingleOf<DefenseRolled>().Blocks);
        Assert.Equal(new Damaged(brute.Id, 1, 1), events.SingleOf<Damaged>());
        Assert.Equal(1, brute.Hearts);
    }

    [Fact]
    public void MonstersBlockOnlyOnSixes()
    {
        GameSession session = TestRun.Start(Duel('B'), new ScriptedDice(5, 6, 6), cards: ["sword"]);
        Monster brute = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(session.HeroInSeat().Id, brute.Id));

        Assert.Equal(2, events.SingleOf<AttackRolled>().Hits);
        Assert.Equal(1, events.SingleOf<DefenseRolled>().Blocks);
        Assert.Equal(1, events.SingleOf<Damaged>().Amount);
    }

    [Fact]
    public void AMissRollsNoDefense()
    {
        GameSession session = TestRun.Start(Duel('B'), new ScriptedDice(4));

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(session.HeroInSeat().Id, session.OnlyMonster().Id));

        Assert.IsType<AttackRolled>(Assert.Single(events));
    }

    [Fact]
    public void DefeatedMonstersAreRemovedAndCountAsKills()
    {
        GameSession session = TestRun.Start(Duel('m'), new ScriptedDice(6, 1));
        Hero hero = session.HeroInSeat();
        Monster grunt = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, grunt.Id));

        Assert.Contains(new MonsterDefeated(grunt.Id, hero.Id), events);
        Assert.Empty(session.RoomById().Monsters);
        Assert.Equal(1, hero.Kills);
    }

    [Fact]
    public void MeleeNeedsAnOrthogonallyAdjacentMonster()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            ....m....
            .....m...
            ....1....
            .........
            .........
            .........
            .........
            """);

        Assert.Empty(session.GetAttackableMonsters());
        foreach (Monster monster in session.RoomById().Monsters)
        {
            Assert.Contains("out of reach", session.Refuse(new AttackMonster(session.HeroInSeat().Id, monster.Id)));
        }
    }

    [Fact]
    public void OneActionPerTurn()
    {
        GameSession session = TestRun.Start(Duel('B'), new ScriptedDice(1));
        Hero hero = session.HeroInSeat();
        Monster brute = session.OnlyMonster();
        session.Do(new AttackMonster(hero.Id, brute.Id));

        Assert.Contains("already used your action", session.Refuse(new AttackMonster(hero.Id, brute.Id)));
        Assert.Empty(session.GetAttackableMonsters());
    }

    [Fact]
    public void RangedWeaponsShootAlongALineOfSight()
    {
        GameSession session = TestRun.Start("""
            .........
            ....B....
            .........
            .........
            ....1....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 5, 1), cards: ["bow"]);
        Monster brute = session.OnlyMonster();

        Assert.Equal([brute], session.GetAttackableMonsters());
        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(session.HeroInSeat().Id, brute.Id));

        Assert.Contains(new MonsterDefeated(brute.Id, session.HeroInSeat().Id), events);
    }

    [Fact]
    public void WallsBlockRangedAttacks()
    {
        GameSession session = TestRun.Start("""
            .........
            ....B....
            ....#....
            .........
            ....1....
            .........
            .........
            .........
            .........
            """, cards: ["bow"]);

        Assert.Contains("out of reach", session.Refuse(new AttackMonster(session.HeroInSeat().Id, session.OnlyMonster().Id)));
    }

    [Fact]
    public void SplittingMonstersBreakIntoPiecesWhenDefeated()
    {
        GameSession session = TestRun.Start(Duel('s'), new ScriptedDice(5, 1));

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(session.HeroInSeat().Id, session.OnlyMonster().Id));

        Assert.Equal(2, events.OfType<MonsterSpawned>().Count());
        Assert.Equal(
            [new GridPosition(4, 3), new GridPosition(4, 2)],
            session.RoomById().Monsters.Select(monster => monster.Position));
        Assert.All(session.RoomById().Monsters, monster => Assert.Equal("piece", monster.Definition.Id));
    }

    [Fact]
    public void ReassemblingMonstersCollapseOnceThenRise()
    {
        GameSession session = TestRun.Start(Duel('k'), new ScriptedDice(5, 5, 5, 1), cards: ["sword"]);
        Hero hero = session.HeroInSeat();
        Monster bones = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, bones.Id));

        Assert.Equal(new MonsterCollapsed(bones.Id), events[^1]);
        Assert.True(bones.Collapsed);
        Assert.Equal(0, hero.Kills);

        Assert.Contains(new MonsterReassembled(bones.Id, 1), session.EndRound());
        Assert.False(bones.Collapsed);

        events = session.Do(new AttackMonster(hero.Id, bones.Id));
        Assert.Contains(new MonsterDefeated(bones.Id, hero.Id), events);
        Assert.Equal(1, hero.Kills);
    }

    [Fact]
    public void BombsDestroyReassemblingMonstersOutright()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            ....k....
            .........
            ....1....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 5), cards: ["bomb"]);
        Hero hero = session.HeroInSeat();
        Monster bones = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new UseCard(hero.Id, hero.Card("bomb").Id, bones.Position));

        Assert.Contains(new MonsterDefeated(bones.Id, hero.Id), events);
        Assert.DoesNotContain(events, gameEvent => gameEvent is MonsterCollapsed);
    }

    [Fact]
    public void LuckHoldsTheAttackOpenForRerolls()
    {
        GameSession session = TestRun.Start(Duel('m'), new ScriptedDice(1, 6, 1), stats: GameRules.HeroBaseStats with { Luck = 2 });
        Hero hero = session.HeroInSeat();
        Monster grunt = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, grunt.Id));
        Assert.IsType<AttackRolled>(Assert.Single(events));
        Assert.NotNull(session.State.Turn!.PendingAttack);
        Assert.Contains("Finish the attack", session.Refuse(new EndTurn(hero.Id)));

        var rerolled = session.Do(new RerollDie(hero.Id, 0)).SingleOf<DieRerolled>();
        Assert.Equal((1, 6, (int?)null), (rerolled.OldFace, rerolled.NewFace, rerolled.MovementPoints));
        Assert.Equal(1, hero.LuckRemaining);

        events = session.Do(new ConfirmAttack(hero.Id));
        Assert.Contains(new MonsterDefeated(grunt.Id, hero.Id), events);
        Assert.Null(session.State.Turn.PendingAttack);
    }

    [Fact]
    public void SpendingTheLastLuckResolvesTheAttack()
    {
        GameSession session = TestRun.Start(Duel('m'), new ScriptedDice(1, 5, 1), stats: GameRules.HeroBaseStats with { Luck = 1 });
        Hero hero = session.HeroInSeat();
        Monster grunt = session.OnlyMonster();
        session.Do(new AttackMonster(hero.Id, grunt.Id));

        IReadOnlyList<GameEvent> events = session.Do(new RerollDie(hero.Id, 0));

        Assert.Contains(new MonsterDefeated(grunt.Id, hero.Id), events);
        Assert.Null(session.State.Turn!.PendingAttack);
        Assert.Contains("no Luck", session.Refuse(new RerollDie(hero.Id, 0)));
    }
}
