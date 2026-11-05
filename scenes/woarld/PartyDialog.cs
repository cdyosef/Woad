using Godot;
using Woad.Core.Characters;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Rules;
using Woad.Core.Saves;
using Woad.Core.Tavern;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Woarld;

/// <summary>
/// The Party menu, out of a dungeon (on the Woarld Map and in the inn): one page with a column for
/// everyone in the party and one for the Party Chest. Each member's column is their card at a glance
/// (see <see cref="PartyMemberCard"/>; click it for their sheet), then their purse, what they wear, what's
/// on their belt and in their pack. Click any card, purse, or the chest's coins, and what can be done
/// with it comes up along the bottom: wear it, take it off, belt it, pack it, put it in the chest, give it
/// to someone, study a scroll; coins go to the chest or to someone, a gold or all of it. Opened on a shop
/// (see <see cref="ShopFront"/>), its wares come first: pick one, then who buys it (anyone, or the chest);
/// cards picked can be sold there, and worn gear mended.
/// </summary>
public partial class PartyDialog : Control
{
    private const float Column = PartyMemberCard.Width;

    private Label _title = null!;
    private ScrollContainer _scroll = null!;
    private HBoxContainer _columns = null!;
    private Label _giving = null!;
    private HFlowContainer _givers = null!;

    /// <summary>The card picked: whose (null: the chest's), its key, and where it is.</summary>
    private (Character? From, string Key, CardPlace Place)? _selected;

    /// <summary>The purse picked (a member's, or the chest's), when it's coins being moved.</summary>
    private IPurse? _purse;

    /// <summary>The shop the menu's open on, if any, and the ware picked there.</summary>
    private ShopFront? _shop;
    private string? _ware;

    /// <summary>A character's sheet, open over the menu, and whose it is.</summary>
    private Control _sheetView = null!;
    private Game.HeroPanel _sheet = null!;
    private Character? _sheetOf;
    private IReadOnlyDictionary<int, string> _sheetKeys = new Dictionary<int, string>();

    public IReadOnlyList<Character> Party { get; set; } = [];

    public ContentCatalog Content { get; init; } = null!;

    /// <summary>The Raiding Party's chest; null where there's none (a Dungeon Run's map).</summary>
    public PartyChest? Chest { get; init; }

    /// <summary>Hearts now and at most, where the raid keeps track; null shows full hearts.</summary>
    public Func<Character, (int Now, int Max)>? Hearts { get; init; }

    /// <summary>A hero loses a heart (a scroll went off among the party), where the raid keeps track.</summary>
    public Action<Character>? Wound { get; init; }

    /// <summary>The dice for studying scrolls.</summary>
    public IRandomSource Random { get; init; } = new Pcg32((ulong)DateTime.UtcNow.Ticks, sequence: 11);

    /// <summary>What came of the last thing done, shown until something else is picked.</summary>
    private string? _news;

    /// <summary>Something about the party changed (a card moved, coins went in the chest, a switch flipped): save them, and the raid.</summary>
    public event Action<IReadOnlyList<Character>>? Changed;

    /// <summary>A character with points to spend: open their points.</summary>
    public event Action<Character>? LevelUpRequested;

    /// <summary>A button by name, scrolled into view if it's on the page (for the click tests).</summary>
    internal Button? Find(string name)
    {
        var button = FindChild(name, recursive: true, owned: false) as Button;
        if (button is not null && _columns.IsAncestorOf(button))
        {
            _scroll.EnsureControlVisible(button);
        }

        return button;
    }

    /// <summary>The line under the party: what to do with what's picked, or what came of the last thing done.</summary>
    internal string Note => _giving.Text;

