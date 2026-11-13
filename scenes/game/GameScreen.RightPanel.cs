using Godot;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The right panel, top to bottom: the floor map, Party and Menu; the hover box (what the mouse is over: its picture,
/// what it is, how to interact); what you've selected (the target's sheet, or a question the game asks); and the log,
/// whose height you set by dragging the bar above it (kept between games). A picked-up card's whole story is in the
/// item slide-out on the left, below the carry and fire tabs.
/// </summary>
public partial class GameScreen
{
    private const float HoverTop = 100;
    private const float LogBottomRoom = 6;

    /// <summary>How tall the item slide-out is: its name, its buttons and the start of its story (the rest scrolls).</summary>
    private const float ItemSlideoutHeight = 130;
    private const float DefaultLogHeight = 66;
    private const string UiPrefsPath = "user://ui.cfg";

    private HoverBox _hoverBox = null!;
    private ItemSlideout _itemSlideout = null!;
    private LogDivider _logDivider = null!;
    private float _logHeight = DefaultLogHeight;

    internal HoverBox HoverPanel => _hoverBox;

    internal HeroPanel Sheet => _heroPanel;

    internal ItemSlideout Slideout => _itemSlideout;

    internal ItemSlideout ItemPanel => _itemSlideout;

    internal float LogHeight => _log.Size.Y;

    /// <summary>For the tests: picks up a card, targets someone, and hovers a tile, as clicks and the mouse would.</summary>
    internal void ShowForTest(int? cardId, int? targetId, GridPosition? hover)
    {
        _selectedCardId = cardId;
        _targetId = targetId;
        RefreshPanels();
        OnTileHovered(hover);
    }

    /// <summary>A card under the mouse: the hover box tells about it; none, and it goes back to the board.</summary>
    private void OnCardHovered(CardInstance? card)
    {
        if (card is null)
        {
            OnTileHovered(_hovered);
            return;
        }

        CardDefinition definition = card.Definition;
        string text = definition.Text;
        int stop = text.IndexOf(". ", StringComparison.Ordinal);
        string first = stop > 0 && stop < 140 ? text[..(stop + 1)] : text;
        bool mine = ActiveHero?.FindCard(card.Id) is not null;
        SetHover(new HoverInfo(CardSlot.Title(card), $"{ItemInfo.Kind(definition)}. {first}",
            mine ? "Click to pick it up: all about it slides out on the left. Double-click to use or equip it; drag it to move it." : "",
            GameArt.Texture($"res://assets/cards/{definition.Id}.png")));
    }

    private void BuildRightPanel()
    {
        CardSlot.Hovered += OnCardHovered;
        _inventory.QuestionChanged += RefreshTarget;
        TreeExiting += () => CardSlot.Hovered -= OnCardHovered;
        _hoverBox = new HoverBox { Position = new Vector2(6, HoverTop), Size = new Vector2(SideWidth - 12, HoverBox.MinHeight) };
        _hoverBox.Grew += PlaceRight;
        _rightPanel.AddChild(_hoverBox);
        _logDivider = new LogDivider { Size = new Vector2(SideWidth - 8, 5), ZIndex = 1 };
        _logDivider.Dragged += y =>
        {
            float bottom = _rightPanel.Size.Y - LogBottomRoom;
            _logHeight = Mathf.Clamp(bottom - y, 24, bottom - HoverTop - 30);
            PlaceRight();
        };
        _logDivider.Released += SaveUiPrefs;
        AddChild(_logDivider);
        var prefs = new ConfigFile();
        if (prefs.Load(UiPrefsPath) == Error.Ok)
        {
            _logHeight = (float)prefs.GetValue("log", "height", DefaultLogHeight).AsDouble();
        }
    }

    private void SaveUiPrefs()
    {
        var prefs = new ConfigFile();
        prefs.Load(UiPrefsPath);
        prefs.SetValue("log", "height", _logHeight);
        prefs.Save(UiPrefsPath);
    }

    /// <summary>The item slide-out, coming out over the board's left side (see <see cref="PlaceItemSlideout"/>).</summary>
    private ItemSlideout BuildItemSlideout()
    {
        _itemSlideout = new ItemSlideout { Position = new Vector2(SideWidth, StripHeight + 2), Size = new Vector2(ItemSlideout.Width, 200), ZIndex = 2 };
        return _itemSlideout;
    }

    /// <summary>
    /// The item slide-out comes out from the sheet's edge over the board's lower left, down to the prompt strip (short,
    /// leaving the board above it to click on): beside the bag's drawer while it's out, or the carry or fire tab, so it
    /// never covers them.
    /// </summary>
    private void PlaceItemSlideout()
    {
        if (_drawer is null || _itemSlideout is null)
        {
            return;
        }

        float left = _boardFrame.Position.X;
        float beside = _drawer.GetChildren().OfType<Control>().Where(view => view.Visible).Select(view => view.Size.X * _sheetScale).DefaultIfEmpty(0).Max();
        if (beside == 0 && (_carryTab.IsOut || _fireTab.Visible))
        {
            beside = 72;
        }

        float width = Mathf.Clamp(_boardFrame.Size.X - beside - 2, 120, ItemSlideout.Width);
        float bottom = _promptStrip.Visible ? _promptStrip.Position.Y : _dice.Position.Y - 1;
        float height = Mathf.Min(ItemSlideoutHeight, bottom - StripHeight - 4);
        _itemSlideout.Position = new Vector2(left + beside, bottom - 2 - height);
        _itemSlideout.Size = new Vector2(width, height);
    }

    /// <summary>Lays out the right panel's boxes, top to bottom: the hover box, what's selected, and the log taking the height you gave it.</summary>
    private void PlaceRight()
    {
        if (_hoverBox is null || _log is null)
        {
            return;
        }

        float width = _rightPanel.Size.X;
        _hoverBox.Size = new Vector2(width - 12, _hoverBox.Size.Y);
        float y = HoverTop + _hoverBox.Size.Y + 6;
        _inventory.Position = new Vector2(0, y);
        _targetSheet.Position = new Vector2(4, y);
        _targetSheet.Size = new Vector2(width - 8, _targetSheet.Size.Y);
        _inventory.Widen(width);
        float below = y + (_targetSheet.Visible ? _targetSheet.Size.Y + 4 : _inventory.IsChoosingHand ? 76 : 0);
        float bottom = _rightPanel.Size.Y - LogBottomRoom;
        float top = Mathf.Min(bottom - 24, Mathf.Max(below, bottom - _logHeight));
        float left = _rightPanel.Position.X;
        _logDivider.Position = new Vector2(left + 4, top - 6);
        _logDivider.Size = new Vector2(width - 8, 5);
        _log.Position = new Vector2(left + 8, top);
        _log.Size = new Vector2(width - 16, bottom - top);
    }

    /// <summary>What the hover box says about a tile: who or what is there, its picture, and how to interact with it.</summary>
    private HoverInfo HoverFor(GridPosition tile)
    {
        Room room = ShownRoom;
        string text = DescribeTile(tile);
        if (room.MonsterAt(tile) is { } monster)
        {
            bool attackable = _session.State.Turn is not null && _session.GetAttackableMonsters().Contains(monster);
            return new HoverInfo($"{monster.Definition.Name}  {monster.Hearts}/{monster.MaxHearts}", text,
                attackable ? "Click to attack it (Rumble). Its sheet shows below once clicked." : "Click to look it over: its sheet shows below.",
                GameArt.Monster(_session.State.ThemeId, monster.Definition.Id), TitleColor: Palette.Danger);
        }

        if (_session.State.ActiveHeroesIn(room).FirstOrDefault(hero => hero.Position == tile) is { } hero)
        {
            string how = hero == ActiveHero ? "That's you. Click yourself to pick up what's here, or Aid yourself."
                : hero.IsKeelingOver ? "Down! Click beside them to Aid them or carry them."
                : "Click to put up their sheet below.";
            return new HoverInfo(hero.Name, text, how, Look: hero.Guise is null ? Looks.Resolve(hero.Appearance, hero.Portrait) : null, TitleColor: Palette.Seat(hero.Seat));
        }

        if (room.MerchantAt(tile) is { } merchant)
        {
            return new HoverInfo(merchant.Name, text, merchant.Chained ? "Beside them: use a key, or Pick lock with Od Hand." : "Click beside them to trade.");
        }

        if (room.LootAt(tile).FirstOrDefault() is { } loot)
        {
            return new HoverInfo(NameAt(tile) is { Length: > 0 } name ? name : loot.Definition.Name, text, "Walk onto it and click yourself to pick it up.",
                GameArt.Texture($"res://assets/cards/{loot.Definition.Id}.png"));
        }

        bool reachable = _session.State.Turn is not null && _session.GetReachableTiles().ContainsKey(tile);
        return new HoverInfo(NameAt(tile), text, reachable ? "Click to walk here." : "");
    }

    /// <summary>The bar above the log: drag it up or down to make the log taller or shorter.</summary>
    private sealed partial class LogDivider : Control
    {
        private bool _dragging;
        private bool _hover;

        public event Action<float>? Dragged;

        public event Action? Released;

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.Vsize;
            TooltipText = "Drag to make the log taller or shorter.";
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
            {
                _dragging = button.Pressed;
                if (!_dragging)
                {
                    Released?.Invoke();
                }

                AcceptEvent();
            }
            else if (@event is InputEventMouseMotion motion && _dragging)
            {
                Dragged?.Invoke(Position.Y + motion.Position.Y);
                AcceptEvent();
            }
        }

        public override void _Notification(int what)
        {
            if (what is (int)NotificationMouseEnter or (int)NotificationMouseExit)
            {
                _hover = what == NotificationMouseEnter;
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            Color line = _hover || _dragging ? Palette.TrimBright : Palette.TrimDim;
            DrawLine(new Vector2(0, 2), new Vector2(Size.X, 2), line, 1);
            for (int i = -1; i <= 1; i++)
            {
                DrawRect(new Rect2(Size.X / 2 + i * 4 - 1, 1, 2, 2), line);
            }
        }
    }
}
