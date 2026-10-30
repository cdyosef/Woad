using Woad.Core.Commands;
using Woad.Core.Content;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

public sealed partial class GameSession
{
    /// <summary>
    /// Tiles the active hero could target with the card right now: the hero's own tile for
    /// self effects, empty if the card can't be used at the moment.
    /// </summary>
    public IReadOnlyList<GridPosition> GetCardTargets(int cardId)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 }
            || ActiveHero.FindCard(cardId) is not { } held
            || CheckCardUsable(held.Card, held.Location) is not null)
        {
            return [];
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        CardEffect effect = held.Card.Definition.Effect!;
        if (effect.Kind is CardEffectKind.Heal or CardEffectKind.CowardsFlight or CardEffectKind.Cure or CardEffectKind.Repair)
        {
            return CheckEffectTarget(hero, room, effect, null, out _) is null ? [hero.Position] : [];
        }

        return room.Positions
            .Where(position => CheckEffectTarget(hero, room, effect, position, out _, IsBomb(held.Card.Definition)) is null)
            .ToList();
    }

    private string? Handle(UseCard command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (hero.FindCard(command.CardId) is not var (card, location))
        {
            return "You don't have that card.";
        }

        if (CheckCardUsable(card, location) is { } error)
        {
            return error;
        }

        CardEffect effect = card.Definition.Effect!;
        if (CheckEffectTarget(hero, room, effect, command.Target, out IReadOnlyList<Monster> targets, IsBomb(card.Definition)) is { } targetError)
        {
            return targetError;
        }

        Reveal(hero, events);
        if (location == CardLocation.Belt)
        {
            Turn.UsedBeltItem = true;
        }
        else
        {
            SpendAction();
        }

        events.Add(new CardUsed(hero.Id, card.Id, command.Target));
        if (card.Definition.Usage == CardUsage.SingleUse && card.Count > 1)
        {
            // One off the stack (a found copy first).
            card.Count--;
            card.BroughtInCount = Math.Min(card.BroughtInCount, card.Count);
            events.Add(new CardConsumed(hero.Id, card.Id, card.Count));
        }
        else if (card.Definition.Usage == CardUsage.SingleUse)
        {
            hero.Remove(card);
            events.Add(new CardConsumed(hero.Id, card.Id));
        }
        else if (card.Definition.Usage == CardUsage.Charges)
        {
            card.ChargesRemaining--;
        }
        else if (card.Definition.BreaksOnUse)
        {
            hero.Remove(card);
            events.Add(new CardConsumed(hero.Id, card.Id));
        }

        switch (effect.Kind)
        {
            case CardEffectKind.Heal:
                int healed = Math.Min(effect.Amount, hero.HeartsToHeal);
                hero.Hearts += healed;
                events.Add(new HeroHealed(hero.Id, healed, hero.Hearts));
                break;

            case CardEffectKind.Cure:
                Cure(hero, effect, events);
                break;

            case CardEffectKind.Holy when targets.Count > 0:
                Sanctify(hero, room, effect, targets[0], events);
                break;

            case CardEffectKind.Holy:
                Cure(hero, effect, events);
                break;

            case CardEffectKind.SinEater:
                BurstDoll(hero, room, card, effect, command.Target!.Value, events);
                break;

            case CardEffectKind.Repair when MostWorn(hero) is { } worn:
                worn.Wear = 0;
                events.Add(new GearMended(hero.Id, worn.Id));
                break;

            case CardEffectKind.Attack when IsBomb(card.Definition):
                // A thrown bomb lands lit, and goes off at the end of the thrower's next turn.
                PlaceBomb(room, command.Target!.Value, lit: true, hero.Id, events);
                break;

            case CardEffectKind.Attack:
                // Fire touching a bomb sets it off at once.
                if (effect.Fire && room.BombAt(command.Target!.Value) is { } kindling)
                {
                    Explode(room, kindling, events, setOffBy: hero);
                    targets = [.. targets.Where(monster => room.FindMonster(monster.Id) is not null)];
                }

                // With friendly fire on, the blast catches heroes too, the thrower included.
                List<int> caught = [.. targets.Select(monster => monster.Id), .. AlliesCaught(room, command.Target!.Value, effect.AreaRadius).Select(ally => ally.Id)];
                if (caught.Count > 0)
                {
                    RollHeroAttack(hero, effect.DieType is null ? effect.Amount : 0, 0, caught, SourceOf(card.Definition), events,
                        typed: effect.DieType is { } type ? [(type, effect.Amount)] : null, chill: effect.Chill);
                }
                else if (effect.Fire && UnlitBrazierAt(room, command.Target!.Value))
                {
                    Kindle(room, command.Target!.Value, events);
                }
                else if (effect.Fire)
                {
                    // Fire aimed at bare floor sets it burning.
                    SetFloorAlight(room, [.. room.Positions.Where(tile => tile.ChebyshevDistanceTo(command.Target!.Value) <= effect.AreaRadius)], events, hero);
                }

                break;

            case CardEffectKind.CowardsFlight:
                // Everything left for fighting and fiddling goes into running; whatever's beside you slows.
                int fleeing = AttackDiceLeft + OdHandDiceLeft;
                Turn.ExtraMoveDice += fleeing;
                Turn.AttackDiceUsed += AttackDiceLeft;
                Turn.OdHandDiceUsed += OdHandDiceLeft;
                List<int> slowed = [.. room.Monsters.Where(monster => monster.Tiles.Any(tile => tile.ChebyshevDistanceTo(hero.Position) <= 1)).Select(monster => monster.Id)];
                foreach (Monster monster in room.Monsters.Where(monster => slowed.Contains(monster.Id)))
                {
                    monster.Slowed = true;
                }

                events.Add(new CowardsFlight(hero.Id, fleeing, slowed));
                break;

            case CardEffectKind.Freeze:
                Monster stunned = targets[0];
                stunned.FrozenTurns = Math.Max(stunned.FrozenTurns, effect.Amount);
                stunned.FreezeImmuneThroughRound = State.Round + 1;
                events.Add(new MonsterFrozen(stunned.Id, stunned.FrozenTurns));
                break;
        }

        return null;
    }

    /// <summary>
    /// Whether equipping, belting and stowing are free right now: they are while no monsters are in
    /// the hero's room. In a fight each one takes the turn's action.
    /// </summary>
    public bool GearIsFree => State.Turn is not null && ActiveRoom.Monsters.Count == 0;

    /// <summary>Why the active hero can't equip the card right now, or null if they can.</summary>
    public string? CheckEquip(int cardId, EquipSlot? hand = null)
    {
        if (CheckCardQuery(cardId, out CardInstance? card, out CardLocation location) is { } error)
        {
            return error;
        }

        CardDefinition definition = card!.Definition;
        if (definition.Slot is null)
        {
            return $"The {definition.Name} isn't worn or held, so it can't be equipped.";
        }

        if (hand is { } chosen && chosen != definition.Slot && chosen != definition.AltSlot)
        {
            return $"The {definition.Name} can't go in your {HandName(chosen)}.";
        }

        Hero hero = ActiveHero;
        if (location == CardLocation.Belt)
        {
            return $"The {definition.Name} is already on your belt.";
        }

        if (location == CardLocation.Equipped)
        {
            if (definition.AltSlot is null)
            {
                return $"The {definition.Name} is already equipped.";
            }

            if (HandFor(hero, card, hand) == hero.SlotOf(card))
            {
                return $"The {definition.Name} is already in your {HandName(hero.SlotOf(card)!.Value)}.";
            }
        }

        IReadOnlyList<CardInstance> displaced = hero.CardsDisplacedBy(definition, HandFor(hero, card, hand));
        if (hero.Backpack.Count - (location == CardLocation.Backpack ? 1 : 0) + displaced.Count > GameRules.BackpackCapacity)
        {
            return "There's no room in your backpack for what you'd take off.";
        }

        return CheckGearAction();
    }

    /// <summary>Why the active hero can't move the card to their belt right now, or null if they can.</summary>
    public string? CheckBelt(int cardId)
    {
        if (CheckCardQuery(cardId, out CardInstance? card, out CardLocation location) is { } error)
        {
            return error;
        }

        if (card!.Definition.Usage != CardUsage.SingleUse && !card.Definition.Belts)
        {
            return "Only single-use cards (potions, bombs, scrolls), Medicine Bags and torches go on your belt.";
        }

        if (location != CardLocation.Backpack)
        {
            return $"The {card.Definition.Name} is already on your belt.";
        }

        if (ActiveHero.Belt.Count >= GameRules.BeltCapacity)
        {
            return $"Your belt is full ({GameRules.BeltCapacity} cards). Stow one to make room.";
        }

        return CheckGearAction();
    }

    /// <summary>Why the active hero can't put the card in their backpack right now, or null if they can.</summary>
    public string? CheckStow(int cardId)
    {
        if (CheckCardQuery(cardId, out CardInstance? card, out CardLocation location) is { } error)
        {
            return error;
        }

        if (location == CardLocation.Backpack)
        {
            return $"The {card!.Definition.Name} is already in your backpack.";
        }

        if (ActiveHero.Backpack.Count >= GameRules.BackpackCapacity)
        {
            return "Your backpack is full.";
        }

        return CheckGearAction();
    }

    /// <summary>Why the active hero can't use the card right now (including having nothing to aim at), or null if they can.</summary>
    public string? CheckUse(int cardId)
    {
        if (CheckCardQuery(cardId, out CardInstance? card, out CardLocation location) is { } error)
        {
            return error;
        }

        if (CheckCardUsable(card!, location) is { } usable)
        {
            return usable;
        }

        if (GetCardTargets(cardId).Count > 0)
        {
            return null;
        }

        CardEffect effect = card!.Definition.Effect!;
        return effect.Kind is CardEffectKind.Heal or CardEffectKind.Cure or CardEffectKind.Repair
            ? CheckEffectTarget(ActiveHero, ActiveRoom, effect, null, out _) ?? "You can't use that now."
            : "There's no monster in range and in sight.";
    }

    /// <summary>The card must be the active hero's, with no roll waiting on a decision.</summary>
    private string? CheckCardQuery(int cardId, out CardInstance? card, out CardLocation location)
    {
        card = null;
        location = default;
        if (State.Turn is not { } turn)
        {
            return "It isn't anyone's turn.";
        }

        if (turn.UnsettledPowerDice > 0)
        {
            return "Add or bank your Power Die first.";
        }

        if (turn.PendingRoll is not null)
        {
            return "Finish the attack first.";
        }

        if (ActiveHero.FindCard(cardId) is not var (held, where))
        {
            return "You don't have that card.";
        }

        card = held;
        location = where;
        return null;
    }

    private string? CheckGearAction() =>
        GearIsFree || !Turn.HasActed ? null : "Changing gear in a fight takes your action, which you've used.";

    /// <summary>Gear changes are free outside a fight; in one they take the action.</summary>
    private void SpendGearAction()
    {
        if (!GearIsFree)
        {
            SpendAction();
        }
    }

    private string? Handle(EquipCard command, List<GameEvent> events)
    {
        if (CheckEquip(command.CardId, command.Hand) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        CardInstance card = hero.FindCard(command.CardId)!.Value.Card;
        EquipSlot? hand = HandFor(hero, card, command.Hand);

        // A stack of Daggers held in one hand, equipped again: one of them goes to the other hand.
        if (card.Count > 1 && card.Definition.OffHandStrike && hero.SlotOf(card) is { } holding && hand is { } other && other != holding)
        {
            card = card.Split(State.NextId(), 1);
        }

        // Torches go one to a hand: the rest of the stack stays where it was.
        if (card.Count > 1 && card.Definition.Torch)
        {
            card = card.Split(State.NextId(), 1);
        }

        IReadOnlyList<CardInstance> displaced = hero.CardsDisplacedBy(card.Definition, hand);
        SpendGearAction();
        if (hero.FindCard(card.Id) is not null)
        {
            hero.Remove(card);
        }

        foreach (CardInstance old in displaced)
        {
            hero.Remove(old);
            hero.AddToBackpack(old);
            events.Add(new CardStowed(hero.Id, old.Id));
        }

        hero.Equip(card, hand);
        hero.ClampHearts();
        events.Add(new CardEquipped(hero.Id, card.Id));
        return null;
    }

    /// <summary>
    /// The hand a card that goes in either (the Boomerang) is equipped into: the one chosen, or, if
    /// it's already equipped, the other one. Null for other cards, and for a free choice.
    /// </summary>
    private static EquipSlot? HandFor(Hero hero, CardInstance card, EquipSlot? chosen = null)
    {
        if (chosen is not null || card.Definition.AltSlot is not { } alt || card.Definition.Slot is not { } own)
        {
            return chosen;
        }

        EquipSlot? current = hero.SlotOf(card);
        return current is null ? null : current == own ? alt : own;
    }

    private static string HandName(EquipSlot slot) => slot switch
    {
        EquipSlot.MainHand => "main hand",
        EquipSlot.OffHand => "off hand",
        _ => slot.ToString().ToLowerInvariant(),
    };

    private string? Handle(BeltCard command, List<GameEvent> events)
    {
        if (CheckBelt(command.CardId) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        CardInstance card = hero.FindCard(command.CardId)!.Value.Card;
        SpendGearAction();
        hero.Remove(card);
        hero.AddToBelt(card);
        events.Add(new CardBelted(hero.Id, card.Id));
        return null;
    }

    private string? Handle(StowCard command, List<GameEvent> events)
    {
        if (CheckStow(command.CardId) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        CardInstance card = hero.FindCard(command.CardId)!.Value.Card;
        SpendGearAction();
        hero.Remove(card);
        hero.AddToBackpack(card);
        hero.ClampHearts();
        events.Add(new CardStowed(hero.Id, card.Id));
        return null;
    }

    private string? Handle(DropCard command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        if (hero.FindCard(command.CardId) is not var (card, _))
        {
            return "You don't have that card.";
        }

        int count = command.Count ?? card.Count;
        if (count < 1 || count > card.Count)
        {
            return $"Drop 1 to {card.Count}.";
        }

        CardInstance dropped = TakeFromHero(hero, card, count);
        GridPosition spot = SafeTile(ActiveRoom, hero.Position);
        ActiveRoom.AddLoot(spot, [dropped]);
        events.Add(new CardDropped(hero.Id, dropped.Id, spot) { Count = dropped.Count });
        return null;
    }

    /// <summary>Why the active hero can't open the chest right now, or null if they can.</summary>
    public string? CheckOpenChest(GridPosition chest)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (room.Layout[chest] != TileKind.Chest)
        {
            return "There's no chest there.";
        }

        if (room.IsChestOpened(chest))
        {
            return "That chest is already open.";
        }

        if (hero.Position.ManhattanDistanceTo(chest) != 1)
        {
            return "Stand next to the chest to open it.";
        }

        if (room.ChestLockAt(chest) is int difficulty && hero.Keys < 1)
        {
            return $"The chest is locked: use a key, or pick the lock with Od Hand ({difficulty}+).";
        }

        return CheckLoot();
    }

    /// <summary>Why the active hero can't take everything from a pile (their own tile by default), or null if they can.</summary>
    public string? CheckPickUp(GridPosition? tile = null)
    {
        GridPosition at = tile ?? ActiveHero.Position;
        if (CheckRummage(at) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (!room.HasLootAt(at))
        {
            return "There's nothing here to pick up.";
        }

        bool onlyCards = room.KeysAt(at) == 0 && room.GoldAt(at) == 0 && room.BossKeyAt != at && room.BombAt(at) is null;
        if (onlyCards && !room.LootAt(at).Any(card => hero.CanCarry(card.Definition)))
        {
            return "Your backpack is full.";
        }

        return LootingAgain(at) ? null : CheckLoot();
    }

    /// <summary>The first loot each turn is free; after that, looting takes the action.</summary>
    private string? CheckLoot() =>
        !Turn.UsedFreeLoot || !Turn.HasActed ? null : "You've used your free loot and your action this turn.";

    private void SpendLoot()
    {
        if (!Turn.UsedFreeLoot)
        {
            Turn.UsedFreeLoot = true;
        }
        else
        {
            SpendAction();
        }
    }

    private string? Handle(OpenChest command, List<GameEvent> events)
    {
        if (CheckOpenChest(command.Chest) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        SpendLoot();
        if (room.MimicAt(command.Chest) is not null)
        {
            WakeMimic(room, command.Chest, hero, events);
            return null;
        }

        if (room.ChestLockAt(command.Chest) is not null)
        {
            hero.Keys--;
            room.UnlockChest(command.Chest);
            events.Add(new KeyUsed(hero.Id, hero.Keys));
            events.Add(new ChestUnlocked(hero.Id, command.Chest, Picked: false));
        }

        if (room.ChestTrapAt(command.Chest) is { } trap)
        {
            room.SetChestTrap(command.Chest, null);
            SpringTrap(hero, room, trap, command.Chest, events);
            if (!hero.IsActive)
            {
                return null;
            }
        }

        // The dead in a grave climb out at whoever disturbed them.
        if (room.RiserAt(command.Chest) is { } riser)
        {
            room.OpenChest(command.Chest);
            RiseFromGrave(room, command.Chest, riser, hero, events);
            return null;
        }

        if (room.IsEmptyChest(command.Chest))
        {
            room.OpenChest(command.Chest);
            events.Add(new ChestOpened(hero.Id, command.Chest, [], AddedToBackpack: false));
            return null;
        }

        // A chest that was locked holds two cards, dealt as if from a floor deeper; a grand chest three,
        // as if from two floors deeper, and gold besides.
        bool superior = room.IsSuperiorChest(command.Chest);
        bool grand = room.IsGrandChest(command.Chest);
        bool bossKey = room.HoldsBossKey(command.Chest);
        room.OpenChest(command.Chest);
        int depth = room.Floor + (grand ? 2 : superior ? 1 : 0);
        List<CardInstance> cards = Enumerable.Range(0, grand ? 3 : superior ? 2 : 1)
            .Select(_ => new CardInstance(State.NextId(), State.LootDeck.Draw(depth), broughtInByHeroId: null))
            .ToList();
        if (grand)
        {
            GiveGold(hero, GameRules.GrandChestGold, events);
        }

        // Coins go straight into the purse; the rest stay in the chest to take or leave.
        foreach (CardInstance coins in cards.Where(card => card.Definition.Coins))
        {
            GiveGold(hero, coins.Definition.Value, events);
        }

        room.AddLoot(command.Chest, cards.Where(card => !card.Definition.Coins));
        Turn.LootedTile = command.Chest;
        events.Add(new ChestOpened(hero.Id, command.Chest, cards.Select(card => card.Id).ToList(), AddedToBackpack: false));
        if (bossKey)
        {
            hero.HasBossKey = true;
            events.Add(new BossKeyFound(hero.Id));
        }

        return null;
    }

    /// <summary>
    /// Puts cards in the backpack while there's room; the rest go on the hero's tile. Coins go straight
    /// into the purse. True if all fit.
    /// </summary>
    private static bool GiveCards(Hero hero, Room room, IReadOnlyList<CardInstance> cards, List<GameEvent> events)
    {
        bool allFit = true;
        foreach (CardInstance card in cards)
        {
            if (card.Definition.Coins)
            {
                GiveGold(hero, card.Definition.Value, events);
            }
            else if (hero.CanCarry(card.Definition))
            {
                hero.AddLoot(card);
            }
            else
            {
                room.AddLoot(SafeTile(room, hero.Position), [card]);
                allFit = false;
            }
        }

        return allFit;
    }

    private string? Handle(SmashPot command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (room.Layout[command.Pot] != TileKind.Pot)
        {
            return "There's no pot there.";
        }

        if (hero.Position.ManhattanDistanceTo(command.Pot) != 1)
        {
            return "Stand next to the pot to smash it.";
        }

        if (CheckCanAct() is { } error)
        {
            return error;
        }

        SpendAction();
        room.Layout.Set(command.Pot, TileKind.Floor);

        ThemeDefinition theme = Content.GetTheme(State.ThemeId);
        if (Chance(theme.Keys.Pot))
        {
            events.Add(new PotSmashed(hero.Id, command.Pot, null, false, 0));
            GiveKeys(hero, 1, events);
            return null;
        }

        PotDefinition? pots = theme.Pots;
        int roll = _random.Loot.NextInt(0, 100);
        int? cardId = null;
        bool addedToBackpack = false;
        int hearts = 0;
        int coins = 0;
        if (pots is not null && roll < pots.TreasureChance)
        {
            var card = new CardInstance(State.NextId(), Content.GetCard(pots.Treasure), broughtInByHeroId: null);
            cardId = card.Id;
            addedToBackpack = card.Definition.Coins || hero.CanCarry(card.Definition);
            if (card.Definition.Coins)
            {
                coins = card.Definition.Value;
            }
            else if (addedToBackpack)
            {
                hero.AddToBackpack(card);
            }
            else
            {
                room.AddLoot(command.Pot, [card]);
            }
        }
        else if (pots is not null && roll < pots.TreasureChance + pots.HeartChance)
        {
            hearts = Math.Min(GameRules.HeartsFromPot, hero.HeartsToHeal);
            hero.Hearts += hearts;
        }

        events.Add(new PotSmashed(hero.Id, command.Pot, cardId, addedToBackpack, hearts));
        GiveGold(hero, coins, events);
        return null;
    }

    private string? Handle(PickUpLoot command, List<GameEvent> events)
    {
        if (CheckPickUp(command.Tile) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        GridPosition at = command.Tile ?? hero.Position;
        SpendLootOn(at);

        // Everything that fits: stacks join what the hero holds, the rest while the backpack has room.
        var taken = new List<int>();
        foreach (CardInstance card in room.LootAt(at).ToList())
        {
            if (hero.CanCarry(card.Definition))
            {
                hero.AddLoot(room.TakeCard(at, card.Id)!);
                taken.Add(card.Id);
            }
        }

        if (taken.Count > 0)
        {
            events.Add(new LootPickedUp(hero.Id, taken));
        }

        // An unlit bomb goes in the backpack as a Bomb card.
        if (room.BombAt(at) is { Lit: false } bomb && Content.FindCard(BombCardId) is { } bombCard && hero.CanCarry(bombCard))
        {
            room.RemoveBomb(bomb);
            var card = new CardInstance(State.NextId(), bombCard, broughtInByHeroId: null);
            hero.AddToBackpack(card);
            events.Add(new LootPickedUp(hero.Id, [card.Id]));
        }

        TakeValuables(hero, room, at, events);
        return null;
    }

    private static void GiveGold(Hero hero, int amount, List<GameEvent> events)
    {
        if (amount > 0)
        {
            hero.Gold += amount;
            events.Add(new GoldFound(hero.Id, amount, hero.Gold));
        }
    }

    /// <summary>A card turning up on the floor (a monster's drop, a stash): coins lie there as gold.</summary>
    private static void DropLoot(Room room, GridPosition tile, CardInstance card, List<GameEvent> events)
    {
        if (card.Definition.Coins)
        {
            room.AddGold(tile, card.Definition.Value);
            events.Add(new GoldAppeared(room.Id, tile, card.Definition.Value));
            return;
        }

        room.AddLoot(tile, [card]);
        events.Add(new LootDropped(room.Id, tile, card.Id));
    }

    private static void GiveKeys(Hero hero, int count, List<GameEvent> events)
    {
        if (count > 0)
        {
            hero.Keys += count;
            events.Add(new KeysFound(hero.Id, count, hero.Keys));
        }
    }

    /// <summary>A percent chance from the loot stream. A chance of 0 draws nothing, so turning a feature off doesn't shift other rolls.</summary>
    private bool Chance(int percent) => percent > 0 && _random.Loot.NextInt(0, 100) < percent;

    /// <summary>
    /// Reusable cards must be equipped to use. Belt items are free once per turn; anything
    /// else uses the action.
    /// </summary>
    private string? CheckCardUsable(CardInstance card, CardLocation location)
    {
        CardDefinition definition = card.Definition;
        if (definition.Effect is null)
        {
            return definition.Type == CardType.Treasure
                ? "Treasure isn't used: carry it out of the dungeon to win."
                : $"The {definition.Name} has nothing to use; equip it for its bonus.";
        }

        if (definition.Usage != CardUsage.SingleUse && location != CardLocation.Equipped)
        {
            return $"Equip the {definition.Name} to use it.";
        }

        if (definition.SinEater && card.SoakedStacks == 0)
        {
            return "The doll holds nothing yet: soak up what ails someone first.";
        }

        if (definition.Usage == CardUsage.Charges && card.ChargesRemaining < 1)
        {
            return $"The {definition.Name} has no charges left. They refill on a new floor.";
        }

        if (location == CardLocation.Belt)
        {
            return Turn.UsedBeltItem ? "You've already used your free belt item this turn." : null;
        }

        return Turn.HasActed ? "Using a backpack card takes your action, which you've used." : null;
    }

    private static bool IsBomb(CardDefinition card) => card.Type == CardType.Bomb;

    /// <summary>The card an unlit bomb becomes when picked up.</summary>
    private const string BombCardId = "bomb";

    /// <param name="isBomb">A bomb: thrown onto any open tile in range, to go off later.</param>
    private string? CheckEffectTarget(Hero hero, Room room, CardEffect effect, GridPosition? target, out IReadOnlyList<Monster> targets, bool isBomb = false)
    {
        targets = [];
        switch (effect.Kind)
        {
            case CardEffectKind.Heal:
                if (target is { } healTile && healTile != hero.Position)
                {
                    return "That can only be used on yourself.";
                }

                return hero.HeartsToHeal == 0 ? hero.Hearts < hero.MaxHearts ? "Black Hearts can't be healed until they're cured." : "You're already at full hearts." : null;

            case CardEffectKind.CowardsFlight:
                return target is { } fleeTile && fleeTile != hero.Position ? "That can only be used on yourself." : null;

            case CardEffectKind.Cure:
                return target is { } cureTile && cureTile != hero.Position ? "That can only be used on yourself."
                    : effect.Cures is { } cures && hero.Conditions.Has(cures) ? null
                    : "There's nothing for it to cure.";

            case CardEffectKind.Repair:
                return target is { } mendTile && mendTile != hero.Position ? "That can only be used on yourself."
                    : MostWorn(hero) is null ? "Nothing you carry needs mending." : null;

            case CardEffectKind.Holy:
                return CheckSanctify(hero, room, effect, target, out targets);

            case CardEffectKind.SinEater:
                return CheckDollThrow(hero, room, effect, target);

            case CardEffectKind.Attack:
                if (target is not { } attackTile)
                {
                    return "Choose a target tile.";
                }

                if (room.Layout[attackTile] == TileKind.Wall
                    || !Targeting.InRangeAndSight(room.Layout, hero.Position, attackTile, effect.Range))
                {
                    return "That tile is out of range.";
                }

                if (IsBlind(hero.Conditions) && attackTile.ChebyshevDistanceTo(hero.Position) > 1)
                {
                    return "You're blind: you can't see past the tiles beside you.";
                }

                // A bomb is thrown onto open ground (or water, which puts it out), anywhere in range.
                if (isBomb)
                {
                    TileKind ground = room.Layout[attackTile];
                    return !(ground.IsWalkable() || ground == TileKind.DeepWater) ? "Throw it onto open ground."
                        : room.BombAt(attackTile) is not null ? "There's a bomb there already."
                        : null;
                }

                // Fire may be aimed at a bomb, to set it off.
                if (effect.Fire && room.BombAt(attackTile) is not null)
                {
                    targets = room.Monsters.Where(monster => monster.NearestTile(attackTile).ChebyshevDistanceTo(attackTile) <= effect.AreaRadius).ToList();
                    return null;
                }

                targets = room.Monsters.Where(monster => monster.NearestTile(attackTile).ChebyshevDistanceTo(attackTile) <= effect.AreaRadius && SpellReaches(hero, room, monster)).ToList();
                if (targets.Count == 0 && room.Monsters.Any(monster => monster.NearestTile(attackTile).ChebyshevDistanceTo(attackTile) <= effect.AreaRadius))
                {
                    return "It's under the water, out of reach.";
                }

                if (targets.Count == 0 && AlliesCaught(room, attackTile, effect.AreaRadius).Count > 0)
                {
                    return null;
                }

                // Fire can be aimed at bare floor, to set it burning.
                return targets.Count > 0 || (effect.Fire && (CanBurn(room, attackTile) || UnlitBrazierAt(room, attackTile))) ? null
                    : effect.Fire ? "Nothing there will burn."
                    : "There are no monsters there.";

            case CardEffectKind.Freeze:
                if (target is not { } stunTile || room.MonsterAt(stunTile) is not { Collapsed: false } monster)
                {
                    return "Choose a monster.";
                }

                if (!Targeting.InRangeAndSight(room.Layout, hero.Position, stunTile, effect.Range)
                    || (IsBlind(hero.Conditions) && stunTile.ChebyshevDistanceTo(hero.Position) > 1))
                {
                    return "That monster is out of range.";
                }

                if (!SpellReaches(hero, room, monster))
                {
                    return "It's under the water, out of reach.";
                }

                if (monster.Definition.StunImmune)
                {
                    return $"The {monster.Definition.Name} can't be stunned.";
                }

                if (monster.FreezeImmuneThroughRound >= State.Round)
                {
                    return $"The {monster.Definition.Name} is still shaking off the last stun.";
                }

                targets = [monster];
                return null;

            default:
                return "That card does nothing.";
        }
    }

    /// <summary>With friendly fire on, the heroes (the user too) an attack card aimed at <paramref name="tile"/> would catch.</summary>
    private List<Hero> AlliesCaught(Room room, GridPosition tile, int radius) => State.FriendlyFire
        ? State.ActiveHeroesIn(room).Where(ally => ally.Position.ChebyshevDistanceTo(tile) <= radius && !Diving(room, ally)).ToList()
        : [];

    private static DamageSource SourceOf(CardDefinition card) => card.Type switch
    {
        CardType.Bomb => DamageSource.Bomb,
        CardType.Spell => DamageSource.Spell,
        _ => DamageSource.Weapon,
    };
}