    public override void _Ready()
    {
        Size = new Vector2(MenuScreen.Width, MenuScreen.Height);
        Visible = false;
        AddChild(new ColorRect { Color = new Color(Palette.Background, 0.85f), Size = Size });
        var panel = new Panel { Position = new Vector2(20, 8), Size = new Vector2(600, 344) };
        AddChild(panel);
        _title = UiTheme.Heading(UiTheme.Label(new Vector2(10, 6), new Vector2(500, 18), "THE PARTY", 16, Palette.Trim), 2);
        panel.AddChild(_title);
        Button close = UiTheme.Button(new Vector2(540, 6), new Vector2(52, 18), "Close");
        close.Name = "Close";
        close.Pressed += Hide;
        panel.AddChild(close);

        // Everyone's column, then the chest's: the page scrolls down, and sideways when the party's big.
        _scroll = new ScrollContainer { Position = new Vector2(8, 28), Size = new Vector2(584, 246) };
        panel.AddChild(_scroll);
        _columns = new HBoxContainer();
        _columns.AddThemeConstantOverride("separation", 8);
        _scroll.AddChild(_columns);

        _giving = UiTheme.Label(new Vector2(10, 278), new Vector2(580, 14), "", color: Palette.TextDim);
        panel.AddChild(_giving);
        _givers = new HFlowContainer { Position = new Vector2(8, 294), Size = new Vector2(584, 44) };
        _givers.AddThemeConstantOverride("h_separation", 4);
        _givers.AddThemeConstantOverride("v_separation", 3);
        panel.AddChild(_givers);

        // A character's sheet, over the menu: click a card on it to pick it; click outside to close it.
        _sheetView = new Control { Size = Size, Visible = false, Name = "SheetView" };
        var dim = new ColorRect { Size = Size, Color = new Color(Palette.Background, 0.8f) };
        dim.GuiInput += input =>
        {
            if (input is InputEventMouseButton { Pressed: true })
            {
                CloseSheet();
            }
        };
        _sheetView.AddChild(dim);
        _sheet = new Game.HeroPanel { Position = new Vector2((Size.X - 176) / 2, 0), Name = "Sheet" };
        _sheet.CardSelected += cardId =>
        {
            if (_sheetOf is { } owner && _sheetKeys.TryGetValue(cardId, out string? key) && PlaceOf(owner, key) is { } place)
            {
                CloseSheet();
                Pick((owner, key, place), null, null);
            }
        };
        _sheetView.AddChild(_sheet);
        _sheetView.AddChild(_sheet.BagView);
        AddChild(_sheetView);
    }

    /// <summary>Whose sheet is open, if any (for the click tests).</summary>
    internal Character? SheetOf => _sheetView.Visible ? _sheetOf : null;

    /// <summary>Opens a character's sheet over the menu.</summary>
    internal void OpenSheet(Character character)
    {
        (int now, _) = Hearts?.Invoke(character) ?? (character.MaxHearts(Content), 0);
        Hero preview = Hero.Preview(character, Content, now, out _sheetKeys);
        _sheetOf = character;
        _sheet.CloseBag();
        _sheet.ShowHero(preview, null, null);
        _sheet.ShowMagic(preview.EffectiveStats.Magic > 0 || preview.Spells.Count > 0, false, $"{character.Name} knows: {string.Join(", ", preview.Spells.Select(spell => spell.Name))}");
        _sheetView.Visible = true;
    }

    private void CloseSheet()
    {
        _sheet.CloseBag();
        _sheetView.Visible = false;
        _sheetOf = null;
    }

    /// <summary>Opens the menu on the party as it is now.</summary>
    public void Open() => Open(null);

    /// <summary>Opens the menu on a shop: its wares beside the party's things.</summary>
    public void OpenShop(ShopFront shop) => Open(shop);

    /// <summary>The shop the menu's open on (for the click tests).</summary>
    internal ShopFront? Shop => Visible ? _shop : null;

    private void Open(ShopFront? shop)
    {
        _shop = shop;
        _title.Text = shop is null ? "THE PARTY" : shop.Name.ToUpperInvariant();
        _selected = null;
        _purse = null;
        _ware = null;
        _news = null;
        _scroll.ScrollHorizontal = 0;
        _scroll.ScrollVertical = 0;
        Refresh();
        Show();
    }

    /// <summary>Picks a card, a purse or a ware (or, picked again, puts it down).</summary>
    private void Pick((Character? From, string Key, CardPlace Place)? card, IPurse? purse, string? ware)
    {
        bool again = (card is not null && _selected == card) || (purse is not null && _purse == purse) || (ware is not null && _ware == ware);
        _selected = again ? null : card;
        _purse = again ? null : purse;
        _ware = again ? null : ware;
        _news = null;
        Refresh();
    }

