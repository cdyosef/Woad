using Godot;
using Woad.Core.Art;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Paint;

/// <summary>
/// Pixel Paint: paints the hero figures players pick in character creation. A figure is 16x16 pixels of
/// fixed colors and of the four player colors (main, trim, skin, hair, each with a shade), with its eyes
/// marked. Any figure can be opened and repainted, or a new one started; a drawing can be laid under
/// the grid to trace. Saved figures go to <see cref="Looks.PaintedDir"/> and turn up in character creation.
/// </summary>
public partial class PaintScreen : MenuScreen
{
    /// <summary>The fixed colors on offer, a row of eight at a time, dark to light by family.</summary>
    public static readonly string[] FixedColors =
    [
        "#181420", "#34303e", "#5a5868", "#6a7484", "#b8c0cc", "#ece6d6", "#ffffff", "#5a4a6a",
        "#3a2414", "#5a3a1a", "#6a4a2a", "#8a5a32", "#a07a4a", "#c8955a", "#d89a6a", "#f4c8a0",
        "#8a1e28", "#d8434a", "#ff9a90", "#e0502a", "#ff7a3a", "#ffb03a", "#a87a20", "#e8b840",
        "#fff0a0", "#1e7a3a", "#4cc85a", "#a8eca0", "#24508a", "#4a8ad8", "#a0e0f0", "#9a5fd8",
        "#f0a0b0",
    ];

    private static readonly (string Paint, string Name)[] PlayerPaints =
    [
        ("T", "Main"), ("t", "Main, shaded"), ("R", "Trim"), ("r", "Trim, shaded"),
        ("S", "Skin"), ("s", "Skin, shaded"), ("H", "Hair"), ("h", "Hair, shaded"),
    ];

    private static readonly string Eye = PaintedLook.Eye.ToString();

    private readonly Dictionary<string, Button> _swatches = [];

    private readonly Dictionary<PaintTool, Button> _toolButtons = [];

    private readonly List<string?> _sets = [null, .. Looks.Sets.Keys];

    private string _name = "New Figure";

    private string? _skinSet = "human";

    private string? _hairSet = "hair";

    /// <summary>Which of the game's colors the preview wears: main, trim, then the skin and hair of their sets.</summary>
    private readonly int[] _worn = [0, 3, 0, 0];

    private Label _skinLabel = null!;

    private Label _hairLabel = null!;

    private Label _status = null!;

    private ColorPickerButton _custom = null!;

    private PaintPreview[] _previews = [];

    private FileDialog? _traceDialog;

    private ImageTexture? _traceTexture;

    public event Action? BackRequested;

    internal PaintCanvas Canvas { get; private set; } = null!;

    internal LineEdit NameEdit { get; private set; } = null!;

    internal Button SaveButton { get; private set; } = null!;

    internal Button DeleteButton { get; private set; } = null!;

    internal Button MirrorButton { get; private set; } = null!;

    internal Button UndoButton { get; private set; } = null!;

    internal Button RedoButton { get; private set; } = null!;

    internal Button NewButton { get; private set; } = null!;

    internal Button NextButton { get; private set; } = null!;

    internal Button BackButton { get; private set; } = null!;

    internal Button ToolButton(PaintTool tool) => _toolButtons[tool];

    /// <summary>For the click tests: the swatch of a paint ("T", "e", "#181420"...).</summary>
    internal Button Swatch(string paint) => _swatches[paint];

    internal string Status => _status.Text;

    /// <summary>The id the figure will be saved under: its name, made fit for a file.</summary>
    internal string LookId => PaintedLook.IdFor(_name);

    public override void _Ready()
    {
        BuildFrame("PIXEL PAINT");
        BuildTools();
        BuildCanvas();
        BuildPaints();
        SelectTool(PaintTool.Pencil);
        SelectPaint("T");
        Refresh();
    }

    public override void _ExitTree()
    {
        _traceTexture?.Dispose();
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key)
        {
            return;
        }

        bool handled = true;
        if (key.CtrlPressed || key.MetaPressed)
        {
            switch (key.Keycode)
            {
                case Key.Z when key.ShiftPressed:
                case Key.Y:
                    Canvas.Redo();
                    break;
                case Key.Z:
                    Canvas.Undo();
                    break;
                case Key.S:
                    Save();
                    break;
                default:
                    handled = false;
                    break;
            }
        }
        else
        {
            switch (key.Keycode)
            {
                case Key.B:
                    SelectTool(PaintTool.Pencil);
                    break;
                case Key.E:
                    SelectTool(PaintTool.Eraser);
                    break;
                case Key.F:
                    SelectTool(PaintTool.Fill);
                    break;
                case Key.I:
                    SelectTool(PaintTool.Pick);
                    break;
                case Key.M:
                    ToggleMirror();
                    break;
                default:
                    handled = false;
                    break;
            }
        }

