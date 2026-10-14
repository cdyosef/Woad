using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Menu;

/// <summary>The roster: browse saved characters, see their stats and gear, create, edit or delete them.</summary>
public partial class CharacterListScreen : MenuScreen
{
    private readonly List<CharacterRow> _rows = [];
    private List<Character> _characters = [];
    private Character? _selected;
    private bool _confirmingDelete;

    private VBoxContainer _list = null!;
    private Label _empty = null!;
    private Label _problems = null!;
    private PortraitView _portrait = null!;
    private Label _name = null!;
    private Label _details = null!;

    public ICharacterRepository Repository { get; init; } = null!;

    public ContentCatalog Content { get; init; } = null!;

    public event Action? BackRequested;

    public event Action? NewRequested;

    public event Action<Character>? EditRequested;

    internal Button NewButton { get; private set; } = null!;

    internal Button EditButton { get; private set; } = null!;

    internal Button DeleteButton { get; private set; } = null!;

    internal Button BackButton { get; private set; } = null!;

    internal bool IsLoaded { get; private set; }

    internal int CharacterCount => _characters.Count;

    internal CharacterRow Row(int index) => _rows[index];

    public override void _Ready()
    {
        BuildFrame("CHARACTERS");

        var scroll = Add(new ScrollContainer
        {
            Position = new Vector2(20, 46),
            Size = new Vector2(300, 260),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        });
        _list = new VBoxContainer { CustomMinimumSize = new Vector2(288, 0) };
        _list.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_list);
        _empty = Add(UiTheme.Label(new Vector2(24, 54), new Vector2(290, 40), "No characters yet. Create one to start playing.", color: Palette.TextDim));

        _portrait = Add(new PortraitView { Position = new Vector2(342, 48), Size = new Vector2(40, 48) });
        _name = Add(UiTheme.Label(new Vector2(392, 50), new Vector2(224, 22), fontSize: 18, color: Palette.TrimBright));
        _details = Add(UiTheme.Label(new Vector2(342, 104), new Vector2(276, 160)));
        _problems = Add(UiTheme.Label(new Vector2(342, 266), new Vector2(276, 44), color: Palette.Danger));

        NewButton = Add(UiTheme.Button(new Vector2(20, 316), new Vector2(130, 26), "New character"));
        NewButton.Pressed += () => NewRequested?.Invoke();
        EditButton = Add(UiTheme.Button(new Vector2(342, 316), new Vector2(84, 26), "Edit"));
        EditButton.Pressed += () =>
        {
            if (_selected is not null)
            {
                EditRequested?.Invoke(_selected);
            }
        };
        DeleteButton = Add(UiTheme.Button(new Vector2(432, 316), new Vector2(84, 26), "Delete"));
        DeleteButton.Pressed += Delete;
        BackButton = Add(UiTheme.Button(new Vector2(530, 316), new Vector2(88, 26), "Back"));
        BackButton.Pressed += () => BackRequested?.Invoke();

        Load();
    }

    private async void Load()
    {
        IsLoaded = false;
        CharacterLoadResult result = await Repository.LoadAllAsync();
        _characters = [.. result.Characters];
        _problems.Text = string.Join("\n", result.Problems.Take(3));
        _selected = _characters.FirstOrDefault(character => character.Id == _selected?.Id) ?? _characters.FirstOrDefault();
        Rebuild();
        IsLoaded = true;
    }

    private void Rebuild()
    {
        foreach (CharacterRow row in _rows)
        {
            _list.RemoveChild(row);
            row.QueueFree();
        }

        _rows.Clear();
        foreach (Character character in _characters)
        {
            var row = new CharacterRow();
            row.Pressed += () => Select(character);
            _list.AddChild(row);
            _rows.Add(row);
        }

        _empty.Visible = _characters.Count == 0;
        Refresh();
    }

    private void Select(Character character)
    {
        _selected = character;
        _confirmingDelete = false;
        Refresh();
    }

    private void Refresh()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            _rows[i].ShowCharacter(_characters[i], _characters[i] == _selected);
        }

        EditButton.Disabled = DeleteButton.Disabled = _selected is null;
        DeleteButton.Text = _confirmingDelete ? "Really?" : "Delete";
        _portrait.Visible = _selected is not null;
        if (_selected is null)
        {
            _name.Text = _details.Text = "";
            return;
        }

        _portrait.PortraitId = _selected.Portrait;
        _name.Text = _selected.Name;
        _details.Text = Describe(_selected);
    }

    private string Describe(Character character)
    {
        Stats stats = character.BaseStats;
        int nextLevelXp = Progression.XpForLevel(character.Level + 1);
        string points = character.UnspentPoints > 0
            ? $"   {character.UnspentPoints} point{(character.UnspentPoints == 1 ? "" : "s")} to spend"
            : "";
        string speed = stats.Speed >= 0 ? $"+{stats.Speed}" : stats.Speed.ToString();

        return (character.IsCompanion ? "Companion: joins a player's party and follows their orders.\n" : "") +
               $"Level {character.Level}   {character.Xp}/{nextLevelXp} XP{points}\n" +
               $"Hearts {stats.Hearts}   Wend {stats.MoveDice}d6{speed}   Luck {stats.Luck}\n" +
               $"Rumble {UiTheme.Count(stats.AttackDice, "die", "dice")}   Defense {UiTheme.Count(stats.DefenseDice, "die", "dice")}   Od Hand {UiTheme.Count(stats.OdHandDice, "die", "dice")}\n\n" +
               $"Equipped: {CardNames(character.Equipped)}\n" +
               $"Belt: {CardNames(character.Belt)}\n" +
               $"Backpack: {CardNames(character.Backpack)}\n\n" +
               $"Gold {character.Gold}   Runs {character.RunsPlayed}   Wins {character.Wins}";
    }

    private string CardNames(IReadOnlyList<string> cardIds) =>
        cardIds.Count == 0 ? "nothing" : string.Join(", ", cardIds.Select(id => Content.GetCard(id).Name));

    private async void Delete()
    {
        if (_selected is null)
        {
            return;
        }

        if (!_confirmingDelete)
        {
            _confirmingDelete = true;
            Refresh();
            return;
        }

        await Repository.DeleteAsync(_selected.Id);
        _selected = null;
        _confirmingDelete = false;
        Load();
    }
}
