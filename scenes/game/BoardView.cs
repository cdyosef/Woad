using Godot;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Draws one room's terrain, its stone rim and doors (see BoardView.Walls.cs), revealed traps, loot and
/// keys, and the move/target highlights, and reports clicks on tiles. It sits in a clipping frame the
/// size of a standard room (<see cref="Pixels"/>) and is its own camera: a bigger room scrolls to
/// follow whoever is acting, a smaller one is centered. The player can pan (right-drag or the arrow
/// keys), but only as far as keeps the active hero in view.
/// </summary>
public partial class BoardView : Control
{
    public const int TileSize = 32;
    /// <summary>The view onto the room: a standard room's width and height.</summary>
    public const int Pixels = RoomGrid.Size * TileSize;

    /// <summary>How fast the arrow keys pan, in pixels a second.</summary>
    private const float PanSpeed = 320;

    /// <summary>A right-click that moves less than this is a click (cancel), not a drag.</summary>
    private const float DragThreshold = 4;

    private Room? _room;
    private GridPosition? _hovered;
    private HashSet<GridPosition> _reachable = [];
    private HashSet<GridPosition> _targets = [];
    private HashSet<GridPosition> _attackable = [];
    private HashSet<GridPosition> _interactive = [];
    private HashSet<GridPosition> _hazards = [];
    private HashSet<GridPosition> _jumps = [];
    private HashSet<GridPosition> _blast = [];
    private HashSet<GridPosition> _range = [];

    /// <summary>The board pixel at the top-left of the view.</summary>
    private Vector2 _camera;

    private Node2D? _follow;
    private GridPosition? _lookAt;
    private bool _panned;
    private GridPosition? _anchor;
    private bool _dragging;
    private bool _dragged;
    private Vector2 _dragFrom;

    /// <summary>How hard the view is shaking (pixels), dying away.</summary>
    private float _shake;

    public event Action<GridPosition>? TileClicked;

    /// <summary>The second press of a double-click (the first came through <see cref="TileClicked"/>).</summary>
    public event Action<GridPosition>? TileDoubleClicked;

    public event Action<GridPosition?>? TileHovered;

    public event Action? CancelRequested;

    /// <summary>Which theme's tile art to draw; without art the board falls back to flat shapes.</summary>
    public string ThemeId { get; set; } = "";

    private LootLayer _loot = null!;

    public override void _Ready()
    {
        Size = new Vector2(Pixels, Pixels);
        MouseFilter = MouseFilterEnum.Stop;
        _loot = new LootLayer { Board = this };
        AddChild(_loot);
    }

    /// <summary>Keeps <paramref name="target"/> (a token on the board) in view, ending any pan. <paramref name="snap"/> jumps there at once.</summary>
    public void Follow(Node2D? target, bool snap = false)
    {
        _follow = target;
        _lookAt = null;
        _panned = false;
        if (snap)
        {
            _camera = Desired();
            Position = -_camera;
        }
    }

    /// <summary>Turns the view to a tile (a bomb going off), ending any pan.</summary>
    public void LookAt(GridPosition tile)
    {
        _follow = null;
        _lookAt = tile;
        _panned = false;
    }

    /// <summary>The tile panning must keep in view (the active hero's), or null to pan freely.</summary>
    public void SetPanAnchor(GridPosition? tile) => _anchor = tile;

    /// <summary>Whether a tile is inside the view right now (and so can be clicked).</summary>
    public bool IsInView(GridPosition tile) => new Rect2(_camera, Pixels, Pixels).Encloses(TileRect(tile));

    public override void _Process(double delta)
    {
        // Lit fuses spark.
        if (_room?.Bombs.Any(bomb => bomb.Lit) == true || _room?.Flames.Count > 0 || _room?.LitBraziers.Count > 0)
        {
            QueueRedraw();
        }

        var keys = new Vector2(
            (Input.IsKeyPressed(Key.Right) ? 1 : 0) - (Input.IsKeyPressed(Key.Left) ? 1 : 0),
            (Input.IsKeyPressed(Key.Down) ? 1 : 0) - (Input.IsKeyPressed(Key.Up) ? 1 : 0));
        if (keys != Vector2.Zero && IsVisibleInTree())
        {
            Pan(keys * PanSpeed * (float)delta);
        }
        else if (!_panned && _room is not null)
        {
            _camera = _camera.Lerp(Desired(), 1 - Mathf.Exp(-10 * (float)delta));
        }

        _shake = Mathf.MoveToward(_shake, 0, 30 * (float)delta);
        Vector2 jolt = _shake > 0.05f ? new Vector2((float)(Random.Shared.NextDouble() * 2 - 1), (float)(Random.Shared.NextDouble() * 2 - 1)) * _shake : Vector2.Zero;
        Position = -_camera + jolt.Round();
    }

