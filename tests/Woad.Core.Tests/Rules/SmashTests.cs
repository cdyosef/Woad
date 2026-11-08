using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Smashing: break marks on doors, crates and walls; break tokens on weapons; the Smashing skill and the Crowbar.</summary>
public class SmashTests
{
    private static GameSession Room(string row, ScriptedDice dice, string[]? cards = null, Skill skills = Skills.All, int attackDice = 1) => TestRun.Start($"""
        .........
        .........
        .........
        .........
        {row}
        .........
        .........
        .........
        .........
        """, dice, stats: TestRun.HeroStats with { AttackDice = attackDice, OdHandDice = 1 }, cards: cards ?? [], skills: skills);

    /// <summary>Lets the hero swing again this turn, as if it were their next.</summary>
    private static void Rested(GameSession session)
    {
        session.State.Turn!.AttackDiceUsed = 0;
        session.State.Turn.StrikesUsed = 0;
        session.State.Turn.OdHandDiceUsed = 0;
    }

    [Fact]
    public void TwoMarksBreakACrateOpen()
    {
        GameSession session = Room("....1+...", new ScriptedDice(3, 3, 3, 3), skills: Skill.None);
        Hero hero = session.HeroInSeat();
        var crate = new GridPosition(5, 4);

        SmashRolled first = session.Do(new Smash(hero.Id, crate)).SingleOf<SmashRolled>();
        Assert.Equal((true, 1, 2, "crate"), (first.Success, first.Breaks, first.Needed, first.What));
        Assert.Equal(1, session.RoomById().BreaksAt(crate));

        Rested(session);
        IReadOnlyList<GameEvent> events = session.Do(new Smash(hero.Id, crate));

        Assert.Single(events.OfType<SmashedOpen>());
        Assert.Equal(TileKind.Floor, session.RoomById().Layout[crate]);
    }

    [Fact]
    public void ABlowThatFallsShortPutsABreakTokenOnTheWeaponAndPastItsValueItBreaks()
    {
        // A wall's toughness 7: a 1 and the sword's 1 fall short.
        GameSession session = Room("....1#...", new ScriptedDice(1, 1), cards: ["sword"], skills: Skill.None);
        Hero hero = session.HeroInSeat();
        CardInstance sword = hero.Card("sword");

        IReadOnlyList<GameEvent> events = session.Do(new Smash(hero.Id, new GridPosition(5, 4)));
        Assert.False(events.SingleOf<SmashRolled>().Success);
        Assert.Equal(new WeaponWorn(hero.Id, sword.Id, 1), events.SingleOf<WeaponWorn>());

        Rested(session);
        events = session.Do(new Smash(hero.Id, new GridPosition(5, 4)));
        Assert.Single(events.OfType<WeaponBroke>());
        Assert.Null(hero.FindCard(sword.Id));
    }

    [Fact]
    public void TenMarksCrackAWallAndFiveMoreBreakItThrough()
    {
        GameSession session = Room("....1#...", new ScriptedDice(Enumerable.Repeat(6, 60).ToArray()), skills: Skill.None, attackDice: 2);
        Hero hero = session.HeroInSeat();
        var wall = new GridPosition(5, 4);
        Room room = session.RoomById();
        for (int blow = 0; blow < GameRules.WallBreaks; blow++)
        {
            Rested(session);
            session.Do(new Smash(hero.Id, wall));
        }

        Assert.Equal((TileKind.CrackedWall, 0), (room.Layout[wall], room.BreaksAt(wall)));
        for (int blow = 0; blow < GameRules.CrackedWallBreaks; blow++)
        {
            Rested(session);
            session.Do(new Smash(hero.Id, wall));
        }

        Assert.Equal(TileKind.Floor, room.Layout[wall]);
    }

    [Fact]
    public void SmashingAddsARumbleDieAndACrowbarADieOnWoodAnOdHandDieAndNoBreakToken()
    {
        // A crate (wood): the Rumble die, the Smashing skill's die, the Crowbar's wood die and the Od Hand die it carries.
        GameSession session = Room("....1+...", new ScriptedDice(1, 1, 1, 1, 1, 1, 1, 1), cards: ["t_crowbar"]);
        Hero hero = session.HeroInSeat();

        SmashRolled smash = session.Do(new Smash(hero.Id, new GridPosition(5, 4), WeaponId: hero.Card("t_crowbar").Id)).SingleOf<SmashRolled>();

        Assert.Equal(4, smash.Roll.Faces.Count);
        Assert.Equal(1, session.State.Turn!.OdHandDiceUsed);
        Assert.Equal(0, hero.Card("t_crowbar").Wear);
    }

    [Fact]
    public void ALockedDoorSmashedInOpensForGoodAndSetsOffItsTrap()
    {
        GameSession session = TestRun.Start(
        [
            new RoomSetup("A", TestRun.Room(".........\n.........\n.........\n.........\n........1\n.........\n.........\n.........\n........."), new Dictionary<Direction, string> { [Direction.East] = "B" }),
            new RoomSetup("B", TestRun.Room(".........\n.........\n.........\n.........\n.........\n.........\n.........\n.........\n........."), new Dictionary<Direction, string> { [Direction.West] = "A" }),
        ], new ScriptedDice(Enumerable.Repeat(6, 40).ToArray()), stats: TestRun.HeroStats with { Hearts = 9 },
            passages: [new PassageSetup("A", Direction.East, PassageKind.Locked, 2, new TrapSetup(TrapKind.Darts, 4))], skills: Skill.None);
        Hero hero = session.HeroInSeat();
        Passage door = session.RoomById().PassageOn(Direction.East)!;
        Assert.Equal(GameRules.DoorBreaks, session.BreakableAt(session.RoomById(), hero.Position)!.Needed);

        IReadOnlyList<GameEvent> events = [];
        for (int blow = 0; blow < GameRules.DoorBreaks; blow++)
        {
            Rested(session);
            events = session.Do(new Smash(hero.Id, hero.Position));
        }

        Assert.True(door is { IsLocked: false, IsBroken: true, Trap: null });
        Assert.Contains(events, gameEvent => gameEvent is TrapSprung);
    }
}
