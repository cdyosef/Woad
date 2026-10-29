using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Rules;

/// <summary>The accessory slot, and the Icarus Wings: flight of their own until a 1 breaks them.</summary>
public class AccessoryTests
{
    private const string Chasm = """
        .........
        .........
        .........
        .........
        .1~~~....
        .........
        .........
        .........
        .........
        """;

    [Fact]
    public void AHoodGoesInTheAccessorySlotWhenTheHeadIsTaken()
    {
        GameSession session = TestRun.Start(Chasm, cards: ["lucky", "t_hood"]);
        Hero hero = session.HeroInSeat();

        Assert.Equal("lucky", hero.Equipment[EquipSlot.Head].Definition.Id);
        Assert.Equal("t_hood", hero.Equipment[EquipSlot.Accessory].Definition.Id);
    }

    [Fact]
    public void CufflinksGiveArmorAndBlock()
    {
        GameSession session = TestRun.Start(Chasm, cards: ["t_cufflinks"]);
        Stats stats = session.HeroInSeat().EffectiveStats;

        Assert.Equal(TestRun.HeroStats.Armor + 1, stats.Armor);
        Assert.Equal(TestRun.HeroStats.Block + 1, stats.Block);
    }

    [Fact]
    public void WingsFlyOverAChasmAndLandSafelyBeyondIt()
    {
        GameSession session = TestRun.Start(Chasm, new ScriptedDice(4), cards: ["t_wings"]);
        Hero hero = session.HeroInSeat();

        Assert.Contains("take off", session.Refuse(new Fly(hero.Id, new GridPosition(5, 4))));
        WingsBeat beat = session.Do(new BeatWings(hero.Id)).SingleOf<WingsBeat>();
        Assert.Equal([4], beat.Faces);
        Assert.Equal(4, beat.FlightLeft);
        Assert.True(hero.Aloft);
        Assert.Contains("land first", session.Refuse(new MoveHero(hero.Id, new GridPosition(0, 4))));

        Assert.Contains("Use up your flight", session.Refuse(new Land(hero.Id)));
        Assert.Contains("Use up your flight", session.Refuse(new BeatWings(hero.Id)));
        session.Do(new Fly(hero.Id, new GridPosition(5, 4)));
        Assert.Equal(0, session.State.Turn!.FlightPoints);
        IReadOnlyList<GameEvent> events = session.Do(new Land(hero.Id));

        Assert.Empty(events.OfType<FellIntoPit>());
        Assert.Empty(events.OfType<WentProne>());
        Assert.False(hero.Aloft);
        Assert.Equal(new GridPosition(5, 4), hero.Position);
    }

    [Fact]
    public void YouCanFlyThroughADoorwayUnlockingItWithAKey()
    {
        GameSession session = TestRun.Start(
            [
                new RoomSetup("A", TestRun.Room(Chasm), new Dictionary<Direction, string> { [Direction.East] = "B" }),
                new RoomSetup("B", TestRun.Room(Chasm.Replace('1', '.')), new Dictionary<Direction, string> { [Direction.West] = "A" }),
            ],
            new ScriptedDice(6, 1, 4),
            cards: ["t_wings"],
            passages: [new PassageSetup("A", Direction.East, PassageKind.Locked, 5)]);
        Hero hero = session.HeroInSeat();
        hero.Keys = 1;
        session.Do(new BeatWings(hero.Id));
        session.Do(new Fly(hero.Id, Direction.East.DoorTile()));
        session.Do(new BeatWings(hero.Id));
        session.Do(new UnlockDoor(hero.Id, Direction.East));
        int left = session.State.Turn!.FlightPoints;

        session.Do(new GoThroughDoor(hero.Id, Direction.East));

        Assert.Equal("B", hero.RoomId);
        Assert.True(hero.Aloft);
        Assert.Equal(left - 1, session.State.Turn!.FlightPoints);
    }

    [Fact]
    public void ASixRollsAPowerDieIntoTheFlight()
    {
        GameSession session = TestRun.Start(Chasm, new ScriptedDice(6, 1), cards: ["t_wings"]);
        Hero hero = session.HeroInSeat();

        // The Power Die's 1 doesn't break the wings.
        WingsBeat beat = session.Do(new BeatWings(hero.Id)).SingleOf<WingsBeat>();
        Assert.Equal([6, 1], beat.Faces);
        Assert.Equal(7, beat.FlightLeft);
        Assert.NotNull(hero.Equipment.GetValueOrDefault(EquipSlot.Accessory));
    }

    [Fact]
    public void AOneBreaksTheWingsAndDropsYouProneAndStunnedIntoThePit()
    {
        GameSession session = TestRun.Start(Chasm, new ScriptedDice(2, 1), cards: ["t_wings"]);
        Hero hero = session.HeroInSeat();
        session.Do(new BeatWings(hero.Id));
        session.Do(new Fly(hero.Id, new GridPosition(3, 4)));
        Assert.Equal(0, session.State.Turn!.FlightPoints);

        IReadOnlyList<GameEvent> events = session.Do(new BeatWings(hero.Id));

        Assert.Contains(new WingsBroke(hero.Id, new GridPosition(3, 4)), events);
        Assert.Single(events.OfType<FellIntoPit>());
        Assert.Null(hero.Equipment.GetValueOrDefault(EquipSlot.Accessory));
        Assert.True(hero.Prone);
        Assert.True(hero.Stun.IsStunned);
        Assert.Equal(0, session.MoveDiceLeft);
        Assert.False(hero.Aloft);
    }

    [Fact]
    public void EndingTheTurnInTheAirBringsYouDownWhereYouAre()
    {
        GameSession session = TestRun.Start(Chasm, new ScriptedDice([3, .. Enumerable.Repeat(1, 10)]), cards: ["t_wings"]);
        Hero hero = session.HeroInSeat();
        session.Do(new BeatWings(hero.Id));
        session.Do(new Fly(hero.Id, new GridPosition(2, 4)));

        IReadOnlyList<GameEvent> events = session.Do(new EndTurn(hero.Id));

        Assert.Contains(new Landed(hero.Id, new GridPosition(2, 4)), events);
        Assert.Single(events.OfType<FellIntoPit>());
    }
}
