"""Generates Woad's placeholder art and sound into assets/.

Everything is original and built from this file: pixel sprites drawn as text grids, tiles and UI
frames drawn procedurally, a heading pixel font (BMFont), and 8-bit style sound effects and music
synthesized from waveforms. The output is deterministic, so re-running it only changes files whose source
changed. Real art can replace any file later under the same name.

Usage (from the repo root):  python tools/build-assets.py
Then let Godot import the new files (open the editor, or run Godot with --headless --import).
Uses only the Python standard library.
"""

import math
import random
import struct
import wave
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
TRANSPARENT = (0, 0, 0, 0)


# ---- PNG writing ---------------------------------------------------------------------------

def rgba(color):
    """'#rrggbb' or '#rrggbbaa' to an (r, g, b, a) tuple."""
    color = color.lstrip("#")
    alpha = int(color[6:8], 16) if len(color) == 8 else 255
    return (int(color[0:2], 16), int(color[2:4], 16), int(color[4:6], 16), alpha)


def stored_zlib(data):
    """A zlib stream of uncompressed (stored) blocks. Unlike zlib.compress, the bytes don't depend on
    which zlib build Python uses, so every machine writes identical files."""
    out = bytearray(b"\x78\x01")
    for start in range(0, max(len(data), 1), 65535):
        block = data[start:start + 65535]
        final = start + 65535 >= len(data)
        out += bytes([1 if final else 0]) + struct.pack("<HH", len(block), 0xFFFF ^ len(block)) + block
    out += struct.pack(">I", zlib.adler32(data) & 0xFFFFFFFF)
    return bytes(out)


def write_png(path, pixels):
    """Writes rows of (r, g, b, a) tuples as an RGBA PNG."""
    height, width = len(pixels), len(pixels[0])
    raw = b"".join(b"\x00" + bytes(channel for pixel in row for channel in pixel) for row in pixels)

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", stored_zlib(raw))
           + chunk(b"IEND", b""))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)


def sprite(rows, palette, mirror=False):
    """Pixels from a text grid. '.' is transparent; other characters look up the palette.
    With mirror=True each row is a left half and is completed by its reflection."""
    if mirror:
        rows = [row + row[::-1] for row in rows]
    width = len(rows[0])
    for index, row in enumerate(rows):
        if len(row) != width:
            raise ValueError(f"row {index} is {len(row)} wide, expected {width}: {row!r}")
        for char in row:
            if char != "." and char not in palette:
                raise ValueError(f"row {index} uses '{char}', which has no palette color")
    return [[TRANSPARENT if char == "." else rgba(palette[char]) for char in row] for row in rows]


# ---- Characters and monsters (16x16) --------------------------------------------------------

OUTLINE = "#181420"

HERO_ROWS = [  # left halves, mirrored; T = tunic (drawn in the seat color by the game)
    "........",
    "......oo",
    ".....ohh",
    "....ohhh",
    "....ohss",
    "....oses",
    "....osss",
    "...ooTTT",
    "..osoTTT",
    "..osoTTT",
    "...oobbb",
    "....oTTT",
    "....oTTo",
    "....okko",
    "....oooo",
    "........",
]
HERO_PALETTE = {"o": OUTLINE, "h": "#7a4a2a", "s": "#f0c8a0", "e": OUTLINE, "b": "#5a3a1a", "k": "#3a2414"}

MONSTERS = {
    "slime": (sprite([
        "........", "........", "........", "........", "........", "........",
        "......oo",
        "....oogg",
        "...ogglg",
        "..oggggg",
        "..ogeegg",
        "..oggggg",
        "..oddddd",
        "...ooooo",
        "........", "........",
    ], {"o": "#14281a", "g": "#5fd06a", "l": "#c8f5b0", "d": "#3a9a48", "e": "#101810"}, mirror=True)),

    "bat": (sprite([
        "........", "........", "........", "........", "........",
        "o.....oo",
        "ow...opp",
        "oww.oppe",
        "owwwoppp",
        "owwwwopp",
        ".owwwoop",
        "..owo.oo",
        "...o....",
        "........", "........", "........",
    ], {"o": "#1a1020", "w": "#8a5fc0", "p": "#6a4a9a", "e": "#ff5050"}, mirror=True)),

    "spear_guard": (sprite([
        "............w...",
        "...........www..",
        "......rr....s...",
        ".....ohho...s...",
        "....ohhhho..s...",
        "....ovvvvo..s...",
        "....ohhhho..s...",
        "...oaaaaaaoos...",
        "..oaaaaaaaaosso.",
        "..oaabbbbaao.s..",
        "...oaaaaaao..s..",
        "...oaaoaaao..s..",
        "...oaao.oaao.s..",
        "...okko.okko.s..",
        "...oooo.oooo....",
        "................",
    ], {"o": "#141824", "h": "#8fa3b8", "v": "#20283a", "r": "#c83a3a", "a": "#6f8399",
        "b": "#5a3a1a", "k": "#3a2a20", "w": "#e8eef4", "s": "#8a6a40"})),

    "archer_guard": (sprite([
        "........",
        "......oo",
        ".....ohh",
        "....ohhh",
        "....ohss",
        "....oses",
        "...ohsss",
        "..obbbbb",
        "..ojjjjj",
        "...ojjjj",
        "...oqqqq",
        "...ojjoj",
        "...okko.",
        "...oooo.",
        "........", "........",
    ], {"o": "#1a1410", "h": "#4f6a3a", "s": "#e8c098", "e": "#1a1410", "b": "#9a6a32",
        "j": "#7a8a4a", "q": "#4a3018", "k": "#3a2814"}, mirror=True)),

    "skeleton": (sprite([
        "........",
        "......oo",
        ".....oww",
        "....owww",
        "....owew",
        "....owww",
        ".....owt",
        "......oo",
        "...oowwo",
        "...owowo",
        "...owowo",
        "....owwo",
        "....ow..",
        "....ow..",
        "...oww..",
        "........",
    ], {"o": "#2a2420", "w": "#e8e2d0", "e": "#c03030", "t": "#2a2420"}, mirror=True)),

    "cinder_imp": (sprite([
        "........",
        "........",
        "....o...",
        "....oo..",
        "....ohoo",
        ".....orr",
        "....orrr",
        "....orer",
        "....orrr",
        ".....orr",
        "...ooorr",
        "..oyyoor",
        "...o.orr",
        ".....oro",
        ".....oo.",
        "........",
    ], {"o": "#2a0e08", "r": "#e0502a", "h": "#f0d070", "e": "#ffe060", "y": "#ffb030"}, mirror=True)),

    "fire_beetle": (sprite([
        "........",
        "........",
        "........",
        "........",
        "......oo",
        "..o..ohh",
        "...o.ooo",
        "..oossss",
        ".ossffss",
        ".osfssff",
        "oossssss",
        ".ossffss",
        "..osssss",
        ".o.ooooo",
        "o.o.o.o.",
        "........",
    ], {"o": "#1a0806", "h": "#3a1a14", "s": "#8a2a1a", "f": "#ffa030"}, mirror=True)),

    "bones": (sprite([
        "........", "........", "........", "........", "........", "........",
        "........", "........", "........",
        "......oo",
        ".....owe",
        ".....ooo",
        "...oowwo",
        "..owwwww",
        "...ooooo",
        "........",
    ], {"o": "#2a2420", "w": "#d8d2c0", "e": "#2a2420"}, mirror=True)),

    "iron_warden": (sprite([
        "..o.....",
        "..oo...o",
        "...ooooh",
        "...ohhhh",
        "...ohvvv",
        "...ohrhh",
        ".ooohhhh",
        "oppoaaaa",
        "oppoaaaa",
        ".oooaccc",
        "..oaaaaa",
        "..obbbbb",
        "..oaaoaa",
        "..oaao..",
        ".okkko..",
        ".ooooo..",
    ], {"o": "#120e14", "h": "#5a5f6a", "v": "#1a1a20", "r": "#ff4040", "p": "#9aa0aa",
        "a": "#4a4f5a", "c": "#a83a46", "b": "#3a2a20", "k": "#2a2a30"}, mirror=True)),

    # The vault's guardian: a gilded knight in crimson, a key emblem on its chest.
    "key_warden": (sprite([
        "........",
        ".....ooo",
        "....oggg",
        "....ogvv",
        "....ogvr",
        "....oggg",
        "..oooaaa",
        ".oppoccc",
        ".oppoccy",
        "..ooocyy",
        "...oaccc",
        "...obbbb",
        "...oaaoa",
        "...oaao.",
        "..okkko.",
        "..ooooo.",
    ], {"o": "#120e14", "g": "#d8b13f", "v": "#1a1a20", "r": "#ffcf40", "a": "#5a4f6a", "p": "#b0a6c0",
        "c": "#7a2a3a", "y": "#f0c94a", "b": "#3a2a20", "k": "#2a2a30"}, mirror=True)),
}

