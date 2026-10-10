# Woad — MVP Design

Draft v0.1, 2026-10-09. Numbers marked *(tunable)* are starting points for playtesting, not commitments.
Items under [Assumptions to confirm](#assumptions-to-confirm) were filled in by default and need a yes/no.

## Pitch

Woad is a turn-based roguelite dungeon crawler played like a board game. One to four players share a
mouse and screen in hot-seat play. Every room is a 9×9 grid. Dice decide how far you move, how hard you
hit and how well you block. Cards are your gear, magic and loot, slotted onto your character. Players
cooperate to survive the dungeon but compete for treasure: the richest player to escape wins.

## Decisions

| Area | Decision |
| --- | --- |
| Engine | Godot 4.7.2 (.NET build), C# on .NET 10 (`net10.0`) |
| Players | 1–4, local hot-seat, mouse only |
| Play mode | Semi-co-op: no direct PvP damage; the escaped player with the most treasure wins |
| Rooms | 9×9 grids; players may split up into different rooms |
| Dice | d6 pools for movement, attack and defense; dice also resolve chance |
| Cards | Abilities and items (weapons, shields, armor, boots, spells, tools, bombs, potions, treasure); single-use or persistent; equipped into body slots |
| Card source | Starter kit, then loot from chests and monsters drawn from the dungeon's loot deck |
| Turn | Move + 1 action, in either order |
| Combat | On the room grid; an enemy phase follows each round of player turns |
| Characters | Created before play: name, portrait, classless point-buy of dice stats |
| Death | Roguelite: keep XP, level and the gear you brought in; lose what you found this run |
| Dungeon | Procedural, 3 floors of 6–8 rooms; a boss and the exit on floor 3 |
| Persistence | Local JSON file per character, behind an interface that a server can replace later |
| First theme | Inspired by *A Link to the Past*: 16-bit green-and-stone dungeon (original art; homage only) |
| UI | Inspired by *Castlevania*: gothic frames, ornate gold trim, dark panels (original art) |
| Presentation | 640×360 base resolution, 32px tiles, integer scaling, original placeholder pixel art |

## Game flow

1. **Main menu**: Play, Characters, Quit.
2. **Characters**: create, view or delete saved characters.
3. **Run setup**: pick 1–4 saved characters and a seat order, plus an optional seed.
4. **Dungeon**: three floors, played in rounds.
5. **Results**: winner, treasure totals, XP gained, items kept or lost. Characters are saved.
6. Back to the main menu.

## Characters

### Stats

Stats are expressed directly as dice and points. Gear cards add to them.

| Stat | Meaning | Base *(tunable)* | Point cost *(tunable)* |
| --- | --- | --- | --- |
| Hearts | Maximum HP | 4 | 1 per heart |
| Move dice | d6 rolled for movement | 1 | Gear only (e.g. boots) |
| Speed | Flat tiles added to the movement roll | 2 | 2 per point |
| Attack | d6 rolled when attacking | 1 | 3 per die |
| Defense | d6 rolled when attacked | 1 | 3 per die |
| Luck | Rerolls of a single die, per floor | 0 | 2 per reroll |

- **Creation:** name, portrait, and 6 points to spend. At creation, at most +1 Attack and +1 Defense die *(tunable)*.
- **Progression:** XP from monster kills and floors reached. Each level grants 1 point.

### Equipment slots

| Slot | Holds |
| --- | --- |
| Head | Helmets, hoods |
| Body | Armor, robes |
| Main hand | Weapons, wands, rods |
| Off hand | Shields, off-hand weapons, tools |
| Feet | Boots |
| Belt | Up to 2 consumables (potions, bombs) ready for quick use |

- Two-handed items fill both the main hand and the off hand.
- **Backpack:** 6 cards that are carried but not equipped *(tunable)*.

## Cards

Each card has: id, name, type, slot (if equippable), usage (persistent, single-use or charges),
stat/dice modifiers, effect, gold value and rarity. Each dungeon theme has its own loot deck.

| Type | Usage | Examples (placeholder names) |
| --- | --- | --- |
| Weapon | Persistent | Wooden Sword (+1 Attack), Knight's Sword (+2 Attack), Short Bow (ranged 4) |
| Shield | Persistent | Buckler (+1 Defense), Tower Shield (+2 Defense, −1 Speed) |
| Armor | Persistent | Leather Cap (+1 Heart), Chain Mail (+1 Defense) |
| Boots | Persistent | Dash Boots (+1 Move die) |
| Spell | Charges or single-use | Flame Scroll (3 Attack dice at range 3), Frost Wand (2 charges: freeze for a round) |
| Tool | Persistent | Boomerang (stun a monster within 4), Grapple Hook (pull yourself up to 4 tiles in a line; not built yet) |
| Bomb | Single-use | Bomb (range 3, 2 Attack dice against every monster in a 3×3 area) |
| Potion | Single-use | Health Potion (restore 3 hearts) |
| Treasure | Kept for score | Coin Pouch (5g), Emerald (15g), Golden Idol (40g) |

- **Starter kit** (new characters): Wooden Sword, Buckler, Health Potion.
- **Equipping or swapping gear** takes your action.
- **Using a belt item** doesn't take your action (once per turn). Using an item from the backpack takes your action.
- **Only single-use cards** (potions, bombs, scrolls) go on the belt. Reusable cards with an effect (boomerang,
  wand) must be equipped to use.
- **Dropping a card** onto your tile is free. Picking up loot from your tile takes your action and fills the
  backpack as far as it can.
- **Chest loot** goes to the backpack, or onto your tile if the backpack is full.
- **Targeted cards** need line of sight and must have a target: a bomb must catch at least one monster in its
  blast, and potions only heal yourself and can't be used at full hearts.
- **Stuns:** a stunned monster skips its next turn and can't be stunned again during the following round.
  Bosses can't be stunned.

## Turn structure

**Round:** each living player takes a turn in seat order. Then comes the **enemy phase**: monsters act in
every room that holds at least one player. Rooms with no players stay paused.

**Player turn:**

1. A turn banner shows the active player. The view jumps to their room.
2. **Move** (optional): click Roll, then move up to Move dice + Speed tiles. Reachable tiles are
   highlighted, and you click one to go there. Steps are orthogonal; walls and monsters block, allies don't.
   Unused steps are lost.
3. **Action** (optional, before or after moving, but not in the middle of a move): attack, use a card,
   open a chest or pick up loot, or equip/swap gear.
4. End turn.

**Doors** sit at the middle tile of each room edge. Stepping through costs 1 step and places you at the
facing door of the next room.

## Combat

- The attacker rolls Attack dice; each **5–6** is a hit.
- The defender rolls Defense dice. Each block cancels one hit: players block on **5–6** and monsters on
  **6** *(tunable)*.
- Each remaining hit removes 1 heart.
- **Melee** needs an orthogonally adjacent target. **Ranged** needs line of sight; walls block it.
- **Line of sight** runs between tile centers. Only walls block it, not pits, chests or other figures, and
  grazing a wall's corner is allowed.
- **Luck** rerolls one of your own movement or attack dice. A movement die can be rerolled any time before
  your first step. With Luck left, an attack waits for you to reroll or confirm; spending your last Luck
  resolves it. Luck doesn't apply to defense rolls.

### Monsters (first theme)

| Monster | Hearts | Move | Attack | Defense | Notes |
| --- | --- | --- | --- | --- | --- |
| Slime | 1 | 2 | 1 | 1 | When defeated, splits into two Slimelings (1 heart, 0 defense, don't split) |
| Bat | 1 | 4 | 1 | 0 | Flies over pits; moves erratically |
| Spear Guard | 2 | 3 | 2 | 1 | Reach 2 in a straight line |
| Archer Guard | 2 | 2 | 2 | 1 | Ranged 5; keeps its distance |
| Skeleton | 2 | 3 | 2 | 1 | Rises again once unless finished by a bomb or spell |
| **Boss: Iron Warden** | 10 | 3 | 3 | 2 | Every other turn, a flail sweep hits every tile within 2 |

**Monster AI:** each monster heads for the closest tile it can attack from and stops as soon as it can
attack. Among the heroes it can reach, it attacks the one with the fewest hearts. Ranged monsters back away
from adjacent heroes before shooting, and erratic monsters take each step in a random direction half the
time. Monsters stay in their own room.

Monster stats and cards live in `src/Woad.Core/Content/Data/*.json`, and each dungeon theme has its own
file under `Themes/`.

## Dungeon

- **3 floors**, each 6–8 rooms, laid out by a random walk on a 5×5 room map.
- **Room roles:** Arrival, Combat, Treasure (big chest), Stairs down (floors 1–2), and Boss + Exit (floor 3).
- **Room contents** come from handcrafted 9×9 text templates (walls, pits, pots, chests, spawn points),
  mirrored or rotated for variety.
- **Fog:** rooms stay hidden until someone enters. The minimap shows explored rooms and every player's
  location.
- **Trap rooms** (homage): the doors shut while monsters remain.
- **Scaling:** difficulty rises with floor depth and party size.
- **Seeds:** every run has one, and the same seed always builds the same dungeon.

## Loot, downed players and winning

- **Sources:** chests draw from the theme's loot deck, with rarity weighted by floor. Monsters have a drop
  chance, and pots can hold coins or hearts.
- **Found items:** cards found during a run are tagged as found.
- **Downed players:** a player reduced to 0 hearts is downed for the rest of the run. Everything they found
  this run drops as a loot pile that anyone can pick up (an action). They keep the gear they brought in and
  their XP.
- **Exit:** beating the boss opens the exit, and stepping onto it escapes.
- **End of the run:** when every player has escaped or been downed.
- **Winner:** the escaped player with the highest treasure value. Ties go to the player with more kills.
- **Rewards:** everyone keeps their XP. Escaped players keep everything they carried out, and treasure
  becomes the character's gold. Spending gold comes after the MVP.

## Persistence

- **Location:** one JSON file per character at `user://characters/{id}.json`. On Windows that is
  `%APPDATA%\Godot\app_userdata\Woad\characters\`.
- **Contents:** schema version, id, name, portrait, level, XP, stat points, equipment, backpack, gold,
  runs played and wins.
- **When saved:** on creation, on edit and when a run ends.
- **Code seam:** the game talks to `ICharacterRepository`. The MVP ships a JSON implementation, and
  online play will swap in a server-backed one.

## Presentation

```
640 px
┌──────────────┬──────────────────────┬──────────────┐
│ Active hero  │ floor / room / turn  │ Backpack (6) │
│ portrait     ├──────────────────────┤ Belt (2)     │
│ hearts/stats │                      │              │
│              │    9×9 room grid     │ Dice tray    │ 360 px
│ Equipment    │   288 × 288 px       │              │
│ slots        │                      │ Actions      │
│              ├──────────────────────┤              │
│              │ message log/minimap  │              │
└──────────────┴──────────────────────┴──────────────┘
   176 px              288 px              176 px
```

- **Scaling:** a 640×360 layout, integer scaled to 1280×720, 1920×1080, 2560×1440 and 4K, with
  nearest-neighbor filtering. The window stretches in `canvas_items` mode, so text renders at full
  resolution and stays sharp while the layout keeps its 640×360 coordinates.
- **Dungeon art:** placeholder tiles and sprites are original, in a 16-bit palette that nods to
  *A Link to the Past*.
- **UI art:** dark panels, ornate gold and bone trim and a gothic pixel font, in the spirit of
  *Castlevania*. The font must be open-licensed and is still to be chosen.
- **Dice:** animated d6 in the dice tray. Hits and blocks are highlighted.
- **Theme files:** everything themeable lives under `assets/themes/<theme>/`: tiles, monsters, card art,
  palette, music and SFX. New dungeons are new themes.

## Architecture

```
Woad (Godot project, repo root)          Woad.Core (src/, pure C#, no Godot)
  scenes, UI, input, animation   ──────▶   game state, rules, commands → events
  mouse click → Command                     dice + seeded RNG (PCG32)
  Event → animation/sound       ◀──────     dungeon generation, content definitions
  JsonCharacterRepository path              character model, ICharacterRepository
                                          Woad.Core.Tests (tests/, xUnit)
```

- **Woad.Core never references Godot.** All rules can be unit tested and could run unchanged on a server.
- **State changes only through commands** (`Move`, `Attack`, `UseCard`, `Equip`, `EndTurn`, …). Core
  validates each command and returns events (or a rejection), and Godot animates the events. Online play
  becomes: clients send commands, and an authoritative server runs Core and broadcasts events.
- **All randomness goes through a seeded `IRandomSource`.** PCG32 has a specified algorithm, so seeds
  reproduce exactly across machines.
- **Content** (cards, monsters, room templates, themes) is data files owned by Core, so a server and the
  client share the same definitions.

## Milestones

| # | Milestone | Done when |
| --- | --- | --- |
| M0 | Scaffold ✅ | Godot 4.7 + .NET 10 project builds, tests pass, and the 640×360 layout runs |
| M1 | Core rules ✅ | Room grid, pathing, dice, turn loop, combat, cards/slots/backpack, monster AI: all unit tested |
| M2 | Playable room ✅ | One room in Godot: mouse move/attack, dice tray, hot-seat turns, enemy phase |
| M3 | Characters | Main menu, point-buy creation, character list, JSON save/load, run setup for 1–4 players |
| M4 | Dungeon | 3 procedural floors, room templates, doors, splitting up, fog, minimap, stairs, boss, exit |
| M5 | Loot & scoring | Loot deck, chests, drops, downed loot piles, results screen, rewards persisted |
| M6 | Theme pass | Original placeholder art for the first theme, gothic UI frames, SFX, dice animation |

## Out of scope for the MVP

Online multiplayer, saving and resuming a run in progress, shops and spending gold, additional
dungeons and themes, keyboard or gamepad controls, music beyond placeholders, achievements and store
integration.

## Assumptions to confirm

These defaults were not covered in the kickoff questions:

1. Movement and melee are **orthogonal only** (no diagonals).
2. Base stats and point costs as in [Stats](#stats). Movement is 1d6 + 2 at the start, and boots add dice.
3. The **belt** holds 2 consumables, and using one doesn't take your action.
4. **No friendly fire at all**, including bombs.
5. Downed players **sit out** the rest of the run. There is no revive in the MVP.
6. Monsters **can't follow** players into other rooms.
7. Carried-out gear persists **without a cap**, and dungeon difficulty scales to compensate.
8. **Saving a run in progress** is not part of the MVP. A run is about 45–60 minutes.
