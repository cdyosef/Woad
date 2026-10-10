using Godot;

namespace Woad.Scenes.Ui;

/// <summary>A row of pixel hearts: full for current hearts, dark for lost ones.</summary>
public partial class HeartsView : Control
{
    private const float HeartWidth = 8;
    private const float Gap = 2;

    private int _hearts;
    private int _maxHearts;

    public void ShowHearts(int hearts, int maxHearts)
    {
        _hearts = hearts;
        _maxHearts = maxHearts;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Texture2D? full = GameArt.Ui("heart_full");
        Texture2D? empty = GameArt.Ui("heart_empty");
        for (int i = 0; i < _maxHearts; i++)
        {
            var origin = new Vector2(i * (HeartWidth + Gap), 0);
            Texture2D? texture = i < _hearts ? full : empty;
            if (texture is not null)
            {
                DrawTextureRect(texture, new Rect2(origin, new Vector2(HeartWidth, HeartWidth)), tile: false);
            }
            else
            {
                DrawHeart(this, origin, HeartWidth, i < _hearts ? Palette.Heart : Palette.HeartEmpty);
            }
        }
    }

    /// <summary>Draws a heart whose bounding box starts at <paramref name="origin"/>.</summary>
    public static void DrawHeart(CanvasItem canvas, Vector2 origin, float width, Color color)
    {
        float lobe = width / 4;
        canvas.DrawCircle(origin + new Vector2(lobe, lobe), lobe, color);
        canvas.DrawCircle(origin + new Vector2(width - lobe, lobe), lobe, color);
        canvas.DrawColoredPolygon(
            [origin + new Vector2(0, lobe), origin + new Vector2(width, lobe), origin + new Vector2(width / 2, width * 0.9f)],
            color);
    }
}
