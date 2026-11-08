using Godot;
using Woad.Core.Dice;
using Woad.Core.Events;
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
/// Luck allows, the roller's dice can be clicked to reroll them: a green Luck die rolls over the one
/// clicked, and the higher stays (see <see cref="LuckRoll"/>). A hero's roll shows the cards it was
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

    /// <summary>
    /// The roll is against the hero whose turn it is (a trap, a monster hitting back): they stay on the left,
    /// their defense there, and what rolled against them goes on the right.
    /// </summary>
    private bool _rollerOnRight;

    /// <summary>What the roll on show is at (a door, a chest, a crate...), pictured on the right; null for none.</summary>
    private RollTarget? _target;

    public event Action<int>? DieClicked;

    /// <summary>Where the roller's die <paramref name="index"/> is on screen, to click it; null if there's no such die.</summary>
    internal Vector2? DiePoint(int index) =>
        _roller is { } row && index < row.Rects.Length ? GetGlobalTransform() * row.Rects[index].GetCenter() : null;

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
    /// <param name="rollerOnRight">A new roll against the hero whose turn it is: it goes on the right, their answer on the left.</param>
    /// <param name="target">What a new roll is at (a door, a chest...), pictured on the right.</param>
    public async Task ShowRoll(string caption, DiceRoll roll, int bonus, bool newGroup, int powerDice = 0, DiceKind kind = DiceKind.Plain, int toolDice = 0, IReadOnlyList<DieType>? types = null, IReadOnlyList<TrayCard>? cards = null, bool rollerOnRight = false, RollTarget? target = null)
    {
        if (newGroup || _roller is null)
        {
            _target = target;
        }

        var row = new Row(caption, [.. roll.Faces], bonus, roll.Faces.Count - powerDice) { Kind = kind, ToolFrom = roll.Faces.Count - powerDice - toolDice, Types = [.. types ?? []], Cards = [.. cards ?? []] };
        ShowedTypedDice |= row.Types.Any(type => type != DieType.Plain);
        if (newGroup || _roller is null)
        {
            _roller = row;
            _defenders.Clear();
            _rollerOnRight = rollerOnRight;
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

    /// <summary>Rolls one more plain die into the roller's dice (an Od Hand die added to a save or a Focus check).</summary>
    public async Task AddDie(int face)
    {
        if (_roller is not { } old)
        {
            return;
        }

        int plain = old.PowerFrom >= old.Faces.Length ? old.Faces.Length + 1 : old.PowerFrom;
        _roller = new Row(old.Caption, [.. old.Faces, face], old.Bonus, plain) { Won = old.Won, Kind = old.Kind, ToolFrom = old.ToolFrom, Types = old.Types, Cards = old.Cards };
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

    /// <summary>
    /// A Luck reroll: the green Luck die tumbles just above the die it might replace. Higher, it drops onto
    /// it with a thump and takes its place (<paramref name="newFace"/>); otherwise it pops in a green burst.
    /// </summary>
    public async Task LuckRoll(int dieIndex, int luckFace, int newFace)
    {
        if (_roller is not { } row || dieIndex >= row.Faces.Length)
        {
            return;
        }

        float rise = row.Rects[dieIndex].Size.Y + 3;
        var luck = new LuckDie(dieIndex) { Lift = -rise };
        _luck = luck;
        Sfx.Play("dice");
        for (int frame = 0; frame < 8; frame++)
        {
            luck.Shown = Random.Shared.Next(1, 7);
            luck.Angle = (float)(Random.Shared.NextDouble() - 0.5);
            QueueRedraw();
            await ToSignal(GetTree().CreateTimer(0.04), SceneTreeTimer.SignalName.Timeout);
        }

        luck.Shown = luckFace;
        luck.Angle = 0;
        QueueRedraw();
        if (luckFace == 6)
        {
            // A lucky 6: confetti, and the Luck isn't used up.
            Confetti(row.Rects[dieIndex].GetCenter() + new Vector2(0, -rise));
        }

        await ToSignal(GetTree().CreateTimer(0.3), SceneTreeTimer.SignalName.Timeout);

        if (newFace == luckFace && luckFace != row.Faces[dieIndex])
        {
            // Higher: it falls on top of the die, and lands with a thump that squashes it.
            await Animate(0.12, amount => luck.Lift = -rise * (1 - amount * amount));
            row.Faces[dieIndex] = newFace;
            row.Shown[dieIndex] = newFace;
            _luck = null;
            Sfx.Play("block");
            await Animate(0.2, amount =>
            {
                row.Pop[dieIndex] = -0.3f * (1 - amount);
                row.Offset[dieIndex] = new Vector2(0, 2 * (1 - amount));
            });
            row.Offset[dieIndex] = Vector2.Zero;
        }
        else
        {
            // Lower (or no better): it pops in a green burst, and the die stays as it was.
            Sfx.Play("click");
            await Animate(0.32, amount => luck.Burst = amount);
            _luck = null;
        }

        QueueRedraw();
    }

    /// <summary>Runs <paramref name="step"/> from 0 to 1 over <paramref name="seconds"/>, redrawing as it goes.</summary>
    private async Task Animate(double seconds, Action<float> step)
    {
        Tween tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(amount =>
        {
            step(amount);
            QueueRedraw();
        }), 0.0f, 1.0f, seconds);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    private readonly List<(Vector2 From, Vector2 Velocity, Color Color, float Spin)> _confetti = [];
    private float _confettiAge = -1;

    private static readonly Color[] ConfettiColors = [Palette.Heal, Palette.TrimBright, Palette.DieRumble, Palette.DieDefense, Palette.Power, Palette.DieOdHand];

    /// <summary>Throws confetti from <paramref name="at"/>: bits of every color flying up and out, then falling as they fade.</summary>
    private async void Confetti(Vector2 at)
    {
        _confetti.Clear();
        for (int i = 0; i < 24; i++)
        {
            float angle = -Mathf.Pi / 2 + (float)(Random.Shared.NextDouble() - 0.5) * 2.4f;
            float speed = 25 + (float)Random.Shared.NextDouble() * 45;
            _confetti.Add((at, Vector2.FromAngle(angle) * speed, ConfettiColors[i % ConfettiColors.Length], (float)Random.Shared.NextDouble() * 10));
        }

        await Animate(0.9, amount => _confettiAge = amount);
        _confettiAge = -1;
        _confetti.Clear();
        QueueRedraw();
    }

    private void DrawConfetti()
    {
        if (_confettiAge < 0)
        {
            return;
        }

        float time = _confettiAge * 0.9f;
        foreach ((Vector2 from, Vector2 velocity, Color color, float spin) in _confetti)
        {
            Vector2 at = from + velocity * time + new Vector2(0, 60 * time * time);
            DrawSetTransform(at, spin * time, Vector2.One);
            DrawRect(new Rect2(-1.5f, -1, 3, 2), color with { A = 1 - _confettiAge * 0.8f });
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>The Luck die in the air over the die it might replace: rolling, falling, or bursting.</summary>
    private sealed class LuckDie(int index)
    {
        public int Index { get; } = index;

        public int Shown { get; set; } = 1;

        public float Angle { get; set; }

        /// <summary>How far above the die it is (negative, up).</summary>
        public float Lift { get; set; }

        /// <summary>How far through its green burst it is, 0 to 1; 0 while it's whole.</summary>
        public float Burst { get; set; }
    }

    private LuckDie? _luck;

    private static readonly Color LuckGreen = Palette.Heal;

    /// <summary>The Luck die over its die, or its green burst.</summary>
    private void DrawLuck()
    {
        if (_luck is not { } luck || _roller is not { } row || luck.Index >= row.Rects.Length)
        {
            return;
        }

        Rect2 under = row.Rects[luck.Index];
        Vector2 center = under.GetCenter() + new Vector2(0, luck.Lift);
        float size = under.Size.X;
        if (luck.Burst <= 0)
        {
            DrawSetTransform(center, luck.Angle, Vector2.One);
            DrawDie(luck.Shown, size, success: false, clickable: false, LuckGreen);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            return;
        }

        // The burst: the die shrinking away in a ring of green sparks.
        float burst = luck.Burst;
        float left = 1 - burst;
        if (left > 0.5f)
        {
            float shrink = (left - 0.5f) * 2;
            DrawSetTransform(center, 0, new Vector2(shrink, shrink));
            DrawDie(luck.Shown, size, success: false, clickable: false, LuckGreen);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }

        DrawArc(center, size * (0.4f + burst), 0, Mathf.Tau, 20, LuckGreen with { A = left }, 1.5f);
        for (int k = 0; k < 8; k++)
        {
            Vector2 toward = Vector2.FromAngle(k * Mathf.Tau / 8 + 0.3f);
            DrawCircle(center + toward * size * (0.3f + 1.1f * burst), 1.5f * left + 0.5f, LuckGreen with { A = left });
        }
    }

    public override void _Draw()
    {
        if (_roller is null)
        {
            return;
        }

        Font font = ThemeDB.FallbackFont;
        if (_rollerOnRight && _defenders.Count == 1)
        {
            // The hero whose turn it is on the left, answering; what came at them on the right.
            DrawSide(font, _defenders[0], new Rect2(0, 0, SideWidth, Size.Y), clickable: false, alignRight: false);
            DrawSide(font, _roller, new Rect2(Size.X - SideWidth, 0, SideWidth, Size.Y), _rerollable, alignRight: true);
            DrawLuck();
            DrawConfetti();
            DrawVs(font);
            return;
        }

        DrawSide(font, _roller, new Rect2(0, 0, SideWidth, Size.Y), _rerollable, alignRight: false);
        DrawLuck();
        DrawConfetti();
        if (_defenders.Count == 0)
        {
            if (_target is { } target)
            {
                DrawTarget(font, target);
            }

            return;
        }

        DrawVs(font);

        // The defenders share the right side: one gets it all, several get a column each.
        float left = Size.X - SideWidth;
        float column = SideWidth / _defenders.Count;
        for (int i = 0; i < _defenders.Count; i++)
        {
            DrawSide(font, _defenders[i], new Rect2(left + i * column, 0, column, Size.Y), clickable: false, alignRight: _defenders.Count == 1);
        }
    }

    /// <summary>
    /// What the roll's at, on the right: a door (planks, an arch, a keyhole for each lock still fastened, iron
    /// bands the heavier the lock, a bar if barred), a chest, a gate, a crate, a wall, or a trap; a trap on it
    /// shows as a red "!", or once found, as itself. Smashing, its break marks so far.
    /// </summary>
    private void DrawTarget(Font font, RollTarget target)
    {
        var box = new Rect2(Size.X - 46, 2, 40, Size.Y - 4);
        Vector2 c = box.GetCenter();
        Color wood = new("7a4a24"), woodDark = new("4a2a14"), iron = new("3a3a44"), ironLight = new("8a8a96"), gold = Palette.TrimBright;
        int bands = target.Locks > 0 ? Math.Clamp(1 + target.Difficulty / 3, 1, 4) : 0;
        switch (target.What)
        {
            case "door":
                var door = new Rect2(c.X - 13, box.Position.Y + 8, 26, box.Size.Y - 9);
                DrawCircle(new Vector2(c.X, door.Position.Y), 13, wood);
                DrawRect(door, wood);
                for (float x = door.Position.X + 6; x < door.End.X; x += 6)
                {
                    DrawLine(new Vector2(x, door.Position.Y - 6), new Vector2(x, door.End.Y), woodDark, 1);
                }

                for (int i = 0; i < bands; i++)
                {
                    float y = door.Position.Y + 3 + i * (door.Size.Y - 6) / Math.Max(1, bands);
                    DrawRect(new Rect2(door.Position.X, y, door.Size.X, 2.5f), iron);
                    DrawRect(new Rect2(door.Position.X + 2, y + 0.5f, 1.5f, 1.5f), ironLight);
                    DrawRect(new Rect2(door.End.X - 3.5f, y + 0.5f, 1.5f, 1.5f), ironLight);
                }

                for (int i = 0; i < target.Locks; i++)
                {
                    Vector2 hole = new(c.X + 6, door.Position.Y + door.Size.Y * (0.45f + i * 0.25f));
                    DrawCircle(hole, 3, gold);
                    DrawCircle(hole, 1.4f, Palette.Ink);
                    DrawRect(new Rect2(hole.X - 0.6f, hole.Y, 1.2f, 3), Palette.Ink);
                }

                if (target.Barred)
                {
                    DrawRect(new Rect2(door.Position.X - 4, door.GetCenter().Y - 2, door.Size.X + 8, 4), woodDark);
                    DrawRect(new Rect2(door.Position.X - 4, door.GetCenter().Y - 2, door.Size.X + 8, 4), iron, filled: false, width: 1);
                }

                break;

            case "chest":
                var chest = new Rect2(c.X - 15, c.Y - 6, 30, 18);
                DrawRect(new Rect2(chest.Position.X, chest.Position.Y - 7, chest.Size.X, 8), wood.Lightened(0.1f));
                DrawRect(chest, wood);
                for (int i = 0; i < Math.Max(1, bands); i++)
                {
                    DrawRect(new Rect2(chest.Position.X + 4 + i * 8, chest.Position.Y - 7, 2, chest.Size.Y + 7), iron);
                }

                if (target.Locks > 0)
                {
                    DrawRect(new Rect2(c.X - 3, chest.Position.Y - 2, 6, 7), gold);
                    DrawCircle(new Vector2(c.X, chest.Position.Y + 1), 1.2f, Palette.Ink);
                }

                break;

            case "gate":
                var gate = new Rect2(c.X - 14, box.Position.Y + 4, 28, box.Size.Y - 6);
                DrawRect(gate, new Color("0a0c10"));
                for (float x = gate.Position.X + 2; x < gate.End.X; x += 4)
                {
                    DrawLine(new Vector2(x, gate.Position.Y), new Vector2(x, gate.End.Y), new Color("2f7a4e"), 1.5f);
                }

                break;

            case "crate":
                var crate = new Rect2(c.X - 13, c.Y - 11, 26, 24);
                DrawRect(crate, new Color("9a6a3a"));
                DrawLine(crate.Position, crate.End, woodDark, 2);
                DrawRect(crate, woodDark, filled: false, width: 2);
                break;

            case "trap":
                break;

            default:
                // A wall, or a cracked one: stone blocks.
                var wall = new Rect2(c.X - 15, c.Y - 13, 30, 28);
                DrawRect(wall, new Color("55695c"));
                for (float y = wall.Position.Y; y < wall.End.Y; y += 7)
                {
                    DrawLine(new Vector2(wall.Position.X, y), new Vector2(wall.End.X, y), new Color("33443a"), 1);
                }

                if (target.What == "cracked wall")
                {
                    DrawPolyline([new Vector2(c.X - 4, wall.Position.Y), new Vector2(c.X + 2, c.Y - 2), new Vector2(c.X - 3, c.Y + 4), new Vector2(c.X + 4, wall.End.Y)], Palette.Ink, 1.5f);
                }

                break;
        }

        // A trap on it: a red "!" while hidden, or the trap itself once found.
        if (target.Trap is { } trap)
        {
            Vector2 at = target.What == "trap" ? c : new Vector2(box.End.X - 6, box.Position.Y + 8);
            float r = target.What == "trap" ? 12 : 6;
            DrawCircle(at, r, Palette.Danger);
            if (!target.TrapSeen)
            {
                DrawString(font, at + new Vector2(-2, 4), "!", fontSize: UiTheme.FontSize, modulate: Colors.White);
            }
            else
            {
                switch (trap)
                {
                    case TrapKind.Darts:
                        for (int i = -1; i <= 1; i++)
                        {
                            DrawLine(at + new Vector2(-r * 0.6f, i * r * 0.4f), at + new Vector2(r * 0.6f, i * r * 0.4f), Colors.White, 1);
                        }

                        break;
                    case TrapKind.Needle:
                        DrawLine(at + new Vector2(-r * 0.6f, r * 0.6f), at + new Vector2(r * 0.6f, -r * 0.6f), Colors.White, 1);
                        break;
                    default:
                        DrawColoredPolygon([at + new Vector2(-r * 0.6f, r * 0.5f), at + new Vector2(0, -r * 0.6f), at + new Vector2(r * 0.6f, r * 0.5f)], Colors.White);
                        break;
                }
            }
        }

        // Smashing: its break marks so far.
        if (target.Needed > 0)
        {
            DrawString(font, new Vector2(box.Position.X - 24, box.GetCenter().Y + 4), $"{target.Breaks}/{target.Needed}", HorizontalAlignment.Right, 22, UiTheme.FontSize, Palette.Danger);
        }
    }

    /// <summary>VS in the middle, slanted, gold on a dark outline.</summary>
    private void DrawVs(Font font)
    {
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
        DieType type = i < row.Types.Length ? row.Types[i] : DieType.Plain;
        Color fill = type != DieType.Plain ? DieTypes.Tint(type)
            : i >= row.PowerFrom ? Palette.Power : i >= row.ToolFrom ? Palette.DieTool : DieColor(row.Kind);
        DrawDie(row.Shown[i], rect.Size.X, row.Won, clickable, fill, type);
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
    /// <param name="type">What the die is made of: a damage die's 6 shows its symbol (a flame on fire) in place of the pips.</param>
    private void DrawDie(int face, float size, bool success, bool clickable, Color fill, DieType type = DieType.Plain)
    {
        var rect = new Rect2(-size / 2, -size / 2, size, size);
        if (success)
        {
            DrawRect(rect.Grow(2.5f), Palette.TrimBright with { A = 0.55f });
        }

        DrawRect(rect, fill);
        DrawRect(new Rect2(rect.Position.X, rect.End.Y - 2, rect.Size.X, 2), fill.Darkened(0.35f));
        DrawRect(rect, clickable ? Palette.Heal : success ? Palette.TrimBright : Palette.Ink, filled: false, width: clickable || success ? 1.5f : 1);
        if (face == 6 && DieTypes.Symbol(type) is { } symbol)
        {
            // Drawn in its own colors.
            DrawTextureRect(symbol, rect.Grow(-size * 0.14f), tile: false);
            return;
        }

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