    /// <summary>Shakes the view, harder for bigger blows.</summary>
    public void Shake(float strength) => _shake = Math.Max(_shake, strength);

    /// <summary>A fighting-game callout (HIT!, K.O.!...) over a tile, kept inside the view.</summary>
    /// <param name="size">Multiples of the heading font: 2 for most, 3 for the big ones.</param>
    /// <param name="below">Shifts it down a line (for a second callout, like a combo count).</param>
    public void Callout(GridPosition tile, string text, Color color, int size = 2, bool below = false)
    {
        Vector2 at = TileOrigin(tile) + new Vector2(TileSize / 2f, below ? 30 : 0);
        float half = text.Length * UiTheme.HeadingPixelSize * size * 0.45f;
        at.X = Mathf.Clamp(at.X, _camera.X + half + 4, _camera.X + Pixels - half - 4);
        at.Y = Mathf.Clamp(at.Y, _camera.Y + 14 * size / 2f, _camera.Y + Pixels - 8);
        AddChild(new ActionCallout { Position = at, Text = text, Color = color, Size = size });
    }

    /// <summary>A strike's slash and sparks on a tile; sparks only for a block.</summary>
    public void Spark(GridPosition tile, Color color, bool slash = true) =>
        AddChild(new HitSpark { Position = TileOrigin(tile) + new Vector2(TileSize / 2f, TileSize / 2f - 2), Color = color, Slash = slash, Backhand = Random.Shared.Next(2) == 0 });

    /// <summary>Where the camera wants to be: centered on what it follows, within the room.</summary>
    private Vector2 Desired()
    {
        Vector2 center = _follow is not null && IsInstanceValid(_follow) && _follow.GetParent() == this
            ? _follow.Position + new Vector2(TileSize / 2f, TileSize / 2f)
            : _lookAt is { } tile ? TileOrigin(tile) + new Vector2(TileSize / 2f, TileSize / 2f)
            : RoomPixels / 2;
        return Clamp(center - new Vector2(Pixels / 2f, Pixels / 2f));
    }

    /// <summary>
    /// Which ways the room runs on past the view (left, up, right, down): where there's more of it to
    /// pan to with a right-drag or the arrow keys.
    /// </summary>
    public (bool Left, bool Up, bool Right, bool Down) MoreBeyond
    {
        get
        {
            Vector2 room = RoomPixels;
            return (_camera.X > 1, _camera.Y > 1, _camera.X + Pixels < room.X - 1, _camera.Y + Pixels < room.Y - 1);
        }
    }

    private Vector2 RoomPixels => _room is null ? new Vector2(Pixels, Pixels) : new Vector2(_room.Layout.Width * TileSize, _room.Layout.Height * TileSize);

    /// <summary>Inside the room; a room smaller than the view is centered in it.</summary>
    private Vector2 Clamp(Vector2 camera)
    {
        Vector2 room = RoomPixels;
        float Axis(float value, float size) => size <= Pixels ? (size - Pixels) / 2 : Mathf.Clamp(value, 0, size - Pixels);
        return new Vector2(Axis(camera.X, room.X), Axis(camera.Y, room.Y));
    }

    /// <summary>Moves the view by <paramref name="by"/> pixels, no further than keeps the anchor tile in view.</summary>
    private void Pan(Vector2 by)
    {
        _panned = true;
        Vector2 camera = Clamp(_camera + by);
        if (_anchor is { } tile)
        {
            Vector2 at = TileOrigin(tile);
            Vector2 room = RoomPixels;
            float Keep(float value, float anchor, float size) => size <= Pixels ? value : Mathf.Clamp(value, anchor + TileSize - Pixels, anchor);
            camera = new Vector2(Keep(camera.X, at.X, room.X), Keep(camera.Y, at.Y, room.Y));
        }

        _camera = camera;
        Position = -_camera;
    }

    public static Vector2 TileOrigin(GridPosition tile) => new(tile.X * TileSize, tile.Y * TileSize);

    /// <summary>Whether a tile of the room on show is deep water (where swimmers go under).</summary>
    public bool IsDeepWater(GridPosition tile) =>
        _room is not null && _room.Layout.Contains(tile) && _room.Layout[tile] == TileKind.DeepWater;

    public void ShowRoom(Room room)
    {
        bool newRoom = _room?.Id != room.Id;
        _room = room;
        Size = RoomPixels;
        if (newRoom)
        {
            _panned = false;
            _camera = Desired();
            Position = -_camera;
        }

        QueueRedraw();
    }

