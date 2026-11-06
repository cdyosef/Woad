using Godot;
using Woad.Core.Dungeon;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Draws one room's terrain, its stone rim and doors (see BoardView.Walls.cs), revealed traps, loot and
/// keys, and the move/target highlights, and reports clicks on tiles. It sits in a clipping frame the
/// size of a standard room (<see cref="Pixels"/>) and is its own camera: a bigger room scrolls to
/// follow whoever is acting, a smaller one is centered. The player can pan (right-drag, a finger, a
/// touchpad swipe or the arrow keys), but only as far as keeps the active hero in view, and zoom (the
/// wheel, a pinch, + and -; Home puts it back).
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

    /// <summary>A finger that moves less than this is a tap (a click on the tile), not a drag.</summary>
    private const float TouchThreshold = 12;

    /// <summary>How far the player can zoom in, and out (no further out than the whole room fits).</summary>
    public const float MaxUserZoom = 2.5f;
    private const float MinUserZoom = 0.5f;

    /// <summary>The most the zoom on the action goes to, on top of the player's.</summary>
    private const float MaxZoom = 3;

    /// <summary>One notch of the wheel zooms by this much.</summary>
    private const float WheelStep = 1.1f;

    /// <summary>How far a touchpad's two-finger swipe pans, in pixels for a whole wheel step.</summary>
    private const float SwipePan = 40;

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

    /// <summary>A finger is down on the board (a touch screen): a tap clicks <see cref="_touchTile"/> when it lifts; a drag pans.</summary>
    private bool _touching;
    private GridPosition? _touchTile;
    private bool _touchDouble;

    /// <summary>The fingers down on the board, by index, where they are on screen (two pinch to zoom).</summary>
    private readonly Dictionary<int, Vector2> _touches = [];

    /// <summary>How far the player has zoomed the board (1: a standard room fills the view).</summary>
    public float UserZoom { get; private set; } = 1;

    /// <summary>The board pixels across the view at the player's zoom.</summary>
    private float ViewPixels => Pixels / UserZoom;

    /// <summary>The zoom on the action at its peak this time (less when the player's zoomed right in).</summary>
    private float _actionPeak = ActionZoom;

    /// <summary>How hard the view is shaking (pixels), dying away.</summary>
    private float _shake;

    /// <summary>How far in the view is zoomed on the action, on top of the player's zoom (1 for not at all), easing toward <see cref="_zoomTarget"/>.</summary>
    private float _zoom = 1;
    private float _zoomTarget = 1;

    /// <summary>What's in view, in board pixels: the camera's view, or less of the board while zoomed in on the action.</summary>
    private Rect2 ViewRect => new(_view, new Vector2(Pixels, Pixels) / (UserZoom * _zoom));

    /// <summary>The board pixel at the top-left of what's in view, zoom and all.</summary>
    private Vector2 _view;

    /// <summary>The board pixel the zoom centers on, gliding toward <see cref="_zoomFocusTarget"/> as the action moves.</summary>
    private Vector2 _zoomFocus;
    private Vector2 _zoomFocusTarget;

    /// <summary>How far in the view zooms on the action: an attack, or a Luck question.</summary>
    public const float ActionZoom = 1.6f;

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
            Place(Vector2.Zero);
        }
    }

    /// <summary>Turns the view to a tile (a bomb going off), ending any pan.</summary>
    public void LookAt(GridPosition tile)
    {
        _follow = null;
        _lookAt = tile;
        _panned = false;
    }

    /// <summary>Zooms the view in on the action, centered between <paramref name="tiles"/> (an attacker and its target).</summary>
    public void ZoomOn(IReadOnlyCollection<GridPosition> tiles)
    {
        if (tiles.Count == 0)
        {
            return;
        }

        _zoomFocusTarget = tiles.Aggregate(Vector2.Zero, (sum, tile) => sum + TileOrigin(tile)) / tiles.Count + new Vector2(TileSize / 2f, TileSize / 2f);
        if (_zoom <= 1.01f)
        {
            _zoomFocus = _zoomFocusTarget;
        }

        _zoomTarget = _actionPeak = Mathf.Clamp(MaxZoom / UserZoom, 1, ActionZoom);
    }

    /// <summary>Eases the view back out to the whole room.</summary>
    public void ZoomOut() => _zoomTarget = 1;

    /// <summary>How far in the zoom on the action is right now (1: not at all).</summary>
    public float ActionZoomLevel => _zoom;

    /// <summary>The zoom has come to rest (in or out), so a tile stays where it is on screen.</summary>
    public bool ZoomSettled => _zoom == _zoomTarget && _zoomFocus.DistanceTo(_zoomFocusTarget) < 0.5f;

    /// <summary>The tile panning must keep in view (the active hero's), or null to pan freely.</summary>
    public void SetPanAnchor(GridPosition? tile) => _anchor = tile;

    /// <summary>Whether a tile is inside the view right now (and so can be clicked).</summary>
    public bool IsInView(GridPosition tile) => ViewRect.Encloses(TileRect(tile));

    public override void _Process(double delta)
    {
        // Lit fuses spark; the ritual circle and the portal swirl.
        if (_room?.Bombs.Any(bomb => bomb.Lit) == true || _room?.Flames.Count > 0 || _room?.LitBraziers.Count > 0 || _room?.PortalLit == true || (_room?.RitualCircle is not null && (RitualUnderWay || _room.Cursed)))
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

        // Zoomed in, the view is centered on the action; easing out, it slides back to where the camera is.
        _zoom = Mathf.Lerp(_zoom, _zoomTarget, 1 - Mathf.Exp(-8 * (float)delta));
        _zoomFocus = _zoomFocus.Lerp(_zoomFocusTarget, 1 - Mathf.Exp(-8 * (float)delta));
        if (Mathf.Abs(_zoom - _zoomTarget) < 0.002f)
        {
            _zoom = _zoomTarget;
        }

        Place(jolt.Round());

        // A room much bigger than the view draws only what's near it: once the view has moved, again.
        if (Culls && _drawnAt.DistanceTo(_camera) > TileSize / 2f)
        {
            QueueRedraw();
            _loot.QueueRedraw();
        }
    }

    /// <summary>The cult's ritual is under way: its circle glows on the temple floor.</summary>
    public bool RitualUnderWay { get; set; }

    private static readonly Color RitualPurple = new("a060ff");

    /// <summary>
    /// The ritual circle: a ring of runes round the circle's middle while the cult chants, and, once the
    /// ritual's done, the portal to the realm of death on it, souls pouring out. A cursed room glows purple.
    /// </summary>
    private void DrawRitual()
    {
        if (_room is not { RitualCircle: { } circle })
        {
            return;
        }

        if (_room.Cursed)
        {
            DrawRect(ShownRect(), RitualPurple with { A = 0.10f });
        }

        if (!RitualUnderWay && !_room.Cursed)
        {
            return;
        }

        float time = (float)Time.GetTicksMsec() / 1000f;
        Vector2 middle = TileOrigin(circle) + new Vector2(TileSize / 2f, TileSize / 2f);
        float radius = TileSize * 2.6f;
        DrawArc(middle, radius, 0, Mathf.Tau, 48, RitualPurple with { A = 0.8f }, 2);
        DrawArc(middle, radius - 6, 0, Mathf.Tau, 48, RitualPurple with { A = 0.4f }, 1);
        for (int k = 0; k < 10; k++)
        {
            float angle = time * 0.4f + Mathf.Tau * k / 10;
            Vector2 at = middle + Vector2.FromAngle(angle) * (radius - 3);
            DrawRect(new Rect2(at - new Vector2(1.5f, 1.5f), new Vector2(3, 3)), RitualPurple);
        }

        if (!_room.Cursed)
        {
            return;
        }

        // The portal: a swirl of purple energy, souls streaming up out of it.
        for (int ring = 5; ring >= 0; ring--)
        {
            float r = TileSize * (0.4f + ring * 0.25f);
            DrawCircle(middle, r, new Color(0.35f + ring * 0.05f, 0.1f, 0.6f + ring * 0.05f, 0.35f + (5 - ring) * 0.1f));
        }

        for (int k = 0; k < 6; k++)
        {
            float angle = -time * 1.5f + Mathf.Tau * k / 6;
            DrawArc(middle, TileSize * (0.6f + k * 0.12f), angle, angle + 1.6f, 12, new Color("e0c0ff") with { A = 0.6f }, 1.5f);
        }

        for (int k = 0; k < 8; k++)
        {
            float rise = (time * 0.35f + k / 8f) % 1f;
            float sway = Mathf.Sin(time * 2 + k) * 6;
            Vector2 soul = middle + new Vector2(sway + (k - 3.5f) * 4, -rise * TileSize * 3);
            DrawCircle(soul, 3 - rise * 1.5f, new Color("d8ffe8") with { A = 1 - rise });
            DrawCircle(soul + new Vector2(-1, -0.5f), 0.6f, Palette.Ink with { A = 1 - rise });
            DrawCircle(soul + new Vector2(1, -0.5f), 0.6f, Palette.Ink with { A = 1 - rise });
        }
    }

    /// <summary>Where the view stood when the board was last drawn.</summary>
    private Vector2 _drawnAt;

    /// <summary>The room is far bigger than the view (a graveyard): only the tiles near the view are drawn.</summary>
    private bool Culls => _room is { IsHuge: true };

    /// <summary>The part of the board drawn: the view and two tiles round it, or all of it.</summary>
    private Rect2 ShownRect() => Culls
        ? new Rect2(_camera - new Vector2(2 * TileSize, 2 * TileSize), new Vector2(ViewPixels + 4 * TileSize, ViewPixels + 4 * TileSize))
        : new Rect2(Vector2.Zero, RoomPixels);

    /// <summary>The tiles drawn (see <see cref="ShownRect"/>).</summary>
    private IEnumerable<GridPosition> ShownTiles()
    {
        if (!Culls)
        {
            return _room!.Positions;
        }

        Rect2 shown = ShownRect();
        int left = Math.Max(0, (int)(shown.Position.X / TileSize)), top = Math.Max(0, (int)(shown.Position.Y / TileSize));
        int right = Math.Min(_room!.Layout.Width - 1, (int)(shown.End.X / TileSize)), bottom = Math.Min(_room.Layout.Height - 1, (int)(shown.End.Y / TileSize));
        return Enumerable.Range(top, Math.Max(0, bottom - top + 1)).SelectMany(y => Enumerable.Range(left, Math.Max(0, right - left + 1)).Select(x => new GridPosition(x, y)));
    }

    /// <summary>A white pulse out from <paramref name="tile"/> to <paramref name="reach"/> tiles round it (a search).</summary>
    public void Pulse(GridPosition tile, int reach) =>
        AddChild(new SearchPulse { Position = TileOrigin(tile) + new Vector2(TileSize / 2f, TileSize / 2f), Reach = reach * TileSize + TileSize / 2f });

    /// <summary>Shakes the view, harder for bigger blows.</summary>
    public void Shake(float strength) => _shake = Math.Max(_shake, strength);

    /// <summary>A fighting-game callout (HIT!, K.O.!...) over a tile, kept inside the view.</summary>
    /// <param name="size">Multiples of the heading font: 2 for most, 3 for the big ones.</param>
    /// <param name="below">Shifts it down a line (for a second callout, like a combo count).</param>
    /// <param name="lift">How far above the usual callout (LUCKY over a HIT!).</param>
    public void Callout(GridPosition tile, string text, Color color, int size = 2, bool below = false, float lift = 0)
    {
        Vector2 at = TileOrigin(tile) + new Vector2(TileSize / 2f, (below ? 30 : 0) - lift);
        float half = text.Length * UiTheme.HeadingPixelSize * size * 0.45f;
        at.X = Mathf.Clamp(at.X, _camera.X + half + 4, _camera.X + ViewPixels - half - 4);
        at.Y = Mathf.Clamp(at.Y, _camera.Y + 14 * size / 2f, _camera.Y + ViewPixels - 8);
        AddChild(new ActionCallout { Position = at, Text = text, Color = color, Size = size });
    }

    private NameTag? _nameTag;

    /// <summary>The name of who's under the mouse, on a tag over their tile; null to take it down.</summary>
    public void ShowNameTag(GridPosition? tile, string text = "")
    {
        if (_nameTag is null)
        {
            _nameTag = new NameTag();
            AddChild(_nameTag);
        }

        _nameTag.Visible = tile is not null && text.Length > 0;
        if (tile is { } at)
        {
            _nameTag.Text = text;
            _nameTag.Position = TileOrigin(at) + new Vector2(TileSize / 2f, -3);
            _nameTag.QueueRedraw();
        }
    }

    /// <summary>A shot flying between two points on the board (in its own coordinates). Returns how long it takes.</summary>
    public double Shot(Vector2 from, Vector2 to, ShotKind kind, Color color)
    {
        var shot = new ShotFx { Position = Vector2.Zero, From = from, To = to, Kind = kind, Color = color };
        AddChild(shot);
        return shot.Seconds;
    }

    /// <summary>A weapon's swing (or a spear's thrust) from <paramref name="at"/> toward <paramref name="toward"/>.</summary>
    public void Swing(Vector2 at, Vector2 toward, Color color, bool thrust) =>
        AddChild(new SwingFx { Position = at, Angle = (toward - at).Angle(), Color = color, Thrust = thrust, Backhand = Random.Shared.Next(2) == 0 });

    /// <summary>A shove's rush of chevrons from <paramref name="at"/> into <paramref name="toward"/>.</summary>
    public void Push(Vector2 at, Vector2 toward, Color color) =>
        AddChild(new PushFx { Position = at, To = toward - at, Color = color });

    /// <summary>Feathers off a figure's wings: a puff on a wingbeat, a burst when they break.</summary>
    public void Feathers(Vector2 at, bool burst) =>
        AddChild(new FeatherFx { Position = at, Burst = burst });

    /// <summary>A strike's slash and sparks on a tile; sparks only for a block.</summary>
    public void Spark(GridPosition tile, Color color, bool slash = true) =>
        AddChild(new HitSpark { Position = TileOrigin(tile) + new Vector2(TileSize / 2f, TileSize / 2f - 2), Color = color, Slash = slash, Backhand = Random.Shared.Next(2) == 0 });

    /// <summary>A small dark tag with a name on it, centered over a point.</summary>
    private sealed partial class NameTag : Node2D
    {
        public string Text { get; set; } = "";

        public override void _Ready() => ZIndex = 18;

        public override void _Draw()
        {
            Font font = GameArt.BodyFont ?? ThemeDB.FallbackFont;
            float width = font.GetStringSize(Text, fontSize: UiTheme.FontSize).X + 6;
            var rect = new Rect2(-width / 2, -11, width, 11);
            DrawRect(rect, Palette.Ink with { A = 0.9f });
            DrawRect(rect, Palette.TextDim, filled: false, width: 1);
            DrawString(font, new Vector2(-width / 2 + 3, -3), Text, fontSize: UiTheme.FontSize, modulate: Palette.Text);
        }
    }

    /// <summary>Where the camera wants to be: centered on what it follows, within the room.</summary>
    private Vector2 Desired()
    {
        Vector2 center = _follow is not null && IsInstanceValid(_follow) && _follow.GetParent() == this
            ? _follow.Position + new Vector2(TileSize / 2f, TileSize / 2f)
            : _lookAt is { } tile ? TileOrigin(tile) + new Vector2(TileSize / 2f, TileSize / 2f)
            : RoomPixels / 2 - new Vector2(0, TopMargin / 2);
        return Clamp(center - new Vector2(ViewPixels / 2, ViewPixels / 2));
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
            return (_camera.X > 1, _camera.Y > 1 - TopMargin, _camera.X + ViewPixels < room.X - 1, _camera.Y + ViewPixels < room.Y - 1);
        }
    }

    private Vector2 RoomPixels => _room is null ? new Vector2(Pixels, Pixels) : new Vector2(_room.Layout.Width * TileSize, _room.Layout.Height * TileSize);

    /// <summary>Inside the room (and what shows above it: the keep's wall over its courtyard); a room smaller than the view is centered in it.</summary>
    private Vector2 Clamp(Vector2 camera)
    {
        Vector2 room = RoomPixels;
        float view = ViewPixels;
        float Axis(float value, float from, float to) => to - from <= view ? (from + to - view) / 2 : Mathf.Clamp(value, from, to - view);
        return new Vector2(Axis(camera.X, 0, room.X), Axis(camera.Y, -TopMargin, room.Y));
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
            float view = ViewPixels;
            float Keep(float value, float anchor, float size) => size <= view ? value : Mathf.Clamp(value, anchor + TileSize - view, anchor);
            camera = new Vector2(Keep(camera.X, at.X, room.X), Keep(camera.Y, at.Y, room.Y));
        }

        _camera = camera;
        Place(Vector2.Zero);
    }

    /// <summary>The least the player can zoom to here: out until the whole room fits, but no further than <see cref="MinUserZoom"/>, nor below 1.</summary>
    private float LeastZoom => Mathf.Clamp(Pixels / Mathf.Max(RoomPixels.X, RoomPixels.Y + TopMargin), MinUserZoom, 1);

    /// <summary>Zooms the player's view by <paramref name="factor"/>, keeping <paramref name="at"/> (a board pixel) where it is on screen.</summary>
    public void ZoomAt(Vector2 at, float factor)
    {
        float zoom = Mathf.Clamp(UserZoom * factor, LeastZoom, MaxUserZoom);
        if (Mathf.IsEqualApprox(zoom, UserZoom))
        {
            return;
        }

        Vector2 onScreen = (at - _camera) * UserZoom;
        UserZoom = zoom;
        _camera = at - onScreen / zoom;
        Pan(Vector2.Zero);
        QueueRedraw();
    }

    /// <summary>Zooms the player's view in or out about its middle (+ and -).</summary>
    public void ZoomBy(float factor) => ZoomAt(_camera + new Vector2(ViewPixels / 2, ViewPixels / 2), factor);

    /// <summary>Puts the player's zoom back to 1 and the view back on the action (Home).</summary>
    public void ResetZoom()
    {
        UserZoom = Mathf.Max(1, LeastZoom);
        _panned = false;
        QueueRedraw();
    }

    /// <summary>
    /// Sets the board's scale and position from the camera, the player's zoom and the zoom on the action:
    /// zoomed in on the action, the view is centered on it; easing out, it slides back to where the camera is.
    /// </summary>
    private void Place(Vector2 jolt)
    {
        float scale = UserZoom * _zoom;
        float zoomed = _actionPeak > 1 ? Mathf.Clamp((_zoom - 1) / (_actionPeak - 1), 0, 1) : 0;
        _view = _camera.Lerp(_zoomFocus - new Vector2(Pixels, Pixels) / (2 * scale), zoomed);
        Scale = new Vector2(scale, scale);
        Position = -_view * scale + jolt;
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
            // The player's zoom carries over, as far as the new room allows.
            _panned = false;
            UserZoom = Mathf.Clamp(UserZoom, LeastZoom, MaxUserZoom);
            _camera = Desired();
            Place(Vector2.Zero);
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

    public override bool _CanDropData(Vector2 atPosition, Variant data) => CardDrag.Accepts(data);

    public override void _DropData(Vector2 atPosition, Variant data) => CardDrag.Drop(data, new DropTarget(DropKind.Board));

    public override void _GuiInput(InputEvent @event)
    {
        // The board runs on beyond the view, under the panels around it: only the part in view takes the mouse.
        if (@event is InputEventMouse mouse && !_dragging && !ViewRect.HasPoint(mouse.Position))
        {
            return;
        }

        switch (@event)
        {
            case InputEventMouseMotion motion when _dragging:
                _dragged |= motion.GlobalPosition.DistanceTo(_dragFrom) > (_touching ? TouchThreshold : DragThreshold);
                if (_dragged && _touches.Count < 2)
                {
                    Pan(-motion.Relative);
                }

                AcceptEvent();
                break;

            // A finger on a touch screen (it comes as the mouse): a drag pans; a tap clicks the tile when it lifts.
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } touch when touch.Device == InputEvent.DeviceIdEmulation:
                _dragging = _touching = true;
                _dragged = _touches.Count > 1;
                _dragFrom = touch.GlobalPosition;
                _touchTile = TileAt(touch.Position);
                _touchDouble = touch.DoubleClick;
                AcceptEvent();
                break;

            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left } lift when _touching:
                if (!_dragged && _touchTile is { } tapped)
                {
                    (_touchDouble ? TileDoubleClicked : TileClicked)?.Invoke(tapped);
                }

                _dragging = _touching = false;
                AcceptEvent();
                break;

            // The wheel: a mouse's notch zooms, about the pointer, and so does a touchpad's pinch (it comes as
            // Ctrl and the wheel); a touchpad's two-finger swipe (finer steps), Shift and the wheel, or a tilt, pans.
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight } wheel:
                float steps = wheel.Factor > 0 ? wheel.Factor : 1;
                bool vertical = wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && !wheel.ShiftPressed;
                float sign = wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelLeft ? -1 : 1;
                if (vertical && (wheel.CtrlPressed || steps >= 0.999f))
                {
                    ZoomAt(wheel.Position, Mathf.Pow(WheelStep, -sign * steps));
                }
                else
                {
                    Pan((vertical ? new Vector2(0, sign) : new Vector2(sign, 0)) * steps * SwipePan / UserZoom);
                }

                AcceptEvent();
                break;

            // A touchpad's gestures, where they come as such (a Mac's): a pinch zooms, a two-finger swipe pans.
            case InputEventMagnifyGesture pinch:
                ZoomAt(pinch.Position, pinch.Factor);
                AcceptEvent();
                break;

            case InputEventPanGesture swipe:
                Pan(swipe.Delta * SwipePan / UserZoom);
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
                _dragFrom = press.GlobalPosition;
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

    /// <summary>Two fingers on the board (a touch screen) pinch to zoom, and pan as they move together.</summary>
    public override void _Input(InputEvent @event)
    {
        if (!IsVisibleInTree())
        {
            return;
        }

        Transform2D toBoard = GetGlobalTransformWithCanvas().AffineInverse();
        switch (@event)
        {
            case InputEventScreenTouch { Pressed: true } touch when ViewRect.HasPoint(toBoard * touch.Position):
                _touches[touch.Index] = touch.Position;
                _dragged |= _touches.Count > 1;
                break;

            case InputEventScreenTouch { Pressed: false } touch:
                _touches.Remove(touch.Index);
                break;

            case InputEventScreenDrag drag when _touches.Count == 2 && _touches.ContainsKey(drag.Index):
                Vector2 other = _touches.First(finger => finger.Key != drag.Index).Value;
                Vector2 was = _touches[drag.Index];
                _touches[drag.Index] = drag.Position;
                Pan(toBoard * ((was + other) / 2) - toBoard * ((drag.Position + other) / 2));
                if (was.DistanceTo(other) > 1)
                {
                    ZoomAt(GetGlobalTransformWithCanvas().AffineInverse() * ((drag.Position + other) / 2), drag.Position.DistanceTo(other) / was.DistanceTo(other));
                }

                GetViewport().SetInputAsHandled();
                break;

            case InputEventScreenDrag drag when _touches.ContainsKey(drag.Index):
                _touches[drag.Index] = drag.Position;
                break;
        }
    }

    /// <summary>+ and - zoom the board; Home puts the zoom back.</summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!IsVisibleInTree() || @event is not InputEventKey { Pressed: true } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Equal or Key.Plus or Key.KpAdd:
                ZoomBy(WheelStep * WheelStep);
                break;
            case Key.Minus or Key.KpSubtract:
                ZoomBy(1 / (WheelStep * WheelStep));
                break;
            case Key.Home:
                ResetZoom();
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
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

        _drawnAt = _camera;
        bool courtyard = IsCourtyard;
        if (courtyard)
        {
            DrawCourtyardSurrounds();
        }

        foreach (GridPosition tile in ShownTiles())
        {
            if (courtyard)
            {
                DrawCourtyardTile(tile);
            }
            else
            {
                DrawTile(tile, _room.Layout[tile]);
            }
        }

        if (courtyard)
        {
            DrawKeepFacade();
        }

        foreach (GridPosition brazier in _room.TilesOfKind(TileKind.Brazier))
        {
            DrawBrazier(brazier, _room.LitBraziers.Contains(brazier));
        }

        foreach (GridPosition portal in _room.TilesOfKind(TileKind.Portal))
        {
            DrawPortal(portal, _room.PortalLit);
        }

        foreach ((GridPosition tile, Trap trap) in _room.Traps)
        {
            if (trap.Revealed)
            {
                DrawTrap(tile, trap.Kind);
            }
        }

        // Outdoors, no rim; the keep's great door is drawn with its wall.
        if (!courtyard)
        {
            DrawRim();
        }

        foreach ((Direction side, string _) in courtyard ? new Dictionary<Direction, string>() : _room.Doors)
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

        foreach ((GridPosition tile, int amount) in _room.Coins)
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

        DrawRitual();
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
            TileKind.Chest when _room!.RelicChest == tile => _room.IsChestOpened(tile) ? "chest_open" : "chest_boss",
            TileKind.Chest when _room!.ChestLookAt(tile) is ChestLook.Grave => _room.IsChestOpened(tile) ? "grave_open" : "grave",
            TileKind.Chest when _room!.ChestLookAt(tile) is ChestLook.Sarcophagus => _room.IsChestOpened(tile) ? "sarcophagus_open"
                : _room.ChestLockAt(tile) is not null ? "sarcophagus_locked" : "sarcophagus",
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
    /// <summary>A portal: a ring of standing stone, dark, or lit with a swirl of green light.</summary>
    private void DrawPortal(GridPosition tile, bool lit)
    {
        Rect2 rect = TileRect(tile);
        Vector2 center = rect.GetCenter();
        float time = Time.GetTicksMsec() / 1000f;
        DrawCircle(center + new Vector2(0, 2), 14, Palette.Ink with { A = 0.35f }, antialiased: true);
        if (lit)
        {
            DrawCircle(center, 12 + Mathf.Sin(time * 3) * 1.2f, PortalGlow with { A = 0.35f }, antialiased: true);
            DrawCircle(center, 9, PortalGlow with { A = 0.75f }, antialiased: true);
            for (int arm = 0; arm < 3; arm++)
            {
                float start = time * 2.5f + arm * Mathf.Tau / 3;
                DrawArc(center, 6, start, start + 2.2f, 10, PortalCore, 2, antialiased: true);
            }
        }
        else
        {
            DrawCircle(center, 10, Palette.Pit, antialiased: true);
        }

        DrawArc(center, 12, 0, Mathf.Tau, 28, Palette.Wall, 5, antialiased: true);
        DrawArc(center, 12, 0, Mathf.Tau, 28, lit ? PortalCore : Palette.WallTop, 1.5f, antialiased: true);
        for (int stone = 0; stone < 6; stone++)
        {
            float angle = stone * Mathf.Tau / 6 - Mathf.Pi / 2;
            DrawCircle(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 12, 2.2f, lit ? PortalGlow : Palette.TextDim, antialiased: true);
        }
    }

    private static readonly Color PortalGlow = new("3fd98a");
    private static readonly Color PortalCore = new("d8ffe9");

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
        Rect2 shown = ShownRect();
        foreach (GridPosition tile in _darkTiles.Where(tile => shown.HasPoint(TileOrigin(tile))))
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
        if (!ViewRect.HasPoint(position))
        {
            return null;
        }

        // The keep's great door, up in its wall: its threshold.
        if (IsCourtyard && GreatDoorRect.HasPoint(position))
        {
            return KeepCourtyard.Door;
        }

        var tile = new GridPosition(Mathf.FloorToInt(position.X / TileSize), Mathf.FloorToInt(position.Y / TileSize));
        return _room?.Layout.Contains(tile) == true ? tile : null;
    }

    private static Rect2 TileRect(GridPosition tile) => new(TileOrigin(tile), new Vector2(TileSize, TileSize));
}
