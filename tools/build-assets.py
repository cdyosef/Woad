"""Generates Woad's placeholder art and sound into assets/.

Everything is original and built from this file: pixel sprites drawn as text grids, tiles and UI
frames drawn procedurally, a heading pixel font (BMFont), and 8-bit style sound effects synthesized
from waveforms. The output is deterministic, so re-running it only changes files whose source
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


def write_png(path, pixels):
    """Writes rows of (r, g, b, a) tuples as an RGBA PNG."""
    height, width = len(pixels), len(pixels[0])
    raw = b"".join(b"\x00" + bytes(channel for pixel in row for channel in pixel) for row in pixels)

    def chunk(tag, data):
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 9))
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

HEART_ROWS = [".oo.oo.", "orrorro", "orlrrro", "orrrrro", ".orrro.", "..oro..", "...o..."]


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
        "victory": notes([(523, 0.1), (659, 0.1), (784, 0.1), (1047, 0.12), (784, 0.08), (1047, 0.35)], volume=0.25),
    }
    for name, samples in sounds.items():
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
    write_png(theme / "tiles" / "pit.png", pit_tile())
    write_png(theme / "tiles" / "stairs.png", stairs_tile())
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

    build_font(ASSETS / "fonts", "woad_gothic")
    build_sounds(ASSETS / "audio")
    print(f"Assets written to {ASSETS}")


if __name__ == "__main__":
    main()
