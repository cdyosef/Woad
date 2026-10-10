using Woad.Core.Events;

namespace Woad.Scenes.Game;

/// <summary>Turns game events into sentences for the message log. Returns null for events shown only visually.</summary>
public sealed class EventNarrator(Func<int, string> nameOf, Func<int, string> cardNameOf)
{
    public string? Describe(GameEvent gameEvent) => gameEvent switch
    {
        RoundStarted round => $"Round {round.Round} begins.",
        MovementRolled rolled => $"{nameOf(rolled.HeroId)} rolls {rolled.Roll.Total} and can move {Count(rolled.MovementPoints, "step")}.",
        DieRerolled { MovementPoints: int points } rerolled =>
            $"{nameOf(rerolled.HeroId)} turns a {rerolled.OldFace} into a {rerolled.NewFace}: {Count(points, "step")}.",
        DieRerolled rerolled => $"{nameOf(rerolled.HeroId)} turns a {rerolled.OldFace} into a {rerolled.NewFace}.",
        HeroEnteredRoom entered => $"{nameOf(entered.HeroId)} enters a new room.",
        AttackRolled attack => $"{nameOf(attack.AttackerId)} attacks {Names(attack.TargetIds)}: {Count(attack.Hits, "hit")}.",
        Damaged { Amount: 0 } damaged => $"{nameOf(damaged.TargetId)} blocks every hit.",
        Damaged damaged => $"{nameOf(damaged.TargetId)} takes {damaged.Amount} damage.",
        MonsterDefeated defeated => $"{nameOf(defeated.MonsterId)} is defeated!",
        MonsterCollapsed collapsed => $"{nameOf(collapsed.MonsterId)} collapses into a heap of bones...",
        MonsterReassembled risen => $"{nameOf(risen.MonsterId)} pulls itself back together!",
        MonsterSpawned spawned => $"A {nameOf(spawned.MonsterId)} wriggles free.",
        MonsterStunned stunned => $"{nameOf(stunned.MonsterId)} is stunned.",
        MonsterTurnSkipped skipped => $"{nameOf(skipped.MonsterId)} is stunned and loses its turn.",
        HeroHealed healed => $"{nameOf(healed.HeroId)} recovers {Count(healed.Amount, "heart")}.",
        HeroDowned downed => $"{nameOf(downed.HeroId)} is downed and drops {Count(downed.DroppedCardIds.Count, "card")}!",
        CardUsed used => $"{nameOf(used.HeroId)} uses the {cardNameOf(used.CardId)}.",
        CardEquipped equipped => $"{nameOf(equipped.HeroId)} equips the {cardNameOf(equipped.CardId)}.",
        CardBelted belted => $"{nameOf(belted.HeroId)} hangs the {cardNameOf(belted.CardId)} on their belt.",
        CardStowed stowed => $"{nameOf(stowed.HeroId)} packs away the {cardNameOf(stowed.CardId)}.",
        CardDropped dropped => $"{nameOf(dropped.HeroId)} drops the {cardNameOf(dropped.CardId)}.",
        ChestOpened { AddedToBackpack: true } chest => $"{nameOf(chest.HeroId)} opens the chest: {cardNameOf(chest.CardId)}!",
        ChestOpened chest => $"{nameOf(chest.HeroId)} opens the chest: {cardNameOf(chest.CardId)}, but has no room and leaves it on the floor.",
        LootPickedUp loot => $"{nameOf(loot.HeroId)} picks up {string.Join(", ", loot.CardIds.Select(cardNameOf))}.",
        HeroDescended { FirstTimeOnFloor: true } descended =>
            $"{nameOf(descended.HeroId)} descends to floor {descended.Floor}. Luck and charges are restored.",
        HeroDescended descended => $"{nameOf(descended.HeroId)} descends to floor {descended.Floor}.",
        HeroEscaped escaped => $"{nameOf(escaped.HeroId)} escapes the dungeon with {escaped.TreasureValue}g of treasure!",
        RoomCleared => "The room falls silent.",
        PotSmashed { CardId: int cardId } smashed => $"{nameOf(smashed.HeroId)} smashes a pot and finds {cardNameOf(cardId)}!",
        PotSmashed { HeartsRestored: > 0 } smashed => $"{nameOf(smashed.HeroId)} smashes a pot and finds a heart!",
        PotSmashed smashed => $"{nameOf(smashed.HeroId)} smashes a pot. Empty.",
        LootDropped dropped => $"It dropped {cardNameOf(dropped.CardId)}!",
        RunEnded { WinnerHeroIds.Count: 0 } => "The run is over. Nobody escaped.",
        RunEnded ended => $"{Names(ended.WinnerHeroIds)} {(ended.WinnerHeroIds.Count == 1 ? "wins" : "win")} the run!",
        _ => null,
    };

    private string Names(IEnumerable<int> ids) => string.Join(" and ", ids.Select(nameOf));

    private static string Count(int amount, string noun) => $"{amount} {noun}{(amount == 1 ? "" : "s")}";
}
