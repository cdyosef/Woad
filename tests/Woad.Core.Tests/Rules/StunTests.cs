using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

public class StunTests
{
    [Theory]
    // Stun: the Od Hand action first (or a die without Od Hand), then a die a stack.
    [InlineData(1, 0, true, 0)]
    [InlineData(1, 0, false, 1)]
    [InlineData(3, 0, true, 2)]
    // Powerful Stun: the Od Hand action and 2 dice (3 without), then 3 a stack; Stun stacks add 1 each.
    [InlineData(0, 1, true, 2)]
    [InlineData(0, 1, false, 3)]
    [InlineData(0, 2, true, 5)]
    [InlineData(2, 1, true, 4)]
    public void StunsTakeDice(int stacks, int powerful, bool odHand, int dice)
    {
        var stun = new StunState();
        stun.Add(powerful: false, stacks);
        stun.Add(powerful: true, powerful);

        Assert.Equal(dice, stun.DiceLost(odHand));
        Assert.Equal(stacks + 3 * powerful, stun.AttackBonus);
    }

    [Fact]
    public void DiceGoWendFirstThenRumbleThenDefense()
    {
        var stun = new StunState();
        stun.Add(powerful: true);

        Assert.Equal((1, 1, 1), stun.Lost(hasOdHand: false, wend: 1, rumble: 1, defense: 2));
    }

    private const string TwoHeroes = """
        .........
        .........
        .........
        .........
        ....1....
        .........
        ......2..
        .........
        .........
        """;

    [Fact]
    public void AStunnedHeroLosesTheirOdHandActionAndFreeActionsUntilTheEndOfTheirTurn()
    {
        GameSession session = TestRun.Start(TwoHeroes, heroes: 2);
        Hero stunned = session.HeroInSeat(1);
        stunned.Stun.Add(powerful: false);

        session.Do(new EndTurn(session.HeroInSeat(0).Id));

        TurnState turn = session.State.Turn!;
        Assert.Equal((0, 1, 1), (session.OdHandDiceLeft, session.MoveDiceLeft, session.AttackDiceLeft));
        Assert.True(turn.UsedFreeLoot && turn.UsedBeltItem);
        Assert.Equal(new StunWoreOff(stunned.Id), session.Do(new EndTurn(stunned.Id)).SingleOf<StunWoreOff>());
        Assert.False(stunned.Stun.IsStunned);
    }

    [Fact]
    public void MoreStacksTakeWendThenRumbleDice()
    {
        GameSession session = TestRun.Start(TwoHeroes, heroes: 2);
        session.HeroInSeat(1).Stun.Add(powerful: false, stacks: 3);

        session.Do(new EndTurn(session.HeroInSeat(0).Id));

        Assert.Equal((0, 0, 0), (session.OdHandDiceLeft, session.MoveDiceLeft, session.AttackDiceLeft));
    }

    [Fact]
    public void AttacksOnAStunnedMonsterGetPlusOneAStack()
    {
        // 3 against the Knight's 1 + armor 2 would tie; +1 for the stun makes it a hit.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1K...
            .........
            .........
            .........
            .........
            """, new ScriptedDice(3, 1));
        Monster knight = session.OnlyMonster();
        knight.Stun.Add(powerful: false);

        IReadOnlyList<GameEvent> events = session.Do(new AttackMonster(session.HeroInSeat().Id, knight.Id, UseWeapon: false));

        Assert.Equal(1, events.SingleOf<Damaged>().Amount);
    }

    [Fact]
    public void AStunnedMonsterWithoutOdHandLosesAWendDieThatIsTwoMoves()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            m........
            .........
            .........
            .........
            .........
            .........
            ........1
            """);
        Monster grunt = session.OnlyMonster();
        grunt.Stun.Add(powerful: false);

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(session.HeroInSeat().Id));

        Assert.Empty(events.OfType<MonsterMoved>());
        Assert.Equal(new StunWoreOff(grunt.Id), events.SingleOf<StunWoreOff>());
    }

    [Fact]
    public void AThrownBoomerangFromTheOffHandReachesFurtherAndItsSixesStun()
    {
        // Sword in hand, Boomerang in the off hand: a target 3 away takes the Boomerang (+1), whose 6 stuns.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1..d.
            .........
            .........
            .........
            .........
            """, new ScriptedDice(6, 1), cards: ["sword", "boomerang"]);
        Hero hero = session.HeroInSeat();
        Assert.Equal(EquipSlot.OffHand, hero.Equipment.Single(slot => slot.Value.Definition.Id == "boomerang").Key);
        Monster dummy = session.OnlyMonster();

        Assert.Equal(1, session.Do(new AttackMonster(hero.Id, dummy.Id)).SingleOf<AttackRolled>().Bonus);
        IReadOnlyList<GameEvent> events = session.Do(new AddPowerDie(hero.Id));

        Assert.Equal(new Stunned(dummy.Id, 1, 0, false), events.SingleOf<Stunned>());
    }

    [Fact]
    public void AStunnedFlyerOverAPitFallsIn()
    {
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ....1~f..
            .........
            .........
            .........
            .........
            """, new ScriptedDice(6, 1), cards: ["boomerang"]);
        Hero hero = session.HeroInSeat();
        Monster flyer = session.OnlyMonster();
        flyer.Position = new GridPosition(5, 4);
        flyer.Hearts = 3;

        session.Do(new AttackMonster(hero.Id, flyer.Id));
        IReadOnlyList<GameEvent> events = session.Do(new AddPowerDie(hero.Id));

        Assert.Contains(events, e => e is MonsterDefeated or MonsterFell);
        Assert.Empty(session.RoomById().Monsters);
    }

    [Fact]
    public void AProneMonsterTakesAnExtraMeleeDieAndGettingUpCostsItTwoMoves()
    {
        // Prone, the Grunt (move 2) spends its moves getting up and can't reach the hero.
        GameSession session = TestRun.Start("""
            .........
            .........
            .........
            .........
            ..m..1...
            .........
            .........
            .........
            .........
            """);
        Monster grunt = session.OnlyMonster();
        grunt.Prone = true;

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(session.HeroInSeat().Id));

        Assert.Equal(new GotUp(grunt.Id), events.SingleOf<GotUp>());
        Assert.Empty(events.OfType<MonsterMoved>());
        Assert.False(grunt.Prone);
    }
}
