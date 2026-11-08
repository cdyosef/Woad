using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>Sterner doors: two locks, two traps (switched off from their far side), and bars.</summary>
public class SternDoorTests
{
    private static readonly string Empty = string.Join('\n', Enumerable.Repeat(".........", 9));

    /// <summary>The hero in room A's east doorway (or B's west doorway), the door between set up as given.</summary>
    private static GameSession Door(PassageSetup door, bool inB = false, ScriptedDice? dice = null)
    {
        string withHero(int x) => string.Join('\n', Enumerable.Range(0, 9).Select(y => y == 4 ? new string([.. Enumerable.Range(0, 9).Select(i => i == x ? '1' : '.')]) : "........."));
        // The party starts in the first room listed.
        var a = new RoomSetup("A", TestRun.Room(inB ? Empty : withHero(8)), new Dictionary<Direction, string> { [Direction.East] = "B" });
        var b = new RoomSetup("B", TestRun.Room(inB ? withHero(0) : Empty), new Dictionary<Direction, string> { [Direction.West] = "A" });
        return TestRun.Start(inB ? [b, a] : [a, b], dice ?? new ScriptedDice(Enumerable.Repeat(3, 30).ToArray()), stats: TestRun.HeroStats with { Hearts = 9 }, passages: [door]);
    }

    [Fact]
    public void TwoLocksTakeTwoKeys()
    {
        GameSession session = Door(new PassageSetup("A", Direction.East, PassageKind.Locked, 5) { Locks = 2 });
        Hero hero = session.HeroInSeat();
        hero.Keys = 2;
        Passage door = session.RoomById().PassageOn(Direction.East)!;

        Assert.Equal(1, session.Do(new UnlockDoor(hero.Id, Direction.East)).SingleOf<DoorUnlocked>().LocksLeft);
        Assert.True(door.IsLocked);
        Assert.Equal(0, session.Do(new UnlockDoor(hero.Id, Direction.East)).SingleOf<DoorUnlocked>().LocksLeft);
        Assert.False(door.IsLocked);
        Assert.Equal(1, hero.DoorsUnlocked);
    }

    [Fact]
    public void ASecondTrapIsSetOnceTheFirstIsGoneAndFromTheFarSideTheyreSwitchedOff()
    {
        // Armed from A; the hero in B's doorway switches them off by hand, one and then the next, no roll.
        var darts = new TrapSetup(TrapKind.Darts, 4);
        GameSession session = Door(new PassageSetup("A", Direction.East, PassageKind.Open, 0, darts) { MoreTraps = [darts] }, inB: true);
        Hero hero = session.HeroInSeat();
        Passage door = session.RoomById("B").PassageOn(Direction.West)!;
        Assert.Equal(2, door.Traps.Count);

        IReadOnlyList<GameEvent> events = session.Do(new Disarm(hero.Id, hero.Position));
        Assert.Single(events.OfType<TrapDisarmed>());
        Assert.Empty(events.OfType<OdHandRolled>());
        Assert.Single(door.Traps);
        session.Do(new Disarm(hero.Id, hero.Position));
        Assert.Null(door.Trap);
    }

    [Fact]
    public void ABarLiftsFromItsOwnSideAndFromTheOtherTheDoorMustBeSmashed()
    {
        GameSession barredHere = Door(new PassageSetup("A", Direction.East, PassageKind.Open) { BarredFrom = "A" });
        Hero hero = barredHere.HeroInSeat();
        barredHere.Do(new RollMovement(hero.Id));
        Assert.Contains("lift the bar", barredHere.Refuse(new GoThroughDoor(hero.Id, Direction.East)));
        barredHere.Do(new LiftBar(hero.Id, Direction.East));
        barredHere.Do(new GoThroughDoor(hero.Id, Direction.East));

        GameSession barredThere = Door(new PassageSetup("A", Direction.East, PassageKind.Open) { BarredFrom = "B" });
        hero = barredThere.HeroInSeat();
        Assert.Contains("other side", barredThere.CheckLiftBar(Direction.East));
        Breakable door = barredThere.BreakableAt(barredThere.RoomById(), hero.Position)!;
        Assert.Equal((BreakableKind.Door, GameRules.DoorToughness + 1, GameRules.DoorBreaks + 2), (door.Kind, door.Toughness, door.Needed));
    }

    [Fact]
    public void AHarderLockIsWorthMoreXp()
    {
        GameSession session = Door(new PassageSetup("A", Direction.East, PassageKind.Locked, 9));
        Hero hero = session.HeroInSeat();
        hero.Keys = 1;

        IReadOnlyList<GameEvent> events = session.Do(new UnlockDoor(hero.Id, Direction.East));

        Assert.Equal(GameRules.KeyXp + 9 / GameRules.LockXpPer, events.SingleOf<XpGained>().Amount);
    }

    [Fact]
    public void DeeperFloorsHaveSomeDoubleLocksAndSomeBarredDoors()
    {
        ContentCatalog content = ContentCatalog.LoadDefault();
        List<PassageSetup> passages = [.. Enumerable.Range(1, 40).SelectMany(seed => DungeonGenerator.CreateRun(content, "sunken_temple", [new HeroSetup("Bot", GameRules.HeroBaseStats, GameRules.StarterKit)], (ulong)seed).Passages)];
        Assert.Contains(passages, passage => passage.Locks == 2);
        Assert.Contains(passages, passage => passage.BarredFrom is not null);
        Assert.Contains(passages, passage => passage.MoreTraps.Count > 0);
    }
}
