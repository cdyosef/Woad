using Godot;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The hands of ability cards on the sheet, one per pool where its symbol goes: Rumble (Attack, Shove,
/// Bull Rush, Guard, High Alert, and Dig out while buried), Wend (Wend, Leap, Prone or Get up, and Drop and roll
/// while burning) and Od Hand (Search, Steal, Pick lock, Disarm, Aid, Parry and Learn). Skill cards are
/// in the hand only once the hero has learned the skill. Each card takes the action of a button
/// the screen already keeps (hidden now), and lights up when there's something in reach to use it on:
/// a monster to hit, a lock beside you to pick. Pick lock, Disarm, Aid and Guard (with an ally beside
/// you) wait for a click on what to use them on; Aid on someone with more than one ailment asks which to cure.
/// </summary>
public partial class GameScreen
{
    private enum OdAim
    {
        None,
        PickLock,
        Disarm,
        Aid,

        /// <summary>Guard: a Rumble skill, but it waits for a click on whom to guard just the same.</summary>
        Guard,
    }

    private HandView _rumbleHand = null!;
    private HandView _wendHand = null!;
    private HandView _odHand = null!;
    private HandView _magicHand = null!;
    private Control _abilities = null!;

    /// <summary>A hidden button for each spell the active hero knows, for its card in the Magic hand to press.</summary>
    private readonly Dictionary<string, Button> _spellButtons = [];
    private HandSpread _spread = null!;
    private Button _pickLock = null!;
    private Button _disarm = null!;
    private Button _aid = null!;
    private Button _parry = null!;
    private Button _guard = null!;
    private Button _highAlert = null!;
    private Button _bullRush = null!;

    /// <summary>Aid's choice of what to cure, when the patient has more than one ailment.</summary>
    private Panel _curePicker = null!;
    private Button _learn = null!;
    private OdAim _odAim;

    private IEnumerable<HandView> Hands => [_rumbleHand, _wendHand, _odHand, _magicHand];

    /// <summary>Puts the old action buttons out of sight (the hands take their actions) and builds the hands.</summary>
    private void BuildHands(Control right)
    {
        var hidden = new Control { Visible = false, Name = "Abilities" };
        right.AddChild(hidden);
        _abilities = hidden;
        foreach (Button button in new[] { _rumble, _shove, _prone, _dropRoll, _digOut, _roll, _leap, _search, _steal })
        {
            button.Reparent(hidden);
        }

        _pickLock = AimButton(hidden, "Pick lock", OdAim.PickLock, "Pick lock: click the lock (a door you stand in, or a chest or gate beside you). Right-click to cancel.");
        _disarm = AimButton(hidden, "Disarm", OdAim.Disarm, "Disarm: click a trap you've found beside you, or a lit bomb. Right-click to cancel.");
        _aid = AimButton(hidden, "Aid", OdAim.Aid, "Aid: click yourself, or an ally beside you: rouse the fallen, or cure what ails them. Right-click to cancel.");
        _learn = new Button { Text = "Learn" };
        _learn.Pressed += LearnFromScroll;
        hidden.AddChild(_learn);
        _parry = new Button { Text = "Parry" };
        _parry.Pressed += () => Execute(new Parry(ActiveHero!.Id));
        hidden.AddChild(_parry);
        _guard = new Button { Text = "Guard" };
        _guard.Pressed += StartGuard;
        hidden.AddChild(_guard);
        _bullRush = new Button { Text = "Bull Rush" };
        _bullRush.Pressed += () =>
        {
            bool rushing = !_rushing;
            CancelSelection();
            _rushing = rushing;
            if (_rushing)
            {
                Log("Bull Rush: click a purple tile to charge that way. Right-click to cancel.", Palette.DieRumble);
            }

            RefreshPanels();
        };
        hidden.AddChild(_bullRush);
        _highAlert = new Button { Text = "High Alert" };
        _highAlert.Pressed += () => Execute(new HighAlert(ActiveHero!.Id));
        hidden.AddChild(_highAlert);

        DicePoolView pool = _heroPanel.Pool;
        _rumbleHand = AddHand(pool.Bounds(DicePool.Rumble).Position, "icon_rumble", "Rumble", Palette.DieRumble,
        [
            new("fist", "Attack", _rumble, () => _session.GetAttackableMonsters(ReadiedWeaponId).Count > 0),
            new("shove", "Shove", _shove, () => _session.GetShoveTargets().Count > 0, () => Knows(Skill.Shove)),
            new("bull_rush", "Bull Rush", _bullRush, () => _session.GetBullRushTargets().Any(tile => ShownRoom.MonsterAt(tile) is not null), () => Knows(Skill.BullRush)),
            new("guard", "Guard", _guard, Threatened, () => Knows(Skill.Guard)),
            new("high_alert", "High Alert", _highAlert, () => true, () => Knows(Skill.HighAlert)),
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
            new("rouse", "Aid", _aid, () => _session.GetAidTargets().Count > 0),
            new("parry", "Parry", _parry, Threatened, () => Knows(Skill.Parry)),
            new("learn", "Learn", _learn, () => true),
        ]);

        // The Magic hand, under the Magic card: the spells they know (filled in as they're learned).
        _magicHand = new HandView { Position = new Vector2(HeroPanel.Columns[2] - 3, HeroPanel.Rows[0] + HeroPanel.CardHeight), Icon = "icon_magic", Title = "Magic", Color = new Color("b07ce8") };
        _magicHand.Opened += _ => OpenSpells();
        _heroPanel.AddChild(_magicHand);

        _spread = new HandSpread { Position = BoardOrigin, Size = new Vector2(BoardView.Pixels, BoardView.Pixels), ZIndex = 3 };
        _spread.Picked += PlayAbility;
        AddChild(_spread);
        AddChild(_heroPanel.BagView);

        _curePicker = new Panel { Position = BoardOrigin + new Vector2(BoardView.Pixels / 2 - 70, 60), Size = new Vector2(140, 40), Visible = false, ZIndex = 4 };
        AddChild(_curePicker);
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

    private bool Knows(Skill skill) => ActiveHero?.Knows(skill) == true;

    /// <summary>Monsters share the hero's room: a die held for defense could matter.</summary>
    private bool Threatened() => ActiveHero is { } hero && _session.State.RoomOf(hero).Monsters.Count > 0;

    /// <summary>Guard: straight onto the hero if there's no one beside them, or wait for a click on whom.</summary>
    private void StartGuard()
    {
        IReadOnlyList<Hero> targets = _session.GetGuardTargets();
        if (targets.Count == 1)
        {
            Execute(new GuardHero(ActiveHero!.Id, targets[0].Id));
            return;
        }

        CancelSelection();
        _odAim = OdAim.Guard;
        Log("Guard: click yourself, or an ally beside you, to hold a Rumble die for their next defense. Right-click to cancel.", Palette.DieRumble);
        RefreshPanels();
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
        foreach (Button aim in new[] { _pickLock, _disarm })
        {
            aim.Disabled = !ready || odHand is not null;
            aim.TooltipText = odHand ?? "";
        }

        bool aidable = _session.GetAidTargets().Count > 0;
        _aid.Disabled = !ready || !aidable;
        _aid.TooltipText = aidable ? "" : "There's no one beside you to rouse or cure (with Od Hand dice, or a Medicine Bag, left).";
        _curePicker.Visible &= ready;
        bool turn = _session.State.Turn is not null;
        Hold(_parry, ready, turn ? _session.CheckParry() : "");
        Hold(_guard, ready, !turn ? "" : _session.GetGuardTargets().Count > 0 ? null : _session.CheckGuard(ActiveHero!.Id) ?? "");
        Hold(_highAlert, ready, turn ? _session.CheckHighAlert() : "");
        Hold(_bullRush, ready, turn ? _session.CheckBullRush() : "");
        _rushing &= !_bullRush.Disabled;

        Hero? hero = ActiveHero;
        CardInstance? scroll = hero?.AllCards.FirstOrDefault(card => card.Definition.Teaches is not null && _session.CheckLearnSpell(card.Id) is null);
        _learn.Disabled = !ready || scroll is null;
        _learn.TooltipText = scroll is null ? "You've no scroll you could learn a spell from now." : $"Learn {scroll.Definition.Name}'s spell: Od Hand and Magic.";
        _odAim = ready ? _odAim : OdAim.None;
        _magicHand.Hold(SpellCards(hero, ready));
        foreach (HandView hand in Hands)
        {
            hand.ShowCards(hand.Cards);
            hand.Visible = hero is not null && _session.State.Turn is not null;
        }

        _magicHand.Visible &= hero?.Spells.Count > 0;

        if (!ready)
        {
            _spread.Close();
        }
    }

    private static void Hold(Button button, bool ready, string? blocked)
    {
        button.Disabled = !ready || blocked is not null;
        button.TooltipText = blocked ?? "";
    }

    /// <summary>A card for each spell the hero knows: castable now, it's lit; if not, it says why.</summary>
    private List<AbilityCard> SpellCards(Hero? hero, bool ready)
    {
        var cards = new List<AbilityCard>();
        foreach (SpellDefinition spell in hero?.Spells ?? [])
        {
            if (!_spellButtons.TryGetValue(spell.Id, out Button? button))
            {
                string id = spell.Id;
                button = new Button { Text = spell.Name };
                button.Pressed += () => BeginCasting(id);
                _abilities.AddChild(button);
                _spellButtons[id] = button;
            }

            string? error = _session.CheckCastSpell(spell.Id);
            button.Disabled = !ready || error is not null;
            button.TooltipText = error ?? $"{spell.Name} ({spell.Cost} Power): {spell.Text}";
            cards.Add(new AbilityCard($"spell_{spell.Id}", spell.Name, button, () => true));
        }

        return cards;
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
            case OdAim.Aid when _session.State.HeroesIn(_session.State.RoomOf(hero)).FirstOrDefault(other => other.Position == tile && (other.IsActive || other.IsKeelingOver)) is { } patient:
                AidHero(patient);
                break;
            case OdAim.Aid:
                Log("Aid: there's no one there.", Palette.TextDim);
                break;
            case OdAim.Guard when _session.State.ActiveHeroesIn(_session.State.RoomOf(hero)).FirstOrDefault(other => other.Position == tile) is { } ally:
                if (_session.CheckGuard(ally.Id) is { } refused)
                {
                    Log($"Guard: {refused}", Palette.TextDim);
                }
                else
                {
                    Execute(new GuardHero(hero.Id, ally.Id));
                }

                break;
            case OdAim.Guard:
                Log("Guard: there's no one there.", Palette.TextDim);
                break;
            default:
                return false;
        }

        RefreshPanels();
        return true;
    }

    /// <summary>Aid on someone: rouse them if they're down, cure their one ailment, or ask which of several.</summary>
    private void AidHero(Hero patient)
    {
        int patientId = patient.Id;
        IReadOnlyList<(Ailment Ailment, int Difficulty)> cures = patient.IsKeelingOver ? [] : _session.GetAidCures(patientId);
        if (cures.Count > 1)
        {
            AskWhich($"Aid {patient.Name}: cure what?", cures.Select(cure => (cure.Ailment, $"{cure.Ailment.Name()} ({cure.Difficulty}+)")),
                ailment => ExecuteOdHand((heroId, dice, tools, power) => new Aid(heroId, patientId, ailment, dice, tools, power)));
        }
        else if (_session.CheckAid(patientId) is { } error)
        {
            Log($"Aid: {error}", Palette.TextDim);
        }
        else
        {
            ExecuteOdHand((heroId, dice, tools, power) => new Aid(heroId, patientId, null, dice, tools, power));
        }
    }

    /// <summary>A small list of ailments over the board to pick one from (or Cancel).</summary>
    private void AskWhich(string title, IEnumerable<(Ailment Ailment, string Label)> options, Action<Ailment> chosen)
    {
        foreach (Node child in _curePicker.GetChildren())
        {
            child.QueueFree();
        }

        _curePicker.AddChild(UiTheme.Label(new Vector2(6, 3), new Vector2(128, 12), title, color: Palette.TrimBright));
        float y = 18;
        foreach ((Ailment ailment, string label) in options)
        {
            Button button = UiTheme.CompactButton(new Vector2(6, y), new Vector2(128, 16), label);
            button.Pressed += () =>
            {
                _curePicker.Visible = false;
                chosen(ailment);
            };
            _curePicker.AddChild(button);
            y += 18;
        }

        Button cancel = UiTheme.CompactButton(new Vector2(6, y), new Vector2(128, 16), "Cancel");
        cancel.Pressed += () => _curePicker.Visible = false;
        _curePicker.AddChild(cancel);
        _curePicker.Size = new Vector2(140, y + 22);
        _curePicker.Visible = true;
        MoveChild(_curePicker, -1);
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
