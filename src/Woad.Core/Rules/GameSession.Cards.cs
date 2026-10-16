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
        if (State.Turn is not { PendingRoll: null }
            || ActiveHero.FindCard(cardId) is not { } held
            || CheckCardUsable(held.Card, held.Location) is not null)
        {
            return [];
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        CardEffect effect = held.Card.Definition.Effect!;
        if (effect.Kind == CardEffectKind.Heal)
        {
            return CheckEffectTarget(hero, room, effect, null, out _) is null ? [hero.Position] : [];
        }

        return RoomGrid.AllPositions()
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

        if (location == CardLocation.Belt)
        {
            Turn.UsedBeltItem = true;
        }
        else
        {
            SpendAction();
        }

        events.Add(new CardUsed(hero.Id, card.Id, command.Target));
        if (card.Definition.Usage == CardUsage.SingleUse)
        {
            hero.Remove(card);
            events.Add(new CardConsumed(hero.Id, card.Id));
        }
        else if (card.Definition.Usage == CardUsage.Charges)
        {
            card.ChargesRemaining--;
        }

        switch (effect.Kind)
        {
            case CardEffectKind.Heal:
                int healed = Math.Min(effect.Amount, hero.MaxHearts - hero.Hearts);
                hero.Hearts += healed;
                events.Add(new HeroHealed(hero.Id, healed, hero.Hearts));
                break;

            case CardEffectKind.Attack:
                if (IsBomb(card.Definition))
                {
                    BreakWalls(room, CrackedWallsIn(room, command.Target!.Value, effect.AreaRadius), events);
                }

                // With friendly fire on, the blast catches heroes too, the thrower included.
                List<int> caught = [.. targets.Select(monster => monster.Id), .. AlliesCaught(room, command.Target!.Value, effect.AreaRadius).Select(ally => ally.Id)];
                if (caught.Count > 0)
                {
                    RollHeroAttack(hero, effect.Amount, 0, caught, SourceOf(card.Definition), events, fire: effect.Fire);
                }

                break;

            case CardEffectKind.Stun:
                Monster stunned = targets[0];
                stunned.StunnedTurns = Math.Max(stunned.StunnedTurns, effect.Amount);
                stunned.StunImmuneThroughRound = State.Round + 1;
                events.Add(new MonsterStunned(stunned.Id, stunned.StunnedTurns));
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
    public string? CheckEquip(int cardId)
    {
        if (CheckCardQuery(cardId, out CardInstance? card, out CardLocation location) is { } error)
        {
            return error;
        }

        if (card!.Definition.Slot is null)
        {
            return $"The {card.Definition.Name} isn't worn or held, so it can't be equipped.";
        }

        if (location != CardLocation.Backpack)
        {
            return $"The {card.Definition.Name} is already {(location == CardLocation.Belt ? "on your belt" : "equipped")}.";
        }

        Hero hero = ActiveHero;
        IReadOnlyList<CardInstance> displaced = hero.CardsDisplacedBy(card.Definition);
        if (hero.Backpack.Count - 1 + displaced.Count > GameRules.BackpackCapacity)
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

        if (card!.Definition.Usage != CardUsage.SingleUse)
        {
            return "Only single-use cards (potions, bombs, scrolls) go on your belt.";
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

        return card!.Definition.Effect!.Kind == CardEffectKind.Heal
            ? "You're already at full hearts."
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
        if (CheckEquip(command.CardId) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        CardInstance card = hero.FindCard(command.CardId)!.Value.Card;
        IReadOnlyList<CardInstance> displaced = hero.CardsDisplacedBy(card.Definition);
        SpendGearAction();
        hero.Remove(card);
        foreach (CardInstance old in displaced)
        {
            hero.Remove(old);
            hero.AddToBackpack(old);
            events.Add(new CardStowed(hero.Id, old.Id));
        }

        hero.Equip(card);
        hero.ClampHearts();
        events.Add(new CardEquipped(hero.Id, card.Id));
        return null;
    }

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

        hero.Remove(card);
        hero.ClampHearts();
        GridPosition spot = SafeTile(ActiveRoom, hero.Position);
        ActiveRoom.AddLoot(spot, [card]);
        events.Add(new CardDropped(hero.Id, card.Id, spot));
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

    /// <summary>Why the active hero can't pick up what's on their tile, or null if they can.</summary>
    public string? CheckPickUp()
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (!room.HasLootAt(hero.Position))
        {
            return "There's nothing here to pick up.";
        }

        bool onlyCards = room.KeysAt(hero.Position) == 0 && room.GoldAt(hero.Position) == 0 && room.BossKeyAt != hero.Position;
        if (onlyCards && hero.Backpack.Count >= GameRules.BackpackCapacity)
        {
            return "Your backpack is full.";
        }

        return CheckLoot();
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

        // A chest that was locked holds two cards, dealt as if from a floor deeper.
        bool superior = room.IsSuperiorChest(command.Chest);
        bool bossKey = room.HoldsBossKey(command.Chest);
        room.OpenChest(command.Chest);
        List<CardInstance> cards = Enumerable.Range(0, superior ? 2 : 1)
            .Select(_ => new CardInstance(State.NextId(), State.LootDeck.Draw(superior ? room.Floor + 1 : room.Floor), broughtInByHeroId: null))
            .ToList();
        bool allFit = GiveCards(hero, room, cards, events);
        events.Add(new ChestOpened(hero.Id, command.Chest, cards.Select(card => card.Id).ToList(), allFit));
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
            else if (hero.Backpack.Count < GameRules.BackpackCapacity)
            {
                hero.AddToBackpack(card);
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
            addedToBackpack = card.Definition.Coins || hero.Backpack.Count < GameRules.BackpackCapacity;
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
            hearts = Math.Min(GameRules.HeartsFromPot, hero.MaxHearts - hero.Hearts);
            hero.Hearts += hearts;
        }

        events.Add(new PotSmashed(hero.Id, command.Pot, cardId, addedToBackpack, hearts));
        GiveGold(hero, coins, events);
        return null;
    }

    private string? Handle(PickUpLoot command, List<GameEvent> events)
    {
        if (CheckPickUp() is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        SpendLoot();
        IReadOnlyList<CardInstance> taken = room.TakeLoot(hero.Position, GameRules.BackpackCapacity - hero.Backpack.Count);
        foreach (CardInstance card in taken)
        {
            hero.AddToBackpack(card);
        }

        if (taken.Count > 0)
        {
            events.Add(new LootPickedUp(hero.Id, taken.Select(card => card.Id).ToList()));
        }

        GiveGold(hero, room.TakeGold(hero.Position), events);
        GiveKeys(hero, room.TakeKeys(hero.Position), events);
        if (room.BossKeyAt == hero.Position)
        {
            room.BossKeyAt = null;
            hero.HasBossKey = true;
            events.Add(new BossKeyFound(hero.Id));
        }

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

    /// <param name="breaksWalls">A bomb: its blast may be aimed at cracked walls alone.</param>
    private string? CheckEffectTarget(Hero hero, Room room, CardEffect effect, GridPosition? target, out IReadOnlyList<Monster> targets, bool breaksWalls = false)
    {
        targets = [];
        switch (effect.Kind)
        {
            case CardEffectKind.Heal:
                if (target is { } healTile && healTile != hero.Position)
                {
                    return "That can only be used on yourself.";
                }

                return hero.Hearts >= hero.MaxHearts ? "You're already at full hearts." : null;

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

                targets = room.Monsters.Where(monster => monster.Position.ChebyshevDistanceTo(attackTile) <= effect.AreaRadius).ToList();
                if (targets.Count == 0 && breaksWalls && CrackedWallsIn(room, attackTile, effect.AreaRadius).Count > 0)
                {
                    return null;
                }

                if (targets.Count == 0 && AlliesCaught(room, attackTile, effect.AreaRadius).Count > 0)
                {
                    return null;
                }

                return targets.Count == 0 ? (breaksWalls ? "There are no monsters or cracked walls there." : "There are no monsters there.") : null;

            case CardEffectKind.Stun:
                if (target is not { } stunTile || room.MonsterAt(stunTile) is not { Collapsed: false } monster)
                {
                    return "Choose a monster.";
                }

                if (!Targeting.InRangeAndSight(room.Layout, hero.Position, stunTile, effect.Range))
                {
                    return "That monster is out of range.";
                }

                if (monster.Definition.StunImmune)
                {
                    return $"The {monster.Definition.Name} can't be stunned.";
                }

                if (monster.StunImmuneThroughRound >= State.Round)
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
