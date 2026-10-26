using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Rules;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Menu;

/// <summary>One hero's line on the results screen; character and reward are null for heroes not tied to a saved character.</summary>
public sealed record ResultRow(HeroRunResult Result, Character? Character, RunReward? Reward);

/// <summary>After a run: who won, and what each hero earned, kept and lost. Results are already saved when this shows.</summary>
public partial class ResultsScreen : MenuScreen
{
    private const float RowHeight = 64;

    private Control _rows = null!;

    public IReadOnlyList<ResultRow> Rows { get; init; } = [];

    public ContentCatalog Content { get; init; } = null!;

    /// <summary>Shown when saving failed, so the player knows their progress may be missing.</summary>
    public string? SaveProblem { get; init; }

    public event Action? DoneRequested;

    internal Button DoneButton { get; private set; } = null!;

    public override void _Ready()
    {
        List<string> winners = Rows.Where(row => row.Result.Won).Select(row => row.Result.Name).ToList();
        BuildFrame(winners.Count == 0 ? "NOBODY ESCAPED"
            : $"{string.Join(" & ", winners).ToUpperInvariant()} {(winners.Count == 1 ? "WINS" : "WIN")}");

        string note = SaveProblem is not null ? $"Couldn't save: {SaveProblem}"
            : Rows.Any(row => row.Character is not null) ? "Progress saved." : "";
        Add(UiTheme.Label(new Vector2(380, 20), new Vector2(236, 14), note, color: SaveProblem is null ? Palette.TextDim : Palette.Danger))
            .HorizontalAlignment = HorizontalAlignment.Right;

        // Four rows fit; a bigger party scrolls.
        var scroll = Add(new ScrollContainer
        {
            Position = new Vector2(20, 44),
            Size = new Vector2(612, 4 * RowHeight - 2),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        });
        _rows = new Control { CustomMinimumSize = new Vector2(600, Rows.Count * RowHeight - 6), MouseFilter = MouseFilterEnum.Pass };
        scroll.AddChild(_rows);
        for (int i = 0; i < Rows.Count; i++)
        {
            AddRow(Rows[i], i * RowHeight);
        }

        DoneButton = Add(UiTheme.Button(new Vector2(516, 316), new Vector2(102, 26), "Done"));
        DoneButton.Pressed += () => DoneRequested?.Invoke();
    }

    private void AddRow(ResultRow row, float top)
    {
        HeroRunResult result = row.Result;
        Put(new Panel { Position = new Vector2(0, top), Size = new Vector2(600, RowHeight - 6), MouseFilter = MouseFilterEnum.Ignore });
        Put(new FigureView { Position = new Vector2(8, top + 9), Size = new Vector2(32, 32) }).Colors = Looks.Resolve(result.Appearance, result.Portrait);

        Put(UiTheme.Label(new Vector2(50, top + 5), new Vector2(200, 18), result.Name, 14, Palette.Seat(result.Seat)));
        string outcome = result.Status switch
        {
            HeroStatus.Escaped when result.Retreated => "Got away",
            HeroStatus.Escaped => $"Escaped with {result.Gold} gold",
            HeroStatus.Dead => $"Died on floor {result.DeepestFloor}{(result.Epitaph is { } epitaph ? $": {epitaph}" : "")}",
            HeroStatus.Active or HeroStatus.KeelingOver => $"Left behind on floor {result.DeepestFloor}",
            _ => "Left the run",
        };
        Put(UiTheme.Label(new Vector2(50, top + 24), new Vector2(240, 12),
            $"{outcome}   {UiTheme.Count(result.Kills, "kill", "kills")}", color: result.Status == HeroStatus.Escaped ? Palette.Heal : Palette.Danger));

        string growth = $"+{result.Xp} XP";
        if (row.Character is { } character)
        {
            growth += row.Reward is { LevelsGained: > 0 }
                ? $"   Level {character.Level}!   {UiTheme.Count(character.UnspentPoints, "point", "points")} to spend"
                : $"   Level {character.Level}, {character.Xp}/{Progression.XpForLevel(character.Level + 1)} XP";
        }

        Put(UiTheme.Label(new Vector2(50, top + 38), new Vector2(240, 12), growth, color: Palette.TrimBright));

        if (row.Reward is { } reward)
        {
            string gear = $"Found: {Names(reward.CardsGained)}\nLost: {Names(reward.CardsLost)}";
            Put(UiTheme.Label(new Vector2(300, top + 8), new Vector2(292, 44), gear));
        }
    }

    /// <summary>Adds a control to the scrolling list of rows.</summary>
    private T Put<T>(T control) where T : Control
    {
        _rows.AddChild(control);
        return control;
    }

    private string Names(IReadOnlyList<string> cardIds) =>
        cardIds.Count == 0 ? "nothing" : string.Join(", ", cardIds.Select(id => Content.GetCard(id).Name));
}
