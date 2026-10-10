using Godot;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>Draws one room's terrain, loot and move/target highlights, and reports clicks on tiles.</summary>
public partial class BoardView : Control
{
    public const int TileSize = 32;
    public const int Pixels = RoomGrid.Size * TileSize;

    private Room? _room;
    private GridPosition? _hovered;
    private HashSet<GridPosition> _reachable = [];
    private HashSet<GridPosition> _targets = [];
    private HashSet<GridPosition> _attackable = [];
    private HashSet<GridPosition> _interactive = [];
    private HashSet<GridPosition> _blast = [];

    public event Action<GridPosition>? TileClicked;

    public event Action<GridPosition?>? TileHovered;

    public event Action? CancelRequested;

    public override void _Ready()
    {
        Size = new Vector2(Pixels, Pixels);
        MouseFilter = MouseFilterEnum.Stop;
    }

    public static Vector2 TileOrigin(GridPosition tile) => new(tile.X * TileSize, tile.Y * TileSize);

    public void ShowRoom(Room room)
    {
        _room = room;
        QueueRedraw();
    }

    public void SetHighlights(
        IEnumerable<GridPosition> reachable,
        IEnumerable<GridPosition> attackable,
        IEnumerable<GridPosition> targets,
        IEnumerable<GridPosition> interactive)
    {
        _reachable = [.. reachable];
        _attackable = [.. attackable];
        _targets = [.. targets];
        _interactive = [.. interactive];
        QueueRedraw();
    }

    public void ClearHighlights() => SetHighlights([], [], [], []);

    public void SetBlast(IEnumerable<GridPosition> tiles)
    {
        _blast = [.. tiles];
        QueueRedraw();
    }

    /// <summary>A number or word that floats up from a tile and fades, e.g. "-2" or "Blocked".</summary>
    public void FloatText(GridPosition tile, string text, Color color)
    {
        Label label = UiTheme.Label(TileOrigin(tile) + new Vector2(-8, 0), new Vector2(TileSize + 16, 12), text, 10, color);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.ZIndex = 5;
        AddChild(label);

        Tween tween = label.CreateTween().SetParallel();
        tween.TweenProperty(label, "position:y", label.Position.Y - 14, 0.7);
        tween.TweenProperty(label, "modulate:a", 0.0, 0.7).SetDelay(0.25);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion:
                UpdateHover(motion.Position);
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                if (TileAt(click.Position) is { } tile)
                {
                    TileClicked?.Invoke(tile);
                }

                AcceptEvent();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                CancelRequested?.Invoke();
                AcceptEvent();
                break;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit && _hovered is not null)
        {
            _hovered = null;
            TileHovered?.Invoke(null);
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_room is null)
        {
            return;
        }

        foreach (GridPosition tile in RoomGrid.AllPositions())
        {
            DrawTile(tile, _room.Layout[tile]);
        }

        foreach ((Direction side, _) in _room.Doors)
        {
            DrawDoor(side, _room.IsSealed);
        }

        foreach (GridPosition tile in _room.LootPositions)
        {
            DrawLoot(tile, _room.LootAt(tile).Count);
        }

        DrawOverlay(_reachable, Palette.Reachable);
        DrawOverlay(_targets, Palette.Target);
        DrawOverlay(_blast, Palette.Blast);

        foreach (GridPosition tile in _attackable)
        {
            DrawRect(TileRect(tile).Grow(-1), Palette.Danger, filled: false, width: 2);
        }

        foreach (GridPosition tile in _interactive)
        {
            DrawRect(TileRect(tile).Grow(-2), Palette.Interact, filled: false, width: 1);
        }