    /// <summary>The chest as a shopper: it pays, takes what's bought, and bargains with the best standing in the party.</summary>
    private Shopper ChestShopper(PartyChest chest) =>
        new(chest, Party.Count > 0 ? Party.Max(member => member.GuildStanding) : 0, key => chest.Cards.Add(key));

    private void Refresh()
    {
        Clear(_columns);
        if (_shop is not null)
        {
            _columns.AddChild(ShopColumn(_shop));
        }

        for (int i = 0; i < Party.Count; i++)
        {
            _columns.AddChild(MemberColumn(Party[i], i));
        }

        if (Chest is not null)
        {
            _columns.AddChild(ChestColumn(Chest));
        }

        Actions();
    }

    /// <summary>A member's column: their card, purse, worn gear, belt, pack, arms, and a companion's switches.</summary>
    private VBoxContainer MemberColumn(Character character, int index)
    {
        VBoxContainer column = NewColumn($"Column{index}");
        var card = new PartyMemberCard { Character = character, Content = Content, Hearts = HeartsOf(character), Name = $"Sheet{index}" };
        card.TooltipText = $"{character.Name}{(character.IsCompanion ? " (a companion)" : "")}: click for the character sheet.";
        card.Pressed += () => OpenSheet(character);
        column.AddChild(card);
        if (character.UnspentPoints > 0)
        {
            Button level = Wide(column, $"Level up ({character.UnspentPoints})", $"LevelUp{index}");
            UiTheme.Accent(level, Palette.Power);
            level.Pressed += () =>
            {
                Hide();
                LevelUpRequested?.Invoke(character);
            };
        }

        Button purse = Wide(column, $"Purse: {Money.Short(character.Coins)}", $"Purse{index}");
        UiTheme.Accent(purse, Palette.Loot);
        purse.ToggleMode = true;
        purse.SetPressedNoSignal(_purse == character);
        purse.TooltipText = $"{character.Name}'s coins: pick them to put in the chest or hand to someone.";
        purse.Pressed += () => Pick(null, character, null);

        Section(column, "Worn", character, CardPlace.Worn, index);
        int capacity = character.BeltCapacity(Content);
        Section(column, capacity > 0 ? $"Belt {character.BeltUsed(Content)}/{capacity}" : "Belt (none)", character, CardPlace.Belt, index);
        Section(column, $"Pack {character.Backpack.Count}/{GameRules.BackpackCapacity}", character, CardPlace.Pack, index);
        if (character.Carried.Count > 0)
        {
            Section(column, "In their arms", character, CardPlace.Carried, index);
        }

        if (character.IsCompanion)
        {
            CompanionPolicy policy = character.Policy;
            (string Label, bool On, Func<CompanionPolicy, CompanionPolicy> Flip)[] switches =
            [
                ("Drinks potions", policy.DrinksPotions, p => p with { DrinksPotions = !p.DrinksPotions }),
                ("Picks up loot", policy.PicksUpLoot, p => p with { PicksUpLoot = !p.PicksUpLoot }),
                ("Wears better gear", policy.EquipsBetterGear, p => p with { EquipsBetterGear = !p.EquipsBetterGear }),
            ];
            Heading(column, "Companion");
            for (int i = 0; i < switches.Length; i++)
            {
                (string label, bool on, Func<CompanionPolicy, CompanionPolicy> flip) = switches[i];
                Button toggle = Wide(column, label, $"Policy{index}_{i}");
                toggle.ToggleMode = true;
                toggle.SetPressedNoSignal(on);
                toggle.Pressed += () =>
                {
                    character.SetPolicy(flip(character.Policy));
                    Changed?.Invoke(Party);
                    Refresh();
                };
            }
        }

        return column;
    }

