using Godot;
using Woad.Core.Chiptune;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Tracker;

/// <summary>
/// The Music Tracker's pattern: a row number down the left, then a column for each channel, each row's
/// note and volume. Beats are shaded, bars more so; the cursor is a gold box and the row playing glows.
/// It takes the keyboard (the screen decides what each key does) and clicks that move the cursor.
/// </summary>
public partial class PatternGrid : Control
{
    public const int RowHeight = 10;
    public const int HeaderHeight = 30;
    public const int NumberWidth = 22;

    /// <summary>The colors the channels are told apart by, in order.</summary>
    public static readonly Color[] ChannelColors = [Palette.DieRumble, Palette.DieOdHand, Palette.DieDefense, Palette.DieWend];

    public ChipSong Song { get; set; } = ChipSong.EightBit();

    public int PatternIndex { get; set; }

    public int CursorChannel { get; set; }

    public int CursorRow { get; set; }

    /// <summary>0: the note; 1: its volume.</summary>
    public int CursorField { get; set; }

    /// <summary>The row of this pattern playing now, or -1.</summary>
    public int PlayRow { get; set; } = -1;

    /// <summary>The first row shown.</summary>
    public int Scroll { get; private set; }

    /// <summary>A key pressed while the grid has the keyboard; return true when it was used.</summary>
    public Func<InputEventKey, bool>? KeyPressed { get; set; }

    /// <summary>A click on a cell: its channel, row and field.</summary>
    public event Action<int, int, int>? CellClicked;

    public ChipPattern Pattern => Song.Patterns[PatternIndex];

    public int VisibleRows => (int)((Size.Y - HeaderHeight) / RowHeight);

    public float ChannelWidth => (Size.X - NumberWidth) / Math.Max(1, Song.Channels.Count);

    public override void _Ready()
    {
        FocusMode = FocusModeEnum.All;
        ClipContents = true;
    }

    /// <summary>Scrolls just enough to keep <paramref name="row"/> in view, then redraws.</summary>
    public void Follow(int row)
    {
        int margin = Math.Min(3, VisibleRows / 4);
        if (row < Scroll + margin)
        {
            Scroll = row - margin;
        }
        else if (row >= Scroll + VisibleRows - margin)
        {
            Scroll = row - VisibleRows + margin + 1;
        }

        Scroll = Math.Clamp(Scroll, 0, Math.Max(0, Pattern.Rows - VisibleRows));
        QueueRedraw();
    }

    /// <summary>The cell's top left corner, inside the grid.</summary>
    public Vector2 CellPosition(int channel, int row, int field = 0) =>
        new(NumberWidth + channel * ChannelWidth + 4 + field * 30, HeaderHeight + (row - Scroll) * RowHeight);

    public override void _GuiInput(InputEvent input)
    {
        switch (input)
        {
            case InputEventKey { Pressed: true } key when KeyPressed?.Invoke(key) == true:
                AcceptEvent();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                Scroll = Math.Clamp(Scroll + (wheel.ButtonIndex == MouseButton.WheelUp ? -4 : 4), 0, Math.Max(0, Pattern.Rows - VisibleRows));
                QueueRedraw();
                AcceptEvent();
                break;

            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                GrabFocus();
                int row = Scroll + (int)((click.Position.Y - HeaderHeight) / RowHeight);
                int channel = (int)((click.Position.X - NumberWidth) / ChannelWidth);
                if (click.Position.Y >= HeaderHeight && row < Pattern.Rows && click.Position.X >= NumberWidth && channel < Song.Channels.Count)
                {
                    float inside = click.Position.X - NumberWidth - channel * ChannelWidth;
                    CellClicked?.Invoke(channel, row, inside >= 32 ? 1 : 0);
                }

                AcceptEvent();
                break;
        }
    }

    public override void _Draw()
    {
        Font font = ThemeDB.FallbackFont;
        const int size = UiTheme.FontSize;
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.PanelDark);

        for (int channel = 0; channel < Song.Channels.Count; channel++)
        {
            float left = NumberWidth + channel * ChannelWidth;
            DrawLine(new Vector2(left, 0), new Vector2(left, Size.Y), Palette.TrimDim);
            ChipChannel settings = Song.Channels[channel];
            DrawString(font, new Vector2(left + 4, 10), settings.Name, fontSize: size, modulate: ChannelColors[channel % ChannelColors.Length]);
            DrawString(font, new Vector2(left + ChannelWidth - 34, 10), $"{(int)Math.Round(settings.Volume * 1000)}", HorizontalAlignment.Right, 30, size, Palette.TextDim);
        }

        DrawLine(new Vector2(0, HeaderHeight - 1), new Vector2(Size.X, HeaderHeight - 1), Palette.Trim);
        int beat = Song.RowsPerBeat;
        for (int row = Scroll; row < Math.Min(Pattern.Rows, Scroll + VisibleRows + 1); row++)
        {
            float y = HeaderHeight + (row - Scroll) * RowHeight;
            if (row == PlayRow)
            {
                DrawRect(new Rect2(0, y, Size.X, RowHeight), Palette.Power with { A = 0.3f });
            }
            else if (row % (beat * 4) == 0)
            {
                DrawRect(new Rect2(0, y, Size.X, RowHeight), Palette.PanelPressed);
            }
            else if (row % beat == 0)
            {
                DrawRect(new Rect2(0, y, Size.X, RowHeight), Palette.Panel);
            }

            DrawString(font, new Vector2(2, y + 8), row.ToString("D2"), fontSize: size, modulate: row % beat == 0 ? Palette.Trim : Palette.TextDim);
            for (int channel = 0; channel < Song.Channels.Count; channel++)
            {
                ChipCell cell = Pattern.Cells[channel][row];
                bool drums = Song.Channels[channel].Wave == ChipWave.Noise;
                Vector2 at = CellPosition(channel, row);
                Color ink = cell.Note == ChipCell.None ? Palette.TextDim with { A = 0.5f } : cell.Note == ChipCell.Off ? Palette.TextDim : Palette.Text;
                DrawString(font, at + new Vector2(0, 8), cell.Note == ChipCell.Off ? "OFF" : cell.NoteText(drums), fontSize: size, modulate: ink);
                DrawString(font, at + new Vector2(30, 8), cell.VolumeText, fontSize: size, modulate: cell.Volume < 0 ? Palette.TextDim with { A = 0.5f } : Palette.DieOdHand);
            }
        }

        if (CursorRow >= Scroll && CursorRow < Scroll + VisibleRows)
        {
            Vector2 at = CellPosition(CursorChannel, CursorRow, CursorField) - new Vector2(2, 0);
            DrawRect(new Rect2(at, new Vector2(CursorField == 0 ? 24 : 10, RowHeight)), HasFocus() ? Palette.TrimBright : Palette.Trim, filled: false);
        }
    }
}
