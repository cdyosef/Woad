using Godot;
using Woad.Core.Art;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Paint;

public enum PaintTool
{
    Pencil,
    Eraser,
    Fill,
    Pick,
}

/// <summary>
/// The Pixel Paint screen's grid: 16x16 pixels, each empty (""), a player-colored paint or an eye (one
/// character, as in <see cref="PaintedLook"/>) or a fixed color ("#rrggbb"). The left button paints with the
/// tool, the right one erases; a drawing to trace can lie under the grid (the wheel zooms it, the middle
/// button drags it).
/// </summary>
public partial class PaintCanvas : Control
{
    public const int Cells = PaintedLook.Size;

    private const int UndoDepth = 100;

    private readonly List<string[,]> _undo = [];

    private readonly List<string[,]> _redo = [];

    private string[,] _pixels = Blank();

    private Color[] _tints = [Colors.White, Colors.White, Colors.White, Colors.White];

    private bool _mirror;

    private Texture2D? _trace;

    private float _traceOpacity = 0.5f;

    private float _traceZoom = 1;

    private Vector2 _traceOffset;

    /// <summary>The button a stroke is being drawn with, and the last cell it touched.</summary>
    private MouseButton _stroke = MouseButton.None;

    private Vector2I _last;

    private bool _strokeChanged;

    private bool _panning;

    /// <summary>The pixels changed (painted, undone, loaded).</summary>
    public event Action? Changed;

    /// <summary>The Pick tool took this paint from the grid.</summary>
    public event Action<string>? Picked;

    public PaintTool Tool { get; set; } = PaintTool.Pencil;

    /// <summary>The paint the pencil and fill lay down.</summary>
    public string Paint { get; set; } = "T";

    /// <summary>Whatever is painted on one half is painted on the other too.</summary>
    public bool Mirror
    {
        get => _mirror;
        set { _mirror = value; QueueRedraw(); }
    }

    /// <summary>The main, trim, skin and hair colors the player-colored paints are shown in.</summary>
    public Color[] Tints
    {
        get => _tints;
        set { _tints = value; QueueRedraw(); }
    }

    public Texture2D? Trace
    {
        get => _trace;
        set
        {
            _trace = value;
            _traceZoom = 1;
            _traceOffset = Vector2.Zero;
            QueueRedraw();
        }
    }

    public float TraceOpacity
    {
        get => _traceOpacity;
        set { _traceOpacity = value; QueueRedraw(); }
    }

    public float TraceZoom => _traceZoom;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string this[int x, int y] => _pixels[x, y];

    public float CellSize => Size.X / Cells;

    /// <summary>For the click tests: the middle of a cell, in this control.</summary>
    public Vector2 CellCenter(int x, int y) => new Vector2(x + 0.5f, y + 0.5f) * CellSize;

    public static string[,] Blank()
    {
        var pixels = new string[Cells, Cells];
        for (int y = 0; y < Cells; y++)
        {
            for (int x = 0; x < Cells; x++)
            {
                pixels[x, y] = "";
            }
        }

        return pixels;
    }

    /// <summary>The color a paint shows as: its own, or its player color (darker for a shade).</summary>
    public static Color ColorOf(string paint, Color[] tints)
    {
        if (paint.Length != 1)
        {
            return new Color(paint);
        }

        int channel = PaintedLook.Channel(paint[0]);
        if (channel == 0)
        {
            return Looks.EyeColor;
        }

        Color tint = tints[channel - 1];
        return PaintedLook.IsShade(paint[0]) ? new Color(tint.R * Looks.MaskShade.R, tint.G * Looks.MaskShade.G, tint.B * Looks.MaskShade.B) : tint;
    }

    public override void _Ready()
    {
        ClipContents = true;
        MouseDefaultCursorShape = CursorShape.Cross;
    }

    /// <summary>Puts a whole picture on the grid, forgetting what could be undone.</summary>
    public void Load(string[,] pixels)
    {
        _pixels = (string[,])pixels.Clone();
        _undo.Clear();
        _redo.Clear();
        Touch();
    }

    public string[,] Snapshot() => (string[,])_pixels.Clone();

    public void Clear()
    {
        Remember();
        _pixels = Blank();
        Touch();
    }

    public void Undo() => Step(_undo, _redo);

