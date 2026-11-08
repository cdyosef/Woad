# How to Play Woad

Woad is a turn-based dungeon crawl played like a board game. One to eight players share one screen and one
mouse, taking turns. Every room is a 9×9 grid of tiles. Dice decide how far you go, how hard you hit and how
well you hold off a blow; cards are your gear, your magic and your loot.

You work together to get through the dungeon alive, but you're also racing each other for treasure: of the
heroes who escape, **the one carrying the most treasure wins**.

---

## The three kinds of dice

Nearly everything you do is rolling d6s from one of three pools. Each pool has its own symbol, and you'll see
those symbols everywhere: on your character sheet, in the dice tray, and in the top-right corner of every
card, showing which pools that card adds to.

| Pool | Symbol | What it's for |
| --- | --- | --- |
| **Wend** | a boot | **Moving.** Roll Wend dice and walk that many tiles. |
| **Rumble** | a fist | **Fighting.** Roll Rumble dice to attack, shove or charge. |
| **Od Hand** | a hamsa (an open hand) | **Everything clever.** Searching, picking locks, disarming traps, stealing, aiding a friend, and a follow-up strike with whatever's in your off hand. |

At the start of each of your turns you get all your dice back. During the turn you spend them in any order,
on as many moves, attacks and checks as you like, until they're gone.

**Rolling several dice together or one at a time is your choice.** Rolling three Rumble dice at once makes one
big attack that's hard to stop; rolling them one by one gives you three smaller tries. Click the dice in the
tray to choose how many go into the next roll.

### Wend: moving

- Press **Wend** (or **W**). Your selected Wend dice are rolled, and the tiles you can reach light up. Click
  one to go there.
- Your **Wend bonus** (the "+1" in "Wend 1d6+1") adds to one move a turn.
- Steps you don't use carry over for the rest of your turn: walk a little, attack, then keep walking.
- Moving is orthogonal (no diagonal steps). Walls and monsters block you; your allies don't.
- **Rubble** and **water** cost 2 steps a tile. You can swim in deep water, but ending your turn in it costs
  a Rumble die to stay afloat.
- **Leap:** pick a tile up to 3 away in a straight line, then roll a Wend die; you sail half the roll. It's
  how you clear pits and traps.

### Rumble: fighting

- Press **Rumble** (or **R** or **Space**). Your reach shows in red; click a monster in it.
- You roll your chosen Rumble dice and add your **weapon's** value (once a turn, on one attack).
- The monster rolls its defense and adds its armor. **Beat it and it loses a heart.** A tie goes to the
  defender.
- Each Rumble die you've bought also gives a **Rumble bonus**, added to one attack a turn.
- **Shove** and **Bull Rush** are Rumble skills: push a monster a tile, or charge down a line into one.
- **Guard:** set a Rumble die aside to protect yourself or an ally beside you from the next blow.
- Double-click a monster you can reach to aim at it, then click it once more to attack.

### Od Hand: the clever stuff

The Od Hand is your "other hand": the one that searches the walls, works the lock and dips into a pocket.
Od Hand dice only work for the **skills you've learned** (or with a tool doing the job for you).

| Check | How it works |
| --- | --- |
| **Search** | Reveals secret doors, hidden traps and stashes around you. Light helps: every torch or flame you stand by adds +1. The first search in each room is worth XP. |
| **Pick Lock** | Stand at a locked door or beside a locked chest with no key, and click it. |
| **Disarm** | Click a trap you've found next to you. |
| **Steal** | Lift something from a monster (or another hero) beside you. Beat their roll. |
| **Aid** | Help yourself or a friend shake off poison, blindness and the like, or rouse someone who's Keeling Over. |
| **Parry** | Set an Od Hand die aside to add to your next defense. |
| **Off-hand strike** | Once you've attacked with Rumble this turn, the item in your off hand (a dagger, a rock, a torch) can follow up with one Od Hand die. |

