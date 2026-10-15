# Woad — MVP Design

Draft v0.1, 2026-10-09. Numbers marked *(tunable)* are starting points for playtesting, not commitments.
Items under [Assumptions to confirm](#assumptions-to-confirm) were filled in by default and need a yes/no.

## Pitch

Woad is a turn-based roguelite dungeon crawler played like a board game. One to eight players share a
mouse and screen in hot-seat play. Every room is a 9×9 grid. Dice decide how far you move, how hard you
hit and how well you block. Cards are your gear, magic and loot, slotted onto your character. Players
cooperate to survive the dungeon but compete for treasure: the richest player to escape wins.

## Decisions

| Area | Decision |
| --- | --- |
| Engine | Godot 4.7.2 (.NET build), C# on .NET 10 (`net10.0`) |
| Players | 1–8, local hot-seat, mouse only |
| Play mode | Semi-co-op: no direct PvP damage; the escaped player with the most treasure wins |
| Rooms | 9×9 grids; players may split up into different rooms |
| Dice | d6 pools for movement, attack and defense. Each Attack or Move die is an action of its own, or several can be rolled together for a stronger one. Every 6 earns a Power Die to add or bank |
| Cards | Abilities and items (weapons, shields, armor, boots, spells, tools, bombs, potions, treasure); single-use or persistent; equipped into body slots |
| Card source | Starter kit, then loot from chests and monsters drawn from the dungeon's loot deck |
| Turn | Spend your Rumble, Wend and Od Hand dice on attacks, moves and Od Hand checks, plus 1 other action, 1 free loot and 1 free belt item, in any order |
| Secrets | Keys and locked doors and chests, a Boss Key to the Boss Door, secret doors, cracked walls broken by bombs, traps and trap doors |
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
3. **Run setup**: pick 1–8 saved characters (players and their companions) and a seat order, an optional seed, and whether friendly fire is on (off by default).
4. **Dungeon**: three floors, played in rounds.
5. **Results**: winner, treasure totals, XP gained, items kept or lost. Characters are saved.
6. Back to the main menu.

## Characters

### Stats

Stats are expressed directly as dice and points. Gear cards add to them.

| Stat | Meaning | Base *(tunable)* | Point cost *(tunable)* |
| --- | --- | --- | --- |
| Hearts | Maximum HP | 4 | 1 per heart |
| Wend (boot) | d6 for moving each turn; each move uses one or more | 1 | Gear only (e.g. boots) |
| Wend bonus | Tiles added to one move per turn (shown as the "+2" in "Wend 1d6+2"; the editor calls it Wend) | 2 | 2 per point |
| Rumble (fist) | d6 for attacking each turn; each attack uses one or more | 1 | 3 per die |
| Od Hand (hamsa) | d6 each turn for picking locks, searching, disarming traps and stealing | 1 | 3 per die |
| Tools | Fixed value added to one Od Hand roll per turn | 0 | Gear only (Lockpicks) |
| Defense | d6 rolled, all of them, whenever you're attacked | 1 | 3 per die |
| Weapon | Fixed value added to one attack per turn | 0 | Gear only (weapons) |
| Armor | Fixed value added to every defense | 0 | Gear only (shields, armor) |
| Luck | Rerolls of a single die, per floor | 0 | 2 per reroll |

- **Creation:** name (1–16 letters, numbers, spaces, apostrophes or hyphens), portrait, and 6 points to
  spend. Unspent points are kept for later. While at level 1, a character can buy at most +1 Attack and
  +1 Defense die *(tunable)*. Points already spent can't be refunded.
- **Progression:** XP from monster kills and floors reached. Level 2 takes 10 XP, level 3 takes 30, then
  60, 100 and so on (10 × 1, 3, 6, 10…). Each level grants 1 point.
- **Portraits:** for now, eight placeholder heraldic crests (a color and a sigil). The art pass replaces
  them with real portraits under the same ids.
- **Characters screen:** lists saved characters with their stats, gear and record. You can create,
  edit (rename, change portrait, spend points) or delete them; delete asks for a second click.
- **Party setup:** click characters to seat them; the order you pick them is the turn order. An
  optional seed replays a specific run.

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
| Weapon | Persistent | Wooden Sword (weapon 1), Knight's Sword (weapon 2), Short Bow (weapon 1, ranged 4) |
| Shield | Persistent | Buckler (armor 2), Tower Shield (armor 3, −1 Wend) |
| Armor | Persistent | Leather Cap (+1 Heart), Chain Mail (armor 1) |
| Boots | Persistent | Dash Boots (+1 Move die) |
| Spell | Charges or single-use | Flame Scroll (a 3-dice attack at range 3), Frost Wand (2 charges: freeze for a round) |
| Tool | Persistent | Boomerang (stun a monster within 4), Grapple Hook (pull yourself up to 4 tiles in a line; not built yet) |
| Bomb | Single-use | Bomb (range 3, a 2-dice attack on every monster in a 3×3 area) |
| Potion | Single-use | Health Potion (restore 3 hearts) |
| Treasure | Kept for score | Coin Pouch (5g), Emerald (15g), Golden Idol (40g) |

- **Starter kit** (new characters): Wooden Sword, Buckler, Health Potion.
- **Equipping, belting or stowing gear** is free while no monsters are in your room, as often as you like.
  In a fight, each one takes your action.
- **Greyed-out card buttons explain themselves:** hovering shows why, and clicking one writes the reason
  in the message log.
- **Using a belt item** doesn't take your action (once per turn). Using an item from the backpack takes your action.
- **Only single-use cards** (potions, bombs, scrolls) go on the belt. Reusable cards with an effect (boomerang,
  wand) must be equipped to use.
- **Dropping a card** onto your tile is free.
- **Looting** (picking up what's on your tile, or opening a chest) is free once per turn; after that it
  takes your action. Picking up fills the backpack as far as it can, and takes every key there too.
- **Chest loot** goes to the backpack, or onto your tile if the backpack is full.
- **Targeted cards** need line of sight and must have a target: a bomb must catch at least one monster or
  cracked wall in its blast, and potions only heal yourself and can't be used at full hearts.
- **Lockpicks** (tool, off hand): Tools 2. In the loot deck.
- **Stuns:** a stunned monster skips its next turn and can't be stunned again during the following round.
  Bosses can't be stunned.

## Turn structure

**Round:** each living player takes a turn in seat order. Then comes the **enemy phase**: monsters act in
every room that holds at least one player. Rooms with no players stay paused.

**Player turn:**

1. A turn banner shows the active player. The view jumps to their room.
2. In any order:
   - **Moves (Wend):** each move rolls one or more of your unused Wend dice, plus your Wend bonus on one move per
     turn, for that many steps. Reachable tiles are highlighted, and you click one to go there. Steps are
     orthogonal; walls and monsters block, allies don't. Steps last the whole turn: you can attack,
     act or roll another move in between, and any steps left over are added to the next move. Only a
     sprung trap ends your movement early. A negative Wend bonus always applies to your first move.
   - **Attacks (Rumble):** each attack uses one or more of your unused Rumble dice, plus your weapon's
     value on one attack per turn (only alongside at least one die). See [Combat](#combat).
   - **Od Hand checks:** each uses one or more of your unused Od Hand dice, plus your tools' value once a
     turn. See [Od Hand](#od-hand). They don't use your action.
   - **One other action:** use a card from your backpack or an equipped one, smash a pot, loot (after your
     free loot), or (in a fight) equip, belt or stow gear. Outside a fight, gear changes are free.
   - **One free loot** and **one free belt item**.
3. End turn. Your dice come back next turn.

On screen, the dice you have left this turn sit under **This turn**, a row each led by its symbol:
Rumble (a fist) and your weapon, Wend (a boot) and your Wend bonus, Od Hand (a hamsa) and your tools, and
your banked Power Dice. All but the Power Dice start selected, so clicking a monster, **Wend** or
**Search** uses everything in that row. Click dice to deselect some and split them across several rolls.

**Doors** sit at the middle tile of each room edge. Stepping through costs 1 step and places you at the
facing door of the next room.

## Combat

- The attacker rolls the Rumble dice they chose and adds them up, plus their weapon's value if they
  used it.
- The defender rolls all their Defense dice and adds their armor.
- If the attack total is higher, the defender loses **1 heart**. Ties go to the defender.
- Rolling dice together makes one attack more likely to land; splitting them gives more chances.
- **Card attacks** (Flame Scroll, bomb) roll their own dice and don't use your Rumble dice. A bomb's
  single roll is compared with each monster's defense in the blast.
- **Melee** needs an adjacent target. Heroes can strike diagonally too, unless walls close off both corners between them; monsters strike orthogonally only. **Ranged** needs line of sight; walls block it.
- **Line of sight** runs between tile centers. Only walls (cracked ones too) block it, not pits, chests or
  other figures, and grazing a wall's corner is allowed.
- **Luck** rerolls one of your own Wend, Rumble or Od Hand dice. A Wend die can be rerolled any time before
  your first step. With Luck left, an attack or Od Hand roll waits for you to reroll or confirm; spending
  your last Luck resolves it. Luck doesn't apply to defense rolls.

### Power Dice

- **Earning:** every **6** rolled earns a Power Die, on any roll by a hero or a monster: attacks,
  moves and defense.
- **Using one:** a Power Die is one more d6 rolled into a roll and added to it. If it also shows a 6,
  it earns another.
- **The bank:** each hero and monster can bank up to **3** *(tunable)*, kept until used (across turns
  and floors). A 6 rolled with a full bank is rolled into that roll straight away instead.
- **Your attacks and moves:** after a 6 you choose **Add Power Die** (roll it in now) or **Bank it**.
  Nothing else can happen until you choose. Banked dice show in the dice pool's POWER row, unselected;
  select them to roll them into your next attack or move.
- **Defense:** sixes are banked automatically. While a defense is losing and the banked dice could still
  make up the gap, they're rolled in one at a time to try to block.
- **Monsters** roll the sixes on their attacks straight in, and bank and spend on defense like heroes.
- A Luck reroll that comes up 6 earns a Power Die too. Od Hand rolls earn and use Power Dice like
  attacks and moves.

## Od Hand

Od Hand dice work like Rumble and Wend dice: choose how many to roll (plus your tools' value once a
turn), sixes earn Power Dice, and Luck can reroll them. None of it takes your action. A check succeeds
when the total reaches its number:

| Check | How | Number *(tunable)* |
| --- | --- | --- |
| **Pick a lock** | Stand in a locked doorway (or next to a locked chest) without a key and click it | Doors 4 + floor (at most 6); chests 5 + floor |
| **Search** | **Search** button: reveals everything in the room your total reaches | Secret doors 4 + floor; traps 3 + floor; stashes 5 + floor |
| **Disarm** | Click a trap you've found: on a tile or chest next to you, or your own doorway | 3 + floor |
| **Steal** | **Steal** button, then an adjacent monster or hero; once a turn | Beat their roll: a monster's defense dice, a hero's Od Hand dice and tools |

- **Stealing from a monster** (once per monster): a key (40% *(tunable)*) or a card from the loot deck.
- **Stealing from a hero:** one of their keys or backpack cards at random. Not equipped gear, belt items
  or the Boss Key.
- Door locks are kept low enough that one die (and its Power Dice) can always open them, so a locked door
  never strands the party.

## Keys, locks and secrets

- **Keys** are a counter, like gold, and don't take backpack space. They turn up when a monster falls
  (15%), in pots (10%), on the floor when a room is cleared (35%), in hidden stashes, and by stealing.
  A key opens one locked door or chest; using it is free. Downed heroes drop their keys.
- **Locked doors** (some doors on each floor): use a key or pick the lock. Once open, they stay open.
- **Locked chests** (iron-bound): use a key or pick the lock, then open as usual. They hold **two** cards,
  dealt as if from a floor deeper.
- **The Boss Door** is the only way into the boss room. Only the **Boss Key** opens it, and it can't be
  picked. The Boss Key lies in a chest in the last floor's treasure room, and a downed hero drops it.
- **Secret doors** link some neighboring rooms. They're hidden (not drawn, not on the map) until a search
  finds them; then they're ordinary doors.
- **Cracked walls** are walls a bomb's blast breaks. Some stand inside rooms with a stash sealed inside;
  some wall up a doorway between rooms.
- **Traps** start hidden. **Spikes** (1 heart) stop you where you step on them and stay armed. **Trap doors**
  drop you into the room underneath (1 heart); found ones can be jumped down safely (1 step).
  **Needles** in chests and **darts** in door frames hit the next hero to open or pass (1 heart). Found
  traps are shown, and paths go around them; you can still step onto one on purpose (it springs again
  and stops you). Od Hand disarms them. Traps never cut a room in two, and never sit beside a chest or pot.
- **Pits:** where a room lies underneath, its floor shows far down the pit; with nothing below (as on
  the last floor) the pit is black. You can walk into a pit: you lose a heart and drop into the room
  below, on the tile under the pit (or the nearest free one), or climb back out of a bottomless pit.
  **Jump** a 1-tile pit to the tile beyond: 2 steps and a roll of 4+ on one die; miss and you fall in.
- **Shove:** roll Rumble dice (and the weapon) at an adjacent monster as for an attack; beat its
  defense and it's pushed one tile straight away from you (diagonally too) instead of hurt. Onto a
  trap, the trap goes off on it (spikes: 1 heart; trap door: it falls). Into a pit, it drops to the
  room below and loses a heart, or, bottomless, is gone for good (it counts as your kill). Bosses can't
  be shoved, and the tile behind must be free floor, a trap or a pit. Flyers don't fall. Monsters
  don't shove yet.
- **Friendly fire** (a switch on the party screen, off by default, kept for Play again): blasts also
  catch heroes and companions in range, the thrower included, and you can attack and shove other
  heroes and companions in reach just like monsters (click them; Shove works on them too). Each
  defends with their own Defense dice and armor. A hero downed this way is downed as by a monster:
  no kill or XP for the attacker. The top bar shows **FF** while it's on.
- **Hidden stashes** hold a key or a card, spilled onto the floor when found.

### Monsters (first theme)

| Monster | Hearts | Move | Attack | Defense | Notes |
| --- | --- | --- | --- | --- | --- |
| Slime | 1 | 2 | 1d6 | 1d6 | When defeated, splits into two Slimelings (1 heart, 1d6 attack, no defense, don't split) |
| Bat | 1 | 4 | 1d6 | none | Flies over pits; moves erratically |
| Spear Guard | 2 | 3 | 1d6+1 | 1d6+1 | Reach 2 in a straight line |
| Archer Guard | 2 | 2 | 1d6+1 | 1d6+1 | Ranged 5; keeps its distance |
| Skeleton | 2 | 3 | 1d6+1 | 1d6+1 | Rises again once unless finished by a bomb or spell |
| **Boss: Iron Warden** | 8 | 3 | 2d6+1 | 1d6+1 | Every other turn, a flail sweep: one roll with all its dice against every hero within 2 |

Monsters follow the same dice rules; the +1s are weapon and armor values. Each turn a monster splits its
Attack dice into whichever attacks are likeliest to land the most hits on its target (its weapon goes on
the first), so a 2-dice monster makes two quick attacks against an unarmored hero but one big one against
a well-armored hero.

**Monster AI:** each monster heads for the closest tile it can attack from and stops as soon as it can
attack. Among the heroes it can reach, it attacks the one with the fewest hearts. Ranged monsters back away
from adjacent heroes before shooting, and erratic monsters take each step in a random direction half the
time. Monsters stay in their own room.

Monster stats and cards live in `src/Woad.Core/Content/Data/*.json`, and each dungeon theme has its own
file under `Themes/`.

## Dungeon

- **3 floors**, each 6–8 rooms, laid out by a random walk on a 5×5 room map that all floors share,
  so a pit or trap door drops into the room underneath (never into the boss room). A floor's Arrival
  room is under the stairs that lead to it. Above the last floor, rooms with nothing underneath get
  layouts without pits. A few extra doors between neighboring rooms add loops.
- **Room roles:** the walk's first room is the Arrival room, which has no monsters. The room farthest
  from it holds the Stairs down (floors 1–2) or the Boss and Exit (floor 3), behind the Boss Door. A dead
  end becomes the Treasure room (chests, one guard; on floor 3 it holds the Boss Key), and the rest are
  Combat rooms.
- **Room layouts** are handcrafted 9×9 text templates in the theme file (walls, pits, chests, stairs,
  exit, hero starts, monster spawn points, the boss). Each use is randomly rotated and mirrored. When
  content loads, every layout is checked: doorways are plain floor, and everything can be reached
  from every door.
- **Monsters** are bought for a room's spawn points from the theme's roster (weighted, some only from
  floor 2) until its threat budget runs out. Slimes and bats cost 1; guards and skeletons cost 2.
  Budgets *(tunable)*: combat and stairs rooms get floor + party size − 1, plus 1 in a trap room;
  treasure rooms 1; arrival rooms 0; the boss room the boss plus party-size worth of guards.
- **Doors** are gold bars on the board's edges. Stand in the doorway and click it again to go through
  (1 step). If someone is standing in the far doorway, you land on the nearest free tile. Locked doors are
  dark with a key mark, the Boss Door red, and walled-up doorways grey. See
  [Keys, locks and secrets](#keys-locks-and-secrets).
- **Secrets:** some doors are locked or trapped; some extra links between rooms are secret doors or
  cracked walls (never into the boss room); rooms get hidden traps (more on deeper floors), locked and
  trapped chests, and sometimes a hidden stash or a cracked wall with something inside.
- **Fog:** rooms stay hidden until someone enters. The minimap shows the floor's explored rooms,
  outlines of rooms seen through their doors, and every player's location.
- **Trap rooms** (one per floor from floor 2): nobody can leave through the doors while monsters
  remain. Heroes outside can still come in to help.
- **Clearing a room** restores 1 heart *(tunable)* to every hero in it.
- **Stairs:** stand on them and click again to go down (1 step). You arrive in the next floor's
  arrival room. Reaching a floor for the first time restores your Luck and recharges wands.
- **Exit:** sealed until the boss in its room is defeated. Stand on it and click again to escape. Your
  turn ends, and you're out of the run with your treasure.
- **Splitting up:** heroes can be on different rooms and floors. During the monsters' phase the view
  cuts to each room where something happens.
- **Seeds:** every run has one. The same seed and party size always build the same dungeon.

## Loot, downed players and winning

- **Chests** deal from the theme's loot deck. Some cards are marked with a minimum floor (better
  weapons and armor from floor 2, the Golden Idol on floor 3). Those stay in the deck until a chest on a
  deep enough floor turns them up.
- **Monster drops:** each monster has a drop chance *(tunable)*: slimes 10%, bats 15%, guards 25%,
  skeletons 30%. The Iron Warden always drops 3 cards. Drops land as a loot pile where the monster fell.
- **Pots** block movement until a hero beside them smashes one (uses the action). In the Emerald Keep:
  35% Loose Coins (2 gold), 25% a heart, otherwise nothing *(tunable)*.
- **Found items:** cards found during a run are tagged as found.
- **XP** *(tunable)*: each monster's XP goes to whoever defeats it (slime 2, guards 4, skeleton 5, Iron
  Warden 25). Reaching a floor for the first time gives 5, and escaping gives 10.
- **Downed players:** a player reduced to 0 hearts is downed for the rest of the run. Everything they found
  this run drops as a loot pile that anyone can pick up (an action). They keep the gear they brought in and
  their XP.
- **Exit:** beating the boss opens the exit, and stepping onto it escapes.
- **End of the run:** when every player has escaped or been downed. Companions don't keep a run going (see [Companions](#companions)).
- **Winner:** the escaped player with the highest treasure value. Ties go to the player with more kills.
- **Rewards:** everyone keeps their XP. Escaped players keep everything they carried out, and treasure
  becomes the character's gold. Downed players keep only the gear they brought in that they still hold.
  Single-use cards used up during the run are gone. Spending gold comes after the MVP.
- **Results screen:** when a run ends, results are saved automatically and the game moves to a results
  screen. It shows the winner(s), and for each hero how the run ended, kills, XP and any level-up, and
  the cards found and lost. **Play again** starts the same party in a new dungeon. Abandoning a run
  from the Menu saves nothing.

## Companions

Companions are saved characters the game plays, following their owner's orders. Recruiting them
from a tavern for gold and rescuing captives in the dungeon come later; for now they're made in the
character editor (choose **Companion** when creating; it can't be changed afterwards). You level
them up there like any character.

- **Seating:** a companion joins the player seated most recently and takes its turn right after
  theirs (and their other companions'). A companion can't be seated without a player before it. Freeing
  a player's seat frees their companions too.
- **Standing orders:** on your turn, **Orders** (beside End turn) sets each companion's order. It
  costs nothing and lasts until changed.
  - **Follow** (the default): stays within 2 steps of you, through doors and down the stairs, attacks
    what comes within reach and goes after monsters closing in on you.
  - **Hold:** stays on the tile it stood on when told, attacking what comes within reach.
  - **Fight:** goes after the monsters in the room; follows you once it's clear.
  - **Stay back:** keeps away from monsters near you, striking only when one is right next to it.
- **What companions don't do:** loot, open chests, smash pots, unlock, search or steal. They keep
  out of doorways, the stairs, the exit, loot and the spots beside unopened chests when they have
  nowhere to be. If their owner is downed or gone, they follow the first player still in.
- **Monster scaling:** a companion counts as half a hero (rounded up) when sizing rooms and the boss.
- **End of the run:** the run ends once every player is downed or out. When a player escapes, their
  companions in the same room escape with them. Companions still inside are left behind: like a
  downed hero, they lose what they found and keep the gear they brought and their XP.
- **Downed:** a downed companion sits out the rest of the run, like a hero. Reviving comes later.

## Persistence

- **Location:** one JSON file per character at `user://characters/{id}.json`. On Windows that is
  `%APPDATA%\Godot\app_userdata\Woad\characters\`.
- **Contents:** schema version, id, name, portrait, creation time, XP (level is derived from it), stat
  points spent and unspent, equipped cards, belt, backpack, gold, runs played and wins.
- **Writes:** each save goes to a temporary file first and then replaces the old one, so a crash can't
  leave a half-written character.
- **When saved:** on creation, on edit, and when a run ends (XP, levels, gold, runs and wins, and the
  gear brought home).
- **Damaged or outdated files:** unreadable files are skipped and reported on the Characters screen.
  Cards that no longer exist are dropped with a warning. A newer schema version is reported, not
  overwritten.
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
- **Dungeon art:** original 16×16 placeholder sprites, drawn at 2× (32px tiles), in a 16-bit palette
  that nods to *A Link to the Past*:
  - **Tiles:** green flagstones, stone blocks, chasms that join up with their neighbors, stairs, a
    barred or lit exit, chests and clay pots.
  - **Heroes:** one adventurer sprite whose tunic is tinted with the player's seat color.
  - **Monsters:** a sprite each. Slimelings are small slimes, the Iron Warden looms larger, and a
    collapsed skeleton becomes a bone pile.
- **UI art:** dark panels in riveted, gold-trimmed frames, and an original serif capitals pixel font
  for headings, titles and banners, in the spirit of *Castlevania*. Body text stays in the default
  font for readability.
- **Dice:** dice jitter and spin as they tumble, then land with a small pop. Successful faces glow gold.
- **Sound:** synthesized 8-bit effects for buttons, dice, footsteps, hits, blocks, defeats, healing,
  coins, chests, pots, doors, stairs, turns, the monsters' phase, being downed and victory.
- **Music:** synthesized chiptune songs that crossfade with the action: a title theme for the menus,
  a dungeon theme during a run, a boss theme while a hero shares a room with a living boss, and a
  short victory or defeat tune on the results screen. **Settings** on the main menu has music and
  sound-effect volume sliders, saved between sessions.
- **Where it comes from:** `tools/build-assets.py` generates all of it into `assets/` (see the
  README). Theme art lives under `assets/themes/<theme>/`, so a new dungeon is a new theme folder.
  Missing art falls back to plain shapes.

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
| M3 | Characters ✅ | Main menu, point-buy creation, character list, JSON save/load, run setup for 1–4 players (later 1–8) |
| M4 | Dungeon ✅ | 3 procedural floors, room templates, doors, splitting up, fog, minimap, stairs, boss, exit |
| M5 | Loot & scoring ✅ | Loot deck, chests, drops, downed loot piles, results screen, rewards persisted |
| M6 | Theme pass ✅ | Original placeholder art for the first theme, gothic UI frames, SFX, dice animation |

## Out of scope for the MVP

Online multiplayer, saving and resuming a run in progress, shops and spending gold, additional
dungeons and themes, keyboard or gamepad controls, music beyond placeholders, achievements and store
integration.

## Assumptions to confirm

These defaults were not covered in the kickoff questions:

1. Movement and monster melee are **orthogonal only**. Heroes can also attack diagonally.
2. Base stats and point costs as in [Stats](#stats). Movement is 1d6 + 2 at the start, and boots add dice. Starting heroes attack with 1d6 + 1 (Wooden Sword)
   and defend with 1d6 + 2 (Buckler).
3. The **belt** holds 2 consumables, and using one doesn't take your action.
4. **No friendly fire at all**, including bombs.
5. Downed players **sit out** the rest of the run. There is no revive in the MVP.
6. Monsters **can't follow** players into other rooms.
7. Carried-out gear persists **without a cap**, and dungeon difficulty scales to compensate.
8. **Saving a run in progress** is not part of the MVP. A run is about 45–60 minutes.