# ---- Objects (16x16) ------------------------------------------------------------------------

WOOD = {"o": "#2a1a10", "w": "#b07840", "d": "#8a5a2b", "g": "#e2c47a", "y": "#3a2a10", "i": "#1a0f08"}

OBJECTS = {
    "chest": sprite([
        "................", "................", "................",
        "..oooooooooooo..",
        ".owwwwwwwwwwwwo.",
        ".owddddddddddwo.",
        ".owddddddddddwo.",
        ".oggggggggggggo.",
        ".oggggoyyoggggo.",
        ".owdddoyyodddwo.",
        ".owddddddddddwo.",
        ".owddddddddddwo.",
        ".oggggggggggggo.",
        ".oooooooooooooo.",
        "................", "................",
    ], WOOD),
    "chest_open": sprite([
        "................",
        "..oooooooooooo..",
        ".owwwwwwwwwwwwo.",
        ".owddddddddddwo.",
        ".oggggggggggggo.",
        ".oooooooooooooo.",
        ".oiiiiiiiiiiiio.",
        ".oggggggggggggo.",
        ".owddddddddddwo.",
        ".owddddddddddwo.",
        ".owddddddddddwo.",
        ".owddddddddddwo.",
        ".oggggggggggggo.",
        ".oooooooooooooo.",
        "................", "................",
    ], WOOD),
    "pot": sprite([
        "................", "................",
        "......oooo......",
        ".....orrrro.....",
        "......occo......",
        ".....occcco.....",
        "....occllcco....",
        "...occlccccco...",
        "...ocllcccccco..",
        "...occccccccco..",
        "...occcccccdco..",
        "....occccccdo...",
        ".....odddddo....",
        "......ooooo.....",
        "................", "................",
    ], {"o": "#2a1408", "r": "#c87a4a", "c": "#a8643c", "l": "#e09a6a", "d": "#7a4528"}),
}

# Bombs: unlit, and lit with the spark at the end of its long fuse in two flickering frames.
BOMB_ROWS = [
    "................",
    "..........ff....",
    ".........f..f...",
    "........f.......",
    "......oof.......",
    ".....oxxoo......",
    "....oooooooo....",
    "...obbbbbbbbo...",
    "..obllbbbbbbbo..",
    "..oblbbbbbbbbo..",
    "..obbbbbbbbbbo..",
    "..obbbbbbbbbbo..",
    "...obbbbbbbbo...",
    "....obbbbbbo....",
    ".....oooooo.....",
    "................",
]
BOMB_PALETTE = {"o": "#0e0c14", "b": "#2c2a3a", "l": "#6a6888", "x": "#8a7a5a", "f": "#c8a060"}


def lit_bomb(sparks):
    rows = [list(row) for row in BOMB_ROWS]
    for x, y, c in sparks:
        rows[y][x] = c
    return sprite(["".join(row) for row in rows], {**BOMB_PALETTE, "s": "#ffd84a", "r": "#ff6a1f"})


OBJECTS["bomb"] = sprite(BOMB_ROWS, BOMB_PALETTE)
OBJECTS["bomb_lit"] = lit_bomb([(12, 2, "s"), (13, 1, "r"), (11, 1, "r"), (13, 3, "r"), (12, 1, "s")])
OBJECTS["bomb_lit2"] = lit_bomb([(12, 2, "r"), (13, 2, "s"), (11, 3, "s"), (12, 0, "r")])

# A pressure plate with a bomb mark: a found bomb trap.
OBJECTS["bomb_trap"] = sprite([
    "................",
    "................",
    "..pppppppppppp..",
    ".pqqqqqqqqqqqqp.",
    ".pqiqqqqqqqqiqp.",
    ".pqqqqqoffqqqqp.",
    ".pqqqqoooqqqqqp.",
    ".pqqqoooooqqqqp.",
    ".pqqqoooooqqqqp.",
    ".pqqqoooooqqqqp.",
    ".pqqqqoooqqqqqp.",
    ".pqiqqqqqqqqiqp.",
    ".pqqqqqqqqqqqqp.",
    "..pppppppppppp..",
    "................",
    "................",
], {"p": "#2a2018", "q": "#6a5a44", "i": "#a89070", "o": "#1a1820", "f": "#d0503a"})

HEART_ROWS = [".oo.oo.", "orrorro", "orlrrro", "orrrrro", ".orrro.", "..oro..", "...o..."]