    /// <summary>A heading and the cards in one of a member's places, a button each (copies of a stack as one).</summary>
    private void Section(VBoxContainer column, string heading, Character character, CardPlace place, int index)
    {
        Heading(column, heading);
        List<IGrouping<string, string>> cards = [.. character.In(place).GroupBy(key => key)];
        if (cards.Count == 0)
        {
            column.AddChild(UiTheme.Label(Vector2.Zero, new Vector2(Column, 12), "  nothing", color: Palette.TextDim));
            return;
        }

        foreach (IGrouping<string, string> stack in cards)
        {
            string key = stack.Key;
            (Character?, string, CardPlace) mine = (character, key, place);
            Button card = Wide(column, Label(key, stack.Count()), $"Card{index}_{CardKey.Id(key)}");
            card.TooltipText = $"{Content.GetCard(key).Name}: {Content.GetCard(key).Text}";
            card.ToggleMode = true;
            card.SetPressedNoSignal(_selected == mine);
            card.Pressed += () => Pick(mine, null, null);
        }
    }

    /// <summary>The shop's column: its name, and a button for each of its wares with what it asks.</summary>
    private VBoxContainer ShopColumn(ShopFront shop)
    {
        VBoxContainer column = NewColumn("ShopColumn");
        var header = new Panel { CustomMinimumSize = new Vector2(Column, PartyMemberCard.Height) };
        var name = UiTheme.Label(new Vector2(4, 4), new Vector2(Column - 8, 28), shop.Name.ToUpperInvariant(), color: Palette.TrimBright);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        header.AddChild(name);
        var note = UiTheme.Label(new Vector2(4, 34), new Vector2(Column - 8, 60), shop.Note, color: Palette.TextDim);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        header.AddChild(note);
        column.AddChild(header);

        Heading(column, "For sale");
        IReadOnlyList<string> wares = shop.Wares();
        if (wares.Count == 0)
        {
            column.AddChild(UiTheme.Label(Vector2.Zero, new Vector2(Column, 12), "  sold out", color: Palette.TextDim));
        }

        Shopper standard = Party.Count > 0 ? Shopper.Of(Party[0], Content) : new Shopper(new PartyChest(), 0, _ => { });
        foreach (IGrouping<string, string> ware in wares.GroupBy(id => id).OrderBy(group => Content.GetCard(group.Key).Name))
        {
            string id = ware.Key;
            Button button = Wide(column, $"{Content.GetCard(id).Name}{(ware.Count() > 1 ? $" x{ware.Count()}" : "")} {Money.Short(shop.Price(id, standard))}", $"Ware_{id}");
            UiTheme.Accent(button, Palette.Trim);
            button.TooltipText = $"{Content.GetCard(id).Name}: {Content.GetCard(id).Text}";
            button.ToggleMode = true;
            button.SetPressedNoSignal(_ware == id);
            button.Pressed += () => Pick(null, null, id);
        }

        return column;
    }

    /// <summary>The chest's column: its coins and its cards.</summary>
    private VBoxContainer ChestColumn(PartyChest chest)
    {
        VBoxContainer column = NewColumn("ChestColumn");
        var header = new Panel { CustomMinimumSize = new Vector2(Column, PartyMemberCard.Height) };
        header.AddChild(UiTheme.Label(new Vector2(4, 4), new Vector2(Column - 8, 14), "PARTY CHEST", color: Palette.Loot));
        if (GameArt.Ui("icon_gold") is { } coin)
        {
            header.AddChild(new TextureRect { Texture = coin, Position = new Vector2((Column - 24) / 2, 26), Size = new Vector2(24, 24), StretchMode = TextureRect.StretchModeEnum.Scale });
        }

        header.AddChild(UiTheme.Label(new Vector2(4, 58), new Vector2(Column - 8, 36),
            $"{chest.Cards.Count} {(chest.Cards.Count == 1 ? "card" : "cards")}\nShared by all; it can pay.", color: Palette.TextDim));
        header.TooltipText = "What the whole party shares: cards and coins. It can pay for things, and stays out of dungeons.";
        column.AddChild(header);

        Button coins = Wide(column, $"Coins: {Money.Short(chest.Coins)}", "ChestCoins");
        UiTheme.Accent(coins, Palette.Loot);
        coins.ToggleMode = true;
        coins.SetPressedNoSignal(_purse == chest);
        coins.TooltipText = "The chest's coins: pick them to hand out.";
        coins.Pressed += () => Pick(null, chest, null);

        Heading(column, "Cards");
        if (chest.Cards.Count == 0)
        {
            column.AddChild(UiTheme.Label(Vector2.Zero, new Vector2(Column, 24), "  empty", color: Palette.TextDim));
        }

        foreach (IGrouping<string, string> stack in chest.Cards.GroupBy(key => key).OrderBy(group => Content.GetCard(group.Key).Name))
        {
            string key = stack.Key;
            (Character?, string, CardPlace) mine = (null, key, CardPlace.Pack);
            Button card = Wide(column, Label(key, stack.Count()), $"Chest_{CardKey.Id(key)}");
            card.TooltipText = $"{Content.GetCard(key).Name}: {Content.GetCard(key).Text}";
            card.ToggleMode = true;
            card.SetPressedNoSignal(_selected == mine);
            card.Pressed += () => Pick(mine, null, null);
        }

        return column;
    }

