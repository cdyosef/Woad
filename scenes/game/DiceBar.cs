using Godot;
using Woad.Core.Dice;
using Woad.Core.Model;
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

/// <summary>A card played into a roll: it sits over its own dice in the tray (the first <paramref name="Dice"/> faces not yet claimed).</summary>
public sealed record TrayCard(Texture2D? Picture, string Name, int Dice, string Bonus, Color Border);

/// <summary>
/// The dice of the latest roll, fighting-game style under the board: the roller and their dice on the
/// left, and when someone answers it, a VS and every defender's dice on the right (smaller when there
/// are several, as in a blast). Each side shows its fixed bonus (weapon, armor or Speed) and total;
/// dice tumble before they land with a small pop, and the side that wins glows gold. Dice are colored
/// by what they are (see <see cref="DiceKind"/>); tools' dice are bronze, Power Dice purple, and typed
/// dice (fire, ice...) their type's own color. When
/// Luck allows, the roller's dice can be clicked to reroll them. A hero's roll shows the cards it was
/// played with over their own dice (the pool's card first, then the weapon, tool or skill), the
/// caption going underneath; a defender's is mirrored, its pool's card on the far right.
/// </summary>
public partial class DiceBar : Control
{
    private const float BigDie = 13;
    private const float Gap = 3;
    private const float SideWidth = 118;
    private const int MaxDefenders = 6;
    private const float ChipHeight = 10;
    private const float ChipGap = 2;

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

    /// <summary>A typed die (fire, ice...) has been shown in its own color (for the tests).</summary>
    internal bool ShowedTypedDice { get; private set; }

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
    /// <param name="types">What each face is made of, in order (Plain past its end).</param>
    /// <param name="cards">The cards played into the roll, each over its own dice, in order.</param>
    public async Task ShowRoll(string caption, DiceRoll roll, int bonus, bool newGroup, int powerDice = 0, DiceKind kind = DiceKind.Plain, int toolDice = 0, IReadOnlyList<DieType>? types = null, IReadOnlyList<TrayCard>? cards = null)
    {
        var row = new Row(caption, [.. roll.Faces], bonus, roll.Faces.Count - powerDice) { Kind = kind, ToolFrom = roll.Faces.Count - powerDice - toolDice, Types = [.. types ?? []], Cards = [.. cards ?? []] };
        ShowedTypedDice |= row.Types.Any(type => type != DieType.Plain);
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

        _roller = new Row(old.Caption, [.. old.Faces, face], old.Bonus, old.PowerFrom) { Won = old.Won, Kind = old.Kind, ToolFrom = old.ToolFrom, Types = old.Types, Cards = old.Cards };
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
    /// <summary>Recaptions the roller's side, to say what came of the roll (what was stolen, say).</summary>
    public void Retitle(string caption)
    {
        if (_roller is not null)
        {
            _roller.Caption = caption;
            QueueRedraw();
        }
    }

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
        if (_roller!.Cards.Length > 0 || _defenders.Any(defender => defender.Cards.Length > 0))
        {
            DrawCarded(font, row, box, clickable, alignRight);
            return;
        }

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
            DrawString(font, new Vector2(tagX, 8), verdict, fontSize: UiTheme.FontSize, modulate: VerdictColor(verdict));
        }

        float dieTop = 12 + (BigDie - die) / 2;
        for (int i = 0; i < row.Faces.Length; i++)
        {
            DrawFace(row, i, new Rect2(x + i * (die + gap), dieTop, die, die), clickable);
        }