# A chest bound in iron with a gold lock plate: it holds better loot, and takes a key or a picked lock.
OBJECTS["chest_locked"] = sprite([
    "................", "................", "................",
    "..oooooooooooo..",
    ".owwwwwwwwwwwwo.",
    ".owddddddddddwo.",
    ".owddddddddddwo.",
    ".oggggggggggggo.",
    ".oggggoyyoggggo.",
    ".owdddoyyodddwo.",
    ".owddddddddddwo.",
    ".owddddddddddwo.",
    ".oggggggggggggo.",
    ".oooooooooooooo.",
    "................", "................",
], {**WOOD, "g": "#9aa4ad", "y": "#f0c94a"})

# The Boss Key's chest in the vault: crimson bands and a gold lock.
OBJECTS["chest_boss"] = sprite([
    "................", "................", "................",
    "..oooooooooooo..",
    ".owwwwwwwwwwwwo.",
    ".owddddddddddwo.",
    ".owddddddddddwo.",
    ".oggggggggggggo.",
    ".oggggoyyoggggo.",
    ".owdddoyyodddwo.",
    ".owddddddddddwo.",
    ".owddddddddddwo.",
    ".oggggggggggggo.",
    ".oooooooooooooo.",
    "................", "................",
], {**WOOD, "g": "#c8343e", "y": "#f0c94a", "w": "#c89040"})

# ---- Stat and key icons (12x12) -------------------------------------------------------------

ICONS = {
    "icon_rumble": sprite([  # a fist
        "............",
        ".oo.oo.oo...",
        "ossossossoo.",
        "osssssssssso",
        "oslslslsssso",
        "osssssssssso",
        "oooooooossso",
        ".ossssssssso",
        ".osssssssso.",
        "..osssssso..",
        "..oooooooo..",
        "............",
    ], {"o": OUTLINE, "s": "#f0c8a0", "l": "#fff0d8"}),
    "icon_wend": sprite([  # a boot
        "............",
        "...oooooo...",
        "...ogggggo..",
        "...obbbbbo..",
        "...obbbbbo..",
        "...obbbbbo..",
        "...obbbbboo.",
        "...obbbbbbbo",
        "..obbbbbbbbo",
        "..oddddddddo",
        "..oooooooooo",
        "............",
    ], {"o": OUTLINE, "g": "#e2c47a", "b": "#8a5a2b", "d": "#3a2414"}),
    "icon_odhand": sprite([  # a hamsa
        "...o.oo.o...",
        "..obobbobo..",
        "..obobbobo..",
        "o.obbbbbbo.o",
        "obobbbbbbobo",
        "obbbbwwbbbbo",
        ".obbwkkwbbo.",
        ".obbbwwbbbo.",
        "..obbbbbbo..",
        "...obbbbo...",
        "....oooo....",
        "............",
    ], {"o": OUTLINE, "b": "#5d8de0", "w": "#e8e0c8", "k": "#15121d"}),
    "icon_power": sprite([  # the Violet crest's four-pointed star, for Power Dice
        ".....o......",
        "....olo.....",
        "....olo.....",
        "...oslso....",
        ".oosslssoo..",
        "olllllwlllo.",
        ".oosslssoo..",
        "...oslso....",
        "....olo.....",
        "....olo.....",
        ".....o......",
        "............",
    ], {"o": OUTLINE, "s": "#9a5fd8", "l": "#c9a6f0", "w": "#f4ecff"}),
}

KEY_ROWS = [
    "............",
    "............",
    ".ooo........",
    "oyyyo.......",
    "oy.yoooooooo",
    "oyyyyyyyyyyo",
    "oy.yooooyoyo",
    "oyyyo...o.o.",
    ".ooo........",
    "............",
    "............",
    "............",
]
ICONS["icon_key"] = sprite(KEY_ROWS, {"o": "#5a4424", "y": "#f0c94a"})
ICONS["icon_boss_key"] = sprite(KEY_ROWS, {"o": "#e2c47a", "y": "#d8434a"})


# ---- Procedural tiles (16x16) ---------------------------------------------------------------

def blank(width=16, height=16, color=TRANSPARENT):
    return [[color for _ in range(width)] for _ in range(height)]


def floor_tile(seed, light, mid, dark, speck):
    """A beveled flagstone: lit top-left edges, grout on the bottom-right, a few worn specks."""
    tile = blank(color=rgba(mid))
    for i in range(16):
        tile[0][i] = rgba(light)
        tile[i][0] = rgba(light)
        tile[15][i] = rgba(dark)
        tile[i][15] = rgba(dark)
    generator = random.Random(seed)
    for _ in range(7):
        x, y = generator.randrange(2, 14), generator.randrange(2, 14)
        tile[y][x] = rgba(speck)
    return tile


def wall_tile():
    """A stone block seen from above and in front: lit top face, darker brick face below."""
    top, top_edge, face, mortar, outline = rgba("#7a9a8a"), rgba("#a8c4b4"), rgba("#3d5a4c"), rgba("#24382f"), rgba("#141c18")
    tile = blank(color=face)
    for y in range(6):
        for x in range(16):
            tile[y][x] = top_edge if y == 0 or x in (0, 15) else top
    for x in range(16):
        tile[6][x] = mortar
        tile[11][x] = mortar
    for y in range(7, 11):
        tile[y][7] = mortar
    for y in range(12, 16):
        tile[y][3] = mortar
        tile[y][11] = mortar
    for i in range(16):
        tile[15][i] = outline
        tile[i][0] = outline if i > 5 else tile[i][0]
        tile[i][15] = outline if i > 5 else tile[i][15]
    return tile


def cracked_wall_tile():
    """A wall block split by dark cracks: a bomb's blast breaks it."""
    tile = wall_tile()
    crack = rgba("#0b100e")
    for x, y in [(4, 2), (5, 3), (5, 4), (6, 5), (6, 6), (7, 7), (7, 8), (8, 9), (8, 10), (9, 11), (9, 12), (10, 13),
                 (8, 8), (9, 8), (10, 7), (6, 10), (5, 11)]:
        tile[y][x] = crack
    return tile


def spikes_tile():
    """Steel spikes poking up through the floor (drawn over it): a trap someone found."""
    return sprite([
        "................",
        "..o....o....o...",
        ".oso..oso..oso..",
        ".oso..oso..oso..",
        "olssoolssoolsso.",
        "olssoolssoolsso.",
        "ooooooooooooooo.",
        "................",
        "....o....o......",
        "...oso..oso.....",
        "...oso..oso.....",
        "..olsso.olsso...",
        "..olsso.olsso...",
        "..ooooo.ooooo...",
        "................",
        "................",
    ], {"o": OUTLINE, "s": "#9aa4ad", "l": "#e8eef2"})