    /// <summary>The line and buttons along the bottom: what can be done with what's picked.</summary>
    private void Actions()
    {
        Clear(_givers);
        if (_purse is { } purse)
        {
            CoinActions(purse);
            return;
        }

        if (_ware is { } ware && _shop is { } shop)
        {
            BuyActions(shop, ware);
            return;
        }

        if (_selected is not { } selected)
        {
            _giving.Text = _news ?? (_shop is not null
                ? "Click a ware to buy it, or anyone's card to sell it. Click a purse to move coins, someone's picture for their sheet."
                : "Click a card, a purse or the chest's coins for what to do with it. Click someone's picture for their sheet.");
            return;
        }

        string name = Content.GetCard(selected.Key).Name;
        if (selected.From is not { } owner)
        {
            // The chest's card: out to whoever wants it.
            _giving.Text = _news ?? $"Take the {name} out of the chest, to:";
            foreach (Character member in Party)
            {
                AddAction(member.Name, $"TakeOutTo{member.Name}", null, () => Done(Chest!.Take(member, selected.Key, Content), $"{member.Name} takes the {name} out of the chest."));
            }

            ShopActions(Chest!, selected.Key, () => Chest!.Cards.Remove(selected.Key), null);
            return;
        }

        string where = selected.Place switch { CardPlace.Worn => "worn", CardPlace.Belt => "on the belt", CardPlace.Carried => "in their arms", _ => "in the pack" };
        _giving.Text = _news ?? $"{owner.Name}'s {name} ({where}):";
        if (selected.Place != CardPlace.Worn)
        {
            AddAction("Wear", "Wear", owner.CheckMove(selected.Key, selected.Place, CardPlace.Worn, Content), () => Move(owner, selected, CardPlace.Worn, $"{owner.Name} puts on the {name}."));
        }

        if (selected.Place != CardPlace.Belt)
        {
            AddAction("Belt", "ToBelt", owner.CheckMove(selected.Key, selected.Place, CardPlace.Belt, Content), () => Move(owner, selected, CardPlace.Belt, $"The {name} goes on {owner.Name}'s belt."));
        }

        if (selected.Place != CardPlace.Pack)
        {
            AddAction(selected.Place == CardPlace.Worn ? "Take off" : "Pack", "ToPack", owner.CheckMove(selected.Key, selected.Place, CardPlace.Pack, Content),
                () => Move(owner, selected, CardPlace.Pack, $"The {name} goes in {owner.Name}'s pack."));
        }

        if (Chest is not null)
        {
            AddAction("To the chest", "ToChest", null, () => Done(Chest.Put(owner, selected.Key, selected.Place, Content), $"{owner.Name} puts the {name} in the Party Chest."), Palette.Loot);
        }

        // A scroll with a spell on it can be studied, here and now: Od Hand and Magic, or it goes off.
        if (Content.GetCard(selected.Key).Teaches is { } spellId && ScrollStudy.Check(owner, selected.Key, Content) is null)
        {
            SpellDefinition spell = Content.GetSpell(spellId);
            Button learn = AddAction("Learn", "Learn", null, () => Study(owner, selected.Key), Palette.Power);
            learn.TooltipText = $"{owner.Name} studies it to learn {spell.Name}: Od Hand plus Magic, {GameSession.LearnDifficulty(spell)} or more. The scroll is used up either way; fail and it goes off among the party!";
        }

        foreach (Character other in Party.Where(other => other != owner))
        {
            AddAction($"To {other.Name}", $"GiveTo{other.Name}", null, () => Give(owner, selected, other));
        }

        ShopActions(owner, selected.Key, () => owner.TakeFrom(selected.Place, selected.Key, Content) is not null,
            selected.Place == CardPlace.Worn && Content.GetCard(selected.Key).Slot == EquipSlot.Belt ? "Take the belt off first (it comes off with all it holds)."
            : CardKey.Unpack(selected.Key) is not null ? "Unpack the belt first: wear it, then take its cards off." : null, owner);
    }

