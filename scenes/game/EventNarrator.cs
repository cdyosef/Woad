using Woad.Core.Events;
using Woad.Core.Model;

namespace Woad.Scenes.Game;

/// <summary>Turns game events into sentences for the message log. Returns null for events shown only visually.</summary>
public sealed class EventNarrator(Func<int, string> nameOf, Func<int, string> cardNameOf)
{
    public string? Describe(GameEvent gameEvent) => gameEvent switch
    {
        RoundStarted round => $"Round {round.Round} begins.",
        MovementRolled rolled => $"{nameOf(rolled.HeroId)} rolls {rolled.Roll.Total} and can move {Count(rolled.MovementPoints, "step")}.",
        AttackRolled { Shove: true } shove => $"{nameOf(shove.AttackerId)} shoves {Names(shove.TargetIds)} with {shove.Total}.",
        AttackRolled attack => $"{nameOf(attack.AttackerId)} attacks {Names(attack.TargetIds)} with {attack.Total}.",
        DieRerolled { MovementPoints: int points } rerolled =>
            $"{nameOf(rerolled.HeroId)} turns a {rerolled.OldFace} into a {rerolled.NewFace}: {Count(points, "step")}.",
        DieRerolled rerolled => $"{nameOf(rerolled.HeroId)} turns a {rerolled.OldFace} into a {rerolled.NewFace}.",
        HeroEnteredRoom entered => $"{nameOf(entered.HeroId)} enters a new room.",
        CompanionOrdered ordered => $"{nameOf(ordered.OwnerId)} tells {nameOf(ordered.CompanionId)} to {Command(ordered.Order)}.",
        FellIntoPit { Bottomless: true } fell => $"{nameOf(fell.EntityId)} falls into the pit!",
        FellIntoPit fell => $"{nameOf(fell.EntityId)} falls through the pit to the room below!",
        ClimbedOut climbed => $"{nameOf(climbed.EntityId)} claws their way back out.",
        Shoved shoved => $"{nameOf(shoved.TargetId)} is pushed back!",
        Pulled pulled => $"The {nameOf(pulled.MonsterId)} drags {nameOf(pulled.HeroId)} to its side!",
        ShoveResisted resisted => $"{nameOf(resisted.TargetId)} holds their ground.",
        MonsterTrapped trapped => $"{nameOf(trapped.MonsterId)} is shoved onto a {(trapped.Kind == TrapKind.TrapDoor ? "trap door" : "spike trap")}!",
        MonsterFell fell => $"{nameOf(fell.MonsterId)} drops to the floor below.",
        Leaped { Roll: < 2 } leap => $"{nameOf(leap.HeroId)} stumbles on the leap ({leap.Roll}) and goes nowhere.",
        Leaped leap when leap.Landing == leap.Target => $"{nameOf(leap.HeroId)} leaps ({leap.Roll}) and lands on target.",
        Leaped leap => $"{nameOf(leap.HeroId)} leaps ({leap.Roll}) but comes down short!",
        StayedAfloat afloat => $"{nameOf(afloat.HeroId)} treads water, spending a Rumble die to stay afloat.",
        Drowned drowned => $"{nameOf(drowned.HeroId)} has no strength left to swim and is washed back to shore!",
        PowerDiceEarned earned => $"{nameOf(earned.HeroId)} rolled a 6 and earns a Power Die!",
        PowerDieAdded added => $"{nameOf(added.HeroId)} adds a Power Die: {added.Face}, for {added.Total}.",
        PowerDiceBanked banked => $"{nameOf(banked.OwnerId)} banks {PowerDice(banked.Count)} ({banked.Banked} banked).",
        PowerDiceSpent spent => $"{nameOf(spent.OwnerId)} spends {PowerDice(spent.Count)}.",
        Damaged { Amount: 0 } damaged => $"Miss! {nameOf(damaged.TargetId)} blocks it.",
        Damaged damaged => $"Hit! {nameOf(damaged.TargetId)} loses {Count(damaged.Amount, "heart")}.",
        MonsterDefeated defeated => $"{nameOf(defeated.MonsterId)} is defeated!",
        MonsterCollapsed collapsed => $"{nameOf(collapsed.MonsterId)} collapses into a heap of bones...",
        MonsterReassembled risen => $"{nameOf(risen.MonsterId)} pulls itself back together!",
        MonsterSpawned spawned => $"A {nameOf(spawned.MonsterId)} wriggles free.",
        MonsterFrozen frozen => $"{nameOf(frozen.MonsterId)} is frozen solid.",
        Stunned { Powerful: true } stunned => $"{nameOf(stunned.EntityId)} is knocked senseless (+{stunned.Stacks + 3 * stunned.PowerfulStacks} to hit).",
        Stunned stunned => $"{nameOf(stunned.EntityId)} is stunned (+{stunned.Stacks + 3 * stunned.PowerfulStacks} to hit).",
        BombPlaced { Lit: true } => "A lit bomb lands, its fuse hissing.",
        BombPlaced => "An unlit bomb drops out.",
        FuseBurned { FuseTurns: 1 } => "The fuse burns low: one more turn.",
        FuseBurned => "The fuse burns down.",
        BombExploded => "BOOM! The bomb goes off.",
        BlastRolled blast => $"The blast rolls {string.Join(", ", blast.Roll.Faces)}.",
        BlastHit { Hits: 0 } hit => $"{nameOf(hit.EntityId)} weathers the blast.",
        BlastHit { Hits: 1 } hit => $"One die of the blast gets through to {nameOf(hit.EntityId)}.",
        BlastHit hit => $"{hit.Hits} dice of the blast get through to {nameOf(hit.EntityId)}.",
        BombStruck { Exploded: true } struck => $"{nameOf(struck.HeroId)} strikes the bomb ({struck.Chance}%) and it goes off!",
        BombStruck struck => $"{nameOf(struck.HeroId)} strikes the bomb ({struck.Chance}%), but it holds.",
        BombMoved => "The bomb rolls away.",
        BombDefused { Doused: true } => "The water puts the fuse out.",
        BombDefused => "The fuse is pinched out: the bomb lies quiet.",
        BombFell { ToRoomId: null } => "The bomb tumbles into the dark.",
        BombFell => "The bomb drops down the pit to the room below.",
        MonsterStole { Hits: 0 } stole => $"{nameOf(stole.MonsterId)} reaches for {nameOf(stole.HeroId)}'s things but comes away with nothing.",
        MonsterStole { CardId: int card } stole => $"{nameOf(stole.MonsterId)} lifts {cardNameOf(card)}{(stole.Gold > 0 ? $" and {stole.Gold} gold" : "")} from {nameOf(stole.HeroId)}!",
        MonsterStole stole => $"{nameOf(stole.MonsterId)} lifts {stole.Gold} gold from {nameOf(stole.HeroId)}!",
        Mugged mugged => $"{nameOf(mugged.MonsterId)} snatches {mugged.Gold} gold from {nameOf(mugged.HeroId)}.",
        StunWoreOff wore => $"{nameOf(wore.EntityId)} shakes off the stun.",
        MonsterTurnSkipped skipped => $"{nameOf(skipped.MonsterId)} is frozen and loses its turn.",
        HeroHealed healed => $"{nameOf(healed.HeroId)} recovers {Count(healed.Amount, "heart")}.",
        Ignited { FireDice: 1 } ignited => $"{nameOf(ignited.EntityId)} catches fire!",
        Ignited ignited => $"The fire on {nameOf(ignited.EntityId)} grows to {ignited.FireDice} dice.",
        FireSpread spread => $"The flames leap from {nameOf(spread.FromId)} to {nameOf(spread.ToId)}!",
        FireBurned burned => $"{nameOf(burned.EntityId)} burns: the fire rolls {burned.Roll.Total}{(burned.FireDiceLeft == 0 ? "" : $" and {(burned.FireDiceLeft == 1 ? "1 fire die burns on" : $"{burned.FireDiceLeft} fire dice burn on")}")}.",
        FireOut { Cause: FireOutCause.Water } fireOut => $"The water puts out the fire on {nameOf(fireOut.EntityId)}.",
        FireOut { Cause: FireOutCause.Rolled } fireOut => $"{nameOf(fireOut.EntityId)} stops, drops and rolls, putting out the flames.",
        FireOut fireOut => $"The fire on {nameOf(fireOut.EntityId)} gutters out.",
        WentProne { Diving: true } prone => $"{nameOf(prone.EntityId)} dives beneath the surface.",
        Flipped flipped => $"The {nameOf(flipped.MonsterId)} is flipped onto its back!",
        WentProne prone => $"{nameOf(prone.EntityId)} drops prone.",
        GotUp up => $"{nameOf(up.EntityId)} gets back up.",
        HeroKeeledOver keeled => $"{nameOf(keeled.HeroId)} is Keeling Over! {keeled.Bane}.",
        KeelRolled { LifeGained: > 0, DeathGained: > 0 } keel => $"{nameOf(keel.HeroId)} rolls the Keel Die: {Count(keel.LifeGained, "mark")} of life and {Count(keel.DeathGained, "mark")} of death ({Marks(keel.LifeMarks, keel.DeathMarks)}).",
        KeelRolled { LifeGained: > 0 } keel => $"{nameOf(keel.HeroId)} rolls the Keel Die: {Count(keel.LifeGained, "mark")} of life ({Marks(keel.LifeMarks, keel.DeathMarks)}).",
        KeelRolled { DeathGained: > 0 } keel => $"{nameOf(keel.HeroId)} rolls the Keel Die: {Count(keel.DeathGained, "mark")} of death ({Marks(keel.LifeMarks, keel.DeathMarks)}).",
        KeelRolled keel => $"{nameOf(keel.HeroId)} rolls the Keel Die: a {keel.Keel}, nothing changes ({Marks(keel.LifeMarks, keel.DeathMarks)}).",
        HeroRoused roused => $"{nameOf(roused.ByHeroId)} tends {nameOf(roused.HeroId)}: {Count(roused.LifeGained, "mark")} of life{(roused.DoomDiceAdded > 0 ? $", but {roused.DoomDiceAdded} Doom {(roused.DoomDiceAdded == 1 ? "Die" : "Dice")} for their next Keel roll" : "")}.",
        KeelerStruck { AttackerId: 0 } struck => $"The flames sear {nameOf(struck.HeroId)} as they lie there: a mark of death ({struck.DeathMarks}/3).",
        KeelerStruck struck => $"{nameOf(struck.AttackerId)} strikes at {nameOf(struck.HeroId)} as they lie there: a mark of death ({struck.DeathMarks}/3).",
        HeroStoodUp stood => $"{nameOf(stood.HeroId)} stands back up with {Count(stood.Hearts, "heart")}!",
        HeroDied died => $"{nameOf(died.HeroId)} is dead. {died.Tombstone.Inscription}: {died.Tombstone.Epitaph}.",
        CardUsed used => $"{nameOf(used.HeroId)} uses the {cardNameOf(used.CardId)}.",
        CardEquipped equipped => $"{nameOf(equipped.HeroId)} equips the {cardNameOf(equipped.CardId)}.",
        CardBelted belted => $"{nameOf(belted.HeroId)} hangs the {cardNameOf(belted.CardId)} on their belt.",
        CardStowed stowed => $"{nameOf(stowed.HeroId)} packs away the {cardNameOf(stowed.CardId)}.",
        CardDropped dropped => $"{nameOf(dropped.HeroId)} drops the {cardNameOf(dropped.CardId)}.",
        ChestOpened { AddedToBackpack: true } chest => $"{nameOf(chest.HeroId)} opens the chest: {Cards(chest.CardIds)}!",
        ChestOpened chest => $"{nameOf(chest.HeroId)} opens the chest: {Cards(chest.CardIds)}, but runs out of room and leaves some on the floor.",
        OdHandRolled rolled => $"{nameOf(rolled.HeroId)} {Doing(rolled.Kind)} with {rolled.Total}{(rolled.Difficulty > 0 ? $" (needs {rolled.Difficulty})" : "")}.",
        CheckFailed { Kind: OdHandCheckKind.Steal } failed => $"{nameOf(failed.HeroId)} is caught and comes away with nothing.",
        CheckFailed failed => $"{nameOf(failed.HeroId)} fails{(failed.Kind is OdHandCheckKind.PickDoor or OdHandCheckKind.PickChest ? ": the lock holds" : "")}.",
        DoorUnlocked { BossDoor: true } door => $"{nameOf(door.HeroId)} turns the Boss Key. The Boss Door swings open!",
        DoorUnlocked { Picked: true } door => $"{nameOf(door.HeroId)} picks the lock. The door opens.",
        GateOpened { Picked: true } gate => $"{nameOf(gate.HeroId)} picks the gate's lock. It swings open.",
        GateOpened gate => $"{nameOf(gate.HeroId)} unlocks the gate with a key.",
        DoorUnlocked door => $"{nameOf(door.HeroId)} unlocks the door.",
        ChestUnlocked { Picked: true } chest => $"{nameOf(chest.HeroId)} picks the chest's lock.",
        ChestUnlocked chest => $"{nameOf(chest.HeroId)} unlocks the chest with a key.",
        SecretsFound { Doors.Count: 0, Traps.Count: 0, Stashes.Count: 0, Gates: null or { Count: 0 } } found => $"{nameOf(found.HeroId)} searches the room but finds nothing.",
        SecretsFound found => $"{nameOf(found.HeroId)} finds {Found(found)}!",
        TrapDisarmed disarmed => $"{nameOf(disarmed.HeroId)} disarms the trap.",
        TrapSprung { Kind: TrapKind.TrapDoor } sprung => $"The floor gives way under {nameOf(sprung.HeroId)}!",
        TrapSprung sprung => $"{nameOf(sprung.HeroId)} sets off a hidden {TrapName(sprung.Kind)}!",
        HeroFell { Jumped: true } fell => $"{nameOf(fell.HeroId)} jumps down the trap door to floor {fell.Floor}.",
        HeroFell fell => $"{nameOf(fell.HeroId)} tumbles down to floor {fell.Floor}.",
        KeysAppeared keys => $"{(keys.Count == 1 ? "A key" : $"{keys.Count} keys")} lie on the floor.",
        GoldFound gold => $"{nameOf(gold.HeroId)} pockets {gold.Amount} gold ({gold.Total} in the purse).",
        GoldAppeared gold => $"{gold.Amount} gold lies on the floor.",
        KeysFound keys => $"{nameOf(keys.HeroId)} takes {(keys.Count == 1 ? "a key" : $"{keys.Count} keys")} ({keys.Total} carried).",
        KeyUsed used => $"{nameOf(used.HeroId)} uses a key ({used.Left} left).",
        BossKeyFound found => $"{nameOf(found.HeroId)} finds the Boss Key!",
        BossKeyDropped => "The Boss Key falls to the floor.",
        WallBroken { Door: not null } => "The blast opens up the walled doorway!",
        WallBroken => "The blast breaks the cracked wall!",
        Stole { Key: true } stole => $"{nameOf(stole.ThiefId)} lifts a key from {nameOf(stole.VictimId)}.",
        Stole stole => $"{nameOf(stole.ThiefId)} steals {(stole.CardId is int card ? cardNameOf(card) : "something")} from {nameOf(stole.VictimId)}.",
        LootPickedUp loot => $"{nameOf(loot.HeroId)} picks up {string.Join(", ", loot.CardIds.Select(cardNameOf))}.",
        HeroDescended { FirstTimeOnFloor: true } descended =>
            $"{nameOf(descended.HeroId)} descends to floor {descended.Floor}. Luck and charges are restored.",
        HeroDescended descended => $"{nameOf(descended.HeroId)} descends to floor {descended.Floor}.",
        HeroAscended ascended => $"{nameOf(ascended.HeroId)} climbs back up to floor {ascended.Floor}.",
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

    private string Cards(IEnumerable<int> ids) => string.Join(" and ", ids.Select(cardNameOf));

    private static string Doing(OdHandCheckKind kind) => kind switch
    {
        OdHandCheckKind.PickDoor => "picks the door's lock",
        OdHandCheckKind.PickChest => "picks the chest's lock",
        OdHandCheckKind.Search => "searches the room",
        OdHandCheckKind.Disarm => "works at the trap",
        OdHandCheckKind.PickGate => "picks the gate's lock",
        OdHandCheckKind.Rouse => "tends a fallen ally",
        _ => "tries to steal",
    };

    private static string Found(SecretsFound found)
    {
        var parts = new List<string>();
        if (found.Doors.Count > 0)
        {
            parts.Add(found.Doors.Count == 1 ? "a secret door" : $"{found.Doors.Count} secret doors");
        }

        if (found.Traps.Count > 0)
        {
            parts.Add(found.Traps.Count == 1 ? "a trap" : $"{found.Traps.Count} traps");
        }

        if (found.Stashes.Count > 0)
        {
            parts.Add(found.Stashes.Count == 1 ? "a hidden stash" : $"{found.Stashes.Count} hidden stashes");
        }

        if (found.Gates is { Count: > 0 } gates)
        {
            parts.Add(gates.Count == 1 ? "a secret way through the wall" : $"{gates.Count} secret ways through the walls");
        }

        return string.Join(" and ", parts);
    }

    private static string TrapName(TrapKind kind) => kind switch
    {
        TrapKind.Spikes => "spike trap",
        TrapKind.Needle => "needle in the lock",
        TrapKind.Darts => "dart trap",
        _ => "trap",
    };

    private static string Command(CompanionOrder order) => order switch
    {
        CompanionOrder.Hold => "hold here",
        CompanionOrder.Fight => "fight",
        CompanionOrder.StayBack => "stay back",
        CompanionOrder.Tend => "tend the fallen",
        _ => "follow",
    };

    private static string Count(int amount, string noun) => $"{amount} {noun}{(amount == 1 ? "" : "s")}";

    private static string Marks(int life, int death) => $"life {life}/3, death {death}/3";

    private static string PowerDice(int amount) => $"{amount} Power {(amount == 1 ? "Die" : "Dice")}";
}