        if (handled)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Opens a figure to work on. False (and nothing changes) if its art can't be read.</summary>
    internal bool Open(Look look)
    {
        if (Looks.Painting(look) is not { } painting)
        {
            Say($"{look.Name}'s art can't be opened here.");
            return false;
        }

        var pixels = PaintCanvas.Blank();
        for (int y = 0; y < PaintCanvas.Cells; y++)
        {
            for (int x = 0; x < PaintCanvas.Cells; x++)
            {
                char pixel = painting.Rows[y][x];
                pixels[x, y] = pixel == PaintedLook.Empty ? ""
                    : painting.Palette.TryGetValue(pixel, out string? color) ? color
                    : pixel.ToString();
            }
        }

        _name = painting.Name;
        NameEdit.Text = _name;
        _skinSet = _sets.Contains(painting.SkinSet) ? painting.SkinSet : null;
        _hairSet = _sets.Contains(painting.HairSet) ? painting.HairSet : null;
        _worn[2] = Math.Max(0, Looks.SetSwatches(_skinSet).ToList().FindIndex(swatch => swatch.Id == look.SkinDefault));
        _worn[3] = Math.Max(0, Looks.SetSwatches(_hairSet).ToList().FindIndex(swatch => swatch.Id == look.HairDefault));
        Canvas.Load(pixels);
        Say(Looks.IsOriginal(look.Id) ? $"Opened {look.Name}. It comes with the game, so saving makes a new figure from it: the original stays as it is." : $"Opened {look.Name}.");
        Refresh();
        return true;
    }

    /// <summary>Lays a drawing under the grid to trace. False if the file isn't a picture.</summary>
    internal bool LoadTrace(string path)
    {
        bool picture = Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp";
        using Image? image = picture && File.Exists(path) ? Image.LoadFromFile(path) : null;
        if (image is null || image.IsEmpty())
        {
            Say("That file isn't a picture the game can open.");
            return false;
        }

        _traceTexture?.Dispose();
        _traceTexture = ImageTexture.CreateFromImage(image);
        Canvas.Trace = _traceTexture;
        Say("Tracing. The wheel zooms the drawing; the middle button drags it.");
        return true;
    }

    /// <summary>The figure as painted, each fixed color filed under a letter.</summary>
    internal PaintedLook Painting()
    {
        var letters = new Dictionary<string, char>();
        var rows = new List<string>();
        for (int y = 0; y < PaintCanvas.Cells; y++)
        {
            char[] row = new char[PaintCanvas.Cells];
            for (int x = 0; x < PaintCanvas.Cells; x++)
            {
                string paint = Canvas[x, y];
                if (paint.Length <= 1)
                {
                    row[x] = paint.Length == 0 ? PaintedLook.Empty : paint[0];
                    continue;
                }

                if (!letters.TryGetValue(paint, out char letter))
                {
                    letters[paint] = letter = letters.Count < PaintedLook.Letters.Length ? PaintedLook.Letters[letters.Count] : PaintedLook.Empty;
                }

                row[x] = letter;
            }

            rows.Add(new string(row));
        }

        return new PaintedLook(LookId, _name.Trim(), _skinSet, _hairSet, rows,
            letters.Where(entry => entry.Value != PaintedLook.Empty).ToDictionary(entry => entry.Value, entry => entry.Key));
    }

