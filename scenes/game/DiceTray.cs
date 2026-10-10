using Godot;
using Woad.Core.Dice;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Shows the dice of the latest roll and the defense rolls answering it. Successful faces
/// are gold. When Luck allows, dice in the first row can be clicked to reroll them.
/// </summary>
public partial class DiceTray : Control
{
    private const float DieSize = 14;
    private const float Gap = 3;
    private const float RowHeight = 26;
    private const int MaxRows = 3;

    private static readonly Vector2[][] Pips =
    [
        [],
        [new(0.5f, 0.5f)],
        [new(0.27f, 0.27f), new(0.73f, 0.73f)],
        [new(0.27f, 0.27f), new(0.5f, 0.5f), new(0.73f, 0.73f)],
        [new(0.27f, 0.27f), new(0.73f, 0.27f), new(0.27f, 0.73f), new(0.73f, 0.73f)],
        [new(0.27f, 0.27f), new(0.73f, 0.27f), new(0.5f, 0.5f), new(0.27f, 0.73f), new(0.73f, 0.73f)],
        [new(0.27f, 0.25f), new(0.73f, 0.25f), new(0.27f, 0.5f), new(0.73f, 0.5f), new(0.27f, 0.75f), new(0.73f, 0.75f)],
    ];

    private readonly List<Row> _rows = [];
    private bool _rerollable;

    public event Action<int>? DieClicked;

    public bool Rerollable
    {
        set { _rerollable = value; QueueRedraw(); }
    }

    public void Clear()
    {
        _rows.Clear();
        QueueRedraw();
    }

    /// <summary>Tumbles and then shows a roll. A new group replaces what the tray showed before.</summary>
    /// <param name="successFace">Faces at or above this are highlighted; 0 highlights nothing.</param>
    public async Task ShowRoll(string caption, DiceRoll roll, int successFace, bool newGroup)
    {
        if (newGroup)
        {
            _rows.Clear();
        }
        else if (_rows.Count == MaxRows)
        {
            _rows.RemoveAt(1);
        }

        var row = new Row(caption, [.. roll.Faces], successFace);
        _rows.Add(row);
        await Tumble(row, Enumerable.Range(0, row.Faces.Length));
    }

    public async Task Reroll(int dieIndex, int newFace)
    {
        if (_rows.Count == 0 || dieIndex >= _rows[0].Faces.Length)
        {
            return;
        }

        _rows[0].Faces[dieIndex] = newFace;
        await Tumble(_rows[0], [dieIndex]);
    }

    public override void _Draw()
    {
        Font font = ThemeDB.FallbackFont;
        for (int r = 0; r < _rows.Count; r++)
        {
            Row row = _rows[r];
            float top = r * RowHeight;
            DrawString(font, new Vector2(0, top + 8), row.Caption, fontSize: UiTheme.FontSize, modulate: Palette.TextDim);

            if (row.Faces.Length == 0)
            {
                DrawString(font, new Vector2(0, top + 21), "no dice", fontSize: UiTheme.FontSize, modulate: Palette.TextDim);
            }

            for (int i = 0; i < row.Faces.Length; i++)
            {
                int face = row.Shown[i];
                bool success = row.SuccessFace > 0 && !row.Rolling && face >= row.SuccessFace;
                DrawDie(DieRect(r, i), face, success, r == 0 && _rerollable);
            }
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click
            || !_rerollable
            || _rows.Count == 0)
        {
            return;
        }

        for (int i = 0; i < _rows[0].Faces.Length; i++)
        {
            if (DieRect(0, i).Grow(1).HasPoint(click.Position))
            {
                DieClicked?.Invoke(i);
                AcceptEvent();
                return;
            }
        }
    }

    private async Task Tumble(Row row, IEnumerable<int> dice)
    {
        int[] tumbling = dice.ToArray();
        row.Rolling = true;
        for (int frame = 0; frame < 6; frame++)
        {
            foreach (int i in tumbling)
            {
                row.Shown[i] = Random.Shared.Next(1, 7);
            }

            QueueRedraw();
            await ToSignal(GetTree().CreateTimer(0.045), SceneTreeTimer.SignalName.Timeout);
        }

        foreach (int i in tumbling)
        {
            row.Shown[i] = row.Faces[i];
        }

        row.Rolling = false;
        QueueRedraw();
    }

    private void DrawDie(Rect2 rect, int face, bool success, bool clickable)
    {
        DrawRect(rect, success ? Palette.TrimBright : Palette.Text);
        DrawRect(rect, clickable ? Palette.Heal : Palette.Ink, filled: false, width: clickable ? 1.5f : 1);
        foreach (Vector2 pip in Pips[Math.Clamp(face, 0, 6)])
        {
            DrawCircle(rect.Position + pip * rect.Size, 1.4f, Palette.Ink, antialiased: true);
        }
    }

    private static Rect2 DieRect(int row, int index) =>
        new(new Vector2(index * (DieSize + Gap) + 1, row * RowHeight + 11), new Vector2(DieSize, DieSize));

    private sealed class Row(string caption, int[] faces, int successFace)
    {
        public string Caption { get; } = caption;

        public int[] Faces { get; } = faces;

        public int[] Shown { get; } = [.. faces];

        public int SuccessFace { get; } = successFace;

        public bool Rolling { get; set; }
    }
}