    /// <param name="hazards">Reachable tiles that hurt: pits and found traps (tinted red over the blue).</param>
    /// <param name="jumps">Tiles a pit jump would land on.</param>
    /// <param name="range">Tiles within the active hero's attack reach (Rumble targeting), tinted red.</param>
    public void SetHighlights(
        IEnumerable<GridPosition> reachable,
        IEnumerable<GridPosition> attackable,
        IEnumerable<GridPosition> targets,
        IEnumerable<GridPosition> interactive,
        IEnumerable<GridPosition>? hazards = null,
        IEnumerable<GridPosition>? jumps = null,
        IEnumerable<GridPosition>? range = null)
    {
        _range = [.. range ?? []];
        _hazards = [.. hazards ?? []];
        _jumps = [.. jumps ?? []];
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
        // The board runs on beyond the view, under the panels around it: only the part in view takes the mouse.
        if (@event is InputEventMouse mouse && !_dragging && !new Rect2(_camera, Pixels, Pixels).HasPoint(mouse.Position))
        {
            return;
        }

        switch (@event)
        {
            case InputEventMouseMotion motion when _dragging:
                _dragged |= motion.Position.DistanceTo(_dragFrom) > DragThreshold;
                if (_dragged)
                {
                    Pan(-motion.Relative);
                }

                AcceptEvent();
                break;

            case InputEventMouseMotion motion:
                UpdateHover(motion.Position);
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                if (TileAt(click.Position) is { } tile)
                {
                    if (click.DoubleClick)
                    {
                        TileDoubleClicked?.Invoke(tile);
                    }
                    else
                    {
                        TileClicked?.Invoke(tile);
                    }
                }

                AcceptEvent();
                break;

            // Right (or middle) button: drag to pan the view; a right-click that doesn't move cancels.
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right or MouseButton.Middle } press:
                _dragging = true;
                _dragged = false;
                _dragFrom = press.Position;
                AcceptEvent();
                break;

            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right or MouseButton.Middle } release:
                if (_dragging && !_dragged && release.ButtonIndex == MouseButton.Right)
                {
                    CancelRequested?.Invoke();
                }

                _dragging = false;
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

        foreach (GridPosition tile in _room.Positions)
        {
            DrawTile(tile, _room.Layout[tile]);
        }

        foreach (GridPosition brazier in _room.TilesOfKind(TileKind.Brazier))
        {
            DrawBrazier(brazier, _room.LitBraziers.Contains(brazier));
        }

        foreach ((GridPosition tile, Trap trap) in _room.Traps)
        {
            if (trap.Revealed)
            {
                DrawTrap(tile, trap.Kind);
            }
        }

        DrawRim();
        foreach ((Direction side, string _) in _room.Doors)
        {
            if (_room.PassageOn(side) is not { IsHidden: true })
            {
                DrawDoor(side, _room.PassageOn(side), _room.IsSealed);
            }
        }

        foreach (GridPosition chest in _room.TilesOfKind(TileKind.Chest))
        {
            if (_room.ChestTrapAt(chest) is { Revealed: true })
            {
                DrawWarning(chest);
            }
        }

        foreach (Tombstone stone in _room.Tombstones)
        {
            DrawTombstone(stone.Position);
        }

        foreach (Bomb bomb in _room.Bombs)
        {
            DrawBomb(bomb);
        }

        foreach ((GridPosition tile, int rounds) in _room.Flames)
        {
            DrawBurningFloor(tile, rounds);
        }

        foreach ((GridPosition tile, bool heroes) in _room.Plants)
        {
            DrawPlants(tile, heroes);
        }

        foreach ((GridPosition tile, int rounds) in _room.Ice)
        {
            DrawIce(tile, rounds);
        }

        _loot.QueueRedraw();

        foreach ((GridPosition tile, int amount) in _room.Gold)
        {
            DrawGold(tile, amount);
        }

        foreach ((GridPosition tile, int count) in _room.Keys)
        {
            DrawKey(tile, "icon_key", count);
        }

        if (_room.BossKeyAt is { } bossKey)
        {
            DrawKey(bossKey, "icon_boss_key", 1);
        }

        DrawOverlay(_reachable, Palette.Reachable);
        DrawOverlay(_hazards, Palette.Hazard);
        DrawOverlay(_jumps, Palette.Jump);
        DrawOverlay(_range, Palette.Range);
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

