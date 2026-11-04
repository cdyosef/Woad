using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Core.Tests.Support;
using Xunit;

namespace Woad.Core.Tests.Rules;

public class SearchRangeTests
{
    private const string Open = """
        .........
        .........
        .........
        .........
        ....1....
        .........
        .........
        .........
        .........
        """;

    private const string Hall = """
        ............
        1...........
        ............
        """;

    private static GameSession WithTraps(string room, ScriptedDice dice, Skill skills, params (GridPosition Tile, int Difficulty)[] traps)
    {
        var features = new RoomFeatures { Traps = traps.ToDictionary(trap => trap.Tile, trap => new TrapSetup(TrapKind.Spikes, trap.Difficulty)) };
        return TestRun.Start([new RoomSetup("A", TestRun.Room(room), Features: features)], dice, skills: skills);
    }

    [Fact]
    public void WithoutAWayToSearchTheOdHandCardFeelsAroundTheTilesNextToYou()
    {
        // A 3 finds the trap next door but not the easier one further off; and no Explorer bonus for it.
        GameSession session = WithTraps(Open, new ScriptedDice(3), Skill.None, (new GridPosition(4, 3), 3), (new GridPosition(4, 1), 1));
        Hero hero = session.HeroInSeat();
        Assert.False(session.Searches);

        IReadOnlyList<GameEvent> events = session.Do(new Search(hero.Id, Dice: 1));

        Assert.Equal(OdHandCheckKind.Feel, events.SingleOf<OdHandRolled>().Kind);
        Assert.True(session.RoomById().TrapAt(new GridPosition(4, 3))!.Revealed);
        Assert.False(session.RoomById().TrapAt(new GridPosition(4, 1))!.Revealed);
        Assert.Empty(events.OfType<Explored>());
        Assert.Equal(0, hero.ExplorerBonus);
    }

    [Fact]
    public void TheFocusBonusAddsToASearchOnTheTilesNextToYou()
    {
        // A 3 and Focus 2: 5 next door, only 3 further off.
        GameSession session = WithTraps(Open, new ScriptedDice(3), Skill.Search, (new GridPosition(5, 5), 5), (new GridPosition(4, 1), 5));
        Hero hero = session.HeroInSeat();
        hero.BaseStats = hero.BaseStats with { Magic = 2 };

        session.Do(new Search(hero.Id, Dice: 1));

        Assert.True(session.RoomById().TrapAt(new GridPosition(5, 5))!.Revealed);
        Assert.False(session.RoomById().TrapAt(new GridPosition(4, 1))!.Revealed);
    }

    [Fact]
    public void ASearchReachesEightTiles()
    {
        GameSession session = WithTraps(Hall, new ScriptedDice(5), Skill.Search, (new GridPosition(8, 1), 1), (new GridPosition(9, 1), 1));
        Hero hero = session.HeroInSeat();

        session.Do(new Search(hero.Id, Dice: 1));

        Assert.True(session.RoomById().TrapAt(new GridPosition(8, 1))!.Revealed);
        Assert.False(session.RoomById().TrapAt(new GridPosition(9, 1))!.Revealed);
    }
}
