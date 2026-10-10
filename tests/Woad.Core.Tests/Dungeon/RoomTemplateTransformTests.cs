using Woad.Core.Dungeon;
using Woad.Core.Grid;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Dungeon;

public class RoomTemplateTransformTests
{
    private static readonly RoomTemplate Marked = TestRun.Room("""
        #.......>
        .M.......
        .........
        .........
        ....&....
        .........
        .......*.
        .1.......
        ........m
        """);

    [Fact]
    public void ParsesGeneratorMarkers()
    {
        Assert.Equal([new GridPosition(1, 1)], Marked.SpawnPoints);
        Assert.Equal(new GridPosition(4, 4), Marked.BossSpawn);
        Assert.Equal(TileKind.StairsDown, Marked.Layout[new GridPosition(8, 0)]);
        Assert.Equal(TileKind.Exit, Marked.Layout[new GridPosition(7, 6)]);
        Assert.Equal(TileKind.Floor, Marked.Layout[new GridPosition(1, 1)]);
    }

    [Fact]
    public void AQuarterTurnRotatesClockwise()
    {
        RoomTemplate turned = Marked.Transformed(quarterTurns: 1, mirrored: false);

        // (x, y) -> (8 - y, x): the top-left wall ends up top-right.
        Assert.Equal(TileKind.Wall, turned.Layout[new GridPosition(8, 0)]);
        Assert.Equal(TileKind.StairsDown, turned.Layout[new GridPosition(8, 8)]);
        Assert.Equal([new GridPosition(7, 1)], turned.SpawnPoints);
        Assert.Equal(new GridPosition(1, 1), turned.HeroStarts[0]);
        Assert.Equal(new GridPosition(0, 8), Assert.Single(turned.Monsters).Position);
    }

    [Fact]
    public void MirroringFlipsLeftAndRight()
    {
        RoomTemplate mirrored = Marked.Transformed(quarterTurns: 0, mirrored: true);

        Assert.Equal(TileKind.Wall, mirrored.Layout[new GridPosition(8, 0)]);
        Assert.Equal(TileKind.StairsDown, mirrored.Layout[new GridPosition(0, 0)]);
        Assert.Equal(new GridPosition(4, 4), mirrored.BossSpawn);
    }

    [Fact]
    public void FourQuarterTurnsChangeNothing()
    {
        RoomTemplate around = Marked.Transformed(4, mirrored: false);

        Assert.All(RoomGrid.AllPositions(), position => Assert.Equal(Marked.Layout[position], around.Layout[position]));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void DoorwaysStayInTheMiddleOfEdges(int quarterTurns, bool mirrored)
    {
        var doorways = Enum.GetValues<Direction>().Select(side => side.DoorTile()).ToHashSet();

        IEnumerable<GridPosition> moved = doorways.Select(door =>
        {
            RoomTemplate single = TestRun.Room(string.Join('\n', RoomGrid.AllPositions()
                .GroupBy(position => position.Y)
                .Select(row => new string(row.Select(position => position == door ? 'M' : '.').ToArray()))));
            return single.Transformed(quarterTurns, mirrored).SpawnPoints[0];
        });

        Assert.All(moved, position => Assert.Contains(position, doorways));
    }
}
