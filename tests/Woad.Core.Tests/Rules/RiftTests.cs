using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>The Emerald Keep's unstable portals: lightning that leaps on, and flings all it strikes about floor 1.</summary>
public class RiftTests
{
    private const string Rows = ".........\n.........\n.........\n.........\n...1.s...\n.........\n.........\n.........\n.........";

    /// <summary>A hero two tiles from a slime, a rift between them; a second room on floor 1 for the flung to land in.</summary>
    private static GameSession Run(ScriptedDice dice, int power = 0)
    {
        var rift = new Dictionary<GridPosition, TrapSetup> { [new GridPosition(4, 4)] = new TrapSetup(TrapKind.Rift, GameRules.RiftDifficulty) };
        GameSession session = TestRun.Start(
        [
            new RoomSetup("A", TestRun.Room(Rows), new Dictionary<Direction, string> { [Direction.East] = "B" }, Features: new RoomFeatures { Traps = rift }),
            new RoomSetup("B", TestRun.Room(Rows.Replace('1', '.').Replace('s', '.')), new Dictionary<Direction, string> { [Direction.West] = "A" }, MapPosition: new GridPosition(1, 0)),
        ], dice, stats: TestRun.HeroStats with { OdHandDice = 1 });
        session.HeroInSeat().Power.Banked = power;
        return session;
    }

    [Fact]
    public void ARiftIsInPlainSight()
    {
        GameSession session = Run(new ScriptedDice());
        Assert.Equal(new { Kind = TrapKind.Rift, Revealed = true }, session.RoomById().TrapAt(new GridPosition(4, 4)) is { } trap ? new { trap.Kind, trap.Revealed } : null);
    }

    [Fact]
    public void SteppingInTheLightningLeapsOnAndFlingsEveryoneItStrikes()
    {
        GameSession session = Run(new ScriptedDice(3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1));
        Hero hero = session.HeroInSeat();
        Monster slime = session.OnlyMonster();
        session.Do(new RollMovement(hero.Id));

        IReadOnlyList<GameEvent> events = session.Do(new MoveHero(hero.Id, new GridPosition(4, 4)));

        RiftCrackled crackled = events.SingleOf<RiftCrackled>();
        Assert.Equal([hero.Id, slime.Id], crackled.StruckIds);
        Assert.Equal(2, events.OfType<RiftZapped>().Count());
        HeroFlung flung = events.SingleOf<HeroFlung>();
        Assert.Equal((flung.RoomId, flung.Position), (hero.RoomId, hero.Position));
        Assert.NotNull(session.RoomById().TrapAt(new GridPosition(4, 4)));
        Assert.True(events.OfType<MonsterFlung>().Any() || events.OfType<MonsterDefeated>().Any(), "the slime is flung too (or the lightning killed it)");
    }

    [Fact]
    public void DisarmingARiftTakesABankedPowerDieAndClosesIt()
    {
        // The Od Hand die's 6 rolls a Power Die in (5): 11.
        GameSession none = Run(new ScriptedDice());
        Hero hero = none.HeroInSeat();
        Assert.Contains("banked Power Die", none.CheckDisarm(new GridPosition(4, 4)));

        GameSession session = Run(new ScriptedDice(6, 5), power: 1);
        hero = session.HeroInSeat();
        session.Do(new Disarm(hero.Id, new GridPosition(4, 4), Dice: 1));
        if (session.State.Turn!.PendingRoll is not null || session.State.Turn.UnsettledPowerDice > 0)
        {
            session.Do(new AddPowerDie(hero.Id));
        }

        Assert.Null(session.RoomById().TrapAt(new GridPosition(4, 4)));
        Assert.Equal(0, hero.Power.Banked);
    }
}
