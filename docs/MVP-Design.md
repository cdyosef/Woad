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
| Dice | d6 pools for movement, attack and defense. Each Attack or Move die is an action of its own, or several can be rolled together for a stronger one. A 6 on a plain die earns a Power Die, to add to the roll or bank; a 6 on a Star die surges (a Power Die rolls in and another is banked). Other typed dice (fire, ice, poison...) roll more of their type on a 6 and lay their effect on a hit |
| Cards | Abilities and items (weapons, shields, armor, boots, spells, tools, bombs, potions, treasure); single-use or persistent; equipped into body slots |
| Card source | Starter kit, then loot from chests and monsters drawn from the dungeon's loot deck |
| Turn | Spend your Rumble, Wend and Od Hand dice on attacks, moves and Od Hand checks, plus 1 other action, 1 free loot and 1 free belt item, in any order |
| Secrets | Keys and locked doors and chests, a Boss Key to the Boss Door, secret doors, cracked walls broken by bombs, traps and trap doors |
| Combat | On the room grid; an enemy phase follows each round of player turns |
| Characters | Created before play: name, look and colors, crest, classless point-buy of dice stats |
| Death | Roguelite: keep XP, level and the gear you brought in; lose what you found this run |
| Dungeon | Procedural, 3 floors of 6–8 rooms; a boss and the exit on floor 3 |
| Persistence | Local JSON file per character, behind an interface that a server can replace later |
| Dungeons | Chosen on the party screen. The Emerald Keep, inspired by *A Link to the Past*: 16-bit green-and-stone (original art; homage only); and the Sunken Temple: flooded sandstone and coral |
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
| Hearts | Maximum HP | 3 | 1 per heart |
| Wend (boot) | d6 for moving each turn; each move uses one or more | 1 | 3, 3, 3, then 4, 5, 6 per die |
| Wend bonus | Tiles added to one move per turn (shown as the "+2" in "Wend 1d6+2"; the editor calls it Wend) | 2 | 2 per point |
| Rumble (fist) | d6 for attacking each turn; each attack uses one or more | 1 | 3, 3, 3, then 4, 5, 6 per die; the first brings Shove free (or Bull Rush, if you know Shove) |
| Od Hand (hamsa) | d6 each turn for the Od Hand skills you've learned (see [Skills](#skills)) | 0 | 1, 2, 2, 3, 4, 5 per die; the first brings an Od Hand skill of your choice free |
| Tools | Fixed value and dice added to one roll a turn of the skills they serve | 0 | Gear only (Lockpicks: +1 and 1d6) |
| Defense | The first d6 (the base die) rolled against every attack; the rest are extra dice, rolled in one at a time only while a defense is losing, then spent until your next turn | 1 | 3, 3, 3, then 4, 5, 6 per die |
| Weapon | Fixed value added to one attack per turn | 0 | Gear only (weapons) |
| Armor | Fixed value added to every defense | 0 | Gear only (shields, armor) |
| Block | Added, all of it, to the first defense that's losing (before extra dice), then spent until your next turn | 0 | Gear only (shields) |
| Luck | Rerolls of a single die, per floor | 0 | 1 per reroll |
| Magic (pentacle) | A Power Dice bank slot and a basic spell per rank, and its rank added to spells cast (see [Magic](#magic)) | 0 | 2 per rank |

At level 1 a character may buy at most **2** extra Rumble, Wend, Defense and Od Hand dice each. The
editor shows what each stat's next step costs.

### Skills

Each skill costs **1 point**. The **Skills...** window in the character editor lists them, and in a run
the Party menu can spend points on them too.

| Skill | Pool | What it does |
| --- | --- | --- |
| Search, Steal, Pick Lock, Disarm, Aid | Od Hand | Lets your Od Hand dice roll on that check (see [Od Hand](#od-hand)) |
| Parry | Od Hand | Roll an Od Hand die (a 6 rolls another) and hold it: it adds to your next defense before your next turn |
| Shove | Rumble | Push a monster a tile (see [Combat](#combat)) |
| Bull Rush | Rumble | Needs Shove. Charge in a straight line, diagonals too, as far as a Wend die (a 6 rolls another) and the Wend bonus carry you, at least a tile and with no stopping early. The first monster in the way takes your Rumble dice and weapon, plus the tiles to spare. A hit pushes it 2 tiles, bosses and huge monsters too; a miss, or running into a wall or anything else, leaves you prone. A hidden trap on the way ends the charge. It costs only the dice, not your action, and the Rumble dice only if you reach a monster |
| Guard | Rumble | Roll a Rumble die (a 6 rolls another) and hold it for the next defense of you or an ally beside you, until your next turn |
| High Alert | Rumble | A Rumble die rolls in with your next search this turn, or makes the search on its own |

- An Od Hand die needs an Od Hand skill to be any use: the first one bought brings one free, and
  buying it opens the Skills window to pick it.
- **Without the skill, a tool stands in**, once a turn, rolling only its own dice: Lockpicks or
  Burgling Tools on a lock, a lit torch in hand on a search, a Medicine Bag (or Bandages, to rouse) on
  Aid, Burgling Tools on a trap, and Burgling Tools, a Cutpurse Knife or Sticky Gloves on a steal. With
  the skill, the tool adds to your Od Hand dice.
- Learning a spell and running from a fight need no skill: a free die starts each roll (learning is 1d6
  plus Magic, with any Od Hand dice on top).
- Characters made before skills existed keep their dice, with one more Od Hand die, and know every Od
  Hand skill and Shove.

- **Creation:** name (1–16 letters, numbers, spaces, apostrophes or hyphens), look, crest, and 6 points to
  spend. Unspent points are kept for later. Each stat is shown with its symbol (a heart, boot, fist, shield, hamsa or clover);
  hovering one explains what it does in the preview panel. While at level 1, a character can buy at most 2 extra dice of
  each kind *(tunable)*. Points already spent can't be refunded.
- **Progression:** XP from monster kills and floors reached. Level 2 takes 10 XP, level 3 takes 30, then
  60, 100 and so on (10 × 1, 3, 6, 10…). Each level grants 1 point.
- **Looks:** purely cosmetic; stats come only from point-buy. A character picks one of 44 figures
  (Wanderer, Knight, Ranger, Rogue, Mage, Wizard, Brute, Barbarian, Wild Woman, Shaka, Druid,
  Princess, Noble, Enchantress, Dancer, Sorceress, Street Rat, Sultan, Bard, Bunny Lady, Cat Lady,
  Monk, Priestess, Elf, She-Elf, Dark Elf, Spider Lover, Brave, Wolf Man, Birdman, Dwarf, Halfling,
  Lizard Man, Stone Man, Log Man, Devilman, Helling, Raider, Swashbuckler, Pirate, Tempest, Riverman,
  Alchemist, Artificer) and its colors:
  - **Main and trim:** two of 12 colors (clothes, then belts, sashes, plumes and the like).
  - **Skin and hair:** from the figure's own sets: skin tones, dark elf or demon skin, scales, stone
    or wood; hair, fur, plumage or leaves. Some looks start on a fitting color (dark elves white-haired). Figures that don't show one (a Knight's helmet, a Monk's bald head) skip it.
  - **Random** rolls a look and colors. Any two players may share colors: on the board each hero
    stands on a ring of their seat color.
  - Characters saved before looks existed are Wanderers in their crest's color.
- **Crests:** eight heraldic sigils, drawn in the character's main and trim colors. They head the
  hero panel in a run.
- **Characters screen:** lists saved characters (with their figures) and their stats, gear and record.
  You can create, edit (rename, restyle, spend points) or delete them; delete asks for a second click.
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
| Belt | Up to 4 consumables and tools (potions, bombs, Lockpicks, Medicine Bags) ready for quick use; tools go there first, as a stack. A torch can ride on it but only lights in a hand |

- Two-handed items fill both the main hand and the off hand.
- **Backpack:** 8 cards that are carried but not equipped *(tunable)*.

## Cards

Each card has: id, name, type, slot (if equippable), usage (persistent, single-use or charges),
stat/dice modifiers, effect, gold value and rarity. Each dungeon theme has its own loot deck.

| Type | Usage | Examples (placeholder names) |
| --- | --- | --- |
| Weapon | Persistent | Wooden Sword (weapon 1), Knight's Sword (weapon 2), Longsword (weapon 1 + 1d6, versatile +1), Short Bow (weapon 1, ranged 6), Longbow (weapon 1 + 1d6, ranged 10; floor 3), Spear (weapon 2, reach 2 in a straight line), Dagger (either hand, stacks: +1 on an attack of its own from the off hand, doubled on a stunned or prone monster; thrown at range 3 it lands at the target's feet; one in each hand +1 each; the Waylay Inn sells them), War Hammer (weapon 2; crushes: hits flip shelled monsters, broken bones stay broken; floor 2), Rapier (weapon 2, Block 1; two strikes a turn with an empty or light off hand; floor 2), Maul (two-handed, weapon 3; any 6 shoves the target a tile, hit or miss; floor 2), Crossbow (two-handed, weapon 3 every loaded shot, range 8; reload with an Od Hand or Rumble die; floor 2), Great Axe (two-handed, weapon 2 + 1d6; each 6 cleaves into another monster beside you; floor 3), Heavy Crossbow (two-handed, weapon 4 every loaded shot, range 9; reload with an Od Hand or Rumble die and a Wend die; floor 3). A weapon's dice always go with its bonus on one attack. Crossbows are loaded on each new floor; reload one with **Use** |
| Shield | Persistent | Buckler (armor 1, Block 1), Tower Shield (armor 3, Block 3, −1 Wend), Knight's Shield (armor 2, an extra Defense die; floor 2+). **Knight's set:** worn with the Knight's Sword, the sword gains +1 Weapon and Block 1, and the shield a Guard Die |
| Armor | Persistent | Head: Leather Cap (+1 Heart), Lucky Helmet (armor 1; when you'd be hit anyway it rolls one more die in, last of all, then breaks), Diving Helm (Temple). Body: Leather Armor (armor 1), Studded Armor (armor 2, floor 2+), Chain Mail (armor 3, floor 3), Warden's Plate (armor 3 and an extra Defense die: the Iron Warden drops it one time in four) |
| Boots | Persistent | Dash Boots (+1 Move die) |
| Accessory | Persistent | Worn in the accessory slot. Lucky Jackalope's Foot (+1 Luck), Shield Cufflinks (armor 1, Block 1; floor 2+), Icarus Wings (floor 2+; see below). The Coward's Cloak goes on the head or as an accessory. **Icarus Wings:** flight of their own, apart from Wend. Beat them for a d6 of flight (a 6 rolls a Power Die straight in; its 1s don't count against the wings) and fly over pits, water, traps and figures, a tile a point, and through doors (a key opens a locked one on the way). Only once the flight is used up comes the choice: **Land.** (a normal landing, but over a pit you fall in) or **Fly!** (beat them again). Any 1 on the wings' die breaks them: down you come where you are, prone and stunned, the rest of the turn's Wend gone. The end of the turn brings a flyer down where they are |
| Spell | Charges or single-use | Flame Scroll (a 3-dice fire attack at range 3: any 6 sets the target alight; aimed at bare floor it sets it burning for 3 rounds), Frost Wand (2 charges: a stack of Freeze, then 2 Ice dice) |
| Tool | Persistent | Torch (stacks in one slot, one to a hand; see below), Grapple Hook (pull yourself up to 4 tiles in a line; not built yet) |
| Bomb | Single-use | Bomb (thrown up to 3 tiles, lit: a 4-dice blast over a 3×3 area at the end of your next turn; see [Bombs](#bombs)) |
| Potion | Single-use | Health Potion (restore 3 hearts) |
| Treasure | Kept for score | Emerald (15g), Golden Idol (40g). Coins (Loose Coins 2g, Coin Pouch 5g) aren't cards: they go straight into your purse as gold |

- **Starter kit** (new characters): Wooden Sword, Buckler, Health Potion, Torch and Lockpicks (on the belt).
- **Equipping, belting or stowing gear** is free while no monsters are in your room, as often as you like.
  In a fight, each one takes your action.
- **Greyed-out card buttons explain themselves:** hovering shows why, and clicking one writes the reason
  in the message log.
- **Using a belt item** doesn't take your action (once per turn). Using an item from the backpack takes your action.
- **Only single-use cards** (potions, bombs, scrolls) go on the belt. Reusable cards with an effect (boomerang,
  wand) must be equipped to use.
- **Stacks:** Lockpicks, Bombs, Health Potions, Flame Scrolls and treasure (but not coins) stack: copies
  gather into one card with a count ("Lockpicks x3"), filling one slot, in the belt, the backpack or (for
  lockpicks) the off hand. Picking up more joins the stack, even with the backpack full; using one takes
  one off; equipping or moving a stack moves all of it. A treasure stack is worth every copy in it.
- **Dropping a card** onto your tile is free; dropping part of a stack asks how many.
- **Loot piles and bags:** loot lies on a tile; two or more things make a loot bag. A pile (or an opened
  chest) can be reached from its tile or any tile touching it, diagonals too, so an ally standing on it
  doesn't block you. Clicking a single thing takes it; clicking a bag or a chest opens it to **rummage**:
  take one card, one off a stack, the gold and keys, or everything (**Take all**), and **Put in** a card
  of your own (part of a stack asks how many). Loot shows above whoever stands on it.
- **Looting** (the first take from a pile, or opening a chest) is free once per turn; after that it takes
  your action. More from the same pile (or the chest just opened) that turn costs nothing. Putting cards
  in is free.
- **Chests** keep what's inside: opening one shows its contents to take or leave (coins go straight to
  your purse), and what you leave stays in the open chest for anyone to rummage through later.
- **Targeted cards** need line of sight and must have a target: a bomb lands on any open tile (or water,
  which puts it out), a Flame Scroll a monster, a bomb (which it sets off) or bare floor to set burning,
  and potions only heal yourself and can't be used at full hearts. A card that can't be used says why
  under its text.
- **Burning floor** (a Flame Scroll aimed at bare floor) burns for **3** rounds: anyone who stands on it
  or walks through it catches fire, and monsters keep off it unless fire can't hurt them.
- **Torches** (tool, stack: a stack fills one backpack slot, and only one goes in each hand): every
  character starts with one, and merchants sell them. Held, a torch is lit: **+1d6** on Search rolls, a
  warm light, and a **Fire die** on your Rumble attacks (see Damage types); any **1** in the attack burns
  the torch out.
- **Lockpicks** (tool, off hand, stack): +1 and 1d6 on one lock-picking roll a turn (doors, chests,
  gates, and any locks added later; not searching, disarming or stealing). A 6 on the pick's die rolls
  another die straight in. A failed lock-picking roll with a 1 showing (not on a Power Die) snaps one
  pick off the stack.
- **Longsword** (main hand, from floor 2): Weapon 1 and 1d6 on one attack a turn; a 6 on its die rolls
  another straight in. Versatile: +1 more with nothing in your off hand (held two-handed).
- **Tool and weapon dice:** a 6 on one rolls another die straight into that roll, with a "6!" callout.
  A typed weapon's dice (the Frost Brand's ice) are dice of its type.
- **Frost Wand** (off hand, 2 charges a floor): a monster within 3 automatically takes a stack of Freeze, then
  2 Ice dice attack it: on a hit each 6 freezes it a stack more (see Damage types).
- **Stun** (the Boomerang's sixes and Elektrik dice, on a hit) and **Powerful Stun** (bomb blasts) stack, and wear off at the end of
  the stunned creature's next turn. The first stack takes its Od Hand action and its free actions (free
  loot, free belt item), or a Wend die if it has no Od Hand, and a flyer drops out of the air (into a pit
  or water under it). Each further Stun takes a die: Wend first, then Rumble, then Defense. A Powerful
  Stun takes 2 more dice to begin with (3 without Od Hand) and 3 for each further one. Attacks on a
  stunned creature get **+1 per Stun and +3 per Powerful Stun**. Bosses can't be stunned.
- **Boomerang:** a weapon for either hand. Thrown at a target within 4 (line of sight) with your Rumble
  dice +1 (in place of your main weapon's bonus); on a hit, every 6 rolled adds a Stun. In the off hand
  it's used for targets your main weapon can't reach, unless you pick it.
- **Two weapons in hand** (a Spear and a Boomerang): pick which one you attack with by clicking it in your
  hands, or with the weapon toggle at the end of the Rumble row (**Auto**, then each weapon). Auto uses the
  main hand when it reaches, else the off hand. The attack range shows the picked weapon's reach, and
  hovering a monster says what you'd hit it with.
- **Prone monsters** (knocked flat by a blast): melee on them rolls an extra die, ranged attacks face an
  extra defense die, and getting up costs them 2 of their moves.
- **Monster movement** is Wend dice worth a fixed 2 moves each (the last one 1, for an odd move), so a
  stun that takes a Wend die takes 2 moves.

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
     **Rough ground** (rubble) and **water** cost 2 steps a tile. You can swim **deep water**, but if your
     turn ends in it, one of your unused Rumble dice keeps you afloat; with none left you **drown**: you
     lose a heart and wash back to the last shallow-water or dry tile you stood on.
   - **Leaps (Wend):** press **Leap**, pick a tile up to 3 away in a straight line (diagonals too) and
     commit; only then is one unused Wend die rolled. The leap carries half the roll, rounded down,
     never past your target. It sails over pits, water, traps and figures, but not walls, chests or pots.
     Coming down short over a pit drops you in; over water, you splash in; a 1 goes nowhere.
   - **Attacks (Rumble):** each attack uses one or more of your unused Rumble dice, plus your weapon's
     value on one attack per turn (only alongside at least one die). Press **Rumble** (or **R** or **Space**): your
     attack range shows in red, and you click a target in it, a monster, a bomb, or with friendly fire on
     an ally. Clicking a monster on its own doesn't attack. See [Combat](#combat).
   - **Hotkeys:** **W** rolls your selected Wend dice (like the Wend button); **R** or **Space** aims Rumble.
   - **Double-click shortcuts:**
     - **An enemy you can hit from where you stand:** aims Rumble at it. One more click on it attacks.
     - **A space, or an enemy you can't hit yet:** if you haven't rolled Wend this turn, rolls your
       selected Wend dice. Clicking it again goes there; for an enemy, up to the reachable tile you can
       hit it from with the shortest walk, and then Rumble is aimed at it, so one more click attacks.
       Already rolled, the double-click goes there straight away.
   - **Od Hand checks:** each uses one or more of your unused Od Hand dice, plus your tools' value once a
     turn. See [Od Hand](#od-hand). They don't use your action.
   - **One other action:** use a card from your backpack or an equipped one, smash a pot, loot (after your
     free loot), or (in a fight) equip, belt or stow gear. Outside a fight, gear changes are free.
   - **One free loot** and **one free belt item**.
3. End turn. Your dice come back next turn.

On screen, the dice you have left this turn sit under **This turn**, a row each led by its symbol:
Rumble (a fist) and your weapon, Wend (a boot) and your Wend bonus, Od Hand (a hamsa) and your tools, and
your Defense (a shield), and your banked Power Dice. Wend starts with every die selected; Rumble and Od Hand
roll **one die at a time** to begin with, and remember how many each hero last chose (clicking a lit die drops
one, a dark one adds one; never fewer than one), from roll to roll and turn to turn, using what's left when
fewer remain. Power Dice start unselected. The Defense row can't be clicked: it shows your base die (framed,
always rolled), your extra dice (lit while ready, dark once spent), your armor and your Block.

**Doors** sit at the middle tile of each room edge. Stepping through costs 1 step and places you at the
facing door of the next room.

## Combat

- The attacker rolls the Rumble dice they chose and adds them up, plus their weapon's value if they
  used it.
- The defender rolls their base Defense die and adds their armor (a monster rolls all its Defense dice).
  While a hero's defense is losing, it's backed up in order: Guard Dice from allies beside them (diagonals
  too; each a d6, and a 6 rolls another straight in), then all their Block, then their extra Defense dice
  one at a time until it holds, then banked Power Dice, and last a Lucky Helmet's die. Guard Dice, Block and
  extra dice stay spent until the start of their owner's next turn; a stun takes the extra dice before the
  base die. Two knights side by side guard each other.
- If the attack total is higher, the defender loses **1 heart**. Ties go to the defender. The dice bar
  tags the defender's dice HIT, MISS or BLOCKED, and the board calls it out (see Presentation).
- Rolling dice together makes one attack more likely to land; splitting them gives more chances.
- **Card attacks** (the Flame Scroll) roll their own dice and don't use your Rumble dice. Bombs work
  differently: see [Bombs](#bombs).

### Bombs

- **Throwing:** a bomb lands lit and goes off at the end of the thrower's **next** turn (its tile shows the
  turns left). A **bomb trap** pops out a lit bomb under whoever springs it, burning down on their turns;
  disarmed, it gives the bomb up unlit.
- **The blast** rolls 4 dice (each 6 rolls another at once) against **everyone** within a tile, friend or
  foe, whatever the friendly fire setting (except a hero diving in deep water). Each creature defends, and
  every die over its defense total is a hit: **1** costs a heart, **2** also shove it a tile away from the
  bomb, **3** also knock it prone, **4 or more** also Powerful Stun it (a stack for each hit past 3).
  Whoever stands on the bomb is thrown any open way and blasted a second time. Blasts break cracked walls
  and set off other bombs; a bomb kill goes to whoever threw it.
- **Lit bombs on the floor:** fire sets one off at once. Strike it with Rumble dice and each die is a 12%
  chance (each point of weapon bonus 2% more) to set it off. Shove it (1 Rumble die) and it just rolls a
  tile away: down a pit, or into water, which puts it out. Click it from beside it to **defuse** it: Od
  Hand 10, or it goes off in your hands.
- **Unlit bombs** are loot: stand on one and pick it up as a Bomb card. Bots never throw bombs, and keep
  clear of lit ones.
- **Melee** needs an adjacent target. Heroes can strike diagonally too, unless walls close off both corners between them; monsters strike orthogonally only. **Ranged** needs line of sight; walls block it.
- **Line of sight** runs between tile centers. Only walls (cracked ones too) block it, not pits, chests or
  other figures, and grazing a wall's corner is allowed.
- **Luck** rerolls one of your own Wend, Rumble or Od Hand dice. A Wend die can be rerolled any time before
  your first step. With Luck left, an attack or Od Hand roll waits for you to reroll or confirm; spending
  your last Luck resolves it. Luck doesn't apply to defense rolls.

### Fire

- **Catching fire:** Fire dice (the Flame Scroll's, torches', and the attacks of Cinder Imps and Fire
  Beetles) set their target alight on a hit, a fire die for each **6** (more on a creature already
  burning). See Damage types.
- **Burning:** at the start of a burning hero's or monster's turn its fire dice are rolled as an attack on
  it, defended as usual (1 heart on a hit; a mark of death for someone Keeling Over). Then each **6** adds
  a fire die and sets a random neighbor alight (diagonals too, anyone: hero, companion or monster), and
  each **1** snuffs a die out. With none left, the fire is out.
- **Putting it out:** stepping or being shoved into shallow or deep water douses it. **Stop, drop and
  roll** (the **Drop & roll** button, only while on fire, before doing anything else that turn) puts it
  out at the cost of the whole turn, and leaves the hero prone.
- **Credit:** a monster that burns to death is a kill (and XP) for whoever set the fire. Skeletons burned
  to death stay down.

### Prone and diving

- **Lying down** (the **Prone** button) costs 1 step of movement; **getting up** costs 2. While prone every
  step costs double, on top of rough ground and water (rubble crawled over costs 4). No leaping while prone.
- **Melee** attacks on a prone hero roll **1 extra die**; **ranged** attacks face **1 extra defense die**.
  Heroes Keeling Over count as prone.
- **Diving:** prone in deep water, a hero is under the surface: no ranged attack, blast or sweep can reach
  them from more than a tile away. Melee still can, and staying afloat still takes a Rumble die.

### Power Dice

- **Earning:** every **6** rolled on a plain die earns a Power Die, on any roll by a hero or a monster:
  attacks, moves, Od Hand rolls and defense.
- **Using one:** a Power Die is one more d6 rolled into a roll and added to it. If it also shows a 6,
  it earns another.
- **The bank:** each hero and monster can bank up to **3** *(tunable)*, kept until used (across turns
  and floors). A 6 rolled with a full bank is rolled into that roll straight away instead.
- **Your attacks, moves and Od Hand rolls:** after a 6 you choose **Add Power Die** (roll it in now) or
  **Bank it**. Nothing else can happen until you choose. Banked dice show in the dice pool's POWER row,
  unselected; select them to roll them into your next attack or move.
- **Star dice** are a damage type of their own (below): a **6** on one **surges**, with no choice to make:
  one Power Die rolls straight into that roll, and another goes into the bank, or with the bank full,
  rolls in too. A surging Power Die's own 6 surges again.
- **Defense:** sixes are banked automatically (rolled in when the bank is full). While a defense is losing
  and the banked dice could still make up the gap, they're rolled in one at a time to try to block.
- **Monsters** roll the sixes on their attacks straight in, and bank and spend on defense like heroes.
  They spend banked dice on attacks too (one at a time, while the roll looks short of the target's armor
  and a middling defense die), and on movement (while a die could still get them to where they can
  strike). Their sixes show **6!** over them.
- **Other typed dice** (below) earn no Power Dice at all.
- **Between runs:** Power Dice still banked at the end of a dungeon or a fight go home with the character
  and come along to the next one.
- A Luck reroll that comes up 6 earns a Power Die too. Od Hand rolls earn and use Power Dice like attacks and
  moves.

### Damage types

Dice can be made of something other than plain: **Star** (see Power Dice), **Fire, Poison, Ice, Acid, Necro, Squall, Radiate,
Life, Earth, Elektrik, Psycho** and **Shadow**. Scrolls roll them (a Frost Scroll's 3 Ice dice), typed
weapons roll their own dice as their type (the Frost Brand, the Quake Hammer), gear can turn your Rumble
dice into a type (the Storm Mail's Elektrik), a held torch adds a Fire die, and some monsters attack with
them (Cinder Imps and Fire Beetles with Fire, Giant Scorpions with Poison, Jellyfish with Elektrik, the
Necromancer with Necro). A typed die earns **no Power Die**: each **6** rolls another die of its type
straight in, and **if the attack hits**, lays a stack of its type's effect on the target. Items may do more
of their own on top (a Flame Scroll aimed at bare floor sets it burning).

- **Fire (Burn):** a fire die on the target (see Fire).
- **Poison (Poisoned):** a Poison token. Each takes 1 off all their rolls (attacks, defense, moves, Od
  Hand). At the end of their turn they roll a die: not higher than their tokens, they lose a heart; a 6
  throws one off.
- **Ice (Freeze):** a stack of Freeze. The first halves their speed (each step costs double; half a
  monster's moves); each one after takes a Wend die (2 of a monster's moves). With no Wend left they're
  **frozen solid**: their turn passes with nothing done, though they still defend (bosses are never frozen
  solid: Freeze only slows them and takes their moves). A stack melts at the
  end of each of their turns, one more for each fire beside them as their turn starts (burning creatures,
  burning floor, held torches), and one for every fire die that attacks them.
- **Acid (Dissolve):** on a hero, a point of wear on the equipped piece giving the most armor (a card
  shows "worn -1"). Wear stays with the card, between runs too, until the **Blacksmith** mends it or an
  **Autosmith Hammer** does. On a monster, its armor is eaten for the rest of the fight. With no armor
  left, an Acid token instead, which attacks them with an Acid die at the end of their next turn and is
  used up.
- **The Blacksmith** (a shack on the road south of The Waylay Inn): mends worn gear for **2 gold a point**
  of wear, paid by whoever owns it, and sells arms, armor and **Autosmith Hammers** from the forge, which
  never runs out. An Autosmith Hammer (single use, stacks; in the loot decks too) mends its user's most
  worn card whole, anywhere. Weapons can be mended too, for whatever wears them later.
- **Necro (Necrosis):** a Black Heart: one of the hearts the hit took can't be healed until cured (Holy
  Water cures disease and curses). A last heart taken with one kills outright, and the dead rise as
  zombies following whoever killed them: a monster a hero kills this way rises as that hero's companion
  (not bosses), and a hero slain by a Necro monster rises as a zombie on the monsters' side.
- **Squall:** blown a tile straight away from the attacker for each 6. For every tile a wall or someone
  stops them, a die hits them, and whoever stopped them too. Bosses and huge monsters are shifted a tile
  for every two 6s, and never into a pit or out of their water.
- **Radiate (Shine):** a Shine token: -1 to their defense each, and they glow. One fades at the end of each
  of their turns.
- **Life:** plants grow on the target's tile, for the attacker's side: rough going for that side's enemies
  (a step there costs 1 more) and +1 defense to its own standing in them. Where plants grow already, a
  **Sentient Shrub** springs up beside the target to fight for the attacker's side (1 heart, 1 Rumble die,
  a Wend die that's always a 1).
- **Earth (Quake):** knocked prone; someone already prone is **buried** instead, a token for each 6. Buried,
  they can't move or attack: they dig with their Rumble dice (the **Dig out** button), every 3 of the roll
  throwing off a token. With none left they're only prone.
- **Elektrik (Electroshock):** a Stun (see Stun).
- **Psycho (Confuse):** a Confuse token. On their turn they lash out with every Rumble die at someone
  beside them (anyone, friend or foe), then stagger off in a random direction as far as their Wend takes
  them, and that's their turn. One wears off at the end of each of their turns.
- **Shadow (Blind):** a Blind token: -1 to their attacks each, and they can't see past the tiles beside
  them (a blind hero can only attack or aim at what's beside them, and the rest of the room goes dark on
  their turn; a blind monster only finds heroes beside it and otherwise wanders). It lasts until cured:
  **Aid** (see [Od Hand](#od-hand)) of 10 plus their tokens, or **Eye Drops**.

Figures show their tokens as colored pips down their left side (with the count), a glow while they shine,
ice over them while they freeze, a mound of earth while buried, and Black Hearts as purple heart pips.
Typed dice show in their own colors in the dice bar.

## Magic

*Built in three batches, all done: (1) the Magic stat, casting and the spells' attacks; (2) learning
scrolls and backfires; (3) ice, light and darkness, and the boons.*

Magic is dangerous in this world, and people don't like it, for good reason.

### The Magic stat

- **Magic** (a pentacle) costs **2 points** a rank and starts at **0** for everyone, like Luck.
- Each rank adds a **Power Dice bank slot** (3, then 4, 5...) and teaches **one basic spell**, picked from
  the list below (up to rank 13, one for each). Raising Magic in the character editor, or with a level's
  points from the Party menu in a run, asks which.
- Companions can take Magic too, and cast their spells under the **Fight** order.

### Casting

- A spell costs **banked Power Dice**: 1 for a basic spell, 3 for one learned from a scroll. The Power is
  spent, not rolled in.
- **Upcasting:** each extra Power spent adds one more die of the spell's type, up to your Magic rank in
  extra dice. At Magic 2, Hurl Fire is 1 to 3 Fire dice.
- The attack is the spell's dice **plus your Magic rank**, against the target's defense and armor like
  any attack. Its dice are typed (see Damage types): a 6 rolls another of its type in and, on a hit, lays
  its effect.
- Casting takes all your **Rumble dice** left this turn (you need at least one), so it's instead of
  attacking, and once a turn.
- **On screen:** the **Cast** button (beside End turn, for a hero who knows spells) lists your spells;
  pick one, then click a target in red (its range is shaded). The cost comes out of the bank by itself;
  select banked Power Dice in the dice pool first to put them in on top, a die each.
- Durations "equal to the dice" count every die rolled, including the extra ones from 6s.

### Basic spells

All cost **1 Power** and can be upcast.

| Spell | Range | Dice | What it does |
| --- | --- | --- | --- |
| Shooting Star | 8 | 1 Star | A 6 surges with Power |
| Hurl Fire | 6 | 1 Fire | |
| Poison Mist | 2, every creature in a straight line | 1 Poison, +1 per Poison token on you | Throws off every Poison token on you |
| Icicle Arrow | 4 | 1 Ice | Or cast on the floor to lay ice (below) |
| Acid Ball | 3 | 1 Acid | The same roll splashes one random creature beside the target, friend or foe |
| Death Touch | 1 | 2 Necro | |
| Squall Wave | 5 | 1 Squall | Your selected Rumble dice go into a shove roll against the target, on top of the Squall pushes |
| Shining Light | 6, ignores line of sight | 1 Radiate | Or cast on yourself or an ally for the Shining Light buff (below) |
| Lifeforce | 3 | 1 Life | Or cast on yourself or an ally beside you: plants grow under them and they heal a heart per 6 |
| Quake | Every enemy beside you | 1 Earth, +1 against targets on rubble | |
| Elektrobolt | 5 | 1 Elektrik | Each 6 jumps the same roll to another monster beside the target (like the Great Axe's cleave) |
| Swirly Eyes | 3 | 1 Psycho | On a hit, you move the target 1 tile of your choice at once (into a pit or onto a trap, too) |
| Shadowbind | 6, ignores line of sight | 1 Shadow, +1 against targets in darkness | Or cast on yourself to become invisible (below) |

- **Icicle Arrow on the floor:** aimed at bare floor, the **total** rolled is how many ice patches you
  lay: the first where it's aimed, the rest a click each, on tiles within range (occupied ones too; over
  water they freeze it over, and it's floor until the ice melts). Choosing a tile that's already icy with
  yourself or an ally on it gives them **Snowshoes** instead: they walk on ice like floor. Patches and
  Snowshoes last **1 round per Ice die rolled**. Rolling 2 dice for a 2 and a 3 makes up to 5 patches, or
  3 patches and 2 Snowshoes, all lasting 2 rounds. Patches left unlaid at the end of the turn are lost.
- **Ice** is slippery: stepping onto a patch without Snowshoes slides you one more tile the same way (into
  a pit or water, too), and that's the end of your move. Monsters slide too, except flyers and big ones.
- **Boons** (Lifeforce, Shining Light and Shadowbind cast on yourself or an ally) roll their dice for how
  long they last and how strong they are, and hurt no one. Rounds count down as each new round starts.
- **Lifeforce boon:** plants grow under them (cover for the party), and they heal a heart per 6.
- **Shining Light buff:** for 1 round per Radiate die rolled, the target gives off light, has **+1
  Defense, +1 more per 6**, and rolls an extra die on Search (like a torch).
- **Shadowbind buff:** invisible for 1 round per Shadow die rolled: monsters can't target you and lose
  track of you. Attacking, casting, or using an item other than lockpicks or keys ends it early.

### Learning scrolls

- Any spell scroll can be learned as a spell: each of the twelve teaches its **Blast** (the Flame Scroll
  **Flame Blast**, the Frost Scroll **Frost Blast**...), which works just like the scroll (3 dice of its type
  at range 3) and costs 3 Power to cast. Learning it uses up the scroll.
- The attempt is an **Od Hand roll plus your Magic rank** against **6 + the spell's level** (the scrolls'
  spells are level 3: 9). It can be tried anywhere. In a dungeon or a fight it's an Od Hand roll like any
  other (your selected Od Hand dice, Power Dice and Luck): select the scroll and press **Learn** (where
  Equip would be). On the Woarld Map, pick the scroll in the **Party** menu and press **Learn**: there it's
  your Od Hand dice, each 6 rolling another in.
- **Backfire:** a failed attempt uses up the scroll too, and casts it **centered on you**: it hits you and
  everything on the 8 tiles around you, friend and foe, each defending against the same roll, whether or
  not friendly fire is on. On the Woarld Map it goes off among the whole party (you travel together): each
  defends with a die and their armor, and each it beats loses a heart (never their last).

### Light and darkness

- Some rooms are **dark**. In a dark room a tile is **in darkness** unless it's within 2 tiles of a light:
  a held torch, fire burning on a tile, a burning or fiery creature (Cinder Imps, Fire Beetles), a lit
  **brazier**, or a Shining Light.
- About **a quarter** of the rooms past the first are dark (never an arrival or the boss's room), each with
  a **brazier** or two, unlit, standing where they block no way through. Braziers block movement, not sight.
- Any fire beside a brazier lights it: a held torch, burning floor, a burning or fiery creature. So does
  fire aimed at it (Hurl Fire, a Flame Scroll). Lit, it stays lit.
- Monsters in darkness are hard to make out: ranged attacks on them take **-1**, and Shadowbind rolls a die
  more against them. On the board, darkness dims the tiles no light reaches.

### Related changes

- **Friendly fire** is on by default for new games (the party screen's switch still turns it off).

## Od Hand

Od Hand dice work like Rumble and Wend dice: choose how many to roll (plus your tools' value once a
turn), sixes earn Power Dice, and Luck can reroll them. Each check needs its skill (see
[Skills](#skills)), or a tool to make it alone. None of it takes your action. A check succeeds when the
total reaches its number:

| Check | How | Number *(tunable)* |
| --- | --- | --- |
| **Pick a lock** | Stand in a locked doorway (or next to a locked chest) without a key and click it | Doors 4 + floor (at most 6); chests 5 + floor |
| **Search** | **Search** button: reveals everything in the room your total reaches | Secret doors 4 + floor; traps 3 + floor; stashes 5 + floor |
| **Disarm** | Click a trap you've found: on a tile or chest next to you, or your own doorway | 3 + floor |
| **Steal** | **Steal** button, then an adjacent monster or hero; once a turn | Beat their roll: a monster's defense dice, a hero's Od Hand dice and tools |
| **Aid** | The **Aid** card, then yourself or an ally beside you (diagonals too); clicking an ally Keeling Over aids them straight away | On someone Keeling Over (a Rouse): every 6 of the total is a mark of life; every die showing 1 is a Doom Die on their next Keel roll (see [Keeling Over](#loot-keeling-over-and-winning)). Otherwise it takes one stack of one ailment off, chosen when there are several: 10 (Nekrot 14). A Shovel in hand digs two stacks of Buried off |

- **What Aid cures:** Poison, Acid, Blind, Shine, Confused and Buried. Freeze, Black Hearts, Burning,
  Stun and Nekrot only with a **Medicine Bag** to hand. One stack a success, bag or not.
- **Medicine Bag** (tool, stacks, goes on the belt and works from there or a hand): +1 and 1d6 on one
  Aid roll a turn (a 6 rolls another), even with no Od Hand dice left. A failed Aid it helped with a 1
  showing uses one up, as a failed pick snaps a lockpick.
- **Bandages** (single use, stack): carried, one goes into each Rouse for +1d6 (a 6 rolls another).
- **Shovel** (two-handed, Weapon 2): a die more and +1 against earth creatures. Held, it digs a stack
  of Buried off you at the start of your turn, and adds a die to digging out and to Aid against Buried.
- **Holy Water** (single use, stack): drink it to cure every Black Heart and every disease (and curses,
  once there are any), or throw it at the undead within 3. Lesser undead (Skeletons, the Drowned, zombies risen from
  monsters) are destroyed outright, bones and all; greater undead (Ghouls, heroes risen as zombies) take
  5 Radiate dice.
- **Antidote**, **Strong Base** and **Smelling Salts** (single use, stack) cure Poison, Acid (only the
  tokens: acid-worn gear stays worn) and Confused, like Eye Drops for Blind.
- **Modern Medicine** (single use, stack; the Waylay Inn sells it) cures every disease.
- **Thieves' Oil** (single use, stack; grave robbers carry it): for 10 rounds disease is resisted on a 3
  or more instead of a 6, and Necro blows hit 2 less.

### Disease

- **Catching it:** a monster that carries a disease tries to give it to a hero it hits whenever a 6
  shows among its dice. The hero rolls a d6 to resist: Nekrot needs a 6 (other diseases may be easier),
  Thieves' Oil makes it 3 or more, and a Medicine Bag in either hand gives a second roll at the same number.
- **Nekrot** (the undead skull, green-eyed and fanged, on the figure): every heart lost turns black.
- **It lingers:** Black Hearts and disease are saved on the character and carried from run to run until
  cured (the character list shows them). A hero starting a run with Black Hearts starts that many hearts down.
- **The sting:** a hero who dies with Nekrot rises as a zombie on the monsters' side.
- **Sineater Doll** (very rare, floor 3 on): soak what ails you or an ally beside you into it, no roll,
  up to 10 stacks of anything (free from the belt once a turn, or your action). Throw it up to 3 tiles:
  it bursts where it lands, laying every stack it holds on everyone within a tile, friend or foe, no
  save, and is gone. The undead shrug off its Poison.

- **Burgling Tools** (off hand): +1 and 1d6 on one roll a turn to pick a lock, disarm a trap or steal;
  a failed roll with a 1 showing breaks them. **Cutpurse Knife** (either hand, weapon 1 like a Dagger)
  and **Sticky Gloves** (accessory): +1 and 1d6 on one steal a turn. The Waylay Inn sells the knife and
  gloves; Burgling Tools turn up from floor 2.
- **Stealing from a monster** (once per monster): a key (40% *(tunable)*) or a card from the loot deck.
- **Stealing from a hero:** one of their keys or backpack cards at random. Not equipped gear, belt items
  or the Boss Key.
- Door locks are kept low enough that one die (and its Power Dice) can always open them, so a locked door
  never strands the party.

## Keys, locks and secrets

- **Keys** are a counter, like gold, and don't take backpack space. They turn up when a monster falls
  (15%), in pots (10%), on the floor when a room is cleared (35%), in hidden stashes, and by stealing.
  A key opens one locked door or chest; using it is free. Heroes who die drop their keys at their tombstone.
- **Locked doors** (some doors on each floor): use a key or pick the lock. Once open, they stay open.
- **Locked chests** (iron-bound): use a key or pick the lock, then open as usual. They hold **two** cards,
  dealt as if from a floor deeper.
- **The Boss Door** is the only way into the boss room. Only the **Boss Key** opens it, and it can't be
  picked. The Boss Key lies in the crimson-banded chest of the last floor's **vault**, guarded by the
  **Key Warden** miniboss; the vault is always reachable through ordinary doors. A hero who dies drops the key at their tombstone.
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
  To cross one, find the bridge or **leap** it (see [Turn structure](#turn-structure)).
- **Divided rooms:** some rooms have a wall across them with one way through. A **locked gate** opens
  with a key or a picked lock (it may stand between you and a doorway, but can always be picked). A
  **cracked wall** (with a stash inside) or a **secret way** (found by searching, with a stash beyond)
  only ever closes off extras. Rooms you could drop into from above only get locked gates.
- **Breaches:** some rooms that share an edge have a cracked wall in it, off the doorway. A bomb breaks
  both sides; stand in the gap and click again to go through to the room next door (1 step).
- **Secret rooms:** some floors have a room off one ordinary room, reached only through a secret door or
  a cracked wall, with no other way in. It holds only extras (treasure, stashes), never anything a run
  needs, and nothing drops into it from above.
- **The Ogre Champ's den (floor 1, always):** an optional room off one ordinary room, through a plain door
  that is its only way in, and over a room on floor 2 when the map allows. Pits ring the walls; a one-tile
  bridge leads from the door to a platform where the Ogre Champ waits, and a second bridge on the far side
  leads to a little platform with a **grand chest** (gilded and jeweled, its lock 2 harder than the floor's
  other locks): three cards dealt as if from two floors deeper, and 10 gold. The Champ roars with laughter
  ("HAR HAR HAR!") when heroes first walk in, and its own pounding song plays while anyone is in there.
- **Chained merchants (about one run in three):** a traveling merchant chained to the wall of an ordinary
  room on a random floor. A key frees them, or pick the lock of their chains with Od Hand (like a chest's).
  Freed, they follow whoever freed them (turning up beside them in the next room, or down the stairs),
  move 3 tiles a turn away from any monster within 3 or else up beside their leader, and trade: click
  beside them to buy their wares (4 cards from the dungeon's loot as good as the floor, better the
  higher the Merchant Guild's level, sometimes one rarer) or sell yours, with the gold found this run.
  One heart, no fighting; monsters go for them only when no hero is in reach (no blame if one dies that
  way). Escape with them and they're **rescued**: everyone in the party gains 1 Guild standing, whoever
  freed them 1 more, and the Merchant Guild gains a level. Stealing from one (Od Hand against their two
  dice) costs the thief 2 Guild standing and 1 Law (1 and 1 if caught empty-handed); striking one down
  costs 5 Guild and 3 Law, and spills their wares.
- **The Merchant Guild:** one level shared by every merchant: 1, plus one for each merchant rescued and
  for each step of gold spent with them (50, 150, 300, 500, 750...). Each level brings every merchant a new
  ware, picked at random from the dungeons' gear and items, from deeper floors as the level climbs.
  Prices are twice a card's worth, less 5% for each point of the buyer's Guild standing (up to half
  off), more for each point below; at -10 merchants won't deal with them. Standing with the Guild and
  the Law is kept on each character; rescued merchants remember who helped them. (The Law, its patrols
  and bounties, are for a later update.)
- **Wandering merchants (Raiding Party):** each day the Guild sends merchants out onto the Woarld Map: one,
  plus one for every three Guild levels (at most three), waiting on open ground 4 to 12 tiles from where
  the party greeted the day, and gone at its end. Merchants the party rescued are the first to come.
  Walk onto one and **Trade** with the leader's gold: four wares as good as the Guild's level, and three
  times in ten one from deeper down. A rescued merchant prices kindly for the heroes who helped them
  (2 more standing), and buying from any wandering merchant counts toward the Guild's level.
- **Travel on the Woarld Map:** a day is 1,000 steps of time (500 of daylight). A tile of road takes 5,
  grass, sand or a bridge 10, hills 15 and forest 20. The party's **pace** takes a step off every tile for
  each Wend die its members have beyond their base one (to half the tile's time at best).
- **Party checks:** the best hero's dice for it, plus a die for each other hero (up to three more).
- **Dangers (Raiding Party):** bands of monsters roam the map, three by day and five by night (night's go
  home at dawn). What they are depends on where they come from: near a dungeon, its monsters; otherwise
  Dire Wolves from the forest, Goblin Raiders from the hills, Wild Boars from the grass, Giant Scorpions
  from the sand, and Bandits on the roads; bigger further from the start, at night and for bigger parties.
  A band within 5 tiles closes in on the party (or a wandering merchant, whom it corners: save them by
  beating the band, Guild +1 each, within 100 steps or they're lost). Caught, the party chooses **Fight**
  or **Flee** (the party's Wend check against the band's chase: two dice and one more per monster past the
  first; get away and lose 50 steps, or be caught and they strike first). Some spots lie in wait, hidden,
  and at night something may spring out of the dark (2% a step): the party's Od Hand check against how
  well it was hidden, or the monsters strike first. **Scout** (50 steps, a party Od Hand check) finds what
  lies in wait within 6 tiles that the total reaches, and gives +2 to ambush checks for the rest of the day.
  A **Bridge Troll** lurks under a bridge or two (a hard ambush): it fills four tiles, has 12 hearts and gets
  one back at the end of each turn it didn't start burning. Fights are one open field shaped by the ground
  (trees, rocks, a river under a bridge), the party on one side and the band on the other; beating the band
  ends it and the party walks on.
- **Toll posts:** on bridges and roads, a Guard Captain and his guards ask 2 gold a head. **Pay**, **Turn
  back**, **Fight**, or **Suss Out** (once): each hero's own Od Hand roll against the captain's hidden
  Convince roll (5 and a die for real guards, 5 and three dice for bandits); match it and you see the truth
  ("These are real guards" / "These guys are not who they say they are", and they show as bandits), or
  "You can't suss out any trouble." Beat bandits: Law +3 each; beat real guards: Law -2 each. The
  Captains (Guard and Bandit) are big, heavily armored (2d6 defense +3), quick, with a two-handed spiked
  mace (2d6+2 and a die of its own): a hit stuns, and each 6 on the mace's die stuns once more.
- **Retreat:** in a fight out on the Woarld Map a **Retreat** button sits in the top strip beside Party and
  Menu, well away from the other actions: your Od Hand dice look
  for a way out, against the monsters' chase (a die more than there are of them, and 3: harder than
  fleeing on the map). Get away, and you and your companions beside you leave the field; when everyone
  still standing has got away, the party is back on the tile it came from, and the band (or the Bridge
  Troll, on its bridge) is still there (Cowards +1). Fail, and you bolt in a panic as far from the monsters
  as your Wend dice take you, and fall flat.
- **Cowards:** fleeing a band or turning back from a toll raises each character's standing with Cowards.
  At 10, **Craven the Cowherder** in the Waylay Inn gives them the **Coward's Cloak** (once): worn on the
  head, used once and gone, it makes the monsters beside you move at half speed on their next turn and
  turns your Rumble and Od Hand dice left this turn into Wend dice.
- **Shove:** roll Rumble dice (and the weapon) at an adjacent monster as for an attack; beat its
  defense and it's pushed one tile straight away from you (diagonally too) instead of hurt. Onto a
  trap, the trap goes off on it (spikes: 1 heart; trap door: it falls). Into a pit, it drops to the
  room below and loses a heart, or, bottomless, is gone for good (it counts as your kill). Bosses can't
  be shoved (nor the Key Warden), and the tile behind must be free floor, a trap, water or a pit. Flyers
  don't fall; a monster shoved into deep water loses a heart and scrambles out. Monsters
  don't shove yet.
- **Friendly fire** (a switch on the party screen, off by default): blasts also
  catch heroes and companions in range, the thrower included, and you can attack and shove other
  heroes and companions in reach just like monsters (click them; Shove works on them too). Each
  defends with their own Defense dice and armor. A hero brought to 0 hearts this way Keels Over as if by a
  monster: no kill or XP for the attacker. The top bar shows **FF** while it's on.
- **Hidden stashes** hold a key or a card, spilled onto the floor when found.

### Monsters (first theme)

| Monster | Hearts | Move | Attack | Defense | Notes |
| --- | --- | --- | --- | --- | --- |
| Slime | 1 | 2 | 1d6 | 1d6 | When defeated, splits into two Slimelings (1 heart, 1d6 attack, no defense, don't split) |
| Bat | 1 | 4 | 1d6 | none | Flies over pits; moves erratically |
| Spear Guard | 2 | 3 | 1d6+1 | 1d6+1 | Reach 2 in a straight line |
| Archer Guard | 2 | 2 | 1d6+1 | 1d6+1 | Ranged 5; keeps its distance |
| Skeleton | 2 | 3 | 1d6+1 | 1d6+1 | Lesser undead. Rises again once unless finished by a bomb or spell; Holy Water destroys it outright |
| Cinder Imp | 1 | 4 | 1d6 | 1d6 | From floor 2. Fire attacks (a 6 sets you alight, hit or miss); can't burn |
| Fire Beetle | 3 | 1 | 2d6 | 1d6+2 | From floor 2. Fire attacks; can't burn; slow and armored |
| Petty Thief | 2 | 4 | 1d6 | 1d6 | Od Hand 3: steals from a hero beside it (each die over their Od Hand roll and tools takes, in turn, 2 gold, an item, 2 gold...); with loot it runs from everyone |
| Mugger Fly | 1 | 6 | 2×1d6 | 1d6 | Flies. Goes for the hero with the most gold; every hit takes 1 gold; Od Hand 1 steals gold only |
| Mimic | 4 | 2 | 2d6+1 | 1d6+1 | Not spawned: about one ordinary chest in ten is a Mimic (never the Boss Key's, nor in an arrival room). Opening it or trying its lock wakes it with a free bite; a Search reaching 5 + floor finds it out first. A hit grabs: the hero can't move or leap until it's hurt, shoved, stunned, frozen or knocked flat. Always drops 2 cards |
| Ogre | 5 | 2 | 2d6+2 | 1d6+1 | Rare, from floor 2. Each hit knocks the hero a tile straight back, into a pit or onto a trap if one's there |
| Ogre Champ | 6 | 1 | 3d6 | 1d6+1 | Only in its den. Reach 2. Drawn bigger: fat, shirtless and horned, with a great club. Each hit knocks the hero a tile back, and every 6 on its roll a tile more, hit or miss |
| Necromancer | 3 | 2 | 1d6+1 | 1d6 | Rare, from floor 2. Ranged 4; keeps its distance. Every other turn raises a Skeleton beside it (from a grave nearby if there is one): 2 at a time, 4 in all. They crumble when it falls and are worth no XP, loot or kill |
| Ghoul | 4 | 6 | 3×1d6 | 1d6 | Rare, from floor 2. Greater undead: Holy Water hits it with 5 Radiate dice. Three attacks of a Necro die each, and every hit stuns. Its miasma attacks every living creature starting its turn beside it with a Poison die |
| Hexer | 2 | 3 | 1d6+1 | 1d6 | Rare, from floor 2. Ranged 4; keeps its distance. Each hit drains a Luck reroll; with none left, it stuns |
| Gelatinous Cube | 4 | 1 | 2d6 | 0d6+1 | Rare, from floor 2. Swallows the cards and gold on every tile it crawls over, dropping them where it falls; splits into two Slimes |
| **Miniboss: Key Warden** | 5 | 2 | 2d6+1 | 1d6+1 | Guards the Boss Key in the last floor's vault; can't be shoved |
| **Boss: Iron Warden** | 8 | 3 | 2d6+1 | 1d6+3, an extra die | Wears its Warden's Plate (armor 3, and an extra Defense die rolled in while a defense is losing). Each turn: first a **chain** flung at the nearest hero in sight within 6 who isn't beside it (3 dice): a hit drags them beside it, **shackled** (no Wend until a key or a picked lock, 9+, frees them: their own, or an ally's beside them). Then its **arc**: a swing through the 3 tiles toward its target (straight on, the tile ahead and the two beside it; on a diagonal, that corner and the two tiles next to it), striking everyone there, monsters too, with one roll; each 6 **cleaves** on into one more creature beside it. Then a **spin**: one roll with all its dice against every hero within 2. One time in four it drops Warden's Plate besides its loot |

Monsters follow the same dice rules; the +1s are weapon and armor values. Whatever a monster stole drops where it falls. Each turn a monster splits its
Attack dice into whichever attacks are likeliest to land the most hits on its target (its weapon goes on
the first), so a 2-dice monster makes two quick attacks against an unarmored hero but one big one against
a well-armored hero.

**Monster AI:** each monster heads for the closest tile it can attack from and stops as soon as it can
attack. Among the heroes it can reach, it attacks the one with the fewest hearts. Ranged monsters back away
from adjacent heroes before shooting, and erratic monsters take each step in a random direction half the
time. Monsters stay in their own room.

Monster stats and cards live in `src/Woad.Core/Content/Data/*.json`, and each dungeon theme has its own
file under `Themes/`.

### The Sunken Temple (second dungeon)

Picked with the Dungeon button on the party screen. Three flooded floors of sandstone, coral and teal
water, with its own exploring song. Hand-made rooms have pools, channels, causeways and islands; built
rooms get pools (shallow at the rim, deep in the middle, now and then with something lurking in them),
streams are never chasms, and the boss arena is a great pool around the Leviathan.

- **Swimmers** move through shallow and deep water at 1 step a tile. One in deep water is **submerged**:
  ranged attacks, spells and bomb blasts can't reach it, only a blow from right beside it (or a
  Trident). It still strikes out of the water as usual.
- **Shells:** a shove that beats a shelled monster's defense flips it onto its back (it's prone, and
  pushed too if there's room). Until it gets up its armor doesn't count.
- **Pulls:** a hit from a tongue or hook drags the hero in a straight line to the monster's side (a pit
  or trap on the way takes them as a shove would).

| Monster | Hearts | Move | Attack | Defense | Notes |
| --- | --- | --- | --- | --- | --- |
| Snapper Eel | 2 | 4 | 2d6 | 1d6 | Swimmer that never leaves the water; lurks submerged and bites anyone at the edge |
| Jellyfish | 1 | 2 | 1d6 | none | Floats over water and pits; swimmer; every 6 on its attack stuns you |
| Crab Knight | 3 | 2 | 1d6+1 | 1d6+2 | Shell |
| Bog Frog | 2 | 3 | 1d6 | 1d6 | Swimmer; tongue reaches 3 in a straight line and pulls you to it |
| Drowned | 2 | 2 | 1d6+1 | 1d6+1 | From floor 2. Swimmer; rises again once, like a Skeleton |
| Petty Thief, Mugger Fly | | | | | Rare, as in the Keep |
| **Miniboss: Crab King** | 6 | 2 | 2d6+1 | 1d6+2 | Shell; guards the Boss Key |
| **Boss: Leviathan** | 10 | 4 | 2d6+1 | 1d6+1 | Never leaves the water, so it's nearly always submerged; bites at reach 2; every third turn a tail sweep hits every hero within 2 |

Its loot deck adds three cards:

- **Trident:** two-handed, Weapon 1, reaching 2 tiles. +1 more against swimmers, and it spears them even
  submerged.
- **Diving Helm:** Armor 1. Water costs 1 step a tile, and staying afloat in deep water takes no Rumble die.
- **Pearl:** treasure worth 20 gold.

### The Unhallowed Graves (graveyard)

A walled graveyard where it is always night, out on the Woarld Map (east of the river, below the
Emerald Keep) and on the Dungeon Run list. It's **one room, 100 by 100 tiles**, not floors of rooms:

- **The ring:** a ragged band of stone pillars, two to five deep, walls it in. The party comes in at
  the gate at the bottom, which rusts shut behind them. The only ways out are a few gaps through the
  ring to an **exit** at the very edge (sparse: about three), and about three more walled up in
  **cracked stone** that a bomb opens. Stand on an exit and Escape.
- **The ruined temple** stands in the middle: pillared walls with a wide doorway, braziers either side
  of the pulpit and the **reliquary** before it.
- **The relic quest:** Father Ambrose, a priest at the Waylay Inn, gives the party the **Holy Key** (a
  quest card: it can't be sold) and asks for the **Relic of Saint Wyn**. The reliquary has a holy lock:
  no key or pick opens it, only the Holy Key, which it keeps. Anyone in the party bringing the relic
  back earns gold (60) and two Holy Water for whoever speaks to him, and a better name at the inn.
  Lose the key and he gives another. Without the quest, the relic stays where it is.
- **The ritual quest:** with the relic home, Father Ambrose sends the party back to stop the cult. Raided
  with the quest taken, the Graves are darker (only two braziers burn) and the ritual is under way: a
  circle glows on the temple floor, a **cabal of five Cabal Necromancers** stands round it and the
  **Cult Leader** chants at the pulpit, all awake and none of them moving (they go on the undead's
  turn). The header counts down **30 rounds** from the gate. Kill all six and the ritual is broken:
  the priest pays 150 gold and three Holy Water. Let the rounds run out and the **portal to the realm
  of death** opens on the circle, purple and pouring souls: the graveyard is **cursed**. An ambient
  purple glow lights it all, every one of the dead wakes and comes, and each moves a d6 further every
  turn. It stays cursed, portal open, every time the party comes back.
- **Crypts** dot the ground: walled rooms with a doorway each, some built onto another with a door
  through the wall they share. Sarcophagi line their back walls; each has a brazier.
- **Graves** lie in plots, in rows with room to walk between, and more **sarcophagi** stand alone
  toward the middle. Both open like chests. A grave may hold a card (30%), a hidden needle trap and a
  card (12%), or one of the dead (18%: a Skeleton or Zombie climbs out beside it and strikes at once);
  the rest are empty. Sarcophagi hold more (45% a card, 15% trapped, 30% the dead: usually a Rotten
  Mummy, else a Ghoul or Zombie), and some are locked (30%), with better treasure.
- **Night:** the whole graveyard is dark. About ten of its braziers burn from the start; the rest wait
  for a torch.
- **The horde** (about 60: Skeletons, Zombies, Rotten Mummies, Death Cultists, a few Ghouls and
  Necromancers) gathers thicker toward the middle and in the crypts; a few **strays** (Mugger Flies, a
  Gelatinous Cube, Slimes, Grave Robbers) wander among them. Nothing starts within 12 tiles of the gate.
- **Rotten Mummy** (greater undead): slow (move 2) and tough (5 hearts, armor 1). **Rotting Hand:** 2
  dice and a Necro die, weapon 1; a 6 among them tries to give Nekrot. With no one in reach of a blow it
  casts **Swirly Eyes** at a hero within 3 (a Psycho die; a hit drags them a tile nearer). Dry as
  tinder, it catches fire two dice at a time.
- **Grave dust:** whenever it loses a heart, each hero beside it rolls a d6, and on 3 or under Nekrot
  tries to take them (then the roll to resist). Destroyed, it bursts: the dust reaches 2 tiles, and a
  Poison and a Necro die strike every hero and living monster there (not the undead). Fire or Radiate
  that hurts or destroys it keeps the dust down.
- **Grave Robber:** a sicklier Petty Thief; may carry a Shovel (25%) and Thieves' Oil (40%).
- **Death Cultist:** robed, masked and armed with a curved knife (Necro dice).
- **The dead lie still:** every monster there sleeps until a hero comes within 6 tiles of it (or it's
  hurt), and only then takes turns. Woken, it stays awake. Every other sleeping stray wanders a step or
  two each turn; the rest stand about.
- **Initiative:** turns go by initiative there, not seat. At the start of each round every hero rolls
  all their Wend dice and adds their Wend bonus; the monsters' turn stands at 5 and the undead's at 1,
  and everyone goes from highest to lowest. A hero who ties an enemy turn goes right after it. Heroes
  tied with each other roll off, a die each (a 6 rolls another), highest first; tied again, a die more.
  The turn order along the top follows the round's initiative.
- **Two enemy turns:** the **monsters** (the strays) take their turn, and the **undead** one of their own: the horde, the dead and those who keep them (Death Cultists and
  Necromancers too; Grave Robbers are monsters). The undead's card in the turn order is a fanged skull
  with glowing green eyes.
- **The horde comes for you:** the more of the dead the heroes wake by coming near, the higher the
  horde's alert: 4 woken raise it to 1, 10 to 2, 18 to 3. At the start of each undead turn every
  sleeper within reach of a hero stirs anyway and comes: 12 tiles at alert 1, 24 at alert 2, the whole
  graveyard at 3. Moves out of sight happen at once.
- **Getting away:** stepping out at an exit, every undead beside you gets a parting blow first; drop,
  and you don't get away.
- The board draws only what's near the view, and the map panel shows the graveyard small: what the
  heroes have come near (stone, graves, exits in green, cracked stone in gold) and their dots.

## Dungeon

- **3 floors**, each 6–8 rooms, laid out by a random walk on a 5×5 room map that all floors share,
  so a pit or trap door drops into the room underneath (never into the boss room), landing as far across
  it as the hole was across its own room. A floor's Arrival
  room is under the stairs that lead to it. Above the last floor, rooms with nothing underneath get
  layouts without pits. A few extra doors between neighboring rooms add loops.
- **Room roles:** the walk's first room is the Arrival room, which has no monsters. The room farthest
  from it holds the Stairs down (floors 1–2) or the Boss and Exit (floor 3), behind the Boss Door. A dead
  end becomes the Treasure room (chests, one guard), on floor 3 another becomes the Vault (the Boss Key's
  chest and the Key Warden), half the floors get a Secret room, floor 1 gets the Ogre Champ's den, and the
  rest are Combat rooms.
- **Terrain:** some rooms get a patch of rubble; some have a wall dividing them, or a chasm (only where
  pits lead somewhere, or on the last floor) or a deep stream across them with one crossing (a bridge or
  a shallow ford); some neighbors get a breach in their shared edge. Every one is checked so that
  doorways, stairs, the exit and monsters all stay reachable, on foot or through a locked gate.
- **Room sizes and shapes:** most rooms are the theme's handcrafted 9×9 text templates (walls, pits,
  chests, stairs, exit, hero starts, monster spawn points, the boss), randomly rotated and mirrored;
  arrival rooms and vaults always are. The rest are built to order: small rooms (5×5 to 7×7, often for
  treasure), long halls and pillared galleries (up to 13×7), and big chambers (11×11 to 13×13) that may
  be round, cross-shaped or L-shaped, with solid rock outside the shape. The **boss arena** is always the
  biggest room, 15×15. Every room keeps its edges clear for doors and all its floor in one piece. When
  content loads, every handcrafted layout is checked: doorways are plain floor, and everything can be
  reached from every door.
- **Monsters** are bought for a room's spawn points from the theme's roster (weighted, some only from
  floor 2) until its threat budget runs out. Slimes and bats cost 1; guards and skeletons cost 2.
  Budgets *(tunable)*: combat and stairs rooms get floor + party size − 1, plus 1 in a trap room; scaled
  by the room's open floor against a standard room's (about 70 tiles), so big rooms hold more;
  treasure rooms 1; arrival rooms 0; the boss room the boss plus party-size worth of guards.
- **Doors** are gold bars on the room's edges, anywhere along them (the middle is most likely), on floor
  with open floor inside. Stand in the doorway and click it again to go through
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
  arrival room. Reaching a floor for the first time restores your Luck and recharges wands. Below the
  first floor, the arrival room has **stairs up** that lead back to the stairs room above (1 step).
- **Exit:** sealed until the boss in its room is defeated. Stand on it and click again to escape. Your
  turn ends, and you're out of the run with your treasure.
- **Splitting up:** heroes can be on different rooms and floors. During the monsters' phase the view
  cuts to each room where something happens.
- **Seeds:** every run has one. The same seed and party size always build the same dungeon.

## Loot, Keeling Over and winning

- **Chests** deal from the theme's loot deck. Some cards are marked with a minimum floor (better
  weapons and armor from floor 2, the Golden Idol on floor 3). Those stay in the deck until a chest on a
  deep enough floor turns them up.
- **Monster drops:** each monster has a drop chance *(tunable)*: slimes 10%, bats 15%, guards 25%,
  skeletons 30%. The Iron Warden always drops 3 cards. Drops land as a loot pile where the monster fell.
- **Pots** block movement until a hero beside them smashes one (uses the action). In the Emerald Keep:
  35% Loose Coins (2 gold), 25% a heart, otherwise nothing *(tunable)*.
- **Found items:** cards found during a run are tagged as found.
- **Gold:** coins (Loose Coins, Coin Pouches) never take a backpack slot: whoever finds them, in a
  chest, a pot or a steal, pockets the gold at once (shown beside Kills on the hero panel). Coins a
  monster drops or a stash holds lie on the floor as gold, picked up like any loot. Gold counts as
  treasure for the winner and becomes the character's gold on escape; someone who dies drops theirs
  on their tombstone, and companions left behind lose it.
- **XP** *(tunable)*: each monster's XP goes to whoever defeats it (slime 2, guards 4, skeleton 5, Iron
  Warden 25). Reaching a floor for the first time gives 5, and escaping gives 10.
- **Keeling Over:** a hero or companion reduced to 0 hearts falls where they stand (X's for eyes) and
  is **Keeling Over**. They keep everything they carry and still take their turns, but all they can do
  is roll the **Keel Die** (the End turn button becomes **Roll Keel Die**):
  - **6:** a mark of life, and a **Power Die** is rolled at once: another mark of life on a 6.
  - **1:** a mark of death, and a **Doom Die** is rolled at once: another mark of death on a 1.
  - **Banked Power Dice** can be added to the roll (select them first). Their 6s are marks of life;
    their 1s do nothing.
  - **Creeping doom:** every Keel roll after the first adds Doom Dice (1s are marks of death): 1 on the
    second roll, 2 on the third, and so on, so nobody lingers for long. It starts over when they stand up.
  - **Three marks of life:** they stand back up with 1 heart (and their turn ends). **Three marks of
    death:** they die. Reaching both at once, life wins. Marks start over each time someone keels over.
  - **Rousing:** a standing ally next to them (diagonals too) can click them to roll Od Hand dice for
    them, like a search: not the action. Every 6 of the total (tools and Power Dice included) is a mark
    of life, and every die showing 1 adds a Doom Die to their next Keel roll. Three marks stand them up
    at once.
  - **Monsters** go after heroes still standing. A monster with nobody standing in reach strikes at
    someone Keeling Over (one attack with all its dice, defended as usual); a hit is a mark of death.
  - **Death:** a hero who dies is out of the run. A **tombstone** marks the spot ("Here lies Bran" or
    "RIP Bran", with what killed them: "Slain by a Goblin", "Drowned", "Impaled on spikes"...). It holds
    everything they found this run and their keys, for anyone to pick up (looting, as usual); it doesn't
    block movement or sight. They keep the gear they brought in and their XP.
- **Exit:** beating the boss opens the exit, and stepping onto it escapes.
- **End of the run:** when every player has escaped or died. A player Keeling Over keeps the run going:
  the others, and the monsters, play on. Companions don't keep a run going (see [Companions](#companions)).
- **Winner:** the escaped player with the highest treasure value. Ties go to the player with more kills.
- **Rewards:** everyone keeps their XP. Escaped players keep everything they carried out, and treasure
  becomes the character's gold. Players who died keep only the gear they brought in that they still hold.
  Dying only loses the run: the character lives on in the roster.
  Single-use cards used up during the run are gone. Spending gold comes after the MVP.
- **Results screen:** when a run ends, results are saved automatically and the game moves to a results
  screen. It shows the winner(s), and for each hero how the run ended, kills, XP and any level-up, and
  the cards found and lost; **Done** goes back to the map (or the party screen). Abandoning a run
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
- **The Party menu** (**Party**, beside Menu in a dungeon and beside Enter on the Woarld Map) shows
  everyone's level, hearts and gear. On your turn you spend the points a level brings, yours and your
  companions', without waiting for the run to end (they're kept); on the map, **Level up** opens a
  character's points, and clicking a card then a name hands it to them. Each companion has three
  switches for its own things, whatever its order: **drinks potions** when badly hurt (on by default),
  **picks up loot** within reach when there's no fight, and **wears better gear** it carries (worth more
  than what it has on).
  - **Follow** (the default): stays within 2 steps of you, through doors and down the stairs, attacks
    what comes within reach and goes after monsters closing in on you.
  - **Hold:** stays on the tile it stood on when told, attacking what comes within reach.
  - **Fight:** goes after the monsters in the room; follows you once it's clear. With a bow or a thrown
    weapon it hangs back where it can shoot, away from the monsters if it can, rather than closing in.
  - **Stay back:** keeps away from monsters near you, striking only when one is right next to it.
  - **Aid:** goes to allies Keeling Over in the room, even mid-fight, and rouses them, and cures what ails
    the allies beside it (the easiest first); otherwise follows.
  - **Find me:** follows you wherever you are, through locked doors too (a key, or picking the lock;
    never the Boss Door) and, when there's no way by the stairs, down a pit to the floor below.
  - **Burgle:** picks the room's locks (doors, chests, gates; keys first) and opens its chests, leaving
    what's inside for you to rummage through; then follows.
  - **Loot:** gathers the room's loot (bags, chests, gold, keys, pots), then follows. Trade with it to share.
  - **Explore:** searches each room once for secret doors, traps and stashes, then follows.
  - Under any order but Hold, Fight and Stay back, a companion guards you: it goes after monsters
    closing in on you. Under any order, it rouses an ally Keeling Over right beside it, and (except
    when holding) goes to one once the room is clear. Keeling Over itself, it rolls its own Keel Die.
    A companion that comes by the Boss Key hands it to you as soon as it's beside you.
- **What companions don't do unless told:** loot, open chests, smash pots, unlock or search; they never
  steal. They keep out of doorways, the stairs, the exit, loot and the spots beside unopened chests when
  they have nowhere to be. If their owner is dead or gone, they follow the first player still in.
- **Trading:** click an ally beside you (diagonals too), a fellow player or a companion, to trade:
  give and take cards (part of a stack asks how many), gold, keys and the Boss Key. Your own
  companion's cards also have **Equip**, **Belt** and **Stow**, so you can dress it. Trading is free;
  if they're standing on loot, it can be opened from there.
- **Monster scaling:** a companion counts as half a hero (rounded up) when sizing rooms and the boss.
- **End of the run:** the run ends once every player has died or escaped. When a player escapes, their
  companions standing in the same room escape with them. Companions still inside (Keeling Over
  included) are left behind: like a hero who died, they lose what they found and keep the gear they
  brought and their XP.
- **Keeling Over:** companions keel over and can die just like heroes (see [Keeling Over](#loot-keeling-over-and-winning)).

## Persistence

- **Location:** one JSON file per character at `user://characters/{id}.json`. On Windows that is
  `%APPDATA%\Godot\app_userdata\Woad\characters\`.
- **Contents:** schema version, id, name, portrait (crest), appearance (look and colors), creation time, XP (level is derived from it), stat
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
│ Active hero  │ floor / room / turn  │ Belt (4)     │
│ portrait     ├──────────────────────┤ Backpack (8) │
│ hearts/stats,│                      │ Card text    │
│ gold, keys   │  9×9 view of room    │ Message log  │ 360 px
│ Equipment    │   288 × 288 px       │              │
│ slots,       │                      │ Actions      │
│ minimap      ├──────────────────────┤              │
│              │ dice bar: A  VS  B   │              │
└──────────────┴──────────────────────┴──────────────┘
   176 px              288 px              176 px
```

- **Camera:** the board is a 9×9 view onto the room. A bigger room scrolls to follow whoever is acting
  and comes back to the active hero after each of their actions; a smaller one sits centered in solid
  rock. Right-drag (or the arrow keys) pans to look around, but only as far as keeps the active hero in
  view. A plain right-click still cancels.
- **Scaling:** a 640×360 layout, integer scaled to 1280×720, 1920×1080, 2560×1440 and 4K, with
  nearest-neighbor filtering. The window stretches in `canvas_items` mode, so text renders at full
  resolution and stays sharp while the layout keeps its 640×360 coordinates.
- **Dungeon art:** original 16×16 placeholder sprites, drawn at 2× (32px tiles), in a 16-bit palette
  that nods to *A Link to the Past*:
  - **Tiles:** green flagstones, stone blocks, chasms that join up with their neighbors, stairs, a
    barred or lit exit, chests (a locked one shows a gold lock plate with a keyhole; the Boss Key's
    chest is crimson with gold horns) and clay pots.
  - **Walls and doors:** a thin rim of the dungeon's stone runs round every room's outline (along the
    rock of round, cross and L-shaped rooms too), broken only by doorways and blown-open breaches.
    Each door sits in a round-topped stone archway: open (a dark way through, its leaf swung back),
    locked (iron-bound wood with a keyhole), the Boss Door (crimson, with gold horns round the frame
    and the keyhole), a found secret door (a stone panel standing ajar), walled up (cracked stone), or
    a sealed trap room's red portcullis. A secret door no one has found is just more rim.
  - **Heroes:** a sprite per look. The art keeps the main, trim, skin and hair pixels as white masks
    (with a grey for shading), which the game tints with the character's colors.
  - **Monsters:** a sprite each. Slimelings are small slimes, the Iron Warden looms larger, and a
    collapsed skeleton becomes a bone pile.
- **UI art:** dark panels in riveted, gold-trimmed frames, and an original serif capitals pixel font
  for headings, titles, banners and callouts, in the spirit of *Castlevania*. Body text is Jersey 10
  (SIL Open Font License; `assets/fonts/Jersey10-OFL.txt`), drawn on its 10-pixel grid so every
  letter stays crisp and clear.
- **Dice colors:** Rumble dice are red, Wend green, Od Hand yellow, Defense blue, tools' dice bronze,
  Power Dice purple, typed dice their type's color; weapon dice look like Rumble dice. Other rolls (the Keel
  Die, blasts, burning) are white.
  The **This turn** dice tokens and the buttons that spend them share those colors, as a stripe and tinted
  text: Rumble and Shove red, Wend and Leap green, Search and Steal yellow.
- **Dice bar:** under the board, fighting-game style. Whoever rolls is on the left with their dice,
  bonus and total; whoever answers is on the right of a slanted VS (several defenders, as in a blast,
  each get a smaller column). The winning side glows gold, and each defender is tagged HIT, MISS or
  BLOCKED. Dice jitter and spin as they tumble, then land with a small pop. With Luck, the roller's
  dice can be clicked to reroll them. Rolls with no opponent (moving, the Keel Die) show one side.
- **Hits, fighting-game style:** a hit freezes the action for a beat (hit-stop), flashes the target
  white, slashes across it in a spray of the attacker's sparks, shakes the view and makes the target
  reel, under a big slanted callout that slams in and slides away:
  - **HIT!**; **CRITICAL!** when Power Dice made the difference; **K.O.!** when it defeats a monster;
    **2 HITS!**, **3 HITS!**... when one attacker (or one blast) lands several in a row.
  - **MISS** for a plain miss; **BLOCKED!**, with blue sparks and a clang, when a shield (or a
    monster's armor) made the difference.
  - **DOWN!** when a hero keels over, and **STUN!**, **BURN!**, **TOPPLED!** and **PULLED!**.
- **Sound:** synthesized 8-bit effects for buttons, dice, footsteps, hits, blocks, defeats, healing,
  coins, chests, pots, doors, stairs, turns, the monsters' phase, keeling over, dying and victory.
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
| M5 | Loot & scoring ✅ | Loot deck, chests, drops, tombstone loot, results screen, rewards persisted |
| M6 | Theme pass ✅ | Original placeholder art for the first theme, gothic UI frames, SFX, dice animation |

## Out of scope for the MVP

Online multiplayer, saving and resuming a run in progress, shops and spending gold, additional
dungeons and themes, keyboard or gamepad controls, music beyond placeholders, achievements and store
integration.

## Assumptions to confirm

These defaults were not covered in the kickoff questions:

1. Movement and monster melee are **orthogonal only**. Heroes can also attack diagonally.
2. Base stats and point costs as in [Stats](#stats). Movement is 1d6 + 2 at the start, and boots add dice. Starting heroes attack with 1d6 + 1 (Wooden Sword)
   and defend with 1d6 + 1 and Block 1 (Buckler).
3. The **belt** holds 4 consumables, and using one doesn't take your action.
4. **No friendly fire at all**, including bombs.
5. Players at 0 hearts **Keel Over** and can be roused or stand up again; three marks of death kill them for the run.
6. Monsters **can't follow** players into other rooms.
7. Carried-out gear persists **without a cap**, and dungeon difficulty scales to compensate.
8. **Saving a run in progress** is not part of the MVP. A run is about 45–60 minutes.
