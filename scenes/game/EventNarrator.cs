using Woad.Core.Events;
using Woad.Core.Content;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>Turns game events into sentences for the message log. Returns null for events shown only visually.</summary>
public sealed class EventNarrator(Func<int, string> nameOf, Func<int, string> cardNameOf, Func<string, string> spellNameOf)
{
    public string? Describe(GameEvent gameEvent) => gameEvent switch
    {
        RoundStarted round => $"Round {round.Round} begins.",
        MovementRolled rolled => $"{nameOf(rolled.HeroId)} rolls {rolled.Roll.Total} and can move {Count(rolled.MovementPoints, "step")}.",
        AttackRolled { Shove: true } shove => $"{nameOf(shove.AttackerId)} shoves {Names(shove.TargetIds)} with {shove.Total}.",
        AttackRolled { Miasma: true } miasma => $"The {nameOf(miasma.AttackerId)}'s miasma chokes {Names(miasma.TargetIds)}: {miasma.Total} of poison.",
        AttackRolled attack => $"{nameOf(attack.AttackerId)} attacks {Names(attack.TargetIds)} with {attack.Total}.",
        DieRerolled { MovementPoints: int points } rerolled =>
            $"{nameOf(rerolled.HeroId)} turns a {rerolled.OldFace} into a {rerolled.NewFace}: {Count(points, "step")}.",
        DieRerolled rerolled => $"{nameOf(rerolled.HeroId)} turns a {rerolled.OldFace} into a {rerolled.NewFace}.",
        HeroEnteredRoom entered => $"{nameOf(entered.HeroId)} enters a new room.",
        CompanionOrdered { Order: CompanionOrder.FindMe } ordered => $"{nameOf(ordered.OwnerId)} tells {nameOf(ordered.CompanionId)} to come and find {nameOf(ordered.OwnerId)}.",
        CompanionOrdered ordered => $"{nameOf(ordered.OwnerId)} tells {nameOf(ordered.CompanionId)} to {Command(ordered.Order)}.",
        FellIntoPit { Bottomless: true } fell => $"{nameOf(fell.EntityId)} falls into the pit!",
        FellIntoPit fell => $"{nameOf(fell.EntityId)} falls through the pit to the room below!",
        ClimbedOut climbed => $"{nameOf(climbed.EntityId)} claws their way back out.",
        Shoved shoved => $"{nameOf(shoved.TargetId)} is pushed back!",
        Pulled pulled => $"The {nameOf(pulled.MonsterId)} drags {nameOf(pulled.HeroId)} to its side!",
        ShoveResisted resisted => $"{nameOf(resisted.TargetId)} holds their ground.",
        MonsterTrapped trapped => $"{nameOf(trapped.MonsterId)} is shoved onto a {(trapped.Kind == TrapKind.TrapDoor ? "trap door" : "spike trap")}!",
        MonsterFell fell => $"{nameOf(fell.MonsterId)} drops to the floor below.",
        BullRushed { MonsterId: int struck } rushed => $"{nameOf(rushed.HeroId)} charges {rushed.Length} and slams into {nameOf(struck)}{(rushed.Leftover > 0 ? $", +{rushed.Leftover} to spare" : "")}!",
        BullRushed rushed when rushed.To == rushed.From => $"{nameOf(rushed.HeroId)} charges, and gets nowhere.",
        BullRushed rushed => $"{nameOf(rushed.HeroId)} charges {rushed.Length}.",
        Leaped { Roll: < 2 } leap => $"{nameOf(leap.HeroId)} stumbles on the leap ({leap.Roll}) and goes nowhere.",
        Leaped leap when leap.Landing == leap.Target => $"{nameOf(leap.HeroId)} leaps ({leap.Roll}) and lands on target.",
        Leaped leap => $"{nameOf(leap.HeroId)} leaps ({leap.Roll}) but comes down short!",
        StayedAfloat afloat => $"{nameOf(afloat.HeroId)} treads water, spending a Rumble die to stay afloat.",
        Drowned drowned => $"{nameOf(drowned.HeroId)} has no strength left to swim and is washed back to shore!",
        PowerDiceEarned earned => $"{nameOf(earned.HeroId)} rolled a 6 and earns a Power Die!",
        PowerDieAdded added => $"{nameOf(added.HeroId)} adds a Power Die: {added.Face}, for {added.Total}.",
        ConditionChanged { Count: 0 } cleared => $"{nameOf(cleared.EntityId)} is rid of {DieTypes.Label(cleared.Condition).ToLowerInvariant()}.",
        ConditionChanged changed => $"{nameOf(changed.EntityId)}: {DieTypes.Describe(changed.Condition, changed.Count)}.",
        GearCorroded corroded => $"Acid eats {nameOf(corroded.HeroId)}'s {cardNameOf(corroded.CardId)}: armor {corroded.ArmorLeft} until it's mended.",
        GearMended mended => $"{nameOf(mended.HeroId)}'s {cardNameOf(mended.CardId)} is mended good as new.",
        Slammed slammed => $"{nameOf(slammed.EntityId)} is slammed into {(slammed.IntoId is int into ? nameOf(into) : "the wall")} ({slammed.Roll.Total}).",
        DugOut dug => dug.BuriedLeft == 0 ? $"{nameOf(dug.EntityId)} digs free ({dug.Roll.Total})." : $"{nameOf(dug.EntityId)} digs ({dug.Roll.Total}): still buried ({dug.BuriedLeft}).",
        PoisonRolled poison => poison.LostHeart ? $"The poison bites {nameOf(poison.EntityId)} ({poison.Face}): a heart lost." : $"{nameOf(poison.EntityId)} fights the poison off ({poison.Face}){(poison.Face == 6 ? ", throwing off a token" : "")}.",
        AcidBurned acid => $"Acid eats at {nameOf(acid.EntityId)} ({acid.Roll.Total}).",
        FrozenSolid frozen => $"{nameOf(frozen.EntityId)} is frozen solid and can't move a muscle.",
        Staggered staggered => $"{nameOf(staggered.EntityId)} is confused: lashing out and staggering about!",
        PlantsGrew grew => $"Plants spring up{(grew.ForHeroes ? " for the party" : " for the monsters")}.",
        HeroSummoned summoned => $"{summoned.Name} {(summoned.Guise == "sentient_shrub" ? "springs up" : "rises")} to follow {nameOf(summoned.OwnerId)}!",
        PowerDiceBanked banked => $"{nameOf(banked.OwnerId)} banks {PowerDice(banked.Count)} ({banked.Banked} banked).",
        PowerDiceSpent spent => $"{nameOf(spent.OwnerId)} spends {PowerDice(spent.Count)}.",
        MonsterSurged surged => $"{nameOf(surged.MonsterId)} surges forward: {string.Join(" + ", surged.Faces)} more moves ({surged.Moves} in all).",
        Damaged { Amount: 0 } damaged => $"Miss! {nameOf(damaged.TargetId)} blocks it.",
        Damaged damaged => $"Hit! {nameOf(damaged.TargetId)} loses {Count(damaged.Amount, "heart")}.",
        MonsterDefeated defeated => $"{nameOf(defeated.MonsterId)} is defeated!",
        MonsterCollapsed collapsed => $"{nameOf(collapsed.MonsterId)} collapses into a heap of bones...",
        MonsterReassembled risen => $"{nameOf(risen.MonsterId)} pulls itself back together!",
        MonsterSpawned { Cause: SpawnCause.Split } spawned => $"A {nameOf(spawned.MonsterId)} wriggles free.",
        InfectionRolled { Resisted: true } resisted => $"{nameOf(resisted.HeroId)} fights off the {DieTypes.Label(resisted.Disease)} ({string.Join(", ", resisted.Faces)}, needing {resisted.Needed}).",
        InfectionRolled caught => $"{nameOf(caught.HeroId)} catches {DieTypes.Label(caught.Disease)} ({string.Join(", ", caught.Faces)}, needing {caught.Needed}): every heart they lose will turn black.",
        GraveDustBurst { Destroyed: true } burst => $"The {nameOf(burst.MonsterId)} bursts in a cloud of grave dust!",
        GraveDustBurst puff => $"Grave dust puffs off the {nameOf(puff.MonsterId)}.",
        HordeRoused { Level: 1 } => "The dead grow restless: more of them stir, farther off.",
        HordeRoused { Level: 2 } => "The dead know you are here: they rise all across the yard and come.",
        HordeRoused => "Every grave empties toward you: all the dead are coming.",
        ParthianSwing swing => $"The {nameOf(swing.MonsterId)} swings at {nameOf(swing.HeroId)} on the way out!",
        RoseFromGrave risen => $"{nameOf(risen.OpenerId)} disturbs the grave, and a {nameOf(risen.MonsterId)} climbs out!",
        MonsterStirred stirred => $"The {nameOf(stirred.MonsterId)} stirs.",
        MimicWoke { HeroId: 0 } woke => $"A sharp eye spots the teeth: that chest is a {nameOf(woke.MonsterId)}!",
        MimicWoke woke => $"The chest {nameOf(woke.HeroId)} reaches for snaps open: it's a {nameOf(woke.MonsterId)}!",
        Grabbed grabbed => $"The {nameOf(grabbed.MonsterId)} clamps onto {nameOf(grabbed.HeroId)}: no moving until they break free!",
        KnockedBack knocked => $"The {nameOf(knocked.MonsterId)}'s blow sends {nameOf(knocked.HeroId)} flying back!",
        Hexed { Drained: true } hexed => $"The {nameOf(hexed.MonsterId)}'s hex drains a Luck reroll from {nameOf(hexed.HeroId)}.",
        Hexed hexed => $"The {nameOf(hexed.MonsterId)}'s hex finds no luck to drain, and leaves {nameOf(hexed.HeroId)} reeling!",
        Engulfed engulfed => $"The {nameOf(engulfed.MonsterId)} swallows the loot in its path.",
        MonsterRaised raised => $"The {nameOf(raised.RaiserId)} raises a {nameOf(raised.MonsterId)} from the dead!",
        MerchantFreed { Picked: true } freed => $"{nameOf(freed.HeroId)} picks the lock on {nameOf(freed.MerchantId)}'s chains! The merchant follows {nameOf(freed.HeroId)} now.",
        MerchantFreed freed => $"{nameOf(freed.HeroId)} unlocks {nameOf(freed.MerchantId)}'s chains! The merchant follows {nameOf(freed.HeroId)} now.",
        MerchantFollowed followed => $"{nameOf(followed.MerchantId)} hurries after the party.",
        MerchantTraded { Bought: true } traded => $"{nameOf(traded.HeroId)} buys the {cardNameOf(traded.CardId)} from {nameOf(traded.MerchantId)} for {traded.Gold} gold.",
        MerchantTraded traded => $"{nameOf(traded.HeroId)} sells {nameOf(traded.MerchantId)} the {cardNameOf(traded.CardId)} for {traded.Gold} gold.",
        MerchantRobbed { Caught: true } robbed => $"{nameOf(robbed.MerchantId)} catches {nameOf(robbed.HeroId)} reaching for the wares! (Guild -{robbed.GuildLost}, Law -{robbed.LawLost})",
        MerchantRobbed robbed => $"{nameOf(robbed.HeroId)} lifts something from {nameOf(robbed.MerchantId)}'s wares. (Guild -{robbed.GuildLost}, Law -{robbed.LawLost})",
        MerchantSlain { ByHero: true } slain => $"{nameOf(slain.KillerId)} strikes {nameOf(slain.MerchantId)} down! The Guild and the Law will hear of this.",
        MerchantSlain slain => $"The {nameOf(slain.KillerId)} cuts {nameOf(slain.MerchantId)} down!",
        MerchantRescued rescued => $"{rescued.Name} is free of the dungeon at last! The Merchant Guild will remember this.",
        WeaponReloaded reloaded => $"{nameOf(reloaded.HeroId)} reloads the {cardNameOf(reloaded.CardId)}.",
        DaggerThrown thrown => $"{nameOf(thrown.HeroId)} throws a dagger!",
        Cleaved cleaved => $"{nameOf(cleaved.HeroId)}'s swing cleaves on into {nameOf(cleaved.MonsterId)}!",
        SpellCast cast => $"{nameOf(cast.HeroId)} casts {spellNameOf(cast.SpellId)} ({cast.Power} Power).",
        ExtraDiceRolled extra => $"An extra die against {Names(extra.TargetIds)}: +{extra.Roll.Total}.",
        IceLaid laid => $"Ice spreads over {Count(laid.Tiles.Count, "tile")} for {Count(laid.Rounds, "round")}.",
        IceMelted => "Some ice melts away.",
        Slid slid => $"{nameOf(slid.EntityId)} slips on the ice!",
        BoonGained { Boon: Boon.ShiningLight } shining => $"{nameOf(shining.HeroId)} shines with light: +{shining.Amount} Defense for {Count(shining.Rounds, "round")}.",
        BoonGained { Boon: Boon.Shadowbound } hidden => $"{nameOf(hidden.HeroId)} melts into the shadows for {Count(hidden.Rounds, "round")}.",
        BoonGained { Boon: Boon.ThievesOil } oiled => $"{nameOf(oiled.HeroId)} rubs on Thieves' Oil: disease is resisted on a 3 or more, and Necro blows hit {oiled.Amount} less, for {Count(oiled.Rounds, "round")}.",
        BoonGained shod => $"{nameOf(shod.HeroId)} gets Snowshoes for {Count(shod.Rounds, "round")}.",
        BoonEnded { Boon: Boon.Shadowbound } seen => $"{nameOf(seen.HeroId)} can be seen again.",
        BoonEnded ended => $"{nameOf(ended.HeroId)}'s {DieTypes.Name(ended.Boon)} fades.",
        BrazierLit => "A brazier flares into light.",
        SpellStudied studied => $"{nameOf(studied.HeroId)} unrolls the scroll and tries to learn {spellNameOf(studied.SpellId)}.",
        Backfired backfired => $"The scroll goes off in {nameOf(backfired.HeroId)}'s hands!",
        SpellLearned learned => $"{nameOf(learned.HeroId)} learns {spellNameOf(learned.SpellId)}.",
        PointSpent spent => $"{nameOf(spent.HeroId)} grows stronger: +1 {spent.Stat} (level {spent.Level}).",
        SkillLearned learned => $"{nameOf(learned.HeroId)} learns {learned.Skill.Name()}.",
        CompanionPolicySet => null,
        TorchBurnedOut burnedOut => $"{nameOf(burnedOut.HeroId)}'s torch gutters and burns out.",
        HeroShackled shackled => $"{nameOf(shackled.MonsterId)}'s chain shackles {nameOf(shackled.HeroId)}! No moving until a key or a picked lock ({shackled.Lock}+) frees them.",
        HeroUnshackled { Picked: true } freed => $"{nameOf(freed.ById)} picks the lock: {nameOf(freed.HeroId)}'s shackles fall away.",
        HeroUnshackled freed => $"{nameOf(freed.ById)} unlocks {nameOf(freed.HeroId)}'s shackles with a key.",
        Guarded guarded => $"{nameOf(guarded.GuardianId)} throws their shield in front of {nameOf(guarded.HeroId)}!",
        DefenseHeld { Skill: Skill.Parry } held => $"{nameOf(held.HeroId)} readies a parry: +{held.Roll.Total} on their next defense.",
        DefenseHeld held when held.ForHeroId == held.HeroId => $"{nameOf(held.HeroId)} stands guard: +{held.Roll.Total} on their next defense.",
        DefenseHeld held => $"{nameOf(held.HeroId)} guards {nameOf(held.ForHeroId)}: +{held.Roll.Total} on their next defense.",
        WentOnHighAlert alert => $"{nameOf(alert.HeroId)} is on High Alert: a Rumble die rolls in with their next search.",
        LastChanceUsed lucky => $"{nameOf(lucky.HeroId)}'s {cardNameOf(lucky.CardId)} turns the blow aside... and breaks!",
        MonsterRegenerated regenerated => $"The {nameOf(regenerated.MonsterId)}'s wounds knit back together.",
        CowardsFlight flight => $"{nameOf(flight.HeroId)} pulls the Coward's Cloak tight and makes ready to run ({flight.WendDice} more Wend {(flight.WendDice == 1 ? "die" : "dice")}){(flight.SlowedMonsterIds.Count > 0 ? "; the monsters beside them falter" : "")}!",
        Crumbled crumbled => $"The {nameOf(crumbled.MonsterId)} crumbles to dust.",
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
        FloorIgnited ignited => $"The floor bursts into flame! It'll burn for {ignited.Rounds} rounds.",
        FloorBurnedOut => "The flames on the floor burn out.",
        FireBurned burned => $"{nameOf(burned.EntityId)} burns: the fire rolls {burned.Roll.Total}{(burned.FireDiceLeft == 0 ? "" : $" and {(burned.FireDiceLeft == 1 ? "1 fire die burns on" : $"{burned.FireDiceLeft} fire dice burn on")}")}.",
        FireOut { Cause: FireOutCause.Water } fireOut => $"The water puts out the fire on {nameOf(fireOut.EntityId)}.",
        FireOut { Cause: FireOutCause.Aided } fireOut => $"The flames on {nameOf(fireOut.EntityId)} are smothered.",
        FireOut { Cause: FireOutCause.Rolled } fireOut => $"{nameOf(fireOut.EntityId)} stops, drops and rolls, putting out the flames.",
        FireOut fireOut => $"The fire on {nameOf(fireOut.EntityId)} gutters out.",
        WentProne { Diving: true } prone => $"{nameOf(prone.EntityId)} dives beneath the surface.",
        Flipped flipped => $"The {nameOf(flipped.MonsterId)} is flipped onto its back!",
        WentProne prone => $"{nameOf(prone.EntityId)} drops prone.",
        WingsBeat beat => $"{nameOf(beat.HeroId)} beats the Icarus Wings ({string.Join(" + ", beat.Faces)}): {beat.FlightLeft} flight left.",
        Flew { FlightLeft: 0 } flew => $"{nameOf(flew.HeroId)} flies {UiTheme.Count(flew.Path.Count, "tile", "tiles")}, out of flight: Land, or Fly!",
        Flew flew => $"{nameOf(flew.HeroId)} flies {UiTheme.Count(flew.Path.Count, "tile", "tiles")}.",
        Landed landed => $"{nameOf(landed.HeroId)} comes down to land.",
        WingsBroke broke => $"The Icarus Wings break! {nameOf(broke.HeroId)} plummets.",
        GotUp up => $"{nameOf(up.EntityId)} gets back up.",
        HeroKeeledOver keeled => $"{nameOf(keeled.HeroId)} is Keeling Over! {keeled.Bane}.",
        KeelRolled { LifeGained: > 0, DeathGained: > 0 } keel => $"{nameOf(keel.HeroId)} rolls the Keel Die: {Count(keel.LifeGained, "mark")} of life and {Count(keel.DeathGained, "mark")} of death ({Marks(keel.LifeMarks, keel.DeathMarks)}).",
        KeelRolled { LifeGained: > 0 } keel => $"{nameOf(keel.HeroId)} rolls the Keel Die: {Count(keel.LifeGained, "mark")} of life ({Marks(keel.LifeMarks, keel.DeathMarks)}).",
        KeelRolled { DeathGained: > 0 } keel => $"{nameOf(keel.HeroId)} rolls the Keel Die: {Count(keel.DeathGained, "mark")} of death ({Marks(keel.LifeMarks, keel.DeathMarks)}).",
        KeelRolled keel => $"{nameOf(keel.HeroId)} rolls the Keel Die: a {keel.Keel}, nothing changes ({Marks(keel.LifeMarks, keel.DeathMarks)}).",
        HeroRoused roused => $"{nameOf(roused.ByHeroId)} aids {nameOf(roused.HeroId)}: {Count(roused.LifeGained, "mark")} of life{(roused.DoomDiceAdded > 0 ? $", but {roused.DoomDiceAdded} Doom {(roused.DoomDiceAdded == 1 ? "Die" : "Dice")} for their next Keel roll" : "")}.",
        Sanctified { Destroyed: true } holy => $"{nameOf(holy.ByHeroId)} throws Holy Water: the {nameOf(holy.MonsterId)} burns away to nothing!",
        Sanctified holy => $"{nameOf(holy.ByHeroId)} throws Holy Water: the {nameOf(holy.MonsterId)} shrieks as it sears.",
        SoakedUp soaked => $"The Sineater Doll soaks {soaked.Stacks} {soaked.Ailment.Name()} off {nameOf(soaked.TargetId)} ({soaked.Held}/{Woad.Core.Rules.GameRules.DollCapacity} held).",
        DollBurst burst => $"The Sineater Doll bursts, and its sins fall on {Names(burst.Caught)}!",
        KeelerStruck { AttackerId: 0 } struck => $"The flames sear {nameOf(struck.HeroId)} as they lie there: a mark of death ({struck.DeathMarks}/3).",
        KeelerStruck struck => $"{nameOf(struck.AttackerId)} strikes at {nameOf(struck.HeroId)} as they lie there: a mark of death ({struck.DeathMarks}/3).",
        HeroStoodUp stood => $"{nameOf(stood.HeroId)} stands back up with {Count(stood.Hearts, "heart")}!",
        HeroDied died => $"{nameOf(died.HeroId)} is dead. {died.Tombstone.Inscription}: {died.Tombstone.Epitaph}.",
        CardUsed used => $"{nameOf(used.HeroId)} uses the {cardNameOf(used.CardId)}.",
        CardEquipped equipped => $"{nameOf(equipped.HeroId)} equips the {cardNameOf(equipped.CardId)}.",
        CardBelted belted => $"{nameOf(belted.HeroId)} hangs the {cardNameOf(belted.CardId)} on their belt.",
        CardStowed stowed => $"{nameOf(stowed.HeroId)} packs away the {cardNameOf(stowed.CardId)}.",
        CardDropped { Count: > 1 } dropped => $"{nameOf(dropped.HeroId)} puts down {dropped.Count} {cardNameOf(dropped.CardId)}.",
        CardDropped dropped => $"{nameOf(dropped.HeroId)} puts down the {cardNameOf(dropped.CardId)}.",
        ChestOpened { CardIds.Count: 0 } chest => $"{nameOf(chest.HeroId)} opens it: nothing but dust.",
        ChestOpened chest => $"{nameOf(chest.HeroId)} opens the chest: {Cards(chest.CardIds)}!",
        OdHandRolled rolled => $"{nameOf(rolled.HeroId)} {Doing(rolled.Kind)} with {rolled.Total}{(rolled.Difficulty > 0 ? $" (needs {rolled.Difficulty})" : "")}.",
        CheckFailed { Kind: OdHandCheckKind.Steal } failed => $"{nameOf(failed.HeroId)} is caught and comes away with nothing.",
        CheckFailed { Kind: OdHandCheckKind.Retreat } failed => $"{nameOf(failed.HeroId)} is cut off!",
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
        CritRolled crit => $"A 6! {nameOf(crit.HeroId)} rolls {crit.Dice} more {(crit.Dice == 1 ? "die" : "dice")} straight in.",
        LockpickSnapped { Left: 0 } snapped => $"{nameOf(snapped.HeroId)}'s last lockpick snaps!",
        LockpickSnapped snapped => $"A lockpick snaps! {nameOf(snapped.HeroId)} has {snapped.Left} left.",
        CardTraded { Count: > 1 } traded => $"{nameOf(traded.FromHeroId)} hands {nameOf(traded.ToHeroId)} {traded.Count} {cardNameOf(traded.CardId)}.",
        CardTraded traded => $"{nameOf(traded.FromHeroId)} hands {nameOf(traded.ToHeroId)} the {cardNameOf(traded.CardId)}.",
        GoldTraded gold => $"{nameOf(gold.FromHeroId)} hands {nameOf(gold.ToHeroId)} {gold.Amount} gold.",
        KeyTraded { BossKey: true } key => $"{nameOf(key.FromHeroId)} hands {nameOf(key.ToHeroId)} the Boss Key.",
        KeyTraded key => $"{nameOf(key.FromHeroId)} hands {nameOf(key.ToHeroId)} a key.",
        CompanionOutfitted outfit => $"{nameOf(outfit.OwnerId)} has {nameOf(outfit.CompanionId)} {outfit.Action switch { Woad.Core.Commands.OutfitAction.Equip => "equip", Woad.Core.Commands.OutfitAction.Belt => "belt", _ => "stow" }} the {cardNameOf(outfit.CardId)}.",
        LootPickedUp loot => $"{nameOf(loot.HeroId)} picks up {string.Join(", ", loot.CardIds.Select(cardNameOf))}.",
        HeroDescended { FirstTimeOnFloor: true } descended =>
            $"{nameOf(descended.HeroId)} descends to floor {descended.Floor}. Luck and charges are restored.",
        HeroDescended descended => $"{nameOf(descended.HeroId)} descends to floor {descended.Floor}.",
        HeroAscended ascended => $"{nameOf(ascended.HeroId)} climbs back up to floor {ascended.Floor}.",
        HeroEscaped escaped => $"{nameOf(escaped.HeroId)} escapes the dungeon with {escaped.TreasureValue}g of treasure!",
        HeroRetreated retreated => $"{nameOf(retreated.HeroId)} finds a way out and gets clear of the fight.",
        Routed routed => $"{nameOf(routed.HeroId)} can't find a way out, bolts {routed.Steps} steps in a panic and falls flat!",
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
        OdHandCheckKind.Rouse => "aids a fallen ally",
        OdHandCheckKind.PickChains or OdHandCheckKind.PickShackles => "picks the lock",
        OdHandCheckKind.DefuseBomb => "works at the fuse",
        OdHandCheckKind.Retreat => "looks for a way out",
        OdHandCheckKind.Cure => "gives aid",
        OdHandCheckKind.Learn => "studies the scroll",
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
        CompanionOrder.Aid => "aid the fallen and the ailing",
        CompanionOrder.Burgle => "burgle the room",
        CompanionOrder.Loot => "loot the room",
        CompanionOrder.Explore => "explore",
        _ => "follow",
    };

    private static string Count(int amount, string noun) => $"{amount} {noun}{(amount == 1 ? "" : "s")}";

    private static string Marks(int life, int death) => $"life {life}/3, death {death}/3";

    private static string PowerDice(int amount) => $"{amount} Power {(amount == 1 ? "Die" : "Dice")}";
}