        if (!row.Rolling.Any(rolling => rolling))
        {
            DrawString(font, new Vector2(x + diceWidth + 1, 23), total, fontSize: UiTheme.FontSize, modulate: row.Won ? Palette.TrimBright : Palette.Text);
        }
    }

    /// <summary>A die where it lands (tumbling about it while it rolls), remembered for clicks.</summary>
    private void DrawFace(Row row, int i, Rect2 rect, bool clickable)
    {
        row.Rects[i] = rect;
        float scale = 1 + 0.35f * row.Pop[i];
        DrawSetTransform(rect.GetCenter() + row.Offset[i], row.Angle[i], new Vector2(scale, scale));
        Color fill = i < row.Types.Length && row.Types[i] != DieType.Plain ? DieTypes.Tint(row.Types[i])
            : i >= row.PowerFrom ? Palette.Power : i >= row.ToolFrom ? Palette.DieTool : DieColor(row.Kind);
        DrawDie(row.Shown[i], rect.Size.X, row.Won, clickable, fill);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>
    /// A side with its played cards along the top, each over its own dice (any dice left over, the
    /// Power Dice, after them), the total after that and the caption underneath. The defender's side
    /// is mirrored: its total first, and its pool's card on the far right.
    /// </summary>
    private void DrawCarded(Font font, Row row, Rect2 box, bool clickable, bool alignRight)
    {
        // Which faces each card holds; whatever's left over goes last, under no card.
        var groups = new List<(TrayCard? Card, int From, int Count)>();
        int next = 0;
        foreach (TrayCard card in row.Cards)
        {
            int count = Math.Clamp(card.Dice, 0, row.Faces.Length - next);
            groups.Add((card, next, count));
            next += count;
        }

        if (next < row.Faces.Length)
        {
            groups.Add((null, next, row.Faces.Length - next));
        }

        string total = TotalText(row, compact: true);
        float totalWidth = font.GetStringSize(total, fontSize: UiTheme.FontSize).X + 2;

        // The biggest dice, with whole names on the cards, that fit; then first words, icons alone, smaller dice.
        float die = 5, gap = 1;
        int named = 0;
        float[] widths = [];
        foreach ((float size, int names) in new[] { (BigDie, 2), (BigDie, 1), (BigDie, 0), (10f, 1), (10f, 0), (8f, 0), (6f, 0), (5f, 0) })
        {
            float step = size + (size >= BigDie ? Gap : 2);
            widths = [.. groups.Select(group => Math.Max(group.Card is { } card ? ChipWidth(font, card, names) : 0, group.Count * step))];
            die = size;
            gap = step - size;
            named = names;
            if (widths.Sum() + ChipGap * (groups.Count - 1) + totalWidth <= box.Size.X)
            {
                break;
            }
        }

        float used = widths.Sum() + ChipGap * (groups.Count - 1) + totalWidth;
        float x = alignRight ? box.End.X - used : box.Position.X;
        bool settled = !row.Rolling.Any(rolling => rolling);
        float dieTop = ChipHeight + 1 + (BigDie - die) / 2;
        if (alignRight)
        {
            if (settled)
            {
                DrawString(font, new Vector2(x, ChipHeight + 11), total, fontSize: UiTheme.FontSize, modulate: row.Won ? Palette.TrimBright : Palette.Text);
            }

            x += totalWidth;
        }

        // Mirrored, the cards run right to left: the first (the pool's own) ends up on the far right.
        IEnumerable<int> order = alignRight ? Enumerable.Range(0, groups.Count).Reverse() : Enumerable.Range(0, groups.Count);
        foreach (int g in order)
        {
            (TrayCard? card, int from, int count) = groups[g];
            if (card is not null)
            {
                DrawChip(font, card, new Rect2(x, 0, widths[g], ChipHeight), named);
            }

            float diceX = x + (widths[g] - (count * (die + gap) - gap)) / 2;
            for (int i = 0; i < count; i++)
            {
                DrawFace(row, from + i, new Rect2(diceX + i * (die + gap), dieTop, die, die), clickable);
            }

            x += widths[g] + ChipGap;
        }

        if (!alignRight && settled)
        {
            DrawString(font, new Vector2(x - ChipGap + 1, ChipHeight + 11), total, fontSize: UiTheme.FontSize, modulate: row.Won ? Palette.TrimBright : Palette.Text);
        }

        // The caption underneath, and the verdict beside it: before it on the right side, after it on the left.
        HorizontalAlignment align = alignRight ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        float verdictWidth = row.Verdict is { } tag ? font.GetStringSize(tag, fontSize: UiTheme.FontSize).X + 3 : 0;
        float captionY = Size.Y - 2;
        DrawString(font, new Vector2(box.Position.X + (alignRight ? verdictWidth : 0), captionY), row.Caption, align, box.Size.X - verdictWidth, UiTheme.FontSize, Palette.TextDim,
            TextServer.JustificationFlag.None, TextServer.Direction.Auto, TextServer.Orientation.Horizontal);
        if (row.Verdict is { } verdict)
        {
            float tagX = alignRight ? box.Position.X : Math.Min(box.Position.X + font.GetStringSize(row.Caption, fontSize: UiTheme.FontSize).X + 3, box.End.X - verdictWidth);
            DrawString(font, new Vector2(tagX, captionY), verdict, fontSize: UiTheme.FontSize, modulate: VerdictColor(verdict));
        }
    }

    /// <param name="named">2 for the card's whole name, 1 for its first word, 0 for its picture alone.</param>
    private static float ChipWidth(Font font, TrayCard card, int named) =>
        ChipHeight + 2 + (named > 0 ? font.GetStringSize(ChipText(card, named), fontSize: UiTheme.FontSize).X + 2 : 0);

    private static string ChipText(TrayCard card, int named)
    {
        string name = named > 1 ? card.Name : card.Name.Split(' ')[0];
        return card.Bonus.Length > 0 ? $"{name} {card.Bonus}" : name;
    }

    /// <summary>A played card, small: its picture, and its name and bonus when there's room, in its pool's border.</summary>
    private void DrawChip(Font font, TrayCard card, Rect2 rect, int named)
    {
        DrawRect(rect, Palette.Ink);
        if (card.Picture is { } picture)
        {
            DrawTextureRect(picture, new Rect2(rect.Position + new Vector2(1, 1), new Vector2(ChipHeight - 2, ChipHeight - 2)), tile: false);
        }

        if (named > 0)
        {
            DrawString(font, rect.Position + new Vector2(ChipHeight + 1, ChipHeight - 2), ChipText(card, named), HorizontalAlignment.Left, rect.Size.X - ChipHeight - 1, UiTheme.FontSize, Palette.Text);
        }

        DrawRect(rect, card.Border, filled: false, width: 1);
    }

    private static Color VerdictColor(string verdict) => verdict switch
    {
        "HIT" or "YOU LOSE" or "CAUGHT" => Palette.Danger,
        "STOLEN" => Palette.Loot,
        "BLOCKED" => Palette.Block,
        "YOU WIN" => Palette.Heal,
        _ => Palette.TextDim,
    };

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

        public string Caption { get; set; } = caption;

        public int[] Faces { get; } = faces;

        public int[] Shown { get; } = [.. faces];

        public int Bonus { get; } = bonus;

        public DiceKind Kind { get; init; }

        /// <summary>Faces from this index (up to <see cref="PowerFrom"/>) are the tools' own dice.</summary>
        public int ToolFrom { get; init; } = int.MaxValue;

        /// <summary>What each face is made of (fire, ice...), in order; Plain past its end.</summary>
        public DieType[] Types { get; init; } = [];

        /// <summary>The cards played into the roll, each over its own dice.</summary>
        public TrayCard[] Cards { get; init; } = [];

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
