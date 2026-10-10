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
    public void RejectsTheWrongSize()
    {
        Assert.Throws<FormatException>(() => TestRun.Room(string.Join('\n', Enumerable.Repeat(".........", 8))));
        Assert.Throws<FormatException>(() => TestRun.Room(string.Join('\n', Enumerable.Repeat("........", 9))));
    }

    [Fact]
    public void RejectsUnknownCharacters()
    {
        var error = Assert.Throws<FormatException>(() => TestRun.Room(string.Join('\n', Enumerable.Repeat("....?....", 9))));

        Assert.Contains("'?'", error.Message);
    }
}