    private void BuildTools()
    {
        Caption(new Vector2(20, 44), "TOOLS");
        (PaintTool Tool, string Text, string Tip)[] tools =
        [
            (PaintTool.Pencil, "Pencil", "Paint pixels (B). The right mouse button always erases."),
            (PaintTool.Eraser, "Eraser", "Rub pixels out (E)"),
            (PaintTool.Fill, "Fill", "Fill an area with the paint (F)"),
            (PaintTool.Pick, "Pick", "Take a paint from the picture (I)"),
        ];
        for (int index = 0; index < tools.Length; index++)
        {
            (PaintTool tool, string text, string tip) = tools[index];
            Button button = Add(UiTheme.CompactButton(new Vector2(20 + index % 2 * 82, 58 + index / 2 * 24), new Vector2(78, 20), text));
            button.ToggleMode = true;
            button.TooltipText = tip;
            button.Pressed += () => SelectTool(tool);
            _toolButtons[tool] = button;
        }

        MirrorButton = Add(UiTheme.CompactButton(new Vector2(20, 106), new Vector2(78, 20), "Mirror"));
        MirrorButton.ToggleMode = true;
        MirrorButton.TooltipText = "Paint both halves at once (M)";
        MirrorButton.Pressed += () => Canvas.Mirror = MirrorButton.ButtonPressed;
        Button clear = Add(UiTheme.CompactButton(new Vector2(102, 106), new Vector2(78, 20), "Clear"));
        clear.TooltipText = "Rub the whole picture out (Undo brings it back)";
        clear.Pressed += () => Canvas.Clear();
        UndoButton = Add(UiTheme.CompactButton(new Vector2(20, 130), new Vector2(78, 20), "Undo"));
        UndoButton.TooltipText = "Ctrl+Z";
        UndoButton.Pressed += () => Canvas.Undo();
        RedoButton = Add(UiTheme.CompactButton(new Vector2(102, 130), new Vector2(78, 20), "Redo"));
        RedoButton.TooltipText = "Ctrl+Y";
        RedoButton.Pressed += () => Canvas.Redo();

        Caption(new Vector2(20, 160), "PLAYERS PICK FROM");
        _skinLabel = SetRow(174, "Skin", delta => _skinSet = StepSet(_skinSet, delta));
        _hairLabel = SetRow(196, "Hair", delta => _hairSet = StepSet(_hairSet, delta));

        Caption(new Vector2(20, 226), "TRACE A DRAWING");
        Button load = Add(UiTheme.CompactButton(new Vector2(20, 240), new Vector2(78, 20), "Open..."));
        load.TooltipText = "Lay a picture under the grid to paint over";
        load.Pressed += AskForTrace;
        Button remove = Add(UiTheme.CompactButton(new Vector2(102, 240), new Vector2(78, 20), "Remove"));
        remove.Pressed += () => Canvas.Trace = null;
        HSlider opacity = Add(new HSlider
        {
            Position = new Vector2(20, 264),
            Size = new Vector2(160, 16),
            MinValue = 0.1,
            MaxValue = 1,
            Step = 0.05,
            Value = 0.5,
            FocusMode = FocusModeEnum.None,
            TooltipText = "How strongly the drawing shows",
        });
        opacity.ValueChanged += value => Canvas.TraceOpacity = (float)value;
        Add(UiTheme.Label(new Vector2(20, 282), new Vector2(170, 24), "Wheel zooms it,\nmiddle button drags it", color: Palette.TextDim));

        BackButton = Add(UiTheme.Button(new Vector2(20, 316), new Vector2(88, 26), "Back"));
        BackButton.Pressed += () => BackRequested?.Invoke();
    }

    private void BuildCanvas()
    {
        Button previous = Add(UiTheme.CompactButton(new Vector2(192, 38), new Vector2(20, 20), "<"));
        previous.TooltipText = "Open the figure before this one";
        previous.Pressed += () => OpenNeighbor(-1);
        NameEdit = Add(new LineEdit
        {
            Position = new Vector2(216, 33),
            Size = new Vector2(150, 20),
            Text = _name,
            MaxLength = 24,
            PlaceholderText = "Name",
            TooltipText = "The figure's name. Saving under a new name makes a new figure.",
        });
        NameEdit.TextChanged += text =>
        {
            _name = text;
            Refresh();
        };
        NameEdit.TextSubmitted += _ => NameEdit.ReleaseFocus();
        NextButton = Add(UiTheme.CompactButton(new Vector2(370, 38), new Vector2(20, 20), ">"));
        NextButton.TooltipText = "Open the figure after this one";
        NextButton.Pressed += () => OpenNeighbor(1);
        NewButton = Add(UiTheme.CompactButton(new Vector2(396, 38), new Vector2(52, 20), "New"));
        NewButton.TooltipText = "Start a blank figure";
        NewButton.Pressed += StartNew;

        Canvas = Add(new PaintCanvas { Position = new Vector2(192, 64), Size = new Vector2(256, 256) });
        Canvas.Changed += Refresh;
        Canvas.Picked += paint =>
        {
            SelectPaint(paint);
            SelectTool(PaintTool.Pencil);
        };

        _status = Add(UiTheme.Label(new Vector2(116, 324), new Vector2(340, 12), "", color: Palette.TextDim));
        _status.HorizontalAlignment = HorizontalAlignment.Center;
    }

