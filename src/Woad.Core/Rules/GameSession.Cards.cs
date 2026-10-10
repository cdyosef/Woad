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
        if (State.Turn is not { PendingAttack: null }
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
            .Where(position => CheckEffectTarget(hero, room, effect, position, out _) is null)
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
        if (CheckEffectTarget(hero, room, effect, command.Target, out IReadOnlyList<Monster> targets) is { } targetError)
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
                RollHeroAttack(hero, effect.Amount, targets.Select(monster => monster.Id).ToList(), SourceOf(card.Definition), events);
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

    private string? Handle(EquipCard command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        if (hero.FindCard(command.CardId) is not var (card, location))
        {
            return "You don't have that card.";
        }

        if (card.Definition.Slot is null)
        {
            return $"The {card.Definition.Name} can't be equipped.";
        }

        if (location != CardLocation.Backpack)
        {
            return $"The {card.Definition.Name} is already equipped.";
        }

        if (CheckCanAct() is { } error)
        {
            return error;
        }

        IReadOnlyList<CardInstance> displaced = hero.CardsDisplacedBy(card.Definition);
        if (hero.Backpack.Count - 1 + displaced.Count > GameRules.BackpackCapacity)
        {
            return "There's no room in your backpack for what you'd take off.";
        }

        SpendAction();
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
        Hero hero = ActiveHero;
        if (hero.FindCard(command.CardId) is not var (card, location))
        {
            return "You don't have that card.";
        }

        if (card.Definition.Usage != CardUsage.SingleUse)
        {
            return "Only single-use cards fit on your belt.";
        }

        if (location != CardLocation.Backpack)
        {
            return $"The {card.Definition.Name} is already on your belt.";
        }

        if (hero.Belt.Count >= GameRules.BeltCapacity)
        {
            return "Your belt is full.";
        }

        if (CheckCanAct() is { } error)
        {
            return error;
        }

        SpendAction();
        hero.Remove(card);
        hero.AddToBelt(card);
        events.Add(new CardBelted(hero.Id, card.Id));
        return null;
    }

    private string? Handle(StowCard command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        if (hero.FindCard(command.CardId) is not var (card, location))
        {
            return "You don't have that card.";
        }

        if (location == CardLocation.Backpack)
        {
            return $"The {card.Definition.Name} is already in your backpack.";
        }

        if (hero.Backpack.Count >= GameRules.BackpackCapacity)
        {
            return "Your backpack is full.";
        }

        if (CheckCanAct() is { } error)
        {
            return error;
        }

        SpendAction();
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
        ActiveRoom.AddLoot(hero.Position, [card]);
        events.Add(new CardDropped(hero.Id, card.Id, hero.Position));
        return null;
    }

    private string? Handle(OpenChest command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (room.Layout[command.Chest] != TileKind.Chest)
        {
            return "There's no chest there.";
        }

        if (room.IsChestOpened(command.Chest))
        {
            return "That chest is already open.";
        }

        if (hero.Position.ManhattanDistanceTo(command.Chest) != 1)
        {
            return "Stand next to the chest to open it.";
        }

        if (CheckCanAct() is { } error)
        {
            return error;
        }

        SpendAction();
        room.OpenChest(command.Chest);
        var card = new CardInstance(State.NextId(), State.LootDeck.Draw(), broughtInByHeroId: null);
        bool fits = hero.Backpack.Count < GameRules.BackpackCapacity;
        if (fits)
        {
            hero.AddToBackpack(card);
        }
        else
        {
            room.AddLoot(hero.Position, [card]);
        }

        events.Add(new ChestOpened(hero.Id, command.Chest, card.Id, fits));
        return null;
    }

    private string? Handle(PickUpLoot command, List<GameEvent> events)
    {
        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (room.LootAt(hero.Position).Count == 0)
        {
            return "There's nothing here to pick up.";
        }

        int space = GameRules.BackpackCapacity - hero.Backpack.Count;
        if (space < 1)
        {
            return "Your backpack is full.";
        }

        if (CheckCanAct() is { } error)
        {
            return error;
        }

        SpendAction();
        IReadOnlyList<CardInstance> taken = room.TakeLoot(hero.Position, space);
        foreach (CardInstance card in taken)
        {
            hero.AddToBackpack(card);
        }

        events.Add(new LootPickedUp(hero.Id, taken.Select(card => card.Id).ToList()));
        return null;
    }

    /// <summary>
    /// Reusable cards must be equipped to use. Belt items are free once per turn; anything
    /// else uses the action.
    /// </summary>
    private string? CheckCardUsable(CardInstance card, CardLocation location)
    {
        CardDefinition definition = card.Definition;
        if (definition.Effect is null)
        {
            return $"The {definition.Name} can't be used.";
        }

        if (definition.Usage != CardUsage.SingleUse && location != CardLocation.Equipped)
        {
            return $"Equip the {definition.Name} to use it.";
        }

        if (definition.Usage == CardUsage.Charges && card.ChargesRemaining < 1)
        {
            return $"The {definition.Name} has no charges left.";
        }

        if (location == CardLocation.Belt)
        {
            return Turn.UsedBeltItem ? "You've already used a belt item this turn." : null;
        }

        return CheckCanAct();
    }

    private string? CheckEffectTarget(Hero hero, Room room, CardEffect effect, GridPosition? target, out IReadOnlyList<Monster> targets)
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
                return targets.Count == 0 ? "There are no monsters there." : null;

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

    private static DamageSource SourceOf(CardDefinition card) => card.Type switch
    {
        CardType.Bomb => DamageSource.Bomb,
        CardType.Spell => DamageSource.Spell,
        _ => DamageSource.Weapon,
    };
}
