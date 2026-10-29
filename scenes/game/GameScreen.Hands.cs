using Godot;
using Woad.Core.Commands;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The hands of ability cards on the sheet, one per pool where its symbol goes: Rumble (Attack, Shove,
/// and Dig out while buried), Wend (Wend, Leap, Prone or Get up, and Drop and roll while burning) and
/// Od Hand (Search, Steal, Pick lock, Disarm, Rouse, Learn, and Tend while blind). Each card takes the
/// action of a button the screen already keeps (hidden now), and lights up when there's something in
/// reach to use it on: a monster to hit, a lock beside you to pick. Pick lock, Disarm and Rouse wait for
/// a click on what to use them on.
/// </summary>
public partial class GameScreen
{
    private enum OdAim
    {
        None,
        PickLock,
        Disarm,
        Rouse,
    }

    private HandView _rumbleHand = null!;
    private HandView _wendHand = null!;
    private HandView _odHand = null!;
    private HandSpread _spread = null!;
    private Button _pickLock = null!;
    private Button _disarm = null!;
    private Button _rouse = null!;
    private Button _learn = null!;
    private OdAim _odAim;

    private IEnumerable<HandView> Hands => [_rumbleHand, _wendHand, _odHand];

    /// <summary>Puts the old action buttons out of sight (the hands take their actions) and builds the hands.</summary>
    private void BuildHands(Control right)
    {
        var hidden = new Control { Visible = false, Name = "Abilities" };
        right.AddChild(hidden);
        foreach (Button button in new[] { _rumble, _shove, _prone, _dropRoll, _digOut, _tend, _roll, _leap, _search, _steal })
        {
            button.Reparent(hidden);
        }

        _pickLock = AimButton(hidden, "Pick lock", OdAim.PickLock, "Pick lock: click the lock (a door you stand in, or a chest or gate beside you). Right-click to cancel.");
        _disarm = AimButton(hidden, "Disarm", OdAim.Disarm, "Disarm: click a trap you've found beside you, or a lit bomb. Right-click to cancel.");
        _rouse = AimButton(hidden, "Rouse", OdAim.Rouse, "Rouse: click an ally Keeling Over beside you. Right-click to cancel.");
        _learn = new Button { Text = "Learn" };
        _learn.Pressed += LearnFromScroll;
        hidden.AddChild(_learn);

        DicePoolView pool = _heroPanel.Pool;
        _rumbleHand = AddHand(pool.Bounds(DicePool.Rumble).Position, "icon_rumble", "Rumble", Palette.DieRumble,
        [
            new("fist", "Attack", _rumble, () => _session.GetAttackableMonsters(ReadiedWeaponId).Count > 0),
            new("shove", "Shove", _shove, () => _session.GetShoveTargets().Count > 0),
            new("dig_out", "Dig out", _digOut, () => true, () => ActiveHero?.Conditions.Has(Condition.Buried) == true),
        ]);
        _wendHand = AddHand(pool.Bounds(DicePool.Wend).Position, "icon_wend", "Wend", Palette.DieWend,
        [
            new("wend", "Wend", _roll, () => true),
            new("leap", "Leap", _leap, () => _session.GetLeapTargets().Count > 0),
            new("prone", "Prone", _prone, () => ActiveHero?.Prone == true),
            new("drop_roll", "Drop & roll", _dropRoll, () => true, () => ActiveHero?.FireDice > 0),
        ]);
        _odHand = AddHand(pool.Bounds(DicePool.OdHand).Position, "icon_odhand", "Od Hand", Palette.DieOdHand,
        [
            new("search", "Search", _search, () => true),
            new("steal", "Steal", _steal, () => Beside().Any(tile => VictimAt(tile) is int id && _session.CheckSteal(id) is null)),
            new("pick_lock", "Pick lock", _pickLock, () => Beside().Any(tile => _session.CheckPickLock(tile) is null)),
            new("disarm", "Disarm", _disarm, () => Beside().Any(tile => _session.CheckDisarm(tile) is null)),
            new("rouse", "Rouse", _rouse, () => _session.GetRousableAllies().Count > 0),
            new("learn", "Learn", _learn, () => true),
            new("tend", "Tend eyes", _tend, () => true, () => ActiveHero?.Conditions.Has(Condition.Blind) == true),
        ]);

        _spread = new HandSpread { Position = BoardOrigin, Size = new Vector2(BoardView.Pixels, BoardView.Pixels), ZIndex = 3 };
        _spread.Picked += PlayAbility;
        AddChild(_spread);
        AddChild(_heroPanel.BagView);
    }

    private HandView AddHand(Vector2 at, string icon, string title, Color color, AbilityCard[] cards)
    {
        var hand = new HandView { Position = at + new Vector2(-1, -2), Icon = icon, Title = title, Color = color };
        hand.Hold(cards);
        hand.Opened += opened =>
        {
            if (_spread.Hand == opened)
            {
                _spread.Close();
            }
            else if (!Locked)
            {
                _spread.Open(opened);
            }
        };
        _heroPanel.AddChild(hand);
        _heroPanel.Pool.CoverIcon(PoolOf(title));
        return hand;
    }

    private static DicePool PoolOf(string title) => title switch
    {
        "Rumble" => DicePool.Rumble,
        "Wend" => DicePool.Wend,
        _ => DicePool.OdHand,
    };

    private Button AimButton(Control parent, string text, OdAim aim, string hint)
    {
        var button = new Button { Text = text };
        button.Pressed += () =>
        {
            CancelSelection();
            _odAim = aim;
            Log(hint, Palette.DieOdHand);
            RefreshPanels();
        };
        parent.AddChild(button);
        return button;
    }

    /// <summary>A card from a spread hand: its action, or why it can't be done now.</summary>
    private void PlayAbility(AbilityCard card)
    {
        if (card.Action.Disabled)
        {
            string why = card.Action.TooltipText.Length > 0 ? card.Action.TooltipText : "not now.";
            Log($"{card.Name}: {why}", Palette.TextDim);
            return;
        }

        card.Action.EmitSignal(BaseButton.SignalName.Pressed);
    }

    /// <summary>Lights the hands' cards for what's in reach now, and the Od Hand cards that wait for a target.</summary>
    private void RefreshHands(bool ready)
    {
        string? odHand = _session.State.Turn is null ? "" : _session.CheckSearch();
        foreach (Button aim in new[] { _pickLock, _disarm, _rouse })
        {
            aim.Disabled = !ready || odHand is not null;
            aim.TooltipText = odHand ?? "";
        }

        Hero? hero = ActiveHero;
        CardInstance? scroll = hero?.AllCards.FirstOrDefault(card => card.Definition.Teaches is not null && _session.CheckLearnSpell(card.Id) is null);
        _learn.Disabled = !ready || scroll is null;
        _learn.TooltipText = scroll is null ? "You've no scroll you could learn a spell from now." : $"Learn {scroll.Definition.Name}'s spell: Od Hand and Magic.";
        _odAim = ready ? _odAim : OdAim.None;
        foreach (HandView hand in Hands)
        {
            hand.ShowCards(hand.Cards);
            hand.Visible = hero is not null && _session.State.Turn is not null;
        }

        if (!ready)
        {
            _spread.Close();
        }
    }

    /// <summary>The hero's tile and the eight around it, in the room.</summary>
    private IEnumerable<GridPosition> Beside()
    {
        if (ActiveHero is not { } hero || _session.State.Turn is null)
        {
            return [];
        }

        Room room = _session.State.RoomOf(hero);
        return [.. hero.Position.SurroundingTiles().Append(hero.Position).Where(tile => room.Layout.Contains(tile))];
    }

    private int? VictimAt(GridPosition tile)
    {
        Room room = _session.State.RoomOf(ActiveHero!);
        return room.MonsterAt(tile)?.Id ?? room.MerchantAt(tile)?.Id ?? _session.State.ActiveHeroesIn(room).FirstOrDefault(other => other.Position == tile && other != ActiveHero)?.Id;
    }

    /// <summary>A click while an Od Hand card waits for its target. Returns whether it took the click.</summary>
    private bool AimOdHand(Hero hero, GridPosition tile)
    {
        OdAim aim = _odAim;
        _odAim = OdAim.None;
        switch (aim)
        {
            case OdAim.PickLock when _session.CheckPickLock(tile) is { } error:
                Log($"Pick lock: {error}", Palette.TextDim);
                break;
            case OdAim.PickLock:
                ExecuteOdHand((heroId, dice, tools, power) => new PickLock(heroId, tile, dice, tools, power));
                break;
            case OdAim.Disarm when _session.CheckDisarm(tile) is { } error:
                Log($"Disarm: {error}", Palette.TextDim);
                break;
            case OdAim.Disarm:
                ExecuteOdHand((heroId, dice, tools, power) => new Disarm(heroId, tile, dice, tools, power));
                break;
            case OdAim.Rouse when _session.GetRousableAllies().FirstOrDefault(ally => ally.Position == tile) is { } fallen:
                ExecuteOdHand((heroId, dice, tools, power) => new Rouse(heroId, fallen.Id, dice, tools, power));
                break;
            case OdAim.Rouse:
                Log("Rouse: there's no one Keeling Over there to rouse.", Palette.TextDim);
                break;
            default:
                return false;
        }

        RefreshPanels();
        return true;
    }

    /// <summary>The Learn card: study the first scroll whose spell can be learned now.</summary>
    private void LearnFromScroll()
    {
        if (ActiveHero?.AllCards.FirstOrDefault(card => card.Definition.Teaches is not null && _session.CheckLearnSpell(card.Id) is null) is not { } scroll)
        {
            return;
        }

        int scrollId = scroll.Id;
        ExecuteOdHand((heroId, dice, _, power) => new LearnSpell(heroId, scrollId, dice, power));
    }
}
