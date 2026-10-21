using Godot;
using Woad.Core.Dice;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>What a roll is, for its dice's color: Rumble red, Wend green, Od Hand yellow, Defense blue; others plain.</summary>
public enum DiceKind
{
    Plain,
    Rumble,
    Wend,
    OdHand,
    Defense,
}

/// <summary>
/// The dice of the latest roll, fighting-game style under the board: the roller and their dice on the
/// left, and when someone answers it, a VS and every defender's dice on the right (smaller when there
/// are several, as in a blast). Each side shows its fixed bonus (weapon, armor or Speed) and total;
/// dice tumble before they land with a small pop, and the side that wins glows gold. Dice are colored
/// by what they are (see <see cref="DiceKind"/>); tools' dice are bronze and Power Dice purple. When
/// Luck allows, the roller's dice can be clicked to reroll them.
/// </summary>
public partial class DiceBar : Control
{
    private const float BigDie = 13;
    private const float Gap = 3;
    private const float SideWidth = 118;
    private const int MaxDefenders = 6;

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

    private readonly List<Row> _defenders = [];
    private Row? _roller;
    private bool _rerollable;

    public event Action<int>? DieClicked;

    public bool Rerollable
    {
        set { _rerollable = value; QueueRedraw(); }
    }

    public void Clear()
    {
        _roller = null;
        _defenders.Clear();
        QueueRedraw();
    }

    /// <summary>Tumbles and then shows a roll: a new roll on the left (clearing the bar), or a defender answering it on the right.</summary>
    /// <param name="bonus">The fixed value added to the dice (weapon, armor or Speed).</param>
    /// <param name="powerDice">How many of the last faces are Power Dice.</param>
    /// <param name="toolDice">How many faces, just before the Power Dice, are the tools' own dice (bronze).</param>
    public async Task ShowRoll(string caption, DiceRoll roll, int bonus, bool newGroup, int powerDice = 0, DiceKind kind = DiceKind.Plain, int toolDice = 0)
    {
        var row = new Row(caption, [.. roll.Faces], bonus, roll.Faces.Count - powerDice) { Kind = kind, ToolFrom = roll.Faces.Count - powerDice - toolDice };
        if (newGroup || _roller is null)
        {
            _roller = row;
            _defenders.Clear();
        }
        else
        {
            if (_defenders.Count == MaxDefenders)
            {
                _defenders.RemoveAt(0);
            }

            _defenders.Add(row);
        }

        await Tumble(row, Enumerable.Range(0, row.Faces.Length));
    }

    /// <summary>Rolls a Power Die into the roller's dice.</summary>
    public async Task AddPowerDie(int face)
    {
        if (_roller is not { } old)
        {
            return;
        }

        _roller = new Row(old.Caption, [.. old.Faces, face], old.Bonus, old.PowerFrom) { Won = old.Won, Kind = old.Kind, ToolFrom = old.ToolFrom };
        await Tumble(_roller, [_roller.Faces.Length - 1]);
    }

    /// <summary>
    /// Lights up the winner of what was just resolved: the roller on a hit (or a success), else the
    /// latest defender, whose dice are tagged with <paramref name="verdict"/> (HIT, MISS, BLOCKED).
    /// </summary>
    public void ShowOutcome(bool hit, string? verdict = null)
    {
        if (_roller is null)
        {
            return;
        }

        if (_defenders.Count == 0)
        {
            _roller.Won = hit;
        }
        else
        {
            _roller.Won |= hit;
            _defenders[^1].Won = !hit;
            _defenders[^1].Verdict = verdict ?? (hit ? "HIT" : "MISS");
        }

        QueueRedraw();
    }

    /// <summary>A draw (as in a game of Bones): neither side glows, and the latest answer is tagged TIE.</summary>
    public void ShowTie()
    {
        if (_defenders.Count > 0)
        {
            _defenders[^1].Verdict = "TIE";
            QueueRedraw();
        }
    }

    public async Task Reroll(int dieIndex, int newFace)
    {
        if (_roller is null || dieIndex >= _roller.Faces.Length)
        {
            return;
        }

        _roller.Faces[dieIndex] = newFace;
        await Tumble(_roller, [dieIndex]);
    }

