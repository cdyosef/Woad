# Terminictionary

Our shared words for the parts of Woad, especially its UI, so we can point at exactly what we mean. It's
also the seed of the instruction manual.

New entries come from a line like `term: <word> - <meaning>`. The meaning is kept in the words it was
given in. Entries are in alphabetical order. A *Notes* line, where there is one, adds where the thing
lives in the game or the code.

---

### Belt fan

The card fan icon beneath the belt square: click it for a full view of all the cards on the belt.

*Notes:* drawn by `BeltSquare`; it opens the belt's full view over the board, every place on the belt
face up (the free ones too).

### Belt square

The equip square that wears the belt: the belt art wraps around it, with the buckle in the middle and
the belt's cards in a straight line on each side of it, staggered left to right, the cards closest to
the buckle on top.

*Notes:* the bottom-right square of the hero panel, `BeltSquare` in `scenes/game/BeltSquare.cs`. Slots
fill from the buckle outward, alternating left and right. The card under the cursor rises in front without
leaving its slot; click picks a card up, double-click uses it from the belt or draws it into a hand (asking
which, when it could be either). With no belt it says Beltless.

### Buckle

The buckle of the belt art on the belt square, which is a button: click it to bring up the buckle's
functions (changing the belt and buckle art, equipping belt and buckle cards and their mods).

*Notes:* for now a click picks up the belt card itself (its text and buttons); the buckle's own menu
comes next. Belt Studs show along the band, Buckle Horns on the buckle.

### Carry tab

A tab on the side of the character sheet that slides out from behind it in its own little window while
you're carrying something, holding the carrying slots, so it doesn't get in the way when you're not.

*Notes:* `CarryTab` in `scenes/game/CarryTab.cs`; it slides out over the board's left edge. The carrying
slots hold two cards or stacks (the Relic of Saint Wyn fills both), and while anything is carried there's
no Rumble and no Od Hand.

### Corner index

The small picture of the item and its count (like "x3") in the corner of a card, as on a playing card,
so that even a card tucked behind others can be told apart.

*Notes:* on the belt square's cards: the left side's in their top-left corner, the right side's in their
top-right, the corners that peek out.

### Equip square

The square slot that the associated cards lay while in on the character sheet.

*Notes:* the main hand (Rumble), off hand (Od Hand) and feet (Wend) squares on the hero panel, drawn
by `HandView` in `scenes/game/HandView.cs`.

### Terminictionary

A dictionary of our repertoire of terminology.

*Notes:* this file, `docs/Terminictionary.md`.
