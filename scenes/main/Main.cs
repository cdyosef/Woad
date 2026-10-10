using Godot;
using Woad.Core.Dice;
using Woad.Core.Grid;
using Woad.Core.Randomness;

namespace Woad.Scenes;

/// <summary>
/// Scaffold scene: lays out the 640x360 screen (gear panel, 9x9 room, dice panel),
/// highlights the tile under the mouse and rolls 2d6 through Woad.Core on click.
/// Replaced by the real main menu in a later milestone.
/// </summary>
public partial class Main : Node2D
{
    private const int TileSize = 32;
    private const int BoardPixels = RoomGrid.Size * TileSize;
    private const int ScreenWidth = 640;
    private const int ScreenHeight = 360;
    private const int PanelWidth = (ScreenWidth - BoardPixels) / 2;
    private const int FontSize = 16;

    private static readonly Vector2 BoardOrigin = new(PanelWidth, (ScreenHeight - BoardPixels) / 2);

    private static readonly Color Background = new("0b0a14");
    private static readonly Color PanelFill = new("1a1626");
    private static readonly Color PanelTrim = new("b08d4a");
    private static readonly Color FloorLight = new("4f7d5c");
    private static readonly Color FloorDark = new("436e50");
    private static readonly Color Highlight = new("f2d16b");
    private static readonly Color Text = new("e8e0c8");

    private readonly DiceRoller _dice = new(new Pcg32((ulong)Time.GetTicksUsec()));

    private GridPosition? _hovered;
    private GridPosition? _clicked;
    private DiceRoll? _lastRoll;

    public override void _Ready()
    {
        GD.Print($"Woad scaffold running on Godot {Engine.GetVersionInfo()["string"]}, " +
                 $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        GD.Print($"Test roll 2d6: {_dice.Roll(2)}");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion:
                UpdateHover();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } when _hovered is { } tile:
                _clicked = tile;
                _lastRoll = _dice.Roll(2);
                GD.Print($"Clicked {tile}, rolled 2d6 {_lastRoll}");
                QueueRedraw();
                break;
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(0, 0, ScreenWidth, ScreenHeight), Background);
        DrawPanel(new Rect2(0, 0, PanelWidth, ScreenHeight));
        DrawPanel(new Rect2(PanelWidth + BoardPixels, 0, PanelWidth, ScreenHeight));

        for (int y = 0; y < RoomGrid.Size; y++)
        {
            for (int x = 0; x < RoomGrid.Size; x++)
            {
                DrawRect(TileRect(new GridPosition(x, y)), (x + y) % 2 == 0 ? FloorLight : FloorDark);
            }
        }

        if (_hovered is { } hovered)
        {
            DrawRect(TileRect(hovered).Grow(-1), Highlight, filled: false, width: 2);
        }

        Font font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(12, 28), "WOAD", fontSize: FontSize * 2, modulate: PanelTrim);
        DrawString(font, new Vector2(12, 52), "Scaffold build", fontSize: FontSize, modulate: Text);

        float rightX = PanelWidth + BoardPixels + 12;
        DrawString(font, new Vector2(rightX, 28), "Click a tile", fontSize: FontSize, modulate: Text);
        if (_clicked is { } clicked && _lastRoll is not null)
        {
            DrawString(font, new Vector2(rightX, 52), $"Tile {clicked}", fontSize: FontSize, modulate: Text);
            DrawString(font, new Vector2(rightX, 72), $"2d6 {_lastRoll}", fontSize: FontSize, modulate: Highlight);
        }
    }

    private void DrawPanel(Rect2 rect)
    {
        DrawRect(rect, PanelFill);
        DrawRect(rect.Grow(-3), PanelTrim, filled: false, width: 2);
    }

    private void UpdateHover()
    {
        Vector2 local = GetLocalMousePosition() - BoardOrigin;
        var tile = new GridPosition(Mathf.FloorToInt(local.X / TileSize), Mathf.FloorToInt(local.Y / TileSize));
        GridPosition? hovered = tile.IsInRoom ? tile : null;

        if (hovered != _hovered)
        {
            _hovered = hovered;
            QueueRedraw();
        }
    }

    private static Rect2 TileRect(GridPosition tile) =>
        new(BoardOrigin + new Vector2(tile.X, tile.Y) * TileSize, new Vector2(TileSize, TileSize));
}
