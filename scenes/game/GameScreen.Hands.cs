using Godot;
using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Events;
using Woad.Core.Model;
using Woad.Core.Rules;
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

    /// <summary>The card each hero last played into each hand's spot (by hero and hand), so it's there again on their turn.</summary>
    private readonly Dictionary<(int HeroId, string Hand), string?> _playedCards = [];

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
        AbilityCard attack = new("fist", "Attack", _rumble, () => _session.GetAttackableMonsters(ReadiedWeaponId).Count > 0);
        AbilityCard wend = new("wend", "Wend", _roll, () => true);
        AbilityCard search = new("search", "Search", _search, () => true);
        _rumbleHand = AddHand(new Vector2(HeroPanel.Columns[2], HeroPanel.Rows[1]), "icon_rumble", "Rumble", Palette.DieRumble, attack,
        [
            attack,
            new("shove", "Shove", _shove, () => _session.GetShoveTargets().Count > 0, () => Knows(Skill.Shove)),
            new("bull_rush", "Bull Rush", _bullRush, () => _session.GetBullRushTargets().Any(tile => ShownRoom.MonsterAt(tile) is not null), () => Knows(Skill.BullRush)),
            new("guard", "Guard", _guard, Threatened, () => Knows(Skill.Guard)),
            new("high_alert", "High Alert", _highAlert, () => true, () => Knows(Skill.HighAlert)),
            new("dig_out", "Dig out", _digOut, () => true, () => ActiveHero?.Conditions.Has(Condition.Buried) == true),
        ]);
        _wendHand = AddHand(new Vector2(HeroPanel.Columns[1], HeroPanel.Rows[2]), "icon_wend", "Wend", Palette.DieWend, wend,
        [
            wend,
            new("leap", "Leap", _leap, () => _session.GetLeapTargets().Count > 0),
            new("prone", "Prone", _prone, () => ActiveHero?.Prone == true),
            new("drop_roll", "Drop & roll", _dropRoll, () => true, () => ActiveHero?.FireDice > 0),
        ]);
        _odHand = AddHand(new Vector2(HeroPanel.Columns[0], HeroPanel.Rows[1]), "icon_odhand", "Od Hand", Palette.DieOdHand, search,
        [
            search,
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
        _spread.Picked += (hand, card) =>
        {
            // Played from the hand, a skill takes its place in the stack, on top; the dice action puts the dice card on top.
            if (hand.Default is not null && ActiveHero is { } hero)
            {
                if (card != hand.Default)
                {
                    hand.Played = card;
                    _playedCards[(hero.Id, hand.Title)] = card.Name;
                }

                _fronts[(hero.Id, hand.Title)] = card == hand.Default ? HandView.DiceKey : HandView.SkillKey;
                ApplyStackFronts();
            }

            PlayAbility(card);
        };
        AddChild(_spread);
        AddChild(_heroPanel.BagView);

        _curePicker = new Panel { Position = BoardOrigin + new Vector2(BoardView.Pixels / 2 - 70, 60), Size = new Vector2(140, 40), Visible = false, ZIndex = 4 };
        AddChild(_curePicker);
    }

    private HandView AddHand(Vector2 at, string icon, string title, Color color, AbilityCard dice, AbilityCard[] cards)
    {
        var hand = new HandView { Position = at, Icon = icon, Title = title, Color = color, Default = dice, DiceColor = color };
        hand.Hold(cards);
        hand.CardSelected += (_, card) =>
        {
            if (!Locked && ActiveHero is { } hero)
            {
                _fronts[(hero.Id, hand.Title)] = card.Key;

                // An item's card is picked up for its buttons too (Use, Equip, Belt...), as its slot's card was.
                if (card.CardId is int cardId)
                {
                    ClickCard(cardId);
                }

                RefreshPanels();
            }
        };
        hand.DieToCard += _ => MoveDie(hand, onto: true);
        hand.DieToPool += _ => MoveDie(hand, onto: false);
        hand.CardUsed += (_, card) => UseStackCard(hand, card);
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
        _heroPanel.Pool.Hide(PoolOf(title));
        return hand;
    }

    /// <summary>A die moved between a pool and its dice card: one more (or fewer) for the next roll.</summary>
    private void MoveDie(HandView hand, bool onto)
    {
        if (Locked || ActiveHero is null)
        {
            return;
        }

        if (hand == _rumbleHand)
        {
            Choose(_rumbleChoice, onto ? SelectedAttackDice : 0, SelectedAttackDice);
        }
        else if (hand == _wendHand)
        {
            _moveDiceHeld = Math.Clamp(_moveDiceHeld + (onto ? -1 : 1), 0, _session.MoveDiceLeft);
        }
        else
        {
            Choose(_odHandChoice, onto ? SelectedOdHandDice : 0, SelectedOdHandDice);
        }

        RefreshPanels();
    }

    /// <summary>The border of a card's own pool: a weapon's red, a shield's blue, a tool's yellow, boots' green.</summary>
    private static Color PoolColor(CardDefinition card) => card switch
    {
        { Type: CardType.Weapon } => Palette.DieRumble,
        { Type: CardType.Shield } or { Slot: EquipSlot.Body } or { Slot: EquipSlot.Head } => Palette.DieDefense,
        { Type: CardType.Tool } => Palette.DieOdHand,
        { Type: CardType.Boots } or { Slot: EquipSlot.Feet } => Palette.DieWend,
        _ => Palette.Trim,
    };

    /// <summary>An item's card in a square: its weapon or tool bonus and dice when it adds to the pool's roll.</summary>
    private StackCard ItemCard(CardInstance item, bool tapped, string bonus = "", int dice = 0) =>
        new(ItemPrefix + item.Id, item.Definition.Id, item.Definition.Name, tapped, bonus, dice, PoolColor(item.Definition), item.Id);

    private static string Plus(int value) => value > 0 ? $"+{value}" : value < 0 ? value.ToString() : "";

    private static TrayCard Tray(CardDefinition card, int dice = 0, string bonus = "") =>
        new(GameArt.Texture($"res://assets/cards/{card.Id}.png"), card.Name, dice, bonus, PoolColor(card));

    /// <summary>A pool's own card for the tray, over its dice.</summary>
    private static TrayCard PoolTray(string icon, string name, int dice, string bonus, Color color) => new(GameArt.Ui(icon), name, dice, bonus, color);

    /// <summary>The skill a hero last played from a hand, when it's still that square's front card.</summary>
    private TrayCard? PlayedTray(HandView hand, int heroId) =>
        _fronts.GetValueOrDefault((heroId, hand.Title)) == HandView.SkillKey && hand.Played is { } played
            ? new(GameArt.Texture($"res://assets/cards/{played.Picture}.png"), played.Name, 0, "", hand.Color)
            : null;

    /// <summary>The cards a hero's attack was played with: Rumble over its dice, then the skill, then the weapon over its own.</summary>
    private TrayCard[]? AttackTray(AttackRolled attack)
    {
        if (_session.State.Heroes.FirstOrDefault(hero => hero.Id == attack.AttackerId) is null || _spellCaption is not null || attack.Miasma)
        {
            return null;
        }

        CardDefinition? weapon = attack.WeaponId is { } id ? Content.FindCard(id) : null;
        int rumble = attack.Roll.Faces.Count - attack.PowerDice - attack.WeaponDice;
        List<TrayCard> cards = [PoolTray("icon_rumble", "Rumble", rumble, weapon is null ? Plus(attack.Bonus) : "", Palette.DieRumble)];
        if (PlayedTray(_rumbleHand, attack.AttackerId) is { } skill)
        {
            cards.Add(skill);
        }

        if (weapon is not null)
        {
            cards.Add(Tray(weapon, attack.WeaponDice, Plus(attack.Bonus)));
        }

        return [.. cards];
    }

    /// <summary>The cards a hero's defense was played with: the Defense card over its dice, then the shield and armor.</summary>
    private TrayCard[]? DefenseTray(DefenseRolled defense)
    {
        if (_session.State.Heroes.FirstOrDefault(hero => hero.Id == defense.DefenderId) is not { } hero)
        {
            return null;
        }

        List<TrayCard> cards = [PoolTray("icon_defense", "Defense", defense.Roll.Faces.Count - defense.PowerDice, Plus(defense.Armor), Palette.DieDefense)];
        foreach (EquipSlot slot in new[] { EquipSlot.OffHand, EquipSlot.Body, EquipSlot.Head })
        {
            if (hero.Equipment.GetValueOrDefault(slot) is { Definition: { Modifiers.Armor: > 0 } armor })
            {
                cards.Add(Tray(armor));
            }
        }

        return [.. cards];
    }

    /// <summary>The cards a hero's move was rolled with: Wend over its dice, then the boots.</summary>
    private TrayCard[]? MoveTray(MovementRolled rolled)
    {
        if (_session.State.Heroes.FirstOrDefault(hero => hero.Id == rolled.HeroId) is not { } hero)
        {
            return null;
        }

        List<TrayCard> cards = [PoolTray("icon_wend", "Wend", rolled.Roll.Faces.Count - rolled.PowerDice, Plus(rolled.Bonus), Palette.DieWend)];
        if (hero.Equipment.GetValueOrDefault(EquipSlot.Feet) is { } boots)
        {
            cards.Add(Tray(boots.Definition));
        }

        return [.. cards];
    }

    /// <summary>The cards an Od Hand check was rolled with: Od Hand over its dice, then each tool over its own.</summary>
    private TrayCard[]? OdHandTray(OdHandRolled rolled)
    {
        if (_session.State.Heroes.FirstOrDefault(hero => hero.Id == rolled.HeroId) is not { } hero)
        {
            return null;
        }

        List<TrayCard> cards = [PoolTray("icon_odhand", "Od Hand", rolled.Roll.Faces.Count - rolled.PowerDice - rolled.ToolDice, rolled.ToolDice == 0 ? Plus(rolled.Bonus) : "", Palette.DieOdHand)];
        int toolDice = rolled.ToolDice;
        foreach (CardInstance tool in hero.EquippedCards.Concat(hero.BeltTools).Where(card => card.Definition.Tool is not null).DistinctBy(card => card.Definition.Id))
        {
            if (toolDice <= 0)
            {
                break;
            }

            int dice = Math.Min(toolDice, tool.Definition.Tool!.Dice);
            cards.Add(Tray(tool.Definition, dice, Plus(tool.Definition.Tool.Bonus)));
            toolDice -= dice;
        }

        return [.. cards];
    }

    /// <summary>The card on top of each hero's stack in each pool (by hero and hand), by key; none stored means the default.</summary>
    private readonly Dictionary<(int HeroId, string Hand), string> _fronts = [];

    /// <summary>The key of the card on top of a hand's stack for the active hero: the one chosen, else a weapon or tool if there is one, else the dice.</summary>
    private string FrontOf(HandView hand, IReadOnlyList<StackCard> stack) =>
        ActiveHero is { } hero && _fronts.TryGetValue((hero.Id, hand.Title), out string? chosen) && stack.Any(card => card.Key == chosen)
            ? chosen
            : stack.FirstOrDefault(card => card.Key.StartsWith(ItemPrefix, StringComparison.Ordinal) && card.Border == hand.Color)?.Key ?? HandView.DiceKey;

    private const string ItemPrefix = "item:";

    /// <summary>The cards in a hand's stack for the hero: its dice card, the items that add to its dice, and its last skill.</summary>
    private List<StackCard> StackOf(HandView hand, Hero hero)
    {
        TurnState? turn = _session.State.Turn;
        bool mine = turn?.HeroId == hero.Id;
        var stack = new List<StackCard>();
        CardInstance? main = hero.Equipment.GetValueOrDefault(EquipSlot.MainHand);
        CardInstance? off = hero.Equipment.GetValueOrDefault(EquipSlot.OffHand);
        if (hand == _rumbleHand)
        {
            // The main hand's square: what's in it, any weapon in the off hand, and the Rumble card with its dice.
            stack.Add(new(HandView.DiceKey, "fist", "Rumble", mine && _session.AttackDiceLeft == 0, Dice: mine ? SelectedAttackDice : 0, Border: Palette.DieRumble));
            IReadOnlyList<CardInstance> weapons = GameSession.HandWeapons(hero);
            foreach (CardInstance item in new[] { main }.Concat(weapons).OfType<CardInstance>().Distinct())
            {
                bool weapon = weapons.Contains(item);
                stack.Add(ItemCard(item, weapon && mine && _session.WeaponBonusLeft(item.Id) == 0 && _session.WeaponDiceLeft(item.Id) == 0,
                    weapon ? Plus(_session.WeaponBonusLeft(item.Id)) : "", weapon && mine ? _session.WeaponDiceLeft(item.Id) : 0));
            }
        }
        else if (hand == _wendHand)
        {
            // The feet's square: the boots, and the Wend card with its dice and the Wend bonus.
            stack.Add(new(HandView.DiceKey, "wend", "Wend", mine && _session.MoveDiceLeft == 0 && turn!.MovementPoints == 0, Plus(_session.SpeedBonusLeft), mine ? SelectedMoveDice : 0, Palette.DieWend));
            if (hero.Equipment.GetValueOrDefault(EquipSlot.Feet) is { } boots)
            {
                stack.Add(ItemCard(boots, tapped: false));
            }
        }
        else if (hand == _odHand)
        {
            // The off hand's square: what's in it (unless it's a weapon, which goes with Rumble), the tools, and the Od Hand card with its dice.
            stack.Add(new(HandView.DiceKey, "search", "Od Hand", mine && _session.OdHandDiceLeft == 0, Dice: mine ? SelectedOdHandDice : 0, Border: Palette.DieOdHand));
            List<CardInstance> tools = [.. hero.EquippedCards.Concat(hero.BeltTools).Where(card => card.Definition.Tool is not null).DistinctBy(card => card.Definition.Id)];
            if (off is not null && off != main && !GameSession.HandWeapons(hero).Contains(off) && !tools.Contains(off))
            {
                stack.Add(ItemCard(off, tapped: false));
            }

            stack.AddRange(tools.Select(tool => ItemCard(tool, mine && turn!.ToolsUsed, Plus(tool.Definition.Tool!.Bonus), mine && !turn!.ToolsUsed ? tool.Definition.Tool.Dice : 0)));
        }

        if (hand.Played is { Held: true } played)
        {
            stack.Add(new(HandView.SkillKey, played.Picture, played.Name, played.Action.Disabled && !played.IsLit && mine && !CanStill(played), Border: hand.Color));
        }

        return stack;
    }

    /// <summary>A skill that's only disabled for the moment (a roll waiting), not used up.</summary>
    private bool CanStill(AbilityCard card) => _session.State.Turn is { PendingRoll: not null } or { UnsettledPowerDice: > 0 } || _busy;

    /// <summary>
    /// What the top of each stack selects for the active hero: on the Rumble stack, a weapon on top swings
    /// with it (readied, if there are two), and the dice card or a skill on top goes without one; on the
    /// Od Hand stack, the dice card alone on top keeps the tools out of the roll.
    /// </summary>
    private void ApplyStackFronts()
    {
        if (ActiveHero is not { } hero || _session.State.Turn is null)
        {
            return;
        }

        string rumble = FrontOf(_rumbleHand, StackOf(_rumbleHand, hero));
        _weaponHeld = !rumble.StartsWith(ItemPrefix, StringComparison.Ordinal);

        // A weapon put on top by choice is the one readied; left to the default, the rules pick.
        if (!_weaponHeld && _fronts.ContainsKey((hero.Id, _rumbleHand.Title)) && int.TryParse(rumble[ItemPrefix.Length..], out int weaponId) && GameSession.HandWeapons(hero).Count > 1)
        {
            _readied[hero.Id] = weaponId;
        }

        _toolsHeld = FrontOf(_odHand, StackOf(_odHand, hero)) == HandView.DiceKey;
    }

    /// <summary>The top card of a stack was double-clicked: its action, with the selected dice.</summary>
    private void UseStackCard(HandView hand, StackCard card)
    {
        if (Locked)
        {
            return;
        }

        if (card.Tapped)
        {
            Log($"{card.Name}: used up for this turn.", Palette.TextDim);
            return;
        }

        if (card.Key == HandView.SkillKey && hand.Played is { } played)
        {
            PlayAbility(played);
            return;
        }

        if (hand == _odHand && card.Key.StartsWith(ItemPrefix, StringComparison.Ordinal) && ActiveHero?.FindCard(int.Parse(card.Key[ItemPrefix.Length..])) is { Card.Definition.Tool: { } tool })
        {
            // A tool goes to work on what it's for.
            Button action = tool.For.HasFlag(Skill.PickLock) ? _pickLock : tool.For.HasFlag(Skill.Steal) ? _steal : _search;
            PlayAbility(new AbilityCard(card.Picture, card.Name, action, () => true));
            return;
        }

        PlayAbility(hand.Default!);
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
            if (hand.Default is not null)
            {
                string? played = _playedCards.GetValueOrDefault((hero?.Id ?? 0, hand.Title));
                hand.Played = played is null ? null : hand.Cards.FirstOrDefault(card => card.Name == played);
                List<StackCard> stack = hero is null ? [] : StackOf(hand, hero);
                bool mine = hero is not null && _session.State.Turn?.HeroId == hero.Id;
                int pool = !mine ? 0 : hand == _rumbleHand ? _session.AttackDiceLeft - SelectedAttackDice
                    : hand == _wendHand ? _session.MoveDiceLeft - SelectedMoveDice
                    : _session.OdHandDiceLeft - SelectedOdHandDice;
                TurnState? turnNow = _session.State.Turn;
                string note = hand == _wendHand && turnNow?.MovementRoll is not null ? UiTheme.Count(turnNow.MovementPoints, "step", "steps") + " left" : "";
                hand.ShowStack(stack, FrontOf(hand, stack), pool, ready && mine, note);
            }

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