    /// <summary>In a shop, what can be done with a card picked: sell it (to the purse of whoever has it), or have it mended.</summary>
    private void ShopActions(IPurse seller, string key, Func<bool> take, string? cannotSell, Character? owner = null)
    {
        if (_shop is not { } shop)
        {
            return;
        }

        string name = Content.GetCard(key).Name;
        if (shop.Sell is { } sell && shop.Pays?.Invoke(key) is { } pays)
        {
            AddAction($"Sell {Money.Short(pays)}", "Sell", cannotSell, () => Traded(sell(key, seller, take)), Palette.Loot);
        }

        if (owner is not null && shop.Mend is { } mend && shop.MendPrice is { } mendPrice && CardKey.Wear(key) > 0)
        {
            int price = mendPrice(key);
            AddAction($"Mend {Money.Short(price)}", "Mend", owner.Coins < price ? $"{owner.Name} has {Money.Long(owner.Coins)}." : null, () => Traded(mend(owner, key, owner)));
            if (Chest is not null)
            {
                AddAction("Mend, chest pays", "MendFromChest", Chest.Coins < price ? $"The chest has {Money.Long(Chest.Coins)}." : null, () => Traded(mend(owner, key, Chest)), Palette.Loot);
            }
        }
    }

    /// <summary>A ware picked: who buys it, each at what the shop asks them.</summary>
    private void BuyActions(ShopFront shop, string ware)
    {
        string name = Content.GetCard(ware).Name;
        _giving.Text = _news ?? $"Buy the {name}: who pays (and takes it)?";
        foreach (Character member in Party)
        {
            Shopper shopper = Shopper.Of(member, Content);
            int price = shop.Price(ware, shopper);
            AddAction($"{member.Name} {Money.Short(price)}", $"BuyFor{member.Name}", member.Coins < price ? $"{member.Name} has {Money.Long(member.Coins)}." : null,
                () => Traded(shop.Buy(ware, shopper)));
        }

        if (Chest is not null)
        {
            Shopper shopper = ChestShopper(Chest);
            int price = shop.Price(ware, shopper);
            AddAction($"Chest {Money.Short(price)}", "BuyForChest", Chest.Coins < price ? $"The chest has {Money.Long(Chest.Coins)}." : null,
                () => Traded(shop.Buy(ware, shopper)), Palette.Loot);
        }
    }

    /// <summary>After a trade: what the shop said, everything put down, and the party (and the shop) saved.</summary>
    private void Traded(ServiceResult result)
    {
        _news = result.Message;
        _selected = null;
        _ware = null;
        if (result.Done)
        {
            Changed?.Invoke(Party);
        }

        Refresh();
    }

    /// <summary>Coins picked: a gold, or all of it, to the chest or to someone.</summary>
    private void CoinActions(IPurse from)
    {
        _giving.Text = _news ?? $"{Capital(from.Name)}'s coins ({Money.Long(from.Coins)}): a gold or all of it, to:";
        List<IPurse> targets = [.. Party.Where(member => member != from)];
        if (Chest is not null && Chest != from)
        {
            targets.Insert(0, Chest);
        }

        foreach (IPurse to in targets)
        {
            string who = to == Chest ? "the chest" : to.Name;
            string tag = to == Chest ? "Chest" : to.Name;
            AddAction($"1g to {who}", $"Gold{tag}", from.Coins < Money.Gold(1) ? "Not a whole gold." : null, () => Pay(from, to, Money.Gold(1)), to == Chest ? Palette.Loot : null);
            AddAction($"All to {who}", $"All{tag}", from.Coins < 1 ? "No coin." : null, () => Pay(from, to, from.Coins), to == Chest ? Palette.Loot : null);
        }
    }