    public override void _Draw()
    {
        if (_roller is null)
        {
            return;
        }

        Font font = ThemeDB.FallbackFont;
        DrawSide(font, _roller, new Rect2(0, 0, SideWidth, Size.Y), _rerollable, alignRight: false);
        if (_defenders.Count == 0)
        {
            return;
        }

        // VS in the middle, slanted, gold on a dark outline.
        Font vs = GameArt.HeadingFont ?? font;
        int vsSize = UiTheme.HeadingPixelSize * 2;
        Vector2 middle = new(Size.X / 2, Size.Y / 2 + 6);
        DrawSetTransformMatrix(new Transform2D(0, Vector2.One, -0.25f, middle));
        float width = vs.GetStringSize("VS", fontSize: vsSize).X;
        foreach (Vector2 offset in Outline)
        {
            DrawString(vs, new Vector2(-width / 2, 0) + offset, "VS", fontSize: vsSize, modulate: Palette.Ink);
        }

        DrawString(vs, new Vector2(-width / 2, 0), "VS", fontSize: vsSize, modulate: Palette.TrimBright);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // The defenders share the right side: one gets it all, several get a column each.
        float left = Size.X - SideWidth;
        float column = SideWidth / _defenders.Count;
        for (int i = 0; i < _defenders.Count; i++)
        {
            DrawSide(font, _defenders[i], new Rect2(left + i * column, 0, column, Size.Y), clickable: false, alignRight: _defenders.Count == 1);
        }
    }

    private static readonly Vector2[] Outline = [new(-1, 0), new(1, 0), new(0, -1), new(0, 1), new(-1, -1), new(1, 1), new(-1, 1), new(1, -1)];

