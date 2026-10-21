using Godot;
using Woad.Core.Grid;
using Woad.Core.Tavern;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Tavern;

/// <summary>
/// Draws a tavern at twice the art's size around the party's leader: the floor plan, its folk
/// (with a name over whoever the leader can talk to) and the party in a line. Clicks name a tile.
/// </summary>
public partial class TavernView : Control
{
    public const int TileSize = 32;

    private static readonly Dictionary<char, string> Art = new()
    {
        ['#'] = "wall", ['B'] = "bar", ['K'] = "barrel", ['F'] = "hearth", ['S'] = "stairs", ['s'] = "stool",
        ['T'] = "table", ['G'] = "dice_table", ['d'] = "desk", ['C'] = "stall", ['R'] = "rug", ['D'] = "door",
    };

    private TavernVisit? _visit;

    public event Action<GridPosition>? TileClicked;

    /// <summary>The party in walking order, leader first: how each is drawn.</summary>
    public IReadOnlyList<LookColors> Party { get; set; } = [];

    public TavernVisit? Visit
    {
        get => _visit;
        set
        {
            _visit = value;
            QueueRedraw();
        }
    }

    private Vector2 Camera
    {
        get
        {
            if (_visit is null)
            {
                return Vector2.Zero;
            }

            TavernDefinition tavern = _visit.Tavern;
            Vector2 center = new Vector2(_visit.Leader.X + 0.5f, _visit.Leader.Y + 0.5f) * TileSize;
            // Follows the leader inside the room; a room smaller than the view sits in its middle.
            static float Axis(float center, float view, float room) =>
                room <= view ? (room - view) / 2 : Math.Clamp(center - view / 2, 0, room - view);
            return new Vector2(
                MathF.Round(Axis(center.X, Size.X, tavern.Width * TileSize)),
                MathF.Round(Axis(center.Y, Size.Y, tavern.Height * TileSize)));
        }
    }

    /// <summary>The middle of a tile, in screen coordinates.</summary>
    internal Vector2 TileCenter(GridPosition tile) =>
        GetGlobalRect().Position + (new Vector2(tile.X + 0.5f, tile.Y + 0.5f) * TileSize) - Camera;

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click && _visit is not null)
        {
            Vector2 pixel = click.Position + Camera;
            TileClicked?.Invoke(new GridPosition((int)MathF.Floor(pixel.X / TileSize), (int)MathF.Floor(pixel.Y / TileSize)));
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Background);
        if (_visit is null)
        {
            return;
        }

        TavernDefinition tavern = _visit.Tavern;
        Vector2 camera = Camera;
        Rect2 TileRect(GridPosition tile) => new(new Vector2(tile.X, tile.Y) * TileSize - camera, new Vector2(TileSize, TileSize));

        for (int y = 0; y < tavern.Height; y++)
        {
            for (int x = 0; x < tavern.Width; x++)
            {
                var tile = new GridPosition(x, y);
                char kind = tavern.TileAt(tile);
                string name = Art.TryGetValue(kind, out string? art) ? art : (x + y) % 2 == 0 ? "planks_a" : "planks_b";
                if (GameArt.Tavern(name) is { } texture)
                {
                    DrawTextureRect(texture, TileRect(tile), tile: false);
                }
                else
                {
                    DrawRect(TileRect(tile), kind == '#' ? new Color("d8c8a0") : new Color("9a6a3c"));
                }
            }
        }

        foreach (TavernNpc npc in tavern.Npcs)
        {
            Rect2 rect = TileRect(npc.Position);
            if (!Looks.Draw(this, rect, Looks.Resolve(npc.Appearance)))
            {
                DrawCircle(rect.GetCenter(), 10, Palette.Trim);
            }
        }

        var walkers = new List<GridPosition> { _visit.Leader };
        walkers.AddRange(_visit.Trail);
        for (int i = Math.Min(walkers.Count, Party.Count) - 1; i >= 0; i--)
        {
            Rect2 rect = TileRect(walkers[i]);
            if (!Looks.Draw(this, rect, Party[i]))
            {
                DrawCircle(rect.GetCenter(), 10, Party[i].Main);
            }
        }

        if (_visit.NpcInReach is { } near)
        {
            Font font = GameArt.BodyFont ?? ThemeDB.FallbackFont;
            Rect2 rect = TileRect(near.Position);
            Vector2 size = font.GetStringSize(near.Name, HorizontalAlignment.Left, -1, UiTheme.FontSize);
            var origin = new Vector2(rect.GetCenter().X - size.X / 2, rect.Position.Y - 3);
            DrawStringOutline(font, origin, near.Name, HorizontalAlignment.Left, -1, UiTheme.FontSize, 3, Palette.Ink);
            DrawString(font, origin, near.Name, HorizontalAlignment.Left, -1, UiTheme.FontSize, Palette.TrimBright);
        }
    }
}