    private void Pay(IPurse from, IPurse to, int amount)
    {
        string? error = PartyChest.Transfer(from, to, amount);
        _purse = null;
        _news = error ?? $"{Capital(from.Name)} hands {Money.Long(amount)} to {to.Name}.";
        if (error is null)
        {
            Changed?.Invoke(Party);
        }

        Refresh();
    }

    private Button AddAction(string text, string name, string? cannot, Action pressed, Color? accent = null)
    {
        Button button = UiTheme.CompactButton(Vector2.Zero, new Vector2(Math.Max(44, text.Length * 5 + 12), 16), text);
        button.CustomMinimumSize = button.Size;
        if (accent is { } color)
        {
            UiTheme.Accent(button, color);
        }

        button.Name = name;
        button.Disabled = cannot is not null;
        button.TooltipText = cannot ?? "";
        button.Pressed += pressed;
        _givers.AddChild(button);
        return button;
    }

    private static VBoxContainer NewColumn(string name)
    {
        var column = new VBoxContainer { Name = name, CustomMinimumSize = new Vector2(Column, 0) };
        column.AddThemeConstantOverride("separation", 2);
        return column;
    }

    private static void Heading(VBoxContainer column, string text) =>
        column.AddChild(UiTheme.Label(Vector2.Zero, new Vector2(Column, 12), text.ToUpperInvariant(), color: Palette.Trim));

    /// <summary>A button as wide as the column.</summary>
    private static Button Wide(VBoxContainer column, string text, string name)
    {
        Button button = UiTheme.CompactButton(Vector2.Zero, new Vector2(Column, 15), text);
        button.CustomMinimumSize = new Vector2(Column, 15);
        button.Name = name;
        button.ClipText = true;
        button.Alignment = HorizontalAlignment.Center;
        column.AddChild(button);
        return button;
    }

    private string Label(string key, int count) =>
        Content.GetCard(key).Name + (CardKey.Wear(key) is > 0 and var wear ? $" -{wear}" : "") + (CardKey.Unpack(key) is not null ? " (packed)" : "") + (count > 1 ? $" x{count}" : "");

    private void Move(Character owner, (Character? From, string Key, CardPlace Place) selected, CardPlace to, string done) =>
        Done(owner.Move(selected.Key, selected.Place, to, Content), done);

    private void Give(Character from, (Character? From, string Key, CardPlace Place) selected, Character to)
    {
        string name = Content.GetCard(selected.Key).Name;
        if (from.TakeFrom(selected.Place, selected.Key, Content) is { } taken)
        {
            to.GiveCard(taken, Content);
            Done(null, $"{from.Name} gives {to.Name} the {name}.");
        }
    }

    /// <summary>After something's done (or refused): the news, the card put down, and the party saved.</summary>
    private void Done(string? error, string done)
    {
        _news = error ?? done;
        _selected = null;
        if (error is null)
        {
            Changed?.Invoke(Party);
        }

        Refresh();
    }

    private void Study(Character learner, string cardId)
    {
        ScrollStudy.Result result = ScrollStudy.Attempt(learner, cardId, Party, Content, Random);
        foreach (Character hurt in result.Wounded)
        {
            Wound?.Invoke(hurt);
        }

        Done(null, result.Message);
    }

    private (int Now, int Max) HeartsOf(Character character) =>
        Hearts?.Invoke(character) ?? (character.MaxHearts(Content), character.MaxHearts(Content));

    private static CardPlace? PlaceOf(Character character, string key) =>
        new[] { CardPlace.Worn, CardPlace.Belt, CardPlace.Pack, CardPlace.Carried }.Cast<CardPlace?>().FirstOrDefault(place => character.In(place!.Value).Contains(key));

    private static string Capital(string name) => name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : name;

    private static void Clear(Node parent)
    {
        foreach (Node old in parent.GetChildren())
        {
            parent.RemoveChild(old);
            old.QueueFree();
        }
    }
}