    public void Redo() => Step(_redo, _undo);

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } button:
                if (button.Pressed && _stroke == MouseButton.None)
                {
                    _stroke = button.ButtonIndex;
                    _strokeChanged = false;
                    _last = CellAt(button.Position);
                    Apply(_last);
                }
                else if (!button.Pressed && button.ButtonIndex == _stroke)
                {
                    _stroke = MouseButton.None;
                }

                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } button:
                _panning = button.Pressed && _trace is not null;
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel when _trace is not null:
                ZoomTrace(wheel.ButtonIndex == MouseButton.WheelUp ? 1.1f : 1 / 1.1f, wheel.Position);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _panning:
                _traceOffset += motion.Relative;
                QueueRedraw();
                break;
            case InputEventMouseMotion motion when _stroke != MouseButton.None && StrokeTool is PaintTool.Pencil or PaintTool.Eraser:
                Vector2I cell = CellAt(motion.Position);
                foreach (Vector2I each in Line(_last, cell))
                {
                    Apply(each);
                }

                _last = cell;
                break;
        }
    }

    public override void _Draw()
    {
        float cell = CellSize;
        var dark = new Color("3a3550");
        var light = new Color("454060");
        for (int y = 0; y < Cells; y++)
        {
            for (int x = 0; x < Cells; x++)
            {
                DrawRect(new Rect2(x * cell, y * cell, cell, cell), (x + y) % 2 == 0 ? dark : light);
            }
        }

        if (_trace is not null)
        {
            DrawTextureRect(_trace, TraceRect(), false, new Color(1, 1, 1, _traceOpacity));
        }

        for (int y = 0; y < Cells; y++)
        {
            for (int x = 0; x < Cells; x++)
            {
                string paint = _pixels[x, y];
                if (paint.Length == 0)
                {
                    continue;
                }

                DrawRect(new Rect2(x * cell, y * cell, cell, cell), ColorOf(paint, _tints));
                if (paint[0] == PaintedLook.Eye)
                {
                    // An eye is marked, to tell it from any other dark pixel.
                    DrawRect(new Rect2(x * cell + cell / 2 - 2, y * cell + cell / 2 - 2, 4, 4), Palette.TrimBright);
                }
            }
        }

        var line = new Color(0, 0, 0, 0.18f);
        for (int at = 1; at < Cells; at++)
        {
            DrawLine(new Vector2(at * cell, 0), new Vector2(at * cell, Size.Y), line);
            DrawLine(new Vector2(0, at * cell), new Vector2(Size.X, at * cell), line);
        }

        if (_mirror)
        {
            DrawLine(new Vector2(Size.X / 2, 0), new Vector2(Size.X / 2, Size.Y), new Color(Palette.TrimBright, 0.6f));
        }

        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Trim, false);
    }

    /// <summary>The right button always erases; the left uses the tool picked.</summary>
    private PaintTool StrokeTool => _stroke == MouseButton.Right ? PaintTool.Eraser : Tool;

    private Vector2I CellAt(Vector2 position) => new(
        Math.Clamp((int)Mathf.Floor(position.X / CellSize), 0, Cells - 1),
        Math.Clamp((int)Mathf.Floor(position.Y / CellSize), 0, Cells - 1));

    private void Apply(Vector2I cell)
    {
        switch (StrokeTool)
        {
            case PaintTool.Pencil:
                Set(cell, Paint);
                break;
            case PaintTool.Eraser:
                Set(cell, "");
                break;
            case PaintTool.Fill:
                Fill(cell, Paint);
                if (_mirror)
                {
                    Fill(Mirrored(cell), Paint);
                }

                break;
            case PaintTool.Pick:
                if (_pixels[cell.X, cell.Y].Length > 0)
                {
                    Picked?.Invoke(_pixels[cell.X, cell.Y]);
                }

                break;
        }
    }

    private static Vector2I Mirrored(Vector2I cell) => new(Cells - 1 - cell.X, cell.Y);

    private void Set(Vector2I cell, string paint)
    {
        Put(cell, paint);
        if (_mirror)
        {
            Put(Mirrored(cell), paint);
        }
    }

    private void Put(Vector2I cell, string paint)
    {
        if (_pixels[cell.X, cell.Y] == paint)
        {
            return;
        }

        RememberStroke();
        _pixels[cell.X, cell.Y] = paint;
        Touch();
    }

    private void Fill(Vector2I start, string paint)
    {
        string old = _pixels[start.X, start.Y];
        if (old == paint)
        {
            return;
        }

        RememberStroke();
        var open = new Stack<Vector2I>([start]);
        while (open.Count > 0)
        {
            Vector2I cell = open.Pop();
            if (cell.X < 0 || cell.Y < 0 || cell.X >= Cells || cell.Y >= Cells || _pixels[cell.X, cell.Y] != old)
            {
                continue;
            }

            _pixels[cell.X, cell.Y] = paint;
            open.Push(cell + Vector2I.Left);
            open.Push(cell + Vector2I.Right);
            open.Push(cell + Vector2I.Up);
            open.Push(cell + Vector2I.Down);
        }

        Touch();
    }

    /// <summary>A whole stroke undoes in one step: the picture is remembered at its first change.</summary>
    private void RememberStroke()
    {
        if (!_strokeChanged)
        {
            _strokeChanged = true;
            Remember();
        }
    }

    private void Remember()
    {
        _undo.Add(Snapshot());
        if (_undo.Count > UndoDepth)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    private void Step(List<string[,]> from, List<string[,]> to)
    {
        if (from.Count == 0)
        {
            return;
        }

        to.Add(Snapshot());
        _pixels = from[^1];
        from.RemoveAt(from.Count - 1);
        Touch();
    }

    private void Touch()
    {
        QueueRedraw();
        Changed?.Invoke();
    }

    /// <summary>Every cell from one to another in a straight line, so a quick drag leaves no gaps.</summary>
    private static IEnumerable<Vector2I> Line(Vector2I from, Vector2I to)
    {
        int steps = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
        for (int step = 1; step <= steps; step++)
        {
            yield return new Vector2I(
                from.X + (int)Math.Round((to.X - from.X) * (double)step / steps),
                from.Y + (int)Math.Round((to.Y - from.Y) * (double)step / steps));
        }
    }

    /// <summary>The drawing fills the grid (all of it showing) until it's zoomed and dragged.</summary>
    private Rect2 TraceRect()
    {
        Vector2 size = _trace!.GetSize();
        float fit = Math.Min(Size.X / size.X, Size.Y / size.Y) * _traceZoom;
        Vector2 shown = size * fit;
        return new Rect2((Size - shown) / 2 + _traceOffset, shown);
    }

    /// <summary>Zooms the drawing about the mouse, so what's under it stays under it.</summary>
    private void ZoomTrace(float factor, Vector2 about)
    {
        float zoom = Math.Clamp(_traceZoom * factor, 0.25f, 16);
        factor = zoom / _traceZoom;
        Rect2 before = TraceRect();
        Vector2 center = before.GetCenter();
        _traceOffset += (about - center) * (1 - factor);
        _traceZoom = zoom;
        QueueRedraw();
    }
}