    /// <summary>Draws a tile from the theme's art: floor first, then whatever stands on it. False when there's no art.</summary>
    private bool DrawTexturedTile(GridPosition tile, TileKind kind)
    {
        if (GameArt.Tile(ThemeId, (tile.X + tile.Y) % 2 == 0 ? "floor_a" : "floor_b") is not { } floor)
        {
            return false;
        }

        Rect2 rect = TileRect(tile);
        string? feature = kind switch
        {
            TileKind.Wall => "wall",
            TileKind.CrackedWall => "cracked_wall",
            TileKind.Pit => "pit",
            TileKind.StairsDown => "stairs",
            TileKind.StairsUp => "stairs_up",
            TileKind.Rubble => "rubble",
            TileKind.Gate => "gate",
            TileKind.ShallowWater => "water_shallow",
            TileKind.DeepWater => "water_deep",
            TileKind.Chest => _room!.IsChestOpened(tile) ? "chest_open"
                : _room.HoldsBossKey(tile) ? "chest_boss"
                : _room.IsGrandChest(tile) ? "chest_grand"
                : _room.ChestLockAt(tile) is not null ? "chest_locked" : "chest",
            TileKind.Pot => "pot",
            TileKind.Exit => _room!.Monsters.Any(monster => monster.Definition.Boss) ? "exit_sealed" : "exit_open",
            _ => null,
        };

        if (kind == TileKind.Pit)
        {
            DrawChasm(tile, rect);
            return true;
        }

        if (kind is not (TileKind.Wall or TileKind.CrackedWall or TileKind.StairsDown or TileKind.StairsUp))
        {
            DrawTextureRect(floor, rect, tile: false);
        }

        if (feature is not null && GameArt.Tile(ThemeId, feature) is { } texture)
        {
            DrawTextureRect(texture, rect, tile: false);
        }

        return true;
    }

    /// <summary>
    /// Pits join into one chasm: a crumbling lip only along edges that border solid ground. Where a
    /// room lies underneath, its floor shows far below; with nothing below the pit is black.
    /// </summary>
    private void DrawChasm(GridPosition tile, Rect2 rect)
    {
        DrawRect(rect, Palette.Pit);
        if (_room!.BelowRoomId is not null && GameArt.Tile(ThemeId, (tile.X + tile.Y) % 2 == 0 ? "floor_b" : "floor_a") is { } below)
        {
            DrawTextureRect(below, rect, tile: false, Palette.FloorBelow);
        }

        Color[] lip = [new Color("2a3a33"), new Color("1a2420"), new Color("0e1412")];
        foreach (Direction side in Enum.GetValues<Direction>())
        {
            if (_room!.Layout[tile.Step(side)] == TileKind.Pit)
            {
                continue;
            }

            for (int depth = 0; depth < lip.Length; depth++)
            {
                float thickness = side == Direction.North ? 3 : 2;
                Rect2 band = side switch
                {
                    Direction.North => new Rect2(rect.Position.X, rect.Position.Y + depth * thickness, TileSize, thickness),
                    Direction.South => new Rect2(rect.Position.X, rect.End.Y - (depth + 1) * thickness, TileSize, thickness),
                    Direction.West => new Rect2(rect.Position.X + depth * thickness, rect.Position.Y, thickness, TileSize),
                    _ => new Rect2(rect.End.X - (depth + 1) * thickness, rect.Position.Y, thickness, TileSize),
                };
                DrawRect(band, lip[depth]);
            }
        }
    }

    /// <summary>Solid rock outside the room's shape, with a wall face along the edges where it meets the room.</summary>
    private void DrawRock(GridPosition tile)
    {
        Rect2 rect = TileRect(tile);
        DrawRect(rect, Palette.Rock);
        foreach (Direction side in Enum.GetValues<Direction>())
        {
            GridPosition next = tile.Step(side);
            if (!_room!.Layout.Contains(next) || _room.Layout[next] == TileKind.Rock)
            {
                continue;
            }

            Rect2 face = side switch
            {
                Direction.North => new Rect2(rect.Position, new Vector2(TileSize, 3)),
                Direction.South => new Rect2(rect.Position + new Vector2(0, TileSize - 6), new Vector2(TileSize, 6)),
                Direction.West => new Rect2(rect.Position, new Vector2(3, TileSize)),
                _ => new Rect2(rect.Position + new Vector2(TileSize - 3, 0), new Vector2(3, TileSize)),
            };
            DrawRect(face, side == Direction.South ? Palette.WallTop : Palette.Wall);
        }
    }

