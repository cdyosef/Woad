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
    public void AnAttackTotalThatBeatsTheDefenseTotalTakesOneHeart()
    {
        GameSession session = TestRun.Start(Duel('B'), new ScriptedDice(4, 3), cards: ["sword"]);
        Hero hero = session.HeroInSeat();
        Monster brute = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, brute.Id));

        var attack = events.SingleOf<AttackRolled>();
        Assert.Equal((hero.Id, 1, 5), (attack.AttackerId, attack.Bonus, attack.Total));
        Assert.Equal([brute.Id], attack.TargetIds);
        var defense = events.SingleOf<DefenseRolled>();
        Assert.Equal((0, 3), (defense.Armor, defense.Total));
        Assert.Equal(new Damaged(brute.Id, 1, 1), events.SingleOf<Damaged>());
        Assert.Equal(1, brute.Hearts);
    }

    [Fact]
    public void TiesGoToTheDefender()
    {
        GameSession session = TestRun.Start(Duel('B'), new ScriptedDice(3, 4), cards: ["sword"]);
        Monster brute = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(session.HeroInSeat().Id, brute.Id));

        Assert.Equal(new Damaged(brute.Id, 0, 2), events.SingleOf<Damaged>());
    }

    [Fact]
    public void ArmorAddsToEveryDefenseRoll()
    {
        GameSession session = TestRun.Start(Duel('K'), new ScriptedDice(5, 3));
        Monster knight = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(session.HeroInSeat().Id, knight.Id));

        var defense = events.SingleOf<DefenseRolled>();
        Assert.Equal((2, 5), (defense.Armor, defense.Total));
        Assert.Equal(0, events.SingleOf<Damaged>().Amount);
    }

    [Fact]
    public void CombinedDiceAreRolledTogetherAndAddedUp()
    {
        GameSession session = TestRun.Start(Duel('B'), new ScriptedDice(4, 3, 5), stats: GameRules.HeroBaseStats with { AttackDice = 2 }, cards: ["sword"]);
        Monster brute = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(session.HeroInSeat().Id, brute.Id));

        var attack = events.SingleOf<AttackRolled>();
        Assert.Equal([4, 3], attack.Roll.Faces);
        Assert.Equal(8, attack.Total);
        Assert.Equal(1, events.SingleOf<Damaged>().Amount);
        Assert.Equal(0, session.AttackDiceLeft);
    }

    [Fact]
    public void AttackDiceCanBeSplitIntoSeparateAttacks()
    {
        GameSession session = TestRun.Start(Duel('B'), new ScriptedDice(5, 1, 3, 3, 2), stats: GameRules.HeroBaseStats with { AttackDice = 3 }, cards: ["sword"]);
        Hero hero = session.HeroInSeat();
        Monster brute = session.OnlyMonster();

        var first = session.Do(new AttackMonster(hero.Id, brute.Id, Dice: 1)).SingleOf<AttackRolled>();
        Assert.Equal((1, 6), (first.Roll.Faces.Count, first.Total));
        Assert.Equal(2, session.AttackDiceLeft);

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, brute.Id, Dice: 2));
        Assert.Equal(0, events.SingleOf<AttackRolled>().Bonus);
        Assert.Contains(new MonsterDefeated(brute.Id, hero.Id), events);
        Assert.Equal(0, session.AttackDiceLeft);
        Assert.False(session.State.Turn!.HasActed);
    }

    [Fact]
    public void AttacksStopWhenTheAttackDiceRunOut()
    {
        GameSession session = TestRun.Start(Duel('B'), new ScriptedDice(1, 6));
        Hero hero = session.HeroInSeat();
        Monster brute = session.OnlyMonster();
        session.Do(new AttackMonster(hero.Id, brute.Id));

        Assert.Contains("used all your Attack dice", session.Refuse(new AttackMonster(hero.Id, brute.Id)));
        Assert.Empty(session.GetAttackableMonsters());
    }

    [Fact]
    public void TheWeaponNeedsAtLeastOneDie()
    {
        GameSession session = TestRun.Start(Duel('B'), cards: ["sword"]);
        Hero hero = session.HeroInSeat();

        Assert.Contains("Choose 1 to 1 Attack dice", session.Refuse(new AttackMonster(hero.Id, session.OnlyMonster().Id, Dice: 0)));
        Assert.Contains("Choose 1 to 1 Attack dice", session.Refuse(new AttackMonster(hero.Id, session.OnlyMonster().Id, Dice: 2)));
    }

    [Fact]
    public void TheWeaponCanBeSavedForALaterAttack()
    {
        GameSession session = TestRun.Start(Duel('K'), new ScriptedDice(1, 5, 5, 1), stats: GameRules.HeroBaseStats with { AttackDice = 2 }, cards: ["sword"]);
        Hero hero = session.HeroInSeat();
        Monster knight = session.OnlyMonster();

        var first = session.Do(new AttackMonster(hero.Id, knight.Id, Dice: 1, UseWeapon: false));
        Assert.Equal(0, first.SingleOf<AttackRolled>().Bonus);
        Assert.Equal(1, session.WeaponBonusLeft());

        var second = session.Do(new AttackMonster(hero.Id, knight.Id, Dice: 1));
        Assert.Equal((1, 6), (second.SingleOf<AttackRolled>().Bonus, second.SingleOf<AttackRolled>().Total));
        Assert.Equal(0, session.WeaponBonusLeft());
    }

    [Fact]
    public void AttackDiceComeBackEachTurn()
    {
        GameSession session = TestRun.Start(Duel('K'), new ScriptedDice(1, 6));
        Hero hero = session.HeroInSeat();
        session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id));
        Assert.Equal(0, session.AttackDiceLeft);

        session.EndRound();

        Assert.Equal(1, session.AttackDiceLeft);
        Assert.NotEmpty(session.GetAttackableMonsters());
    }

    [Fact]
    public void DefeatedMonstersAreRemovedAndCountAsKills()
    {
        GameSession session = TestRun.Start(Duel('m'), new ScriptedDice(5, 1));
        Hero hero = session.HeroInSeat();
        Monster grunt = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, grunt.Id));

        Assert.Contains(new MonsterDefeated(grunt.Id, hero.Id), events);
        Assert.Empty(session.RoomById().Monsters);
        Assert.Equal(1, hero.Kills);
    }

    [Fact]
    public void MeleeReachesTheEightSurroundingTilesOnly()
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
        Monster diagonal = session.RoomById().Monsters.Single(monster => monster.Position == new GridPosition(5, 3));
        Monster twoAway = session.RoomById().Monsters.Single(monster => monster.Position == new GridPosition(4, 2));

        Assert.Equal([diagonal], session.GetAttackableMonsters());
        Assert.Contains("out of reach", session.Refuse(new AttackMonster(session.HeroInSeat().Id, twoAway.Id)));
    }

    [Fact]
    public void ADiagonalAttackIsBlockedWhenWallsCloseBothCorners()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            ....#m...
            ....1#...
            .........
            .........
            .........
            .........
            """);

        Assert.Empty(session.GetAttackableMonsters());
        Assert.Contains("out of reach", session.Refuse(new AttackMonster(session.HeroInSeat().Id, session.OnlyMonster().Id)));
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
            """, new ScriptedDice(5, 1), cards: ["bow"]);
        Monster brute = session.OnlyMonster();

        Assert.Equal([brute], session.GetAttackableMonsters());
        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(session.HeroInSeat().Id, brute.Id));

        Assert.Equal(new Damaged(brute.Id, 1, 1), events.SingleOf<Damaged>());
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
        GameSession session = TestRun.Start(Duel('k'), new ScriptedDice(5, 5, 5), stats: GameRules.HeroBaseStats with { AttackDice = 2 });
        Hero hero = session.HeroInSeat();
        Monster bones = session.OnlyMonster();

        session.Do(new AttackMonster(hero.Id, bones.Id, Dice: 1));
        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, bones.Id, Dice: 1));

        Assert.Equal(new MonsterCollapsed(bones.Id), events[^1]);
        Assert.True(bones.Collapsed);
        Assert.Equal(0, hero.Kills);

        Assert.Contains(new MonsterReassembled(bones.Id, 1), session.EndRound());
        Assert.False(bones.Collapsed);

        events = session.Do(new AttackMonster(hero.Id, bones.Id, Dice: 1));
        Assert.Contains(new MonsterDefeated(bones.Id, hero.Id), events);
        Assert.Equal(1, hero.Kills);
    }

    [Fact]
    public void LuckHoldsTheAttackOpenForRerolls()
    {
        GameSession session = TestRun.Start(Duel('m'), new ScriptedDice(1, 5, 1), stats: GameRules.HeroBaseStats with { Luck = 2 });
        Hero hero = session.HeroInSeat();
        Monster grunt = session.OnlyMonster();

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(hero.Id, grunt.Id));
        Assert.IsType<AttackRolled>(Assert.Single(events));
        Assert.NotNull(session.State.Turn!.PendingRoll);
        Assert.Contains("Finish the roll", session.Refuse(new EndTurn(hero.Id)));

        var rerolled = session.Do(new RerollDie(hero.Id, 0)).SingleOf<DieRerolled>();
        Assert.Equal((1, 5, (int?)null), (rerolled.OldFace, rerolled.NewFace, rerolled.MovementPoints));
        Assert.Equal(1, hero.LuckRemaining);

        events = session.Do(new ConfirmAttack(hero.Id));
        Assert.Contains(new MonsterDefeated(grunt.Id, hero.Id), events);
        Assert.Null(session.State.Turn.PendingRoll);
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
        Assert.Null(session.State.Turn!.PendingRoll);
        Assert.Contains("no Luck", session.Refuse(new RerollDie(hero.Id, 0)));
    }

    [Fact]
    public void MonstersSplitTheirDiceWhenSmallAttacksWouldLandMore()
    {
        // Against no defense at all, two 1-die attacks are two sure hits.
        GameSession session = TestRun.Start(Duel('B'), new ScriptedDice(1, 1), stats: TestRun.HeroStats with { DefenseDice = 0 });
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal([1, 1], events.OfType<AttackRolled>().Select(attack => attack.Roll.Faces.Count));
        Assert.Equal(2, hero.Hearts);
    }

    [Fact]
    public void MonstersCombineTheirDiceAgainstStrongerDefense()
    {
        GameSession session = TestRun.Start(Duel('B'), new ScriptedDice(3, 3, 2), cards: ["shield"]);
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal(6, events.SingleOf<AttackRolled>().Total);
        Assert.Equal((1, 3), (events.SingleOf<DefenseRolled>().Armor, events.SingleOf<DefenseRolled>().Total));
        Assert.Equal(3, hero.Hearts);
    }

    [Fact]
    public void AMonsterAddsItsWeaponToItsFirstAttack()
    {
        // The duelist (2 dice, weapon 2) does better with 1d6+2 and then 1d6 against a single defense die.
        GameSession session = TestRun.Start(Duel('D'), new ScriptedDice(1, 2, 4, 3));
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.EndRound();

        Assert.Equal([(2, 3), (0, 4)], events.OfType<AttackRolled>().Select(attack => (attack.Bonus, attack.Total)));
        Assert.Equal(2, hero.Hearts);
    }
}