    private void BuildPaints()
    {
        Caption(new Vector2(462, 44), "PLAYER COLORS");
        string[] names = ["Main", "Trim", "Skin", "Hair"];
        for (int index = 0; index < PlayerPaints.Length; index++)
        {
            (string paint, string name) = PlayerPaints[index];
            AddSwatch(paint, new Vector2(462 + index / 2 * 40 + index % 2 * 17, 58), $"{name}: players choose this color");
            if (index % 2 == 0)
            {
                Label label = Add(UiTheme.Label(new Vector2(462 + index / 2 * 40, 75), new Vector2(34, 10), names[index / 2], color: Palette.TextDim));
                label.HorizontalAlignment = HorizontalAlignment.Center;
            }
        }

        Caption(new Vector2(462, 92), "FIXED COLORS");
        AddSwatch(Eye, new Vector2(462, 106), "Eye: dark, and crossed out while Keeling Over. Mark two.");
        for (int index = 0; index < FixedColors.Length; index++)
        {
            int slot = index + 1;
            AddSwatch(FixedColors[index], new Vector2(462 + slot % 9 * 17, 106 + slot / 9 * 17), FixedColors[index]);
        }

        _custom = Add(new ColorPickerButton
        {
            Position = new Vector2(462, 182),
            Size = new Vector2(76, 18),
            Color = new Color("c06030"),
            EditAlpha = false,
            FocusMode = FocusModeEnum.None,
            TooltipText = "Mix any other color",
        });
        _custom.ColorChanged += color => SelectPaint("#" + color.ToHtml(false));
        _custom.Pressed += () => SelectPaint("#" + _custom.Color.ToHtml(false));
        Add(UiTheme.Label(new Vector2(544, 186), new Vector2(76, 10), "Any color", color: Palette.TextDim));

        Caption(new Vector2(462, 210), "IN THE GAME");
        _previews =
        [
            Add(new PaintPreview { Position = new Vector2(462, 224), Size = new Vector2(64, 64) }),
            Add(new PaintPreview { Position = new Vector2(534, 224), Size = new Vector2(32, 32) }),
            Add(new PaintPreview { Position = new Vector2(574, 224), Size = new Vector2(16, 16) }),
        ];
        Button colors = Add(UiTheme.CompactButton(new Vector2(534, 268), new Vector2(86, 20), "Other colors"));
        colors.TooltipText = "See the figure in other colors a player might pick";
        colors.Pressed += WearOtherColors;

        SaveButton = Add(UiTheme.Button(new Vector2(462, 316), new Vector2(76, 26), "Save"));
        SaveButton.TooltipText = "Ctrl+S";
        SaveButton.Pressed += Save;
        DeleteButton = Add(UiTheme.Button(new Vector2(544, 316), new Vector2(76, 26), "Delete"));
        DeleteButton.Pressed += AskDelete;

        // Deleting asks first: it can't be undone.
        _confirmDelete = Add(new Panel { Position = new Vector2(200, 140), Size = new Vector2(240, 74), Visible = false, ZIndex = 5 });
        _confirmDelete.AddThemeStyleboxOverride("panel", UiTheme.Box(Palette.Ink, Palette.Danger, 2));
        _confirmQuestion = UiTheme.Label(new Vector2(8, 6), new Vector2(224, 32), "", color: Palette.Text);
        _confirmQuestion.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _confirmQuestion.HorizontalAlignment = HorizontalAlignment.Center;
        _confirmDelete.AddChild(_confirmQuestion);
        ConfirmDeleteButton = UiTheme.Accent(UiTheme.Button(new Vector2(16, 44), new Vector2(98, 22), "Delete"), Palette.Danger);
        ConfirmDeleteButton.Pressed += () =>
        {
            _confirmDelete.Visible = false;
            Delete();
        };
        _confirmDelete.AddChild(ConfirmDeleteButton);
        KeepButton = UiTheme.Button(new Vector2(126, 44), new Vector2(98, 22), "Keep it");
        KeepButton.Pressed += () => _confirmDelete.Visible = false;
        _confirmDelete.AddChild(KeepButton);
    }

    private Panel _confirmDelete = null!;
    private Label _confirmQuestion = null!;

    /// <summary>The prompt's buttons (for the click tests).</summary>
    internal Button ConfirmDeleteButton { get; private set; } = null!;