def trapdoor_tile():
    """A wooden hatch in the floor (drawn over it): a trap door someone found."""
    tile = blank()
    outline, wood, seam, iron = rgba("#2a1a10"), rgba("#8a5a2b"), rgba("#5a3a1a"), rgba("#9aa4ad")
    for y in range(2, 14):
        for x in range(2, 14):
            edge = y in (2, 13) or x in (2, 13)
            tile[y][x] = outline if edge else (seam if x in (5, 8, 11) else wood)
    for y in (4, 11):
        for x in range(3, 7):
            tile[y][x] = iron
    tile[8][10] = tile[8][11] = iron
    return tile


def pit_tile():
    """A hole: a crumbling rim fading into black."""
    rim = [rgba("#2a3a33"), rgba("#1a2420"), rgba("#0e1412")]
    tile = blank(color=rgba("#05070a"))
    for depth, color in enumerate(rim):
        for i in range(depth, 16 - depth):
            tile[depth][i] = color
            tile[i][depth] = color
            tile[i][15 - depth] = color
    return tile


def stairs_tile():
    """Steps leading down: lit treads that shrink toward the dark bottom."""
    tile = blank(color=rgba("#141c18"))
    tread, riser = rgba("#8aa898"), rgba("#4d6158")
    for step in range(5):
        y = 1 + step * 3
        inset = 1 + step
        for x in range(inset, 16 - inset):
            tile[y][x] = tread
            tile[y + 1][x] = riser
    return tile


def rubble_tile():
    """Broken stones scattered over the floor (drawn on top of it): rough ground, 2 steps a tile."""
    tile = blank()
    stone, light, dark = rgba("#8a8f86"), rgba("#b8bcb0"), rgba("#3a4038")
    for left, top, width, height in [(1, 2, 4, 3), (7, 1, 3, 3), (11, 3, 4, 3), (3, 7, 3, 3),
                                     (8, 6, 4, 4), (1, 11, 3, 3), (6, 12, 4, 3), (12, 10, 3, 4)]:
        for y in range(top, top + height):
            for x in range(left, left + width):
                edge = y == top + height - 1 or x == left + width - 1
                tile[y][x] = dark if edge else light if y == top else stone
    return tile


def water_tile(deep):
    """Water, rippled with light crests: shallow shows a little floor through; deep is dark and opaque."""
    base = rgba("#1d3a66" if deep else "#4a8ab8aa")
    crest = rgba("#5f8fd0" if deep else "#c8e6f8d2")
    tile = blank(color=base)
    for row, offset in [(3, 1), (8, 5), (13, 2)]:
        for x in range(16):
            if (x + offset) % 8 in (0, 1, 2):
                tile[row][x] = crest
    return tile


def gate_tile():
    """An iron gate (drawn over the floor): dark bars and crossbars with a gold lock plate."""
    tile = blank()
    iron, shine, gold = rgba("#2a2a32"), rgba("#6a6a78"), rgba("#f0c94a")
    for x in (2, 5, 8, 11, 14):
        for y in range(1, 15):
            tile[y][x] = shine if y < 3 else iron
            if x + 1 < 16:
                tile[y][x - 1] = iron
    for y in (3, 11):
        for x in range(1, 16):
            tile[y][x] = iron
    for y in range(6, 10):
        for x in range(6, 10):
            tile[y][x] = gold if (x, y) != (7, 8) else iron
    return tile


def stairs_up_tile():
    """Steps leading up: treads that widen toward a lit landing at the top."""
    tile = blank(color=rgba("#2a3a33"))
    tread, riser = rgba("#c8dccf"), rgba("#6a8a7c")
    for step in range(5):
        y = 1 + step * 3
        inset = 5 - step
        for x in range(inset, 16 - inset):
            tile[y][x] = tread
            tile[y + 1][x] = riser
    return tile


def exit_tile(sealed):
    """A stone archway: lit from beyond when open, dark and barred while sealed."""
    stone, stone_dark = rgba("#8a8f9a"), rgba("#4a4f5a")
    light, glow = rgba("#f8e6a0"), rgba("#e2c47a")
    tile = blank()
    for y in range(1, 16):
        for x in range(2, 14):
            inside = 4 <= x <= 11 and y >= 4 and not (y == 4 and x in (4, 11))
            if inside:
                tile[y][x] = rgba("#05070a") if sealed else (light if 6 <= x <= 9 else glow)
            else:
                tile[y][x] = stone if y < 3 or x in (2, 3, 12, 13) else stone_dark
    if sealed:
        for x in (5, 7, 9, 11):
            for y in range(4, 16):
                tile[y][x] = rgba("#6a5228")
    return tile


# ---- UI frame (12x12, drawn as a nine-patch with 4px margins) --------------------------------

def frame(edge, inner_edge, fill):
    """A gothic panel border: dark outline, gold trim, darker inner trim, riveted corners."""
    size = 12
    tile = blank(size, size, rgba(fill))
    outline = rgba("#07060c")
    for i in range(size):
        for layer, color in ((0, outline), (1, rgba(edge)), (2, rgba(inner_edge))):
            tile[layer][i] = color
            tile[size - 1 - layer][i] = color
            tile[i][layer] = color
            tile[i][size - 1 - layer] = color
    for cx, cy in ((1, 1), (10, 1), (1, 10), (10, 10)):
        tile[cy][cx] = rgba("#fff0c0")
    return tile


# ---- Heading font (BMFont) ------------------------------------------------------------------

