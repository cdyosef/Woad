using Woad.Core.Commands;
using Woad.Core.Events;
using Woad.Core.Grid;
using Woad.Core.Model;

namespace Woad.Core.Rules;

/// <summary>
/// Loot piles and chests you can rummage through. Loot lying on a tile (a bag, once there are two or
/// more things) and an opened chest's contents can be reached from the tile itself or any tile touching
/// it, diagonals too. Take things one at a time, a few off a stack, or all of it; the first take each
/// turn is the free loot (then the action), and more from the same pile that turn cost nothing.
/// Putting your own cards into a pile is free, like dropping them.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Why the active hero can't reach into the pile on a tile (or an opened chest) right now, or null.</summary>
    public string? CheckRummage(GridPosition tile)
    {
        if (State.Turn is not { PendingRoll: null, UnsettledPowerDice: 0 })
        {
            return "Finish the roll first.";
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (!room.Layout.Contains(tile) || hero.Position.ChebyshevDistanceTo(tile) > 1)
        {
            return "Stand on it or next to it.";
        }

        return room.Layout[tile] == TileKind.Chest && !room.IsChestOpened(tile) ? "Open the chest first." : null;
    }

    /// <summary>Whether taking from this pile is free now: it's the one already looted this turn.</summary>
    private bool LootingAgain(GridPosition tile) => Turn.LootedTile == tile;

    private void SpendLootOn(GridPosition tile)
    {
        if (!LootingAgain(tile))
        {
            SpendLoot();
            Turn.LootedTile = tile;
        }
    }

    private string? Handle(TakeLoot command, List<GameEvent> events)
    {
        if (CheckRummage(command.Tile) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (!LootingAgain(command.Tile) && CheckLoot() is { } spent)
        {
            return spent;
        }

        if (command.CardId is not int cardId)
        {
            if (room.GoldAt(command.Tile) == 0 && room.KeysAt(command.Tile) == 0 && room.BossKeyAt != command.Tile)
            {
                return "There's no gold or key there.";
            }

            SpendLootOn(command.Tile);
            TakeValuables(hero, room, command.Tile, events);
            return null;
        }

        if (room.LootAt(command.Tile).FirstOrDefault(card => card.Id == cardId) is not { } card)
        {
            return "That isn't there any more.";
        }

        int count = command.Count ?? card.Count;
        if (count < 1 || count > card.Count)
        {
            return $"Take 1 to {card.Count}.";
        }

        if (!hero.CanCarry(card.Definition))
        {
            return "Your backpack is full.";
        }

        SpendLootOn(command.Tile);
        CardInstance taken = count == card.Count ? room.TakeCard(command.Tile, card.Id)! : card.Split(State.NextId(), count);
        CardInstance holder = hero.AddLoot(taken);
        events.Add(new LootPickedUp(hero.Id, [taken.Id]) { HolderId = holder.Id });
        return null;
    }

    private string? Handle(PutLoot command, List<GameEvent> events)
    {
        if (CheckRummage(command.Tile) is { } error)
        {
            return error;
        }

        Hero hero = ActiveHero;
        Room room = ActiveRoom;
        if (hero.FindCard(command.CardId) is not var (card, _))
        {
            return "You don't have that card.";
        }

        if (room.Layout[command.Tile] != TileKind.Chest && !(room.Layout[command.Tile].IsWalkable() || room.Layout[command.Tile] == TileKind.DeepWater))
        {
            return "Put it on open ground or in an opened chest.";
        }

        int count = command.Count ?? card.Count;
        if (count < 1 || count > card.Count)
        {
            return $"Put down 1 to {card.Count}.";
        }

        CardInstance put = TakeFromHero(hero, card, count);
        room.AddLoot(command.Tile, [put]);
        events.Add(new CardDropped(hero.Id, put.Id, command.Tile) { Count = put.Count });
        return null;
    }

    /// <summary>The gold, keys and Boss Key on a tile, into the hero's purse.</summary>
    private void TakeValuables(Hero hero, Room room, GridPosition tile, List<GameEvent> events)
    {
        GiveGold(hero, room.TakeGold(tile), events);
        GiveKeys(hero, room.TakeKeys(tile), events);
        if (room.BossKeyAt == tile)
        {
            room.BossKeyAt = null;
            hero.HasBossKey = true;
            events.Add(new BossKeyFound(hero.Id));
        }
    }

    /// <summary>
    /// Takes <paramref name="count"/> of a card the hero holds: the whole card (gone from the hero), or
    /// that many split off its stack.
    /// </summary>
    private CardInstance TakeFromHero(Hero hero, CardInstance card, int count)
    {
        if (count < card.Count)
        {
            return card.Split(State.NextId(), count);
        }

        hero.Remove(card);
        hero.ClampHearts();
        return card;
    }

    /// <summary>What a hero who falls (or a companion left behind) can't keep: everything they found, stacks split.</summary>
    private List<CardInstance> TakeFound(Hero hero)
    {
        var found = new List<CardInstance>();
        foreach (CardInstance card in hero.AllCards.ToList())
        {
            int copies = card.FoundBy(hero.Id);
            if (copies > 0)
            {
                found.Add(TakeFromHero(hero, card, copies));
            }
        }

        return found;
    }
}