    internal Button KeepButton { get; private set; } = null!;

    internal bool IsAskingToDelete => _confirmDelete.Visible;

    /// <summary>Asks before deleting: it can't be undone.</summary>
    private void AskDelete()
    {
        if (!Looks.IsSaved(LookId))
        {
            return;
        }

        string name = _name.Trim();
        _confirmQuestion.Text = Looks.IsBuilt(LookId)
            ? $"Throw away your repainting of {name}? It goes back to how it came. This can't be undone."
            : $"Delete {name}? This can't be undone.";
        _confirmDelete.Visible = true;
    }

    private void AddSwatch(string paint, Vector2 position, string tip)
    {
        Button swatch = Add(new Button
        {
            Position = position,
            Size = new Vector2(16, 16),
            FocusMode = FocusModeEnum.None,
            TooltipText = tip,
        });
        swatch.Pressed += () =>
        {
            SelectPaint(paint);
            if (Canvas.Tool is PaintTool.Eraser or PaintTool.Pick)
            {
                SelectTool(PaintTool.Pencil);
            }
        };
        _swatches[paint] = swatch;
    }

    private Label SetRow(float top, string what, Action<int> step)
    {
        Add(UiTheme.Label(new Vector2(20, top + 4), new Vector2(30, 12), what, color: Palette.TextDim));
        Button previous = Add(UiTheme.CompactButton(new Vector2(52, top), new Vector2(18, 18), "<"));
        Label label = Add(UiTheme.Label(new Vector2(72, top + 4), new Vector2(88, 12)));
        label.HorizontalAlignment = HorizontalAlignment.Center;
        Button next = Add(UiTheme.CompactButton(new Vector2(162, top), new Vector2(18, 18), ">"));
        previous.Pressed += () => Stepped(-1);
        next.Pressed += () => Stepped(1);
        return label;

        void Stepped(int delta)
        {
            step(delta);
            _worn[2] = _worn[3] = 0;
            Refresh();
        }
    }

    private string? StepSet(string? current, int delta)
    {
        int index = Math.Max(0, _sets.IndexOf(current));
        return _sets[((index + delta) % _sets.Count + _sets.Count) % _sets.Count];
    }

    private static string SetName(string? set) => set is null ? "None" : char.ToUpperInvariant(set[0]) + set[1..];

    private void SelectTool(PaintTool tool)
    {
        Canvas.Tool = tool;
        foreach ((PaintTool each, Button button) in _toolButtons)
        {
            button.SetPressedNoSignal(each == tool);
        }
    }

    private void ToggleMirror()
    {
        MirrorButton.SetPressedNoSignal(!MirrorButton.ButtonPressed);
        Canvas.Mirror = MirrorButton.ButtonPressed;
    }

    private void SelectPaint(string paint)
    {
        Canvas.Paint = paint;
        if (paint.Length > 1 && !_swatches.ContainsKey(paint))
        {
            _custom.Color = new Color(paint);
        }

        StyleSwatches();
    }

    private void StyleSwatches()
    {
        foreach ((string paint, Button swatch) in _swatches)
        {
            Style(swatch, PaintCanvas.ColorOf(paint, Canvas.Tints), paint == Canvas.Paint);
            swatch.Text = paint == Eye ? "e" : "";
            swatch.AddThemeColorOverride("font_color", Palette.TrimBright);
            swatch.AddThemeColorOverride("font_hover_color", Palette.TrimBright);
        }
    }

    private static void Style(Button swatch, Color color, bool selected)
    {
        StyleBoxFlat box = UiTheme.Box(color, selected ? Colors.White : Palette.Ink, selected ? 2 : 1);
        foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled" })
        {
            swatch.AddThemeStyleboxOverride(state, box);
        }
    }

    private Color[] Tints()
    {
        IReadOnlyList<Swatch> skins = Looks.SetSwatches(_skinSet);
        IReadOnlyList<Swatch> hairs = Looks.SetSwatches(_hairSet);
        return
        [
            Looks.Colors[_worn[0] % Looks.Colors.Count].Color,
            Looks.Colors[_worn[1] % Looks.Colors.Count].Color,
            skins.Count > 0 ? skins[_worn[2] % skins.Count].Color : Colors.White,
            hairs.Count > 0 ? hairs[_worn[3] % hairs.Count].Color : Colors.White,
        ];
    }