GLYPHS = {
    "A": ["...#...", "..###..", "..#.##.", ".##.##.", ".##..##", ".######", "##...##", "##...##", "###.###"],
    "B": ["######.", ".##..##", ".##..##", ".#####.", ".##..##", ".##..##", ".##..##", ".##..##", "######."],
    "C": ["..####.", ".##..##", "##....#", "##.....", "##.....", "##.....", "##....#", ".##..##", "..####."],
    "D": ["#####..", ".##.##.", ".##..##", ".##..##", ".##..##", ".##..##", ".##..##", ".##.##.", "#####.."],
    "E": ["#######", ".##...#", ".##....", ".##.#..", ".####..", ".##.#..", ".##....", ".##...#", "#######"],
    "F": ["#######", ".##...#", ".##....", ".##.#..", ".####..", ".##.#..", ".##....", ".##....", "####..."],
    "G": ["..####.", ".##..##", "##....#", "##.....", "##.####", "##...##", "##...##", ".##..##", "..#####"],
    "H": ["###.###", ".##..##", ".##..##", ".##..##", ".######", ".##..##", ".##..##", ".##..##", "###.###"],
    "I": ["####", ".##.", ".##.", ".##.", ".##.", ".##.", ".##.", ".##.", "####"],
    "J": ["..####", "...##.", "...##.", "...##.", "...##.", "...##.", "##.##.", "##.##.", ".###.."],
    "K": ["###.###", ".##..#.", ".##.#..", ".####..", ".###...", ".####..", ".##.##.", ".##..##", "###.###"],
    "L": ["####...", ".##....", ".##....", ".##....", ".##....", ".##....", ".##...#", ".##..##", "#######"],
    "M": ["##...##", "###.###", "#######", "##.#.##", "##.#.##", "##...##", "##...##", "##...##", "###.###"],
    "N": ["##...##", "###..##", "####.##", "##.####", "##..###", "##...##", "##...##", "##...##", "###..##"],
    "O": ["..###..", ".##.##.", "##...##", "##...##", "##...##", "##...##", "##...##", ".##.##.", "..###.."],
    "P": ["######.", ".##..##", ".##..##", ".##..##", ".#####.", ".##....", ".##....", ".##....", "####..."],
    "Q": ["..###..", ".##.##.", "##...##", "##...##", "##...##", "##.#.##", "##..##.", ".##.##.", "..##.##"],
    "R": ["######.", ".##..##", ".##..##", ".##..##", ".#####.", ".##.##.", ".##..##", ".##..##", "###.###"],
    "S": [".#####.", "##...##", "##.....", ".##....", "..###..", "....##.", ".....##", "##...##", ".#####."],
    "T": ["#######", "#.###.#", "..###..", "..###..", "..###..", "..###..", "..###..", "..###..", ".#####."],
    "U": ["###.###", ".##..##", ".##..##", ".##..##", ".##..##", ".##..##", ".##..##", ".##..##", "..####."],
    "V": ["###.###", ".##..##", ".##..##", ".##..##", ".##..##", "..#.##.", "..####.", "...##..", "...#..."],
    "W": ["##...##", "##...##", "##...##", "##.#.##", "##.#.##", "##.#.##", "#######", ".##.##.", ".#...#."],
    "X": ["###.###", ".##..#.", "..##.#.", "...##..", "...##..", "..#.##.", ".#..##.", ".#...##", "###.###"],
    "Y": ["###.###", ".##..#.", ".##..#.", "..##.#.", "...##..", "...##..", "...##..", "...##..", "..####."],
    "Z": ["#######", "#...##.", "...##..", "...##..", "..##...", "..##...", ".##....", ".##...#", "#######"],
    "0": [".####.", "##..##", "##..##", "##.###", "###.##", "##..##", "##..##", "##..##", ".####."],
    "1": ["..##..", ".###..", "..##..", "..##..", "..##..", "..##..", "..##..", "..##..", ".####."],
    "2": [".####.", "##..##", "....##", "...##.", "..##..", ".##...", "##....", "##..##", "######"],
    "3": [".####.", "##..##", "....##", "....##", "..###.", "....##", "....##", "##..##", ".####."],
    "4": ["...##.", "..###.", ".####.", "##.##.", "##.##.", "######", "...##.", "...##.", "..####"],
    "5": ["######", "##....", "##....", "#####.", "....##", "....##", "....##", "##..##", ".####."],
    "6": ["..###.", ".##...", "##....", "#####.", "##..##", "##..##", "##..##", "##..##", ".####."],
    "7": ["######", "##..##", "....##", "...##.", "...##.", "..##..", "..##..", "..##..", "..##.."],
    "8": [".####.", "##..##", "##..##", ".####.", "##..##", "##..##", "##..##", "##..##", ".####."],
    "9": [".####.", "##..##", "##..##", "##..##", ".#####", "....##", "....##", "...##.", ".###.."],
    " ": ["...", "...", "...", "...", "...", "...", "...", "...", "..."],
    "!": ["##", "##", "##", "##", "##", "##", "..", "##", "##"],
    "'": ["##", "##", ".#", "..", "..", "..", "..", "..", ".."],
    "&": [".###...", "##.##..", "##.##..", ".###...", ".###.##", "##.####", "##..##.", "##..###", ".###.##"],
    ",": ["..", "..", "..", "..", "..", "..", "..", "##", "#."],
    "-": [".....", ".....", ".....", ".....", "#####", ".....", ".....", ".....", "....."],
    ".": ["..", "..", "..", "..", "..", "..", "..", "##", "##"],
    ":": ["..", "..", "##", "##", "..", "..", "##", "##", ".."],
    "?": [".####.", "##..##", "....##", "...##.", "..##..", "..##..", "......", "..##..", "..##.."],
    "/": ["....#", "...##", "...#.", "..##.", "..#..", ".##..", ".#...", "##...", "#...."],
}


