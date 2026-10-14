using Godot;
using Woad.Core.Content;
using Woad.Core.Dungeon;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The current floor's map: explored rooms, doors to rooms not yet seen (as dim outlines), the
/// room on screen outlined in gold, and a dot for each hero on the floor.
/// </summary>
public partial class MinimapView : Control
{
    private const float CellWidth = 30;
    private const float CellHeight = 15;
    private const float Top = 14;

    private GameState? _state;
    private int _floor = 1;
    private string _shownRoomId = "";

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public void ShowFloor(GameState state, int floor, string shownRoomId)
    {
        _state = state;
        _floor = floor;
        _shownRoomId = shownRoomId;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Font font = ThemeDB.FallbackFont;
        if (GameArt.HeadingFont is { } heading)
        {
            DrawString(heading, new Vector2(0, 9), $"FLOOR {_floor}", fontSize: UiTheme.HeadingPixelSize, modulate: Palette.Trim);
        }
        else
        {
            DrawString(font, new Vector2(0, 9), $"FLOOR {_floor}", fontSize: UiTheme.FontSize, modulate: Palette.Trim);
        }
        if (_state is null)
        {
            return;
        }

        List<Room> rooms = _state.Rooms.Values.Where(room => room.Floor == _floor).ToList();
        IEnumerable<KeyValuePair<Direction, string>> Seen(Room room) =>
            room.Doors.Where(door => room.PassageOn(door.Key) is not { IsHidden: true });
        var known = rooms.Where(room => room.Explored)
            .SelectMany(room => Seen(room).Select(door => door.Value).Append(room.Id))
            .ToHashSet();

        foreach (Room room in rooms.Where(room => room.Explored))
        {
            foreach ((Direction side, string targetId) in Seen(room).Where(door => door.Key is Direction.East or Direction.South || !_state.Rooms[door.Value].Explored))
            {
                Vector2 from = Center(room.MapPosition);
                Vector2 to = Center(_state.Rooms[targetId].MapPosition);
                Color color = room.PassageOn(side) switch
                {
                    { IsLocked: true, NeedsBossKey: true } => Palette.Heart,
                    { IsLocked: true } => Palette.Loot.Darkened(0.4f),
                    { IsBlocked: true } => Palette.WallTop,
                    _ => Palette.TrimDim,
                };
                DrawLine(from, to, color, 2);
            }
        }

        foreach (Room room in rooms.Where(room => known.Contains(room.Id)))
        {
            Rect2 cell = Cell(room.MapPosition);
            if (room.Explored)
            {
                DrawRect(cell, room.IsSealed ? Palette.Danger.Darkened(0.5f) : Palette.FloorDark);
                DrawRect(cell, room.Id == _shownRoomId ? Palette.TrimBright : Palette.Trim, filled: false, width: room.Id == _shownRoomId ? 1.5f : 1);
                if (Marker(room) is { } marker)
                {
                    DrawString(font, new Vector2(cell.Position.X, cell.End.Y - 3), marker, HorizontalAlignment.Center, cell.Size.X, 8, Palette.TrimBright);
                }
            }
            else
            {
                DrawRect(cell, Palette.TrimDim, filled: false, width: 1);
            }
        }

        foreach (IGrouping<string, Hero> group in _state.Heroes.Where(hero => hero.IsActive).GroupBy(hero => hero.RoomId))
        {
            Room room = _state.Rooms[group.Key];
            if (room.Floor != _floor)
            {
                continue;
            }

            Rect2 cell = Cell(room.MapPosition);
            int index = 0;
            foreach (Hero hero in group)
            {
                DrawCircle(cell.Position + new Vector2(5 + index * 6, 4), 2.2f, Palette.Seat(hero.Seat), antialiased: true);
                index++;
            }
        }
    }

    private static string? Marker(Room room) => room.Role switch
    {
        RoomRole.Stairs => "v",
        RoomRole.Boss => room.Monsters.Any(monster => monster.Definition.Boss) ? "!" : "exit",
        RoomRole.Treasure => "$",
        _ => null,
    };

    private static Rect2 Cell(GridPosition position) =>
        new(position.X * CellWidth + 2, Top + position.Y * CellHeight + 2, CellWidth - 4, CellHeight - 4);

    private static Vector2 Center(GridPosition position) => Cell(position).GetCenter();
}
