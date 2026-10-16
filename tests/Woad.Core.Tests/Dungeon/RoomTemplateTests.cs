using Woad.Core.Dungeon;
using Woad.Core.Grid;
using Woad.Core.Tests.Support;

namespace Woad.Core.Tests.Dungeon;

public class RoomTemplateTests
{
    [Fact]
    public void ParsesTerrainMonstersAndHeroStarts()
    {
        var template = TestRun.Room("""
            #.......C
            .........
            ..~......
            .........
            ....1...m
            .........
            .........
            .2.......
            .........
            """);

        Assert.Equal(TileKind.Wall, template.Layout[new GridPosition(0, 0)]);
        Assert.Equal(TileKind.Chest, template.Layout[new GridPosition(8, 0)]);
        Assert.Equal(TileKind.Pit, template.Layout[new GridPosition(2, 2)]);
        Assert.Equal(TileKind.Floor, template.Layout[new GridPosition(4, 4)]);
        Assert.Equal(TileKind.Floor, template.Layout[new GridPosition(8, 4)]);

        Assert.Equal(new GridPosition(4, 4), template.HeroStarts[0]);
        Assert.Equal(new GridPosition(1, 7), template.HeroStarts[1]);

        var spawn = Assert.Single(template.Monsters);
        Assert.Equal(new GridPosition(8, 4), spawn.Position);
        Assert.Equal("grunt", spawn.Monster.Id);
    }

    [Fact]
    public void OutsideTheRoomReadsAsWall()
    {
        var template = TestRun.Room(string.Join('\n', Enumerable.Repeat(".........", 9)));

        Assert.Equal(TileKind.Wall, template.Layout[new GridPosition(-1, 4)]);
        Assert.Equal(TileKind.Wall, template.Layout[new GridPosition(4, 9)]);
    }

    [Fact]
    public void AcceptsAnyRectangleButNotRaggedOrOversizedOnes()
    {
        RoomTemplate wide = TestRun.Room(string.Join('\n', Enumerable.Repeat(new string('.', 13), 7)));
        Assert.Equal((13, 7), (wide.Layout.Width, wide.Layout.Height));

        Assert.Throws<FormatException>(() => TestRun.Room(".........\n........"));
        Assert.Throws<FormatException>(() => TestRun.Room(string.Join('\n', Enumerable.Repeat(new string('.', RoomGrid.MaxSize + 1), 3))));
    }

    [Fact]
    public void TurningANonSquareRoomSwapsItsWidthAndHeight()
    {
        RoomTemplate hall = TestRun.Room("#.....\n......\n......");

        RoomTemplate turned = hall.Transformed(1, mirrored: false);

        Assert.Equal((3, 6), (turned.Layout.Width, turned.Layout.Height));
        Assert.Equal(TileKind.Wall, turned.Layout[new GridPosition(2, 0)]);
        Assert.Equal(TileKind.Floor, turned.Layout[new GridPosition(0, 0)]);
    }

    [Fact]
    public void RejectsUnknownCharacters()
    {
        var error = Assert.Throws<FormatException>(() => TestRun.Room(string.Join('\n', Enumerable.Repeat("....?....", 9))));

        Assert.Contains("'?'", error.Message);
    }
}