**Tools** add to your Od Hand rolls: Lockpicks on a lock, Burgling Tools on a trap, Sticky Gloves on a steal,
a Medicine Bag on Aid. Where a tool is matters:

- **In a hand** (shown in the Od Hand square), it adds its bonus to your Od Hand dice.
- **On your belt or worn**, it works on its own: double-click it to use just the tool.
- Without the skill at all, a tool can still do its job alone, rolling only its own dice.

Od Hand checks **don't use up your action.**

---

## Your character sheet

The panel on the left is you. Its squares are where your cards sit:

- **Top row:** Accessory, Head, and Magic (your spells).
- **Middle row:** the **off hand** (it shows the Od Hand card, with your off-hand item tucked in it), your
  **Body** armor, and the **main hand** (your weapon, which is your Rumble).
- **Bottom row:** your **Bag** (8 cards carried but not equipped), your **feet** (boots, and your Wend), and
  your **Belt**.

Beside each square are little symbols showing your dice: lit while you still have them this turn, dark once
spent.

### Hands, two-handed weapons and the belt

- A **two-handed** weapon (a Great Axe, a Maul, a Big Club) fills both hands.
- Your **belt** holds things ready for quick use: potions, bombs, tools, even a spare weapon or shield.
  **Using one belt item a turn is free.** Double-click a belt card to use it, or to draw it into a hand.
- A weapon or shield on the belt is only being carried there. Draw it into a hand to use it (double-click
  it, or drag it onto the hand). Whatever was in that hand goes onto the belt in its place, so it's still to
  hand later. It only goes in your bag if the belt is full.
- **In a fight you can't take anything out of your bag.** Keep the weapons and tools you'll want on your belt.
- Sticky Gloves and similar accessories can be dragged from the accessory square into your off hand. They
  leave a faded "ghost" behind; double-click the ghost to put them back on.
- **Out of a fight, changing gear is free. In a fight it costs your action.**

---

## A turn, step by step