    /// <summary>One side's caption, dice and total in its box (the defender's flush right).</summary>
    private void DrawSide(Font font, Row row, Rect2 box, bool clickable, bool alignRight)
    {
        (float die, float gap) = DieSizeFor(row, box.Size.X);
        string total = TotalText(row, compact: die < BigDie);
        float totalWidth = font.GetStringSize(total, fontSize: UiTheme.FontSize).X;
        float diceWidth = row.Faces.Length * (die + gap);
        float x = alignRight ? box.End.X - diceWidth - totalWidth - 4 : box.Position.X;

        // The caption (trimmed to fit), and the verdict beside it: before it on the right side, after it on the left.
        HorizontalAlignment align = alignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        float verdictWidth = row.Verdict is { } tag ? font.GetStringSize(tag, fontSize: UiTheme.FontSize).X + 3 : 0;
        DrawString(font, new Vector2(box.Position.X + (alignRight ? verdictWidth : 0), 8), row.Caption, align, box.Size.X - verdictWidth, UiTheme.FontSize, Palette.TextDim,
            TextServer.JustificationFlag.None, TextServer.Direction.Auto, TextServer.Orientation.Horizontal);
        if (row.Verdict is { } verdict)
        {
            float tagX = alignRight ? box.Position.X : Math.Min(box.Position.X + font.GetStringSize(row.Caption, fontSize: UiTheme.FontSize).X + 3, box.End.X - verdictWidth);
            Color color = verdict switch { "HIT" or "YOU LOSE" => Palette.Danger, "BLOCKED" => Palette.Block, "YOU WIN" => Palette.Heal, _ => Palette.TextDim };
            DrawString(font, new Vector2(tagX, 8), verdict, fontSize: UiTheme.FontSize, modulate: color);
        }

        float dieTop = 12 + (BigDie - die) / 2;
        for (int i = 0; i < row.Faces.Length; i++)
        {
            Rect2 rect = new(x + i * (die + gap), dieTop, die, die);
            row.Rects[i] = rect;
            float scale = 1 + 0.35f * row.Pop[i];
            DrawSetTransform(rect.GetCenter() + row.Offset[i], row.Angle[i], new Vector2(scale, scale));
            Color fill = i >= row.PowerFrom ? Palette.Power : i >= row.ToolFrom ? Palette.DieTool : DieColor(row.Kind);
            DrawDie(row.Shown[i], die, row.Won, clickable, fill);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        if (!row.Rolling.Any(rolling => rolling))
        {
            DrawString(font, new Vector2(x + diceWidth + 1, 23), total, fontSize: UiTheme.FontSize, modulate: row.Won ? Palette.TrimBright : Palette.Text);
        }
    }

    /// <summary>Full-size dice when they fit; smaller ones for a crowded side.</summary>
    private static (float Die, float Gap) DieSizeFor(Row row, float width)
    {
        float room = width - 30;
        int count = Math.Max(1, row.Faces.Length);
        foreach (float size in new[] { BigDie, 10f, 8f, 6f })
        {
            float gap = size >= BigDie ? Gap : 2;
            if (count * (size + gap) <= room)
            {
                return (size, gap);
            }
        }

        return (5, 1);
    }

    private static string TotalText(Row row, bool compact)
    {
        int sum = row.Faces.Sum() + row.Bonus;
        if (row.Faces.Length == 0)
        {
            return $"none = {sum}";
        }

        string bonus = row.Bonus == 0 ? "" : row.Bonus > 0 ? $"+{row.Bonus}" : $"{row.Bonus}";
        return compact ? $"{bonus}={sum}" : $"{(bonus.Length > 0 ? bonus + " " : "")}= {sum}";
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click || !_rerollable || _roller is null)
        {
            return;
        }

        for (int i = 0; i < _roller.Faces.Length; i++)
        {
            if (_roller.Rects[i].Grow(1).HasPoint(click.Position))
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

    private static Color DieColor(DiceKind kind) => kind switch
    {
        DiceKind.Rumble => Palette.DieRumble,
        DiceKind.Wend => Palette.DieWend,
        DiceKind.OdHand => Palette.DieOdHand,
        DiceKind.Defense => Palette.DieDefense,
        _ => Palette.Text,
    };

    /// <summary>Draws a die of <paramref name="size"/> centered on the current transform's origin; a winning one glows gold.</summary>
    private void DrawDie(int face, float size, bool success, bool clickable, Color fill)
    {
        var rect = new Rect2(-size / 2, -size / 2, size, size);
        if (success)
        {
            DrawRect(rect.Grow(2.5f), Palette.TrimBright with { A = 0.55f });
        }

        DrawRect(rect, fill);
        DrawRect(new Rect2(rect.Position.X, rect.End.Y - 2, rect.Size.X, 2), fill.Darkened(0.35f));
        DrawRect(rect, clickable ? Palette.Heal : success ? Palette.TrimBright : Palette.Ink, filled: false, width: clickable || success ? 1.5f : 1);
        float pip = Math.Max(0.8f, size / 10);
        foreach (Vector2 spot in Pips[Math.Clamp(face, 0, 6)])
        {
            DrawCircle(rect.Position + spot * rect.Size, pip, Palette.Ink, antialiased: true);
        }
    }

    private sealed class Row(string caption, int[] faces, int bonus, int powerFrom)
    {
        /// <summary>Faces from this index on are Power Dice.</summary>
        public int PowerFrom { get; } = powerFrom;

        public string Caption { get; } = caption;

        public int[] Faces { get; } = faces;

        public int[] Shown { get; } = [.. faces];

        public int Bonus { get; } = bonus;

        public DiceKind Kind { get; init; }

        /// <summary>Faces from this index (up to <see cref="PowerFrom"/>) are the tools' own dice.</summary>
        public int ToolFrom { get; init; } = int.MaxValue;

        /// <summary>This side won; its dice glow.</summary>
        public bool Won { get; set; }

        /// <summary>On a defender: HIT when the attack got through, MISS or BLOCKED when it didn't.</summary>
        public string? Verdict { get; set; }

        /// <summary>Where each die was last drawn (for clicks).</summary>
        public Rect2[] Rects { get; } = new Rect2[faces.Length];

        public bool[] Rolling { get; } = new bool[faces.Length];

        public float[] Angle { get; } = new float[faces.Length];

        public Vector2[] Offset { get; } = new Vector2[faces.Length];

        public float[] Pop { get; } = new float[faces.Length];
    }
}