    private void DrawTile(GridPosition tile, TileKind kind)
    {
        if (kind == TileKind.Rock)
        {
            DrawRock(tile);
            return;
        }

        if (DrawTexturedTile(tile, kind))
        {
            return;
        }

        Rect2 rect = TileRect(tile);
        DrawRect(rect, (tile.X + tile.Y) % 2 == 0 ? Palette.FloorLight : Palette.FloorDark);
        DrawLine(rect.Position + new Vector2(0, TileSize - 0.5f), rect.End - new Vector2(0, 0.5f), Palette.Grout);
        DrawLine(rect.Position + new Vector2(TileSize - 0.5f, 0), rect.End - new Vector2(0.5f, 0), Palette.Grout);

        switch (kind)
        {
            case TileKind.Wall or TileKind.CrackedWall:
                DrawRect(rect, Palette.Wall);
                DrawRect(new Rect2(rect.Position, new Vector2(TileSize, 8)), Palette.WallTop);
                DrawRect(rect, Palette.Ink, filled: false, width: 1);
                if (kind == TileKind.CrackedWall)
                {
                    DrawPolyline([rect.Position + new Vector2(9, 4), rect.Position + new Vector2(14, 14), rect.Position + new Vector2(12, 20), rect.Position + new Vector2(19, 28)], Palette.Ink, 2);
                }

                break;

            case TileKind.Pit:
                DrawRect(rect.Grow(-2), _room!.BelowRoomId is null ? Palette.Pit : Palette.FloorDark * Palette.FloorBelow);
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

            case TileKind.Rubble:
                foreach ((float x, float y) in new[] { (5f, 6f), (16f, 4f), (22f, 14f), (9f, 18f), (18f, 24f) })
                {
                    DrawRect(new Rect2(rect.Position.X + x, rect.Position.Y + y, 6, 5), Palette.WallTop);
                }

                break;

            case TileKind.Gate:
                for (float x = 4; x < TileSize; x += 6)
                {
                    DrawRect(new Rect2(rect.Position.X + x, rect.Position.Y + 2, 2, TileSize - 4), Palette.Ink);
                }

                DrawRect(new Rect2(rect.Position.X + 12, rect.Position.Y + 12, 8, 8), Palette.Loot);
                break;

            case TileKind.ShallowWater:
                DrawRect(rect, Palette.ShallowWater);
                break;

            case TileKind.DeepWater:
                DrawRect(rect, Palette.DeepWater);
                break;

            case TileKind.StairsUp:
                DrawRect(rect.Grow(-2), Palette.FloorDark);
                for (int step = 0; step < 4; step++)
                {
                    float inset = 13 - step * 3;
                    DrawRect(new Rect2(rect.Position.X + inset, rect.Position.Y + 5 + step * 6, TileSize - inset * 2, 3), Palette.Text);
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
                DrawRect(new Rect2(body.Position + new Vector2(0, 5), new Vector2(20, 2)), open ? Palette.TrimDim : _room.ChestLockAt(tile) is not null ? Palette.TextDim : Palette.TrimBright);
                DrawRect(body, Palette.Ink, filled: false, width: 1);
                break;
        }
    }

    /// <summary>A revealed trap on a floor tile: spikes or a trap door.</summary>
    private void DrawTrap(GridPosition tile, TrapKind kind)
    {
        Rect2 rect = TileRect(tile);
        string artName = kind switch { TrapKind.TrapDoor => "trapdoor", TrapKind.BombTrap => "bomb_trap", _ => "spikes" };
        if (GameArt.Tile(ThemeId, artName) is { } art)
        {
            DrawTextureRect(art, rect, tile: false);
            return;
        }

        DrawRect(rect.Grow(-5), kind == TrapKind.TrapDoor ? Palette.Chest : Palette.TextDim, filled: kind == TrapKind.TrapDoor);
        DrawRect(rect.Grow(-5), Palette.Ink, filled: false, width: 1);
    }

    /// <summary>A bomb: unlit, or lit with its fuse sparking and the turns left on it.</summary>
    private void DrawBomb(Bomb bomb)
    {
        Rect2 rect = TileRect(bomb.Position);
        bool spark = Time.GetTicksMsec() / 140 % 2 == 0;
        string art = !bomb.Lit ? "bomb" : spark ? "bomb_lit" : "bomb_lit2";
        if (GameArt.Tile(ThemeId, art) is { } texture)
        {
            DrawTextureRect(texture, rect, tile: false);
        }
        else
        {
            DrawCircle(rect.GetCenter() + new Vector2(0, 3), 9, Palette.Ink);
            if (bomb.Lit)
            {
                DrawCircle(rect.GetCenter() + new Vector2(6, -9), 2.5f, spark ? Palette.FireCore : Palette.Fire);
            }
        }

        if (bomb.Lit)
        {
            DrawString(ThemeDB.FallbackFont, rect.Position + new Vector2(2, 10), bomb.FuseTurns.ToString(), fontSize: UiTheme.FontSize, modulate: Palette.FireCore);
        }
    }

    /// <summary>Plants Life dice grew: leafy green tufts for the party's side, dark thorny ones for the monsters'.</summary>
    private void DrawPlants(GridPosition tile, bool heroes)
    {
        Rect2 rect = TileRect(tile);
        Color leaf = heroes ? DieTypes.Tint(DieType.Life) : new Color("4a6a3a");
        Color shade = leaf.Darkened(0.4f);
        DrawRect(rect.Grow(-3), leaf with { A = 0.16f });
        foreach ((float x, float y) in new[] { (6f, 26f), (16f, 29f), (26f, 25f), (11f, 14f), (22f, 12f) })
        {
            var foot = rect.Position + new Vector2(x, y);
            DrawColoredPolygon([foot + new Vector2(-3, 0), foot + new Vector2(0, -7), foot + new Vector2(1, 0)], shade);
            DrawColoredPolygon([foot + new Vector2(-1, 0), foot + new Vector2(2, -6), foot + new Vector2(3, 0)], leaf);
        }
    }

    /// <summary>A brazier: a bowl of coals on three legs; lit, flames leap from it and it glows.</summary>
    private void DrawBrazier(GridPosition tile, bool lit)
    {
        Rect2 rect = TileRect(tile);
        Vector2 bowl = rect.Position + new Vector2(TileSize / 2f, 15);
        if (lit)
        {
            float glow = 0.12f + 0.04f * Mathf.Sin(Time.GetTicksMsec() / 1000f * 6 + tile.X);
            DrawCircle(bowl, 15, Palette.FireCore with { A = glow }, antialiased: true);
        }

        foreach (float x in new[] { -7f, 0f, 7f })
        {
            DrawLine(bowl + new Vector2(x * 0.6f, 2), bowl + new Vector2(x, 14), Palette.Ink, 2);
        }

        DrawColoredPolygon([bowl + new Vector2(-10, -2), bowl + new Vector2(10, -2), bowl + new Vector2(6, 4), bowl + new Vector2(-6, 4)], Palette.TextDim);
        DrawRect(new Rect2(bowl + new Vector2(-10, -3), new Vector2(20, 2)), Palette.Ink);
        if (!lit)
        {
            return;
        }

        float time = Time.GetTicksMsec() / 1000f;
        for (int i = 0; i < 3; i++)
        {
            float height = 10 + 3 * Mathf.Sin(time * 9 + i * 2.1f + tile.Y) + (i == 1 ? 3 : 0);
            Vector2 foot = bowl + new Vector2(-6 + i * 6, -3);
            DrawColoredPolygon([foot + new Vector2(-3, 0), foot + new Vector2(3, 0), foot + new Vector2(0, -height)], Palette.Fire);
            DrawColoredPolygon([foot + new Vector2(-1.5f, 0), foot + new Vector2(1.5f, 0), foot + new Vector2(0, -height * 0.55f)], Palette.FireCore);
        }
    }

    /// <summary>An ice patch: pale blue with a sheen and a crack or two, and the rounds it has left.</summary>
    private void DrawIce(GridPosition tile, int rounds)
    {
        Rect2 rect = TileRect(tile);
        Color ice = DieTypes.Tint(DieType.Ice);
        DrawRect(rect.Grow(-1), ice with { A = 0.45f });
        DrawLine(rect.Position + new Vector2(5, 8), rect.Position + new Vector2(14, 4), Colors.White with { A = 0.7f }, 1.5f);
        DrawLine(rect.Position + new Vector2(18, 26), rect.Position + new Vector2(27, 19), Colors.White with { A = 0.5f }, 1);
        DrawLine(rect.Position + new Vector2(10, 18), rect.Position + new Vector2(16, 22), ice.Darkened(0.3f), 1);
        DrawString(ThemeDB.FallbackFont, rect.Position + new Vector2(TileSize - 7, 10), rounds.ToString(), fontSize: UiTheme.FontSize, modulate: Palette.Ink);
    }

    /// <summary>Burning floor: a red-orange glow and three flickering tongues of flame, and the rounds it has left.</summary>
    private void DrawBurningFloor(GridPosition tile, int rounds)
    {
        Rect2 rect = TileRect(tile);
        float time = Time.GetTicksMsec() / 1000f;
        DrawRect(rect.Grow(-2), Palette.Fire with { A = 0.28f + 0.08f * Mathf.Sin(time * 7 + tile.X) });
        for (int i = 0; i < 3; i++)
        {
            float flicker = 3 * Mathf.Sin(time * 9 + i * 2.1f + tile.Y);
            var foot = rect.Position + new Vector2(7 + i * 9, 27);
            float height = 14 + flicker + (i == 1 ? 4 : 0);
            DrawColoredPolygon([foot + new Vector2(-4, 0), foot + new Vector2(4, 0), foot + new Vector2(1, -height)], Palette.Fire);
            DrawColoredPolygon([foot + new Vector2(-2, 0), foot + new Vector2(2, 0), foot + new Vector2(0, -height * 0.55f)], Palette.FireCore);
        }

        DrawString(ThemeDB.FallbackFont, rect.Position + new Vector2(TileSize - 7, 10), rounds.ToString(), fontSize: UiTheme.FontSize, modulate: Palette.FireCore);
    }

    /// <summary>A blast: a flash and a ring of fire spreading over the 3x3 around the bomb.</summary>
    public void Explosion(GridPosition center)
    {
        var blast = new ExplosionView { Position = TileOrigin(center) + new Vector2(TileSize / 2f, TileSize / 2f), ZIndex = 10 };
        AddChild(blast);
    }

    /// <summary>A small red "!" in a tile's corner: a trap someone found on a chest or door.</summary>
    private void DrawWarning(GridPosition tile)
    {
        Vector2 corner = TileOrigin(tile) + new Vector2(TileSize - 9, 1);
        DrawRect(new Rect2(corner, new Vector2(8, 10)), Palette.Danger);
        DrawString(ThemeDB.FallbackFont, corner + new Vector2(2, 8), "!", fontSize: UiTheme.FontSize, modulate: Palette.Text);
    }

    private void DrawKey(GridPosition tile, string icon, int count)
    {
        Vector2 origin = TileOrigin(tile) + new Vector2(3, 3);
        if (GameArt.Ui(icon) is { } art)
        {
            DrawTextureRect(art, new Rect2(origin, new Vector2(18, 18)), tile: false);
        }
        else
        {
            DrawCircle(origin + new Vector2(6, 9), 4, icon == "icon_boss_key" ? Palette.Heart : Palette.Loot);
        }

        if (count > 1)
        {
            DrawString(ThemeDB.FallbackFont, origin + new Vector2(16, 18), count.ToString(), fontSize: UiTheme.FontSize, modulate: Palette.Loot);
        }
    }

    /// <summary>A headstone on a little mound of earth, "RIP" carved in it.</summary>
    private void DrawTombstone(GridPosition tile)
    {
        Vector2 origin = TileOrigin(tile);
        DrawSetTransform(origin + new Vector2(16, 26), 0, new Vector2(1, 0.35f));
        DrawCircle(Vector2.Zero, 11, Palette.Grave, antialiased: true);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // Upright slab with a rounded top.
        var body = new Rect2(origin + new Vector2(9, 11), new Vector2(14, 15));
        DrawRect(body, Palette.Stone);
        DrawCircle(origin + new Vector2(16, 11), 7, Palette.Stone, antialiased: true);
        DrawArc(origin + new Vector2(16, 11), 7, Mathf.Pi, Mathf.Tau, 12, Palette.Ink, 1, antialiased: true);
        DrawLine(body.Position, body.Position + new Vector2(0, body.Size.Y), Palette.Ink, 1);
        DrawLine(body.End - new Vector2(0, body.Size.Y), body.End, Palette.Ink, 1);
        DrawLine(body.Position + new Vector2(0, body.Size.Y), body.End, Palette.Ink, 1);
        DrawRect(new Rect2(origin + new Vector2(18, 6), new Vector2(3, 18)), Palette.StoneShade);
        DrawString(ThemeDB.FallbackFont, origin + new Vector2(9, 19), "RIP", HorizontalAlignment.Center, 14, 6, Palette.Ink);
    }

    /// <summary>A little stack of coins in the bottom-left corner, with the amount.</summary>
    private void DrawGold(GridPosition tile, int amount)
    {
        Vector2 origin = TileOrigin(tile) + new Vector2(3, 20);
        for (int i = 0; i < 3; i++)
        {
            var coin = new Rect2(origin + new Vector2(0, 6 - i * 3), new Vector2(9, 3));
            DrawRect(coin, Palette.Loot);
            DrawRect(coin, Palette.PotRim, filled: false, width: 1);
        }

        DrawString(ThemeDB.FallbackFont, origin + new Vector2(11, 9), amount.ToString(), fontSize: UiTheme.FontSize, modulate: Palette.Loot);
    }

    /// <summary>
    /// Loot on the floor, drawn above the figures so loot under someone still shows: one thing is a gold
    /// gem; two or more lie in a bulging sack with the count. An opened chest with things left in it
    /// shows a little gem and the count at its corner.
    /// </summary>
    private static void DrawLoot(CanvasItem canvas, GridPosition tile, int count, bool inChest)
    {
        if (inChest || count < 2)
        {
            Vector2 origin = TileOrigin(tile) + (inChest ? new Vector2(22, 2) : new Vector2(22, 20));
            canvas.DrawColoredPolygon([origin + new Vector2(4, 0), origin + new Vector2(8, 5), origin + new Vector2(4, 10), origin + new Vector2(0, 5)], Palette.Loot);
            canvas.DrawPolyline([origin + new Vector2(4, 0), origin + new Vector2(8, 5), origin + new Vector2(4, 10), origin + new Vector2(0, 5), origin + new Vector2(4, 0)], Palette.Ink, 1);
            if (count > 1)
            {
                canvas.DrawString(ThemeDB.FallbackFont, origin + new Vector2(-6, 9), count.ToString(), fontSize: UiTheme.FontSize, modulate: Palette.Loot);
            }

            return;
        }

        // A loot bag: a round brown sack tied at the neck, gold peeking out.
        Vector2 bag = TileOrigin(tile) + new Vector2(25, 24);
        Color sack = new("9a6a3a"), dark = new("3a2410");
        canvas.DrawCircle(bag, 6, dark);
        canvas.DrawCircle(bag, 5, sack);
        canvas.DrawColoredPolygon([bag + new Vector2(-3, -5), bag + new Vector2(3, -5), bag + new Vector2(2, -8), bag + new Vector2(-2, -8)], sack);
        canvas.DrawLine(bag + new Vector2(-3, -5), bag + new Vector2(3, -5), dark, 1.5f);
        canvas.DrawCircle(bag + new Vector2(0, -8.5f), 1.6f, Palette.Loot);
        canvas.DrawString(ThemeDB.FallbackFont, bag + new Vector2(-2.5f, 3), count.ToString(), fontSize: 7, modulate: Palette.Ink);
    }

    /// <summary>Draws every loot mark onto the layer above the figures.</summary>
    private void DrawLootMarks(CanvasItem canvas)
    {
        if (_room is null)
        {
            return;
        }

        foreach (GridPosition tile in _room.LootPositions)
        {
            DrawLoot(canvas, tile, _room.LootAt(tile).Sum(card => card.Count), inChest: _room.Layout[tile] == TileKind.Chest);
        }

        // In a dark room, what no light reaches is hard to make out.
        foreach (GridPosition tile in _darkTiles)
        {
            canvas.DrawRect(TileRect(tile), Palette.Background with { A = 0.6f });
        }

        // A blind hero on their turn sees only the tiles beside them.
        if (BlindAt is { } eyes)
        {
            foreach (GridPosition tile in _room.Positions.Where(tile => tile.ChebyshevDistanceTo(eyes) > 1))
            {
                canvas.DrawRect(TileRect(tile), Palette.Background with { A = 0.92f });
            }
        }
    }

    /// <summary>Where the hero whose turn it is stands, while they're blind: the rest of the room goes dark.</summary>
    public GridPosition? BlindAt
    {
        get => _blindAt;
        set
        {
            if (_blindAt != value)
            {
                _blindAt = value;
                _loot.QueueRedraw();
            }
        }
    }

    private GridPosition? _blindAt;

    /// <summary>The shown room's tiles in darkness (a dark room, out of reach of every light).</summary>
    public IReadOnlyCollection<GridPosition> DarkTiles
    {
        get => _darkTiles;
        set
        {
            if (!_darkTiles.SetEquals(value))
            {
                _darkTiles = [.. value];
                _loot.QueueRedraw();
            }
        }
    }

    private HashSet<GridPosition> _darkTiles = [];

    /// <summary>The layer above the figures that loot marks are drawn on.</summary>
    private sealed partial class LootLayer : Node2D
    {
        public BoardView Board { get; init; } = null!;

        public override void _Ready() => ZIndex = 4;

        public override void _Draw() => Board.DrawLootMarks(this);
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

    private GridPosition? TileAt(Vector2 position)
    {
        // Only tiles inside the view: the board runs on under the panels around it.
        if (!new Rect2(_camera, Pixels, Pixels).HasPoint(position))
        {
            return null;
        }

        var tile = new GridPosition(Mathf.FloorToInt(position.X / TileSize), Mathf.FloorToInt(position.Y / TileSize));
        return _room?.Layout.Contains(tile) == true ? tile : null;
    }

    private static Rect2 TileRect(GridPosition tile) => new(TileOrigin(tile), new Vector2(TileSize, TileSize));
}