1. **A banner shows whose turn it is**, and the view jumps to that hero.
2. **In any order, as you like:**
   - Move with **Wend**.
   - Attack with **Rumble**.
   - Make **Od Hand** checks.
   - Take **one other action**: use a card from your bag, smash a pot, loot a second time, or (in a fight)
     change your gear.
   - Take **one free loot** (pick up what's on your tile) and use **one free belt item**.
3. Press **End turn**. Your dice come back next turn.

When every hero has gone, it's the **enemy phase**: every monster in a room with a hero in it takes its
turn. Then a new round begins.

---

## Defending

When a monster attacks you, you roll your **Defense**: your base die plus your **armor**. If you're
losing, the game backs you up automatically, in order:

1. **Guard** dice from allies standing beside you.
2. Your shield's **Block**.
3. Your **extra Defense dice**, one at a time, until the blow is stopped.
4. Your banked **Power Dice**.

Block and extra dice, once spent, come back at the start of your next turn. Standing together helps: two
heroes with the Guard skill side by side can guard each other, and a hero bearing a shield painted with their crest
(**Shield Paint**) gives everyone beside them +1 defense.

---

## Power Dice and Luck

- **Power Dice:** every **6** on a plain die earns a Power Die. When it pops up over the dice tray, choose
  **Roll** (add one more die to this roll right now) or **Bank** (save it). You can bank up to 3, and they
  stay with you between turns, floors and even runs. Banked dice show in the tray's POWER row: select them
  to roll them into your next attack or move.
- **Luck:** each point of Luck is one reroll of a single die, per floor. When a roll is about to fail,
  **Try your Luck?** pops up: click a die and a green Luck die rolls beside it. **The higher of the two
  stays.** A lucky 6 doesn't even use the Luck up. Lucky Clovers found in the dungeon give extra Luck.

---

## Typed dice

Some dice are made of something: **Fire, Ice, Poison, Acid, Earth, Elektrik** and more. A held torch adds a
Fire die; a Frost Brand's die is Ice; a Boulder brings an Earth die. A typed die earns no Power Die, but each
6 rolls another of its type straight in, and **on a hit** lays its effect on the target: Fire sets them
burning, Ice slows and freezes them, Poison weakens their rolls, Earth knocks them flat.

---

## Keeling Over (and getting back up)

At 0 hearts you don't die straight away: you **Keel Over**. You still take turns, but all you can do is roll
the **Keel Die**:

- A **6** is a mark of life, and wins you a Power Die that you keep if you stand back up.
- A **5** gives you a Power Die to roll in with your next Keel roll.
- A **3** or **4** does nothing.
- A **2** adds a Doom Die to your next Keel roll.
- A **1** is a mark of death (and rolls a Doom Die for another).
- From your 10th Keel roll on, extra Doom Dice creep in, so nobody lingers for ever.
- **Three marks of life:** you come round with 1 heart, lying prone, and play the rest of your turn as normal.
  **Three marks of death:** you die.

Friends can help. A standing ally beside you can **Aid** you with their Od Hand dice (every 6 is a mark of
life), or **carry** you out of danger.

If you die, a tombstone marks the spot, holding everything you found this run for others to take. Your
character isn't lost: they keep their XP and the gear they brought in, and can play again.

---

## Magic

Each rank of the **Magic** stat teaches a basic spell, holds a Power Die, and adds its rank to the spells you
cast. Spells cost Power and take your Rumble dice. Scrolls found in the dungeon can be cast once, or learned
as a new spell. Magic is dangerous, and people don't like it.

---

## Making a character

From **Characters**, create a hero:

- **Name, look and colors:** pick a figure (there are dozens, from Knights and Witches to Frog Men and
  Turtle Reapers) and its main, trim, skin and hair colors.
- **Crest:** your shield's field pattern, charge (a sun, a tower, an eagle...), and its colors. Left alone,
  it follows your character's colors.
- **Points:** 6 to spend on Hearts, Wend, Rumble, Defense, Od Hand, Luck and Magic. Hover a stat to see
  what it does. Your **first Od Hand die brings a free Od Hand skill**, and **every Rumble die brings a free Rumble skill** of your choice.
- **Skills:** 1 point each, from the **Skills...** window.

Every hero starts with a Basic Belt and Buckle, a Club, a Torch, a Health Potion and Lockpicks.

---

## Winning, losing and getting stronger

- **Find the way down** through each floor, past locked doors, traps and secret passages, to the boss.
  Beating the boss opens the exit.
- **Escape** by stepping onto the exit. A run ends when every player has escaped or died.
- **The winner** is the escaped player with the most treasure.
- **XP** comes from monsters (shared by the damage each hero did, plus a bonus for the killing blow),
  picking locks, disarming traps, the first search of each room, new floors and escaping. Each level gives
  another point to spend.
- **Escaped heroes keep everything** they carried out, and their treasure becomes gold for the Waylay Inn's
  shops and smithy.

---

## Quick reference

| Key or click | Does |
| --- | --- |
| **W** | Roll Wend (move) |
| **R** or **Space** | Aim Rumble (attack) |
| Double-click a monster in reach | Aim at it; click again to attack |
| Double-click an empty tile | Roll Wend, then click again to go there |
| Double-click a belt card | Use it, or draw it into a hand |
| Click a die in the tray | Choose how many dice go into the next roll |
| Hold the Luck clover | Choose which rolls ask whether to try your Luck |

| Word | Means |
| --- | --- |
| **Wend** | Movement dice (the boot) |
| **Rumble** | Fighting dice (the fist) |
| **Od Hand** | Dice for skills and tricks: search, locks, traps, steal, aid (the hamsa) |
| **Power Die** | An extra die earned by rolling a 6 |
| **Luck** | A reroll of one die; the higher result stays |
| **Block** | A shield's bonus, added to the first defense that's losing |
| **Keeling Over** | Down at 0 hearts, rolling to get back up |
| **Belt** | Quick-use slots; one belt item a turn is free |
| **Off hand** | Your second hand: shields, tools, a dagger to follow up with |
