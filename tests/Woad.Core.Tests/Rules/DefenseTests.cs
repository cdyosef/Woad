using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>
/// Heroes defend with a base die and armor; while that's losing, their Block goes in, then their
/// extra Defense dice one at a time (spent until their next turn), and last a Lucky Helmet's die.
/// </summary>
public sealed class DefenseTests
{
    // Two Sluggers (1d6+4 each, one attack apiece) beside the hero.
    private const string TwoSluggers = """
        .........
        .........
        .........
        .........
        ...U1U...
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void ExtraDiceRollInOnlyWhileLosingAndStaySpentUntilYourTurn()
    {
        // First Slugger: 4+4=8 against the base 2, then extras 4 (6, still losing) and 3 (9, holds).
        // Second Slugger: 8 against the base 1 alone, with the extras spent: a hit.
        GameSession session = TestRun.Start(TwoSluggers, new ScriptedDice(4, 2, 4, 3, 4, 1), stats: TestRun.HeroStats with { DefenseDice = 3 });
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        List<DefenseRolled> defenses = events.OfType<DefenseRolled>().ToList();
        Assert.Equal([2, 4, 3], defenses[0].Roll.Faces);
        Assert.Equal(2, defenses[0].ExtraDice);
        Assert.Equal([1], defenses[1].Roll.Faces);
        Assert.Equal([0, 1], events.OfType<Damaged>().Select(damaged => damaged.Amount));

        // A new turn: the extras are back.
        Assert.Equal(2, GameSession.ExtraDefenseDiceReady(hero));
    }

    [Fact]
    public void AWinningBaseDieLeavesTheExtrasUntouched()
    {
        // A Slugger's 1+4=5 against the base 5: held, with no extra dice rolled.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1U...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(1, 5), stats: TestRun.HeroStats with { DefenseDice = 3 });
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Equal([5], events.SingleOf<DefenseRolled>().Roll.Faces);
        Assert.Equal(0, events.SingleOf<Damaged>().Amount);
    }

    [Fact]
    public void BlockGoesAllInOnTheFirstLosingDefenseThenIsSpent()
    {
        // 1+4=5 against the base 3 and Block 2: held. Then 5 against the base 3 alone: a hit.
        GameSession session = TestRun.Start(TwoSluggers, new ScriptedDice(1, 3, 1, 3), cards: ["guard"]);
        Hero hero = session.HeroInSeat();

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        List<DefenseRolled> defenses = events.OfType<DefenseRolled>().ToList();
        Assert.Equal((2, 5), (defenses[0].Block, defenses[0].Total));
        Assert.Equal((0, 3), (defenses[1].Block, defenses[1].Total));
        Assert.Equal([0, 1], events.OfType<Damaged>().Select(damaged => damaged.Amount));
        Assert.Equal(2, GameSession.BlockReady(hero));
    }

    [Fact]
    public void TheLuckyHelmetIsTheLastLineOfDefenseAndBreaks()
    {
        // 4+4=8 against the base 2: losing, so the helmet rolls a 6 in (8, a tie: held), and breaks.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1U...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(4, 2, 6), cards: ["lucky"]);
        Hero hero = session.HeroInSeat();
        int helmetId = hero.Card("lucky").Id;

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.True(events.SingleOf<DefenseRolled>().LastChance);
        Assert.Equal(0, events.SingleOf<Damaged>().Amount);
        Assert.Equal(new LastChanceUsed(hero.Id, helmetId), events.SingleOf<LastChanceUsed>());
        Assert.Null(hero.FindCard(helmetId));
    }

    [Fact]
    public void ASetBonusComesOnlyWithBothHalvesWorn()
    {
        GameSession session = TestRun.Start(TwoSluggers, cards: ["ksword", "kshield"]);
        Hero hero = session.HeroInSeat();
        Assert.Equal((3, 1, 1), (hero.EffectiveStats.Weapon, hero.EffectiveStats.Block, hero.EffectiveStats.GuardDice));

        GameSession swordOnly = TestRun.Start(TwoSluggers, cards: ["ksword"]);
        Hero alone = swordOnly.HeroInSeat();
        Assert.Equal((2, 0, 0), (alone.EffectiveStats.Weapon, alone.EffectiveStats.Block, alone.EffectiveStats.GuardDice));
    }

    [Fact]
    public void AGuardDieRollsInForAnAllyBesideYouBeforeTheirOwnBlock()
    {
        // The Slugger's 5+4=9 against the hero's base 1 and armor 2: losing, so their neighbor's Guard Die
        // rolls in: a 6, which rolls a 2 straight in, for 11. Their own Block and extra die stay unspent.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ..U12....
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5, 1, 6, 2), heroes: 2, cards: ["ksword", "kshield"]);
        Hero hero = session.HeroInSeat(0);
        Hero knight = session.HeroInSeat(1);

        IReadOnlyList<GameEvent> events = session.EndRound();

        DefenseRolled defense = events.SingleOf<DefenseRolled>();
        Assert.Equal((1, 0, 0, 11), (defense.GuardDice, defense.Block, defense.ExtraDice, defense.Total));
        Assert.Equal(new Guarded(knight.Id, hero.Id), events.SingleOf<Guarded>());
        Assert.Equal(0, GameSession.GuardDiceReady(knight));
    }

    [Fact]
    public void AMonsterCanDropARareCardBesidesItsLoot()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1V...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(5));
        Hero hero = session.HeroInSeat();

        session.Do(new AttackMonster(hero.Id, session.OnlyMonster().Id, UseWeapon: false));

        Room room = session.RoomById();
        Assert.Contains(room.LootPositions.SelectMany(room.LootAt), card => card.Definition.Id == "shield");
    }
}