def build_font(directory, name):
    """Packs the glyphs into one texture and writes a BMFont text descriptor Godot can import."""
    cell_width, cell_height, per_row = 8, 10, 16
    chars = list(GLYPHS.items())
    rows = math.ceil(len(chars) / per_row)
    atlas = blank(cell_width * per_row, cell_height * rows)
    ink = rgba("#ffffff")
    lines = []
    for index, (char, glyph) in enumerate(chars):
        width = len(glyph[0])
        if any(len(row) != width for row in glyph) or len(glyph) != 9:
            raise ValueError(f"glyph {char!r} must be 9 rows of equal width")
        x0, y0 = (index % per_row) * cell_width, (index // per_row) * cell_height
        for y, row in enumerate(glyph):
            for x, pixel in enumerate(row):
                if pixel == "#":
                    atlas[y0 + y][x0 + x] = ink
        lines.append(f"char id={ord(char)} x={x0} y={y0} width={width} height=9 xoffset=0 yoffset=0 "
                     f"xadvance={width + 1} page=0 chnl=15")
    write_png(directory / f"{name}.png", atlas)
    header = [
        f'info face="Woad Gothic" size=9 bold=0 italic=0 charset="" unicode=1 stretchH=100 smooth=0 aa=1 padding=0,0,0,0 spacing=1,1',
        f"common lineHeight=12 base=9 scaleW={len(atlas[0])} scaleH={len(atlas)} pages=1 packed=0",
        f'page id=0 file="{name}.png"',
        f"chars count={len(chars)}",
    ]
    (directory / f"{name}.fnt").write_text("\n".join(header + lines) + "\n", encoding="ascii", newline="\n")


# ---- Sound effects --------------------------------------------------------------------------

RATE = 22050


def square(frequency, t):
    return 1.0 if math.sin(2 * math.pi * frequency * t) >= 0 else -1.0


def tone(frequency_at, seconds, wave_fn=square, volume=0.35, attack=0.005, release=0.06):
    """A note whose frequency can glide: frequency_at(t) gives Hz at time t."""
    samples = []
    phase = 0.0
    for i in range(int(RATE * seconds)):
        t = i / RATE
        phase += frequency_at(t) / RATE
        envelope = min(1.0, t / attack) * min(1.0, (seconds - t) / release)
        value = wave_fn(phase) if wave_fn is not square else (1.0 if math.sin(2 * math.pi * phase) >= 0 else -1.0)
        samples.append(value * envelope * volume)
    return samples


def sine(phase):
    return math.sin(2 * math.pi * phase)


def noise(seconds, volume=0.4, decay=20.0, seed=1, lowpass=0.5):
    generator = random.Random(seed)
    samples, previous = [], 0.0
    for i in range(int(RATE * seconds)):
        previous = previous * lowpass + generator.uniform(-1, 1) * (1 - lowpass)
        samples.append(previous * volume * math.exp(-decay * i / RATE))
    return samples


def notes(sequence, wave_fn=square, volume=0.3):
    """Back-to-back notes: (frequency, seconds) pairs."""
    samples = []
    for frequency, seconds in sequence:
        samples += tone(lambda t, f=frequency: f, seconds, wave_fn, volume, release=min(0.05, seconds / 2))
    return samples


def mix(*tracks):
    length = max(len(track) for track in tracks)
    return [sum(track[i] for track in tracks if i < len(track)) for i in range(length)]


def write_wav(path, samples):
    path.parent.mkdir(parents=True, exist_ok=True)
    with wave.open(str(path), "wb") as file:
        file.setnchannels(1)
        file.setsampwidth(2)
        file.setframerate(RATE)
        file.writeframes(b"".join(struct.pack("<h", int(max(-1.0, min(1.0, s)) * 32000)) for s in samples))


def build_sounds(directory):
    clack = lambda seed: noise(0.035, 0.55, decay=90, seed=seed, lowpass=0.2)
    dice = []
    for index, gap in enumerate([0.0, 0.06, 0.05, 0.08, 0.07]):
        dice += [0.0] * int(RATE * gap) + clack(index + 3)

    sounds = {
        "click": tone(lambda t: 1400, 0.03, volume=0.2, release=0.02),
        "dice": dice,
        "step": noise(0.05, 0.35, decay=60, seed=7, lowpass=0.85),
        "hit": mix(noise(0.12, 0.5, decay=30, seed=11, lowpass=0.4), tone(lambda t: 220 - 900 * t, 0.12, volume=0.25)),
        "block": mix(tone(lambda t: 1500, 0.16, sine, 0.3, release=0.15), tone(lambda t: 2300, 0.12, sine, 0.15, release=0.1)),
        "defeat": notes([(392, 0.06), (311, 0.06), (233, 0.12)], volume=0.25),
        "heal": notes([(523, 0.07), (659, 0.07), (784, 0.07), (1047, 0.14)], sine, 0.3),
        "coin": notes([(988, 0.06), (1319, 0.16)], volume=0.25),
        "treasure": notes([(523, 0.09), (587, 0.09), (659, 0.09), (784, 0.3)], volume=0.25),
        "door": noise(0.22, 0.4, decay=10, seed=19, lowpass=0.92),
        "stairs": notes([(659, 0.08), (523, 0.08), (440, 0.08), (330, 0.18)], volume=0.22),
        "pot": mix(noise(0.16, 0.6, decay=25, seed=23, lowpass=0.3), noise(0.08, 0.3, decay=60, seed=29, lowpass=0.1)),
        "turn": mix(tone(lambda t: 880, 0.25, sine, 0.22, release=0.22), tone(lambda t: 1320, 0.2, sine, 0.1, release=0.18)),
        "monsters": tone(lambda t: 110 + 30 * math.sin(t * 40), 0.3, volume=0.18, release=0.2),
        "downed": tone(lambda t: 330 - 500 * t, 0.45, volume=0.25, release=0.2),
        "boom": mix(noise(0.7, 0.75, decay=5, seed=41, lowpass=0.12), tone(lambda t: 85 - 50 * t, 0.55, volume=0.3, release=0.35)),
        "fuse": noise(0.25, 0.18, decay=8, seed=43, lowpass=0.9),
        "victory": notes([(523, 0.1), (659, 0.1), (784, 0.1), (1047, 0.12), (784, 0.08), (1047, 0.35)], volume=0.25),
    }
    for name, samples in sounds.items():
        write_wav(directory / f"{name}.wav", samples)


# ---- Music --------------------------------------------------------------------------------
# Songs are written as short scores: "A4/1.5" is A4 for 1.5 beats, "-/1" is a beat of rest, and
# "|" marks bar lines (ignored, for reading). Looping songs are a whole number of bars long and
# every note ends inside them, so the end joins the start without a click.

NOTE_STEPS = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def midi(name):
    """'A4' -> 69, 'C#5' -> 73, 'Bb3' -> 58."""
    accidental = {"#": 1, "b": -1}.get(name[1:-1], 0)
    return NOTE_STEPS[name[0]] + accidental + 12 * (int(name[-1]) + 1)


def hz(note):
    return 440.0 * 2 ** ((note - 69) / 12)


def pulse(duty):
    return lambda phase: 1.0 if phase % 1.0 < duty else -1.0


def triangle(phase):
    return 4.0 * abs(phase % 1.0 - 0.5) - 1.0


def parse_score(score, transpose=0):
    """Score text to (midi note or None, beats) pairs."""
    events = []
    for token in score.replace("|", " ").split():
        name, beats = token.split("/")
        events.append((None if name == "-" else midi(name) + transpose, float(beats)))
    return events


def chord_tones(chord):
    """'Am' -> [A3, C4, E4] as midi notes; 'F' -> [F3, A3, C4]."""
    root = midi(chord.rstrip("m") + "3")
    return [root, root + (3 if chord.endswith("m") else 4), root + 7]


class Song:
    """A buffer that voices are mixed into, sized in beats at a tempo."""

    def __init__(self, bpm, beats, tail_seconds=0.0):
        self.beat = 60.0 / bpm
        self.samples = [0.0] * (round(beats * self.beat * RATE) + int(tail_seconds * RATE))

    def place(self, start_beat, samples):
        start = round(start_beat * self.beat * RATE)
        for i, value in enumerate(samples[:len(self.samples) - start]):
            self.samples[start + i] += value

    def note(self, start_beat, note, beats, wave_fn, volume, gate=0.9, decay=0.0, vibrato=False):
        """One note, held for `gate` of its length, with an optional fade (decay) and late vibrato."""
        seconds = beats * self.beat * gate
        frequency = hz(note)
        release = min(0.04, seconds / 3)
        samples, phase = [], 0.0
        for i in range(int(seconds * RATE)):
            t = i / RATE
            wobble = 1 + 0.005 * math.sin(2 * math.pi * 5.5 * t) if vibrato and t > 0.25 else 1
            phase += frequency * wobble / RATE
            envelope = min(1.0, t / 0.005) * min(1.0, (seconds - t) / release) * math.exp(-decay * t)
            samples.append(wave_fn(phase) * envelope * volume)
        self.place(start_beat, samples)

    def voice(self, score, wave_fn, volume, transpose=0, **style):
        beat = 0.0
        for note, beats in parse_score(score, transpose):
            if note is not None:
                self.note(beat, note, beats, wave_fn, volume, **style)
            beat += beats

    def arpeggio(self, chords, pattern, wave_fn, volume, octave=1, step=0.5):
        """Broken chords, one chord per bar: `pattern` indexes the chord's tones, one per step."""
        beat = 0.0
        for chord in chords:
            tones = [tone + 12 * octave for tone in chord_tones(chord)]
            for index in pattern:
                self.note(beat, tones[index % 3] + 12 * (index // 3), step, wave_fn, volume, gate=0.7, decay=6)
                beat += step

    def bass(self, chords, rhythm, volume, octave=-1, start_bar=0):
        """One chord per bar. `rhythm` is (offset from the root in semitones, beats) pairs."""
        beat = start_bar * 4.0
        for chord in chords:
            root = chord_tones(chord)[0] + 12 * octave
            for offset, beats in rhythm:
                self.note(beat, root + offset, beats, triangle, volume, gate=0.85)
                beat += beats

    def drums(self, bars, pattern, volume):
        """The same bar of drums, `bars` times over."""
        for bar in range(bars):
            self.drum_bar(bar, pattern, volume)

    def drum_bar(self, bar, pattern, volume):
        """`pattern` is one bar of sixteenths: k kick, s snare, h hi-hat, c crash cymbal, . rest."""
        sounds = {
            "k": tone(lambda t: 150 - 1000 * t, 0.09, sine, volume * 1.6, release=0.05),
            "s": noise(0.12, volume, decay=28, seed=31, lowpass=0.35),
            "h": noise(0.03, volume * 0.45, decay=120, seed=37, lowpass=0.05),
            "c": noise(0.9, volume * 0.8, decay=5, seed=41, lowpass=0.08),
        }
        for step, hit in enumerate(pattern):
            if hit in sounds:
                self.place(bar * 4 + step / 4, sounds[hit])


TITLE_BEAT = [  # one pattern per bar: a lone kick grows into a full beat, a fill, then the climax
    *["k..............."] * 4,
    *["k...h...k...h..."] * 4,
    *["k.h.s.h.k.hks.h."] * 3,
    "k...s...k.s.ssss",
    "c.hhs.hhk.hks.hh",
    "k.hhs.hhk.hks.hh",
    "c.hhs.hhk.hks.hh",
    "k.hhs.hhc.......",
]


def title_theme():
    """The menus: a slow, wistful D minor over a beat that swells to a climax, then starts again calm."""
    chords = ["Dm", "Bb", "F", "C", "Dm", "Bb", "Gm", "A"] * 2
    song = Song(bpm=84, beats=64)
    for bar, pattern in enumerate(TITLE_BEAT):
        song.drum_bar(bar, pattern, 0.025 + 0.135 * (bar / 15) ** 1.3)
    # The climax (last four bars): the melody doubled an octave up and a busier bass.
    song.voice("-/48 F5/1 E5/1 D5/1 A4/1 | Bb4/1 D5/1 F5/1 D5/1 | Bb4/1.5 A4/0.5 G4/2 | A4/3 -/1",
               pulse(0.25), 0.06, transpose=12, vibrato=True)
    song.bass(chords[12:], [(0, 0.5), (12, 0.5)] * 4, 0.1, start_bar=12)
    song.voice("A4/2 F4/1 D4/1 | D5/1.5 C5/0.5 Bb4/1 A4/1 | A4/2 C5/1 F5/1 | E5/3 -/1 |"
               "F5/1.5 E5/0.5 D5/1 A4/1 | Bb4/1.5 C5/0.5 D5/2 | G4/1 Bb4/1 D5/1 C5/1 | C#5/3 -/1 |"
               "D5/2 F5/1 A5/1 | G5/1.5 F5/0.5 D5/2 | C5/1 F5/1 A5/1 G5/1 | E5/2 G5/1 E5/1 |"
               "F5/1 E5/1 D5/1 A4/1 | Bb4/1 D5/1 F5/1 D5/1 | Bb4/1.5 A4/0.5 G4/2 | A4/3 -/1",
               pulse(0.5), 0.11, vibrato=True)
    song.arpeggio(chords, [0, 1, 2, 3, 2, 1, 0, 1], pulse(0.25), 0.05)
    song.bass(chords, [(0, 2), (7, 2)], 0.2)
    return song.samples


def dungeon_theme():
    """Exploring: a steady A minor walk with a quiet beat."""
    chords = ["Am", "Am", "F", "G", "Am", "Am", "Dm", "E"] * 2
    song = Song(bpm=104, beats=64)
    song.voice("E5/1.5 D5/0.5 C5/1 B4/1 | A4/3 -/1 | A4/1 C5/1 F5/1.5 E5/0.5 | D5/2 B4/2 |"
               "C5/1.5 B4/0.5 A4/1 E5/1 | E5/3 -/1 | F5/1 E5/1 D5/1 C5/1 | B4/2 G#4/2 |"
               "A5/1.5 G5/0.5 E5/1 C5/1 | D5/1 E5/1 C5/2 | F5/1.5 E5/0.5 D5/1 C5/1 | B4/1 D5/1 G5/2 |"
               "E5/1 A5/1 G5/1 E5/1 | C5/3 -/1 | D5/1 F5/1 A5/1 F5/1 | E5/2 G#4/1 B4/1",
               pulse(0.5), 0.09, vibrato=True)
    song.arpeggio(chords, [0, 2, 1, 2] * 2, pulse(0.125), 0.04)
    song.bass(chords, [(0, 0.5), (0, 0.5), (12, 0.5), (0, 0.5)] * 2, 0.2)
    song.drums(16, "k...h...k.k.h...", 0.12)
    return song.samples


BOSS_BEAT = [  # double kicks under a hard backbeat, a crash every four bars, fills into each new section
    "c.kks.h.k.kks.hh", "k.kks.h.k.kks.hh", "k.kks.h.k.kks.hh", "k.kks.h.k.kks.hh",
    "c.kks.h.k.kks.hh", "k.kks.h.k.kks.hh", "k.kks.h.k.kks.hh", "k.kks.h.s.sssskk",
    "c.kks.h.k.kks.hh", "k.kks.h.k.kks.hh", "k.kks.h.k.kks.hh", "k.kks.h.k.kks.hh",
    "c.kks.h.k.kks.hh", "k.kks.h.k.kks.hh", "k.kks.h.k.kks.hh", "s.s.s.sssssskkss",
]


def boss_theme():
    """The Iron Warden: fast, relentless E minor with a galloping bass."""
    chords = ["Em", "Em", "C", "D", "Em", "Em", "C", "B", "Am", "Am", "Em", "Em", "C", "D", "B", "B"]
    song = Song(bpm=168, beats=64)
    melody = ("E5/0.5 E5/0.5 G5/0.5 E5/0.5 B5/1 A5/1 | G5/1 F#5/1 E5/2 |"
              "E5/0.5 E5/0.5 G5/0.5 E5/0.5 C6/1 B5/1 | A5/1 F#5/1 D5/2 |"
              "E5/0.5 E5/0.5 G5/0.5 E5/0.5 B5/1 A5/1 | G5/1 A5/1 B5/2 | C6/1 B5/1 A5/1 G5/1 | F#5/2 D#5/2 |"
              "A5/1.5 G5/0.5 E5/2 | C6/1 B5/1 A5/2 | G5/1.5 F#5/0.5 E5/2 | B4/1 E5/1 G5/2 |"
              "C6/1.5 B5/0.5 G5/2 | D6/1.5 C6/0.5 A5/2 | B5/1 A5/1 F#5/1 D#5/1 | B4/2 -/2")
    song.voice(melody, pulse(0.25), 0.1, transpose=-12)
    song.voice(melody, pulse(0.125), 0.045)  # the same line an octave up, for a hard edge
    song.arpeggio(chords, [0, 1, 2, 3, 2, 1] * 2 + [0, 1, 2, 3], pulse(0.125), 0.03, step=0.25)
    song.bass(chords, [(0, 0.25), (0, 0.25), (12, 0.5)] * 4, 0.2)  # gallop: da-da-DUM
    for bar, pattern in enumerate(BOSS_BEAT):
        song.drum_bar(bar, pattern, 0.14)
    return song.samples


def victory_tune():
    """The results screen after someone escapes: a rising C major fanfare."""
    song = Song(bpm=132, beats=8, tail_seconds=0.8)
    song.voice("G4/0.333 C5/0.333 E5/0.334 G5/1 E5/0.5 F5/0.5 G5/1 A5/0.5 B5/0.5 C6/3",
               pulse(0.5), 0.12, gate=0.95, vibrato=True)
    song.voice("E4/0.333 G4/0.333 C5/0.334 E5/1 C5/0.5 C5/0.5 D5/1 F5/0.5 F5/0.5 E5/3", pulse(0.25), 0.06, gate=0.95)
    song.voice("C3/2 F2/1 G2/1 C3/4", triangle, 0.22, decay=0.4)
    song.drums(1, "s.s.s.......s...", 0.1)
    return song.samples


def defeat_tune():
    """The results screen when nobody escaped: a slow fall to A minor."""
    song = Song(bpm=76, beats=8, tail_seconds=0.8)
    song.voice("E5/1 D5/1 C5/1 B4/1 | A4/0.5 G#4/0.5 A4/3", pulse(0.5), 0.11, gate=0.95, vibrato=True)
    song.voice("C5/2 A4/2 | E4/1 E4/3", pulse(0.25), 0.05, gate=0.95)
    song.voice("A2/2 F2/2 | E2/2 A2/2", triangle, 0.22, decay=0.3)
    return song.samples


def build_music(directory):
    songs = {
        "title": title_theme(),
        "dungeon": dungeon_theme(),
        "boss": boss_theme(),
        "victory": victory_tune(),
        "defeat": defeat_tune(),
    }
    for name, samples in songs.items():
        write_wav(directory / f"{name}.wav", samples)


# ---- Build --------------------------------------------------------------------------------

def main():
    theme = ASSETS / "themes" / "emerald_keep"
    for name, pixels in MONSTERS.items():
        write_png(theme / "monsters" / f"{name}.png", pixels)
    for name, pixels in OBJECTS.items():
        write_png(theme / "tiles" / f"{name}.png", pixels)

    write_png(theme / "tiles" / "floor_a.png", floor_tile(1, "#6fa48a", "#5a8f78", "#3d6555", "#4f7f6a"))
    write_png(theme / "tiles" / "floor_b.png", floor_tile(2, "#68a084", "#548a72", "#3a6152", "#4a7863"))
    write_png(theme / "tiles" / "wall.png", wall_tile())
    write_png(theme / "tiles" / "cracked_wall.png", cracked_wall_tile())
    write_png(theme / "tiles" / "spikes.png", spikes_tile())
    write_png(theme / "tiles" / "trapdoor.png", trapdoor_tile())
    write_png(theme / "tiles" / "pit.png", pit_tile())
    write_png(theme / "tiles" / "stairs.png", stairs_tile())
    write_png(theme / "tiles" / "stairs_up.png", stairs_up_tile())
    write_png(theme / "tiles" / "rubble.png", rubble_tile())
    write_png(theme / "tiles" / "gate.png", gate_tile())
    write_png(theme / "tiles" / "water_shallow.png", water_tile(deep=False))
    write_png(theme / "tiles" / "water_deep.png", water_tile(deep=True))
    write_png(theme / "tiles" / "exit_open.png", exit_tile(sealed=False))
    write_png(theme / "tiles" / "exit_sealed.png", exit_tile(sealed=True))

    heroes = ASSETS / "heroes"
    write_png(heroes / "hero_base.png", sprite(HERO_ROWS, {**HERO_PALETTE, "T": "#00000000"}, mirror=True))
    tunic = {char: "#00000000" for char in HERO_PALETTE}
    tunic["T"] = "#ffffff"
    write_png(heroes / "hero_tunic.png", sprite(HERO_ROWS, tunic, mirror=True))

    ui = ASSETS / "ui"
    write_png(ui / "frame.png", frame("#b08d4a", "#5a4424", "#1a1626"))
    write_png(ui / "frame_hover.png", frame("#e2c47a", "#8a6a34", "#2a2338"))
    write_png(ui / "frame_pressed.png", frame("#e2c47a", "#8a6a34", "#3a2f4d"))
    write_png(ui / "frame_disabled.png", frame("#4a4030", "#2a2418", "#120f1c"))
    write_png(ui / "heart_full.png", sprite(HEART_ROWS, {"o": "#2a0a10", "r": "#d8434a", "l": "#ffb0a0"}))
    write_png(ui / "heart_empty.png", sprite(HEART_ROWS, {"o": "#2a0a10", "r": "#3a2a30", "l": "#4a3a40"}))
    for name, pixels in ICONS.items():
        write_png(ui / f"{name}.png", pixels)

    build_font(ASSETS / "fonts", "woad_gothic")
    build_sounds(ASSETS / "audio")
    build_music(ASSETS / "audio" / "music")
    print(f"Assets written to {ASSETS}")


if __name__ == "__main__":
    main()
