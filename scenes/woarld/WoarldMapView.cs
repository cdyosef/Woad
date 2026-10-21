using Godot;
using Woad.Core.Grid;
using Woad.Core.Woarld;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Woarld;

/// <summary>
/// Draws the part of the Woarld Map around the party's leader: terrain, clouds over what hasn't
/// been explored, Parts Unknown, the places, and the party walking in a line. Clicks name a tile.
/// </summary>
public partial class WoarldMapView : Control
{
    public const int TileSize = 16;

    private static readonly Dictionary<Terrain, string> TerrainArt = new()
    {
        [Terrain.Grass] = "grass_a",
        [Terrain.Forest] = "forest",
        [Terrain.Hills] = "hills",
        [Terrain.Mountain] = "mountain",
        [Terrain.Water] = "water",
        [Terrain.Road] = "road",
        [Terrain.Bridge] = "bridge",
        [Terrain.Sand] = "sand",
        [Terrain.Unknown] = "unknown",
    };

    /// <summary>Fallback colors, if the art is missing.</summary>
    private static readonly Dictionary<Terrain, Color> TerrainColors = new()
    {
        [Terrain.Grass] = new("5f9a4c"),
        [Terrain.Forest] = new("3f7a36"),
        [Terrain.Hills] = new("78a456"),
        [Terrain.Mountain] = new("8a8a98"),
        [Terrain.Water] = new("2c5c9c"),
        [Terrain.Road] = new("b8945e"),
        [Terrain.Bridge] = new("b07840"),
        [Terrain.Sand] = new("dcc88e"),
        [Terrain.Unknown] = new("3a3226"),
    };

    private WoarldJourney? _journey;

    public event Action<GridPosition>? TileClicked;

    /// <summary>The party in walking order, leader first: how each is drawn.</summary>
    public IReadOnlyList<LookColors> Party { get; set; } = [];

    /// <summary>Where a click sent the party, marked until they get there.</summary>
    public GridPosition? Destination { get; set; }

    public WoarldJourney? Journey
    {
        get => _journey;
        set
        {
            _journey = value;
            QueueRedraw();
        }
    }

    /// <summary>The map pixel at the view's top-left: centered on the leader, kept inside the map.</summary>
    private Vector2 Camera
    {
        get
        {
            if (_journey is null)
            {
                return Vector2.Zero;
            }

            WoarldMap map = _journey.Map;
            Vector2 center = new Vector2(_journey.Leader.X + 0.5f, _journey.Leader.Y + 0.5f) * TileSize;
            float x = Math.Clamp(center.X - Size.X / 2, 0, Math.Max(0, map.Width * TileSize - Size.X));
            float y = Math.Clamp(center.Y - Size.Y / 2, 0, Math.Max(0, map.Height * TileSize - Size.Y));
            return new Vector2(MathF.Round(x), MathF.Round(y));
        }
    }

    /// <summary>The middle of a map tile, in screen coordinates.</summary>
    internal Vector2 TileCenter(GridPosition tile) =>
        GetGlobalRect().Position + (new Vector2(tile.X + 0.5f, tile.Y + 0.5f) * TileSize) - Camera;

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click && _journey is not null)
        {
            Vector2 pixel = click.Position + Camera;
            TileClicked?.Invoke(new GridPosition((int)MathF.Floor(pixel.X / TileSize), (int)MathF.Floor(pixel.Y / TileSize)));
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Background);
        if (_journey is null)
        {
            return;
        }

        WoarldMap map = _journey.Map;
        Vector2 camera = Camera;
        int left = (int)(camera.X / TileSize), top = (int)(camera.Y / TileSize);
        int right = Math.Min(map.Width - 1, (int)((camera.X + Size.X) / TileSize));
        int bottom = Math.Min(map.Height - 1, (int)((camera.Y + Size.Y) / TileSize));
        Rect2 TileRect(GridPosition tile) => new(new Vector2(tile.X, tile.Y) * TileSize - camera, new Vector2(TileSize, TileSize));

        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                var tile = new GridPosition(x, y);
                Terrain terrain = map.TerrainAt(tile);
                string art = terrain == Terrain.Grass && (x * 7 + y * 13) % 3 == 0 ? "grass_b" : TerrainArt[terrain];
                DrawArt(art, TileRect(tile), TerrainColors[terrain]);
            }
        }

        foreach (WoarldPlace place in map.Places.Where(place => _journey.IsRevealed(place.Position)))
        {
            DrawArt("place_" + place.Icon, TileRect(place.Position), Palette.TrimBright);
            if (!_journey.IsOpen(place))
            {
                DrawArt("place_lock", TileRect(place.Position), Palette.Danger);
            }
        }

        // Clouds over the unexplored; Parts Unknown shows as itself.
        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                var tile = new GridPosition(x, y);
                if (!_journey.IsRevealed(tile) && map.TerrainAt(tile) != Terrain.Unknown)
                {
                    DrawCloud(tile, TileRect(tile));
                }
            }
        }

        if (Destination is { } destination)
        {
            DrawRect(TileRect(destination).Grow(-2), Palette.TrimBright, filled: false, width: 1);
        }

        // Followers from the back of the line, so the leader is drawn on top.
        var walkers = new List<GridPosition> { _journey.Leader };
        walkers.AddRange(_journey.Trail);
        for (int i = Math.Min(walkers.Count, Party.Count) - 1; i >= 0; i--)
        {
            Rect2 rect = TileRect(walkers[i]);
            if (!Looks.Draw(this, rect, Party[i]))
            {
                DrawCircle(rect.GetCenter(), 6, Party[i].Main);
            }
        }

        if (!_journey.IsDay)
        {
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.04f, 0.05f, 0.2f, 0.45f));
        }

        Font font = GameArt.BodyFont ?? ThemeDB.FallbackFont;
        foreach (WoarldLabel label in map.Labels)
        {
            Vector2 at = new Vector2(label.X + 0.5f, label.Y + 0.75f) * TileSize - camera;
            Vector2 size = font.GetStringSize(label.Text, HorizontalAlignment.Left, -1, 8);
            Vector2 origin = new(Math.Clamp(at.X - size.X / 2, 2, Size.X - size.X - 2), at.Y);
            DrawStringOutline(font, origin, label.Text, HorizontalAlignment.Left, -1, 8, 3, Palette.Ink);
            DrawString(font, origin, label.Text, HorizontalAlignment.Left, -1, 8, Palette.Trim);
        }
    }

    /// <summary>The cloud art covers 3x3 tiles: each tile draws its own part, so the haze doesn't repeat every tile.</summary>
    private void DrawCloud(GridPosition tile, Rect2 rect)
    {
        if (GameArt.Woarld("cloud") is { } texture)
        {
            int span = texture.GetWidth() / TileSize;
            DrawTextureRectRegion(texture, rect, new Rect2(tile.X % span * TileSize, tile.Y % span * TileSize, TileSize, TileSize));
        }
        else
        {
            DrawRect(rect, new Color("8a94aa"));
        }
    }

    private void DrawArt(string name, Rect2 rect, Color fallback)
    {
        if (GameArt.Woarld(name) is { } texture)
        {
            DrawTextureRect(texture, rect, tile: false);
        }
        else
        {
            DrawRect(rect, fallback);
        }
    }
}
