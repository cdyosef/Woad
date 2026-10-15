using Godot;
using Woad.Core.Dice;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// Shows the dice of the latest roll and the defense rolls answering it, each with its fixed bonus
/// (weapon, armor or Speed) and total. Dice tumble (jittering and spinning) before they land with a
/// small pop; the side that wins an attack glows gold. Power Dice are purple. When Luck allows, dice
/// in the first row can be clicked to reroll them.
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
    /// <param name="bonus">The fixed value added to the dice (weapon, armor or Speed).</param>
    /// <param name="powerDice">How many of the last faces are Power Dice.</param>
    public async Task ShowRoll(string caption, DiceRoll roll, int bonus, bool newGroup, int powerDice = 0)
    {
        if (newGroup)
        {
            _rows.Clear();
        }
        else if (_rows.Count == MaxRows)
        {
            _rows.RemoveAt(1);
        }

        var row = new Row(caption, [.. roll.Faces], bonus, roll.Faces.Count - powerDice);
        _rows.Add(row);
        await Tumble(row, Enumerable.Range(0, row.Faces.Length));
    }

    /// <summary>Rolls a Power Die into the first row (the attack or move being made).</summary>
    public async Task AddPowerDie(int face)
    {
        if (_rows.Count == 0)
        {
            return;
        }

        Row old = _rows[0];
        var row = new Row(old.Caption, [.. old.Faces, face], old.Bonus, old.PowerFrom) { Won = old.Won };
        _rows[0] = row;
        await Tumble(row, [row.Faces.Length - 1]);
    }

    /// <summary>Lights up the winner of the attack just resolved: the attack row on a hit, else the latest defense.</summary>
    public void ShowOutcome(bool hit)
    {
        if (_rows.Count > 0)
        {
            _rows[hit ? 0 : ^1].Won = true;

            // Against a defense roll, the defender's line says how it went.
            if (_rows.Count > 1)
            {
                _rows[^1].Verdict = hit ? "Hit!" : "Miss!";
            }

            QueueRedraw();
        }
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
            if (row.Verdict is { } verdict)
            {
                float after = font.GetStringSize(row.Caption, fontSize: UiTheme.FontSize).X + 4;
                DrawString(font, new Vector2(after, top + 8), verdict, fontSize: UiTheme.FontSize, modulate: verdict == "Hit!" ? Palette.Danger : Palette.Heal);
            }


            for (int i = 0; i < row.Faces.Length; i++)
            {
                float scale = 1 + 0.35f * row.Pop[i];
                DrawSetTransform(DieRect(r, i).GetCenter() + row.Offset[i], row.Angle[i], new Vector2(scale, scale));
                DrawDie(row.Shown[i], row.Won, r == 0 && _rerollable, power: i >= row.PowerFrom);
                DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }

            if (!row.Rolling.Any(rolling => rolling))
            {
                float x = row.Faces.Length == 0 ? 0 : DieRect(r, row.Faces.Length - 1).End.X + 4;
                string sum = (row.Faces.Length == 0 ? "no dice " : "")
                    + (row.Bonus == 0 ? "" : row.Bonus > 0 ? $"+{row.Bonus} " : $"{row.Bonus} ")
                    + $"= {row.Faces.Sum() + row.Bonus}";
                DrawString(font, new Vector2(x, top + 21), sum, fontSize: UiTheme.FontSize, modulate: row.Won ? Palette.TrimBright : Palette.Text);
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
        foreach (int i in tumbling)
        {
            row.Rolling[i] = true;
        }

        for (int frame = 0; frame < 7; frame++)
        {
            foreach (int i in tumbling)
            {
                row.Shown[i] = Random.Shared.Next(1, 7);
                row.Angle[i] = (float)(Random.Shared.NextDouble() - 0.5);
                row.Offset[i] = new Vector2(Random.Shared.Next(-2, 3), Random.Shared.Next(-2, 3));
            }

            QueueRedraw();
            await ToSignal(GetTree().CreateTimer(0.04), SceneTreeTimer.SignalName.Timeout);
        }

        foreach (int i in tumbling)
        {
            row.Shown[i] = row.Faces[i];
            row.Angle[i] = 0;
            row.Offset[i] = Vector2.Zero;
            row.Rolling[i] = false;
        }

        // Land with a small pop that settles back to normal size.
        Tween pop = CreateTween();
        pop.TweenMethod(Callable.From<float>(amount =>
        {
            foreach (int i in tumbling)
            {
                row.Pop[i] = amount;
            }

            QueueRedraw();
        }), 1.0f, 0.0f, 0.18);
        await ToSignal(pop, Tween.SignalName.Finished);
    }

    /// <summary>Draws a die centered on the current transform's origin.</summary>
    private void DrawDie(int face, bool success, bool clickable, bool power)
    {
        var rect = new Rect2(-DieSize / 2, -DieSize / 2, DieSize, DieSize);
        if (success)
        {
            DrawRect(rect.Grow(2), Palette.TrimBright with { A = 0.35f });
        }

        DrawRect(rect, power ? Palette.Power : success ? Palette.TrimBright : Palette.Text);
        DrawRect(new Rect2(rect.Position.X, rect.End.Y - 2, rect.Size.X, 2), success ? Palette.Trim : Palette.TextDim);
        DrawRect(rect, clickable ? Palette.Heal : Palette.Ink, filled: false, width: clickable ? 1.5f : 1);
        foreach (Vector2 pip in Pips[Math.Clamp(face, 0, 6)])
        {
            DrawCircle(rect.Position + pip * rect.Size, 1.4f, Palette.Ink, antialiased: true);
        }
    }

    private static Rect2 DieRect(int row, int index) =>
        new(new Vector2(index * (DieSize + Gap) + 3, row * RowHeight + 11), new Vector2(DieSize, DieSize));

    private sealed class Row(string caption, int[] faces, int bonus, int powerFrom)
    {
        /// <summary>Faces from this index on are Power Dice.</summary>
        public int PowerFrom { get; } = powerFrom;

        public string Caption { get; } = caption;

        public int[] Faces { get; } = faces;

        public int[] Shown { get; } = [.. faces];

        public int Bonus { get; } = bonus;

        /// <summary>This side won the attack; its dice glow.</summary>
        public bool Won { get; set; }

        /// <summary>On a defense line: "Hit!" when the attack got through, "Miss!" when it was blocked.</summary>
        public string? Verdict { get; set; }

        public bool[] Rolling { get; } = new bool[faces.Length];

        public float[] Angle { get; } = new float[faces.Length];

        public Vector2[] Offset { get; } = new Vector2[faces.Length];

        public float[] Pop { get; } = new float[faces.Length];
    }
}