        if (_hovered is { } hovered)
        {
            DrawRect(TileRect(hovered), Palette.TrimBright, filled: false, width: 1);
        }
    }

    private void DrawTile(GridPosition tile, TileKind kind)
    {
        Rect2 rect = TileRect(tile);
        DrawRect(rect, (tile.X + tile.Y) % 2 == 0 ? Palette.FloorLight : Palette.FloorDark);
        DrawLine(rect.Position + new Vector2(0, TileSize - 0.5f), rect.End - new Vector2(0, 0.5f), Palette.Grout);
        DrawLine(rect.Position + new Vector2(TileSize - 0.5f, 0), rect.End - new Vector2(0.5f, 0), Palette.Grout);

        switch (kind)
        {
            case TileKind.Wall:
                DrawRect(rect, Palette.Wall);
                DrawRect(new Rect2(rect.Position, new Vector2(TileSize, 8)), Palette.WallTop);
                DrawRect(rect, Palette.Ink, filled: false, width: 1);
                break;

            case TileKind.Pit:
                DrawRect(rect.Grow(-2), Palette.Pit);
                DrawRect(new Rect2(rect.Position + new Vector2(2, 2), new Vector2(TileSize - 4, 4)), Palette.Wall);
                break;

            case TileKind.StairsDown:
                DrawRect(rect.Grow(-2), Palette.Wall);
                for (int step = 0; step < 4; step++)
                {
                    float inset = 4 + step * 3;
                    DrawRect(new Rect2(rect.Position.X + inset, rect.Position.Y + 5 + step * 6, TileSize - inset * 2, 3), Palette.WallTop);
                }

                break;

            case TileKind.Exit:
                bool sealedExit = _room!.Monsters.Any(monster => monster.Definition.Boss);
                var arch = new Rect2(rect.Position + new Vector2(5, 3), new Vector2(22, 27));
                DrawRect(arch, sealedExit ? Palette.Pit : Palette.TrimBright with { A = 0.85f });
                DrawRect(arch, Palette.Trim, filled: false, width: 2);
                if (sealedExit)
                {
                    for (int bar = 1; bar < 4; bar++)
                    {
                        DrawLine(arch.Position + new Vector2(bar * 5.5f, 0), arch.Position + new Vector2(bar * 5.5f, arch.Size.Y), Palette.TrimDim, 2);
                    }
                }

                break;

            case TileKind.Pot:
                Vector2 belly = rect.GetCenter() + new Vector2(0, 3);
                DrawCircle(belly, 9, Palette.Pot, antialiased: true);
                DrawRect(new Rect2(belly.X - 5, belly.Y - 13, 10, 5), Palette.PotRim);
                DrawArc(belly, 9, 0, Mathf.Tau, 24, Palette.Ink, 1, antialiased: true);
                break;

            case TileKind.Chest:
                bool open = _room!.IsChestOpened(tile);
                var body = new Rect2(rect.Position + new Vector2(6, 10), new Vector2(20, 14));
                DrawRect(body, open ? Palette.ChestOpen : Palette.Chest);
                DrawRect(new Rect2(body.Position + new Vector2(0, 5), new Vector2(20, 2)), open ? Palette.TrimDim : Palette.TrimBright);
                DrawRect(body, Palette.Ink, filled: false, width: 1);
                break;
        }
    }

    /// <summary>A bar on the board's edge marking a door: gold when open, red while a trap room is sealed.</summary>
    private void DrawDoor(Direction side, bool sealedShut)
    {
        Rect2 tile = TileRect(side.DoorTile());
        Rect2 bar = side switch
        {
            Direction.North => new Rect2(tile.Position.X + 4, 0, TileSize - 8, 4),
            Direction.South => new Rect2(tile.Position.X + 4, Pixels - 4, TileSize - 8, 4),
            Direction.West => new Rect2(0, tile.Position.Y + 4, 4, TileSize - 8),
            _ => new Rect2(Pixels - 4, tile.Position.Y + 4, 4, TileSize - 8),
        };
        DrawRect(bar, sealedShut ? Palette.Danger : Palette.TrimBright);
    }

    private void DrawLoot(GridPosition tile, int count)
    {
        Vector2 origin = TileOrigin(tile) + new Vector2(21, 19);
        DrawColoredPolygon([origin + new Vector2(4, 0), origin + new Vector2(8, 5), origin + new Vector2(4, 10), origin + new Vector2(0, 5)], Palette.Loot);
        if (count > 1)
        {
            DrawString(ThemeDB.FallbackFont, origin + new Vector2(-6, 9), count.ToString(), fontSize: 8, modulate: Palette.Loot);
        }
    }

    private void DrawOverlay(HashSet<GridPosition> tiles, Color color)
    {
        foreach (GridPosition tile in tiles)
        {
            DrawRect(TileRect(tile).Grow(-1), color);
        }
    }

    private void UpdateHover(Vector2 position)
    {
        GridPosition? tile = TileAt(position);
        if (tile != _hovered)
        {
            _hovered = tile;
            TileHovered?.Invoke(tile);
            QueueRedraw();
        }
    }

    private static GridPosition? TileAt(Vector2 position)
    {
        var tile = new GridPosition(Mathf.FloorToInt(position.X / TileSize), Mathf.FloorToInt(position.Y / TileSize));
        return tile.IsInRoom ? tile : null;
    }

    private static Rect2 TileRect(GridPosition tile) => new(TileOrigin(tile), new Vector2(TileSize, TileSize));
}