    private void WearOtherColors()
    {
        _worn[0] = Random.Shared.Next(Looks.Colors.Count);
        _worn[1] = (_worn[0] + 1 + Random.Shared.Next(Looks.Colors.Count - 1)) % Looks.Colors.Count;
        _worn[2] = Random.Shared.Next(100);
        _worn[3] = Random.Shared.Next(100);
        Refresh();
    }

    private void Refresh()
    {
        Color[] tints = Tints();
        Canvas.Tints = tints;
        string[,] pixels = Canvas.Snapshot();
        foreach (PaintPreview preview in _previews)
        {
            preview.Display(pixels, tints);
        }

        _skinLabel.Text = SetName(_skinSet);
        _hairLabel.Text = SetName(_hairSet);
        UndoButton.Disabled = !Canvas.CanUndo;
        RedoButton.Disabled = !Canvas.CanRedo;
        DeleteButton.Disabled = !Looks.IsSaved(LookId);
        DeleteButton.TooltipText = Looks.IsBuilt(LookId) ? "Throw the repainting away; the figure goes back to how it came" : "Delete this painted figure";
        StyleSwatches();
    }

    private void StartNew()
    {
        _name = "New Figure";
        NameEdit.Text = _name;
        _skinSet = "human";
        _hairSet = "hair";
        Canvas.Load(PaintCanvas.Blank());
        Say("A blank figure. Name it, paint it, save it.");
        Refresh();
    }

    private void OpenNeighbor(int delta)
    {
        IReadOnlyList<Look> all = Looks.All;
        int index = all.ToList().FindIndex(look => look.Id == LookId);
        index = index < 0 ? (delta > 0 ? 0 : all.Count - 1) : ((index + delta) % all.Count + all.Count) % all.Count;
        Open(all[index]);
    }

    private void Save()
    {
        // The game's own figures are never painted over: a painting of one is saved as a new figure.
        string? original = Looks.IsOriginal(LookId) ? _name.Trim() : null;
        if (original is not null)
        {
            _name = Looks.FreshName(_name);
            NameEdit.Text = _name;
        }

        PaintedLook painting = Painting();
        if (painting.Problem() is { } problem)
        {
            Say(problem);
            return;
        }

        try
        {
            Looks.SavePainted(painting);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            Say($"Couldn't save: {failure.Message}");
            return;
        }

        Sfx.Play("coin");
        int eyes = painting.Eyes.Count();
        string copy = original is null ? "" : $" ({original} stays as it came.)";
        Say((eyes == 2 ? $"Saved {painting.Name}. Pick it in Characters."
            : $"Saved {painting.Name}. It has {UiTheme.Count(eyes, "eye", "eyes")} marked (most have two).") + copy);
        Refresh();
    }

    private void Delete()
    {
        string id = LookId;
        if (!Looks.IsSaved(id))
        {
            return;
        }

        Looks.DeletePainted(id);
        Say(Looks.IsBuilt(id) ? $"{_name.Trim()} is back to how it came." : $"Deleted {_name.Trim()}.");
        if (Looks.IsBuilt(id))
        {
            Open(Looks.Get(id));
        }

        Refresh();
    }

    private void AskForTrace()
    {
        if (_traceDialog is null)
        {
            _traceDialog = Add(new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenFile,
                Access = FileDialog.AccessEnum.Filesystem,
                UseNativeDialog = true,
                Title = "Open a drawing to trace",
                Filters = ["*.png, *.jpg, *.jpeg, *.webp, *.bmp ; Pictures"],
            });
            _traceDialog.FileSelected += path => LoadTrace(path);
        }

        _traceDialog.PopupCentered();
    }

    private void Say(string text) => _status.Text = text;
}

/// <summary>The figure being painted at the size it's drawn in the game (or a whole multiple of it), on a dungeon floor's green.</summary>
public partial class PaintPreview : Control
{
    private string[,] _pixels = PaintCanvas.Blank();

    private Color[] _tints = [Colors.White, Colors.White, Colors.White, Colors.White];

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public void Display(string[,] pixels, Color[] tints)
    {
        _pixels = pixels;
        _tints = tints;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.FloorLight);
        float cell = Size.X / PaintCanvas.Cells;
        for (int y = 0; y < PaintCanvas.Cells; y++)
        {
            for (int x = 0; x < PaintCanvas.Cells; x++)
            {
                if (_pixels[x, y].Length > 0)
                {
                    DrawRect(new Rect2(x * cell, y * cell, cell, cell), PaintCanvas.ColorOf(_pixels[x, y], _tints));
                }
            }
        }
    }
}
