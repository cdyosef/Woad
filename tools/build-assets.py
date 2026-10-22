"""Generates Woad's placeholder art and sound into assets/.

Everything is original and built from this file: pixel sprites drawn as text grids (plus the hero
look list, scenes/ui/Looks.Generated.cs), tiles and UI
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

# Hero looks. Each is a 16x16 grid: a left half mirrored (with "extra" pixels drawn over the result
# for anything lopsided), or full rows with "full": True. The player picks the colors of four
# channels, so those pixels go into white masks the game tints: T/t main color (t its shade),
# R/r trim, S/s skin (or scales, stone, wood...) and H/h hair (or fur, leaves). Other letters are
# fixed colors from LOOK_COLORS or the look's own "palette". 'e' marks the eyes (X'd out when Keeling Over).
# "skin" and "hair" name the color sets the game offers for those channels (None: the look has none);
# "skinDefault" and "hairDefault" pick the color a new look starts with (otherwise the set's first).

LOOK_COLORS = {
    "o": OUTLINE, "e": OUTLINE, "b": "#5a3a1a", "k": "#3a2414", "m": "#b8c0cc", "M": "#6a7484",
    "g": "#e8b840", "w": "#ece6d6", "n": "#a07a4a", "N": "#6a4a2a", "p": "#f0a0b0", "c": "#a0e0f0",
    "G": "#60d060", "l": "#8a5a32",
}
CHANNELS = {"T": 1, "t": 1, "R": 2, "r": 2, "S": 3, "s": 3, "H": 4, "h": 4}
MASK_LIGHT, MASK_SHADE = "#ffffff", "#a8a8a8"

LOOKS = [
    {"id": "wanderer", "name": "Wanderer", "skin": "human", "hair": "hair", "rows": [
        "........", "......oo", ".....oHH", "....oHHH", "....oHSS", "....oSeS", "....oSSS", "...ootTT",
        "..oSotTT", "..oSotTT", "...oobbR", "....otTT", "....otTo", "....okko", "....oooo", "........"]},
    {"id": "knight", "name": "Knight", "skin": None, "hair": None, "rows": [
        ".......R", "......oR", ".....omm", "....ommm", "....oMMM", "....oMeM", "....ommm", "...ommTT",
        "..omotTR", "..omotTR", "...oobbb", "....otTT", "....oMmo", "....oMMo", "....oooo", "........"]},
    {"id": "ranger", "name": "Ranger", "skin": "human", "hair": None, "rows": [
        "........", "......oo", ".....oTT", "....oTTT", "...otTSS", "...otSeS", "...otSSS", "..oRRtTT",
        "..oRotTT", "..oSotTT", "..oRobbb", "...oRtTT", "....ollo", "....okko", "....oooo", "........"],
     "extra": [(6, 13, "n"), (7, 14, "n"), (8, 14, "n"), (9, 14, "n"), (10, 13, "n"), (7, 13, "w"), (8, 13, "w"), (9, 13, "w")]},
    {"id": "rogue", "name": "Rogue", "skin": "human", "hair": "hair", "rows": [
        "........", "......oo", ".....oHH", "....oHHH", "....oHSS", "....oSeS", "....oRRR", "...ootTT",
        "..oSotTT", "..oSotTT", "...oobbR", "....otTT", "....otTo", "....okko", "....oooo", "........"],
     "extra": [(6, 12, "R"), (7, 12, "r"), (8, 13, "m"), (9, 13, "m"), (10, 13, "k")]},
    {"id": "mage", "name": "Mage", "skin": "human", "hair": "hair", "rows": [
        ".......o", "......oT", ".....otT", ".....oRR", "...ooooo", "....oHeS", "....oHSS", "...ootTT",
        "..oSotTR", "..oSotTR", "...otTTR", "...otTTR", "...otTTR", "...oRRRR", "...ooooo", "........"],
     "extra": [(5, 13, "g"), (6, 13, "n"), (7, 13, "n"), (8, 13, "n"), (9, 13, "n"), (10, 13, "n"), (11, 13, "n"), (12, 13, "n"), (4, 13, "g"), (5, 12, "g"), (5, 14, "g")]},
    {"id": "wizard", "name": "Wizard", "skin": "human", "hair": "hair", "rows": [
        "........", ".......o", "......oT", ".....otT", "..oRRRRR", "....oSeS", "....oHHH", "...otHHH",
        "..oSoTHH", "..oSotHH", "...otTTH", "...otTTT", "...otTTT", "...oRRRR", "...ooooo", "........"],
     "extra": [(1, 8, "T"), (1, 9, "o"), (0, 9, "o"), (0, 10, "o"), (3, 7, "g"), (3, 8, "g"), (2, 7, "g"),
               (2, 1, "c"), (3, 0, "c"), (3, 1, "w"), (3, 2, "c"), (4, 1, "c"),
               (5, 1, "n"), (6, 1, "N"), (7, 1, "n"), (8, 1, "n"), (9, 1, "N"), (10, 1, "n"), (11, 1, "n"), (12, 1, "N"), (13, 1, "n")]},
    {"id": "brute", "name": "Brute", "skin": "human", "hair": "hair", "rows": [
        "........", "......oo", ".....oSS", "....oSSS", "....osSS", "....oSeS", "....oHHH", "..oSSHHH",
        ".oSSsHHH", ".oSoSSSh", "..oSoRRR", "....oTTT", "....otTo", "....okko", "....oooo", "........"]},
    {"id": "barbarian", "name": "Barbarian", "skin": "human", "hair": "hair", "rows": [
        "........", ".....oHH", "....oHHH", "...oHHHH", "...oHHSS", "...oHSeS", "...oHSSS", "..oSSSSS",
        ".oSRoSSS", ".oSSoSsS", "...obbbb", "...oTtTT", "....oSSo", "....onno", "....oooo", "........"],
     "extra": [(3, 14, "m"), (4, 13, "m"), (4, 15, "m"), (5, 13, "M"), (5, 15, "M"), (4, 14, "N"), (5, 14, "N"),
               (6, 14, "N"), (7, 14, "N"), (8, 14, "N"), (9, 14, "N"), (10, 14, "N"), (11, 14, "N")]},
    {"id": "wild_woman", "name": "Wild Woman", "skin": "human", "hair": "hair", "rows": [
        "........", ".....oHH", "....oHHH", "...oHHHH", "...oHHSS", "...oHSeS", "...oHSSS", "...oHSSS",
        "..oSoTTS", "..oRoSSS", "...oSSSS", "...otTTT", "....oSSo", "....onno", "....oooo", "........"],
     "extra": [(3, 1, "m"), (4, 1, "m"), (5, 1, "m"), (6, 1, "m"), (7, 1, "m"), (8, 1, "m"), (9, 0, "g"), (9, 1, "g"), (10, 1, "N")]},
    {"id": "shaka", "name": "Shaka", "skin": "human", "hair": "hair", "skinDefault": "deep", "hairDefault": "black", "rows": [
        "........", "......oo", ".....oHH", "....onnn", "....oSSS", "....oSeS", "....oSSS", "...ooSSS",
        "..oSoSSS", "..owoSSS", "...oTTTT", "...oTtTt", "....oSSo", "....owwo", "....oooo", "........"],
     "extra": [(5, 1, "o"), (6, 0, "w"), (6, 1, "w"), (6, 2, "w"), (7, 0, "R"), (7, 1, "w"), (7, 2, "R"), (8, 0, "w"), (8, 1, "R"), (8, 2, "w"),
               (9, 0, "R"), (9, 1, "w"), (9, 2, "w"), (10, 0, "w"), (10, 1, "R"), (10, 2, "R"), (11, 0, "w"), (11, 1, "w"), (11, 2, "w"), (12, 1, "o"),
               (4, 1, "N"), (13, 1, "N"),
               (1, 14, "m"), (2, 14, "m"), (3, 14, "M"), (4, 14, "N"), (5, 14, "N"), (6, 14, "N"), (7, 14, "N"), (8, 14, "N"), (9, 14, "N"), (10, 14, "N"), (11, 14, "N"), (12, 14, "N")]},
    {"id": "druid", "name": "Druid", "skin": "human", "hair": "hair", "rows": [
        "..n.n...", "...nn.oo", "....noHH", "....oRRR", "....oHSS", "....oSeS", "....oHSS", "...ootTT",
        "..oSotTT", "..oSotTT", "...oobbb", "...otTTT", "...otTTT", "...oRRRR", "...ooooo", "........"]},
    {"id": "princess", "name": "Princess", "skin": "human", "hair": "hair", "rows": [
        "........", ".....g.g", ".....ogg", "....oHHH", "...oHHSS", "...oHSeS", "...oHSSS", "...oHtTT",
        "..oSHtTT", "..oSotTT", "...ooRRR", "...otTTT", "..otTTTT", ".oRRRRRR", ".ooooooo", "........"]},
    {"id": "noble", "name": "Noble", "skin": "human", "hair": "hair", "rows": [
        "........", "......oo", "....oTTT", "...otTTT", "....oHSS", "....oSeS", "....oSSS", "...owwww",
        "..oTotTg", "..oSotTg", "...otTTg", "....otTT", "....oRRo", "....okko", "....oooo", "........"],
     "extra": [(0, 11, "w"), (1, 11, "w"), (1, 12, "w"), (0, 12, "w"), (0, 13, "w")]},
    {"id": "enchantress", "name": "Enchantress", "skin": "human", "hair": "hair", "full": True, "rows": [
        "................", "......oooo......", ".....oHHHHo.....", "....oHHHHHHo....",
        "...oHHHSSSSo....", "...oHHeSSeSo....", "...oHHSppSSg....", "..oHHSSggSSo....",
        "..oHHTTSSTToSo..", "..oHHtTTTTtoRo..", "...oHtTTTTtRRo..", "...otTTTTTTto...",
        "...otTTTToSTo...", "..otTTTTToSTo...", "..oRRRRRRokRo...", "..ooooooooooo..."]},
    {"id": "dancer", "name": "Dancer", "skin": "human", "hair": "hair", "rows": [
        "........", "......oo", ".....oHH", "....oHHH", "...oHHSS", "...oHSeS", "...oHSSS", "...oSSTT",
        "..oSotTT", "..oSoSSS", "...ogggg", "...oRRRR", "..orRRRR", "....oSSo", "....oooo", "........"],
     "extra": [(4, 12, "R"), (3, 12, "R"), (5, 12, "H"), (6, 13, "H"), (7, 13, "H")]},
    {"id": "sorceress", "name": "Sorceress", "skin": "human", "hair": "hair", "hairDefault": "black", "rows": [
        "........", "......oo", ".....oHH", "....oggc", "...oHHSS", "...oHSeS", "...oHSSS", "...oHSSS",
        "..oSHTTT", "..oSoSSS", "...oRRRR", "..otTTTT", "..otTTTT", "....oggo", "....oooo", "........"],
     "extra": [(9, 3, "H"), (10, 2, "H"), (11, 1, "H"), (12, 1, "h"), (6, 3, "g"), (6, 12, "g")]},
    {"id": "street_rat", "name": "Street Rat", "skin": "human", "hair": "hair", "hairDefault": "black", "rows": [
        "......oo", ".....oRR", ".....oRR", "....oHHH", "....oHSS", "....oSeS", "....oSSS", "...oTTSS",
        "..oSoTSS", "..oSoTSS", "...oRRRR", "...owwww", "...owwwo", "....oSSo", "....oooo", "........"],
     "extra": [(1, 10, "k"), (2, 10, "k")]},
    {"id": "sultan", "name": "Sultan", "skin": "human", "hair": "hair", "hairDefault": "black", "rows": [
        "......oo", "....ooRR", "...oRRRR", "...oRrRg", "....oHSS", "....oSeS", "....oHHH", "...otHHH",
        "..oTotTH", "..oSoggg", "...otTTT", "...otTTT", "...otTTT", "....oggo", "....oooo", "........"],
     "extra": [(0, 9, "w"), (0, 10, "w"), (1, 10, "w")]},
    {"id": "bard", "name": "Bard", "skin": "human", "hair": "hair", "rows": [
        "........", "......oo", ".....oRR", "....oRRR", "....oHSS", "....oSeS", "....oSSS", "...ootTT",
        "..oSotTT", "..oSotTT", "...oobbb", "....oRRR", "....oRRo", "....okko", "....oooo", "........"],
     "extra": [(11, 8, "T"), (11, 9, "T"), (11, 10, "T"), (12, 9, "T"), (12, 10, "T"),
               (0, 12, "w"), (0, 13, "w"), (1, 13, "w"),
               (6, 2, "N"), (7, 2, "N"), (8, 1, "n"), (8, 2, "n"), (9, 0, "n"), (9, 1, "n"), (9, 2, "o"), (10, 0, "n"), (10, 1, "n"), (10, 2, "n"), (11, 1, "n")]},
    {"id": "bunny_lady", "name": "Bunny Lady", "skin": "human", "hair": "hair", "rows": [
        "....oHpo", "....oHpo", "....oHHH", "....oHHH", "...oHHSS", "...oHSeS", "....oSSS", "...ootTT",
        "..oSotTT", "..oSotTT", "...ooRRR", "...otTTT", "...oRRRR", "....oSSo", "....okko", "....oooo"]},
    {"id": "cat_lady", "name": "Cat Lady", "skin": "human", "hair": "hair", "rows": [
        "........", "....o...", "....oHo.", "....oHHH", "...oHHSS", "...oHSeS", "....oSSS", "...ootTT",
        "..oSotTT", "..oSotTT", "...oobbb", "....otTT", "....otTo", "....okko", "....oooo", "........"],
     "extra": [(12, 12, "H"), (11, 13, "H"), (10, 14, "H"), (9, 14, "H"), (8, 14, "h")]},
    {"id": "monk", "name": "Monk", "skin": "human", "hair": None, "rows": [
        "........", "......oo", ".....oSS", "....oSSS", "....osSS", "....oSeS", "....oSSS", "...ootTT",
        "..oSotTT", "..oSotTT", "...oRRRR", "...otTTT", "...otTTT", "....oSSo", "....okko", "....oooo"],
     "extra": [(7, 6, "n"), (8, 5, "n"), (9, 5, "n"), (8, 10, "n"), (9, 10, "n"), (7, 9, "n")]},
    {"id": "priestess", "name": "Priestess", "skin": "human", "hair": "hair", "rows": [
        "........", "......oo", ".....oRR", "....oRRR", "...oRHSS", "...oRSeS", "...oRSSS", "...oRtTT",
        "..oSotTg", "..oSotgg", "...otTTg", "...otTTT", "...otTTT", "...oRRRR", "...ooooo", "........"]},
    {"id": "elf", "name": "Elf", "skin": "human", "hair": "hair", "rows": [
        "........", "......oo", ".....oHH", "....oHHH", "...ooHSS", "..oSSSeS", "....oSSS", "...oHtTT",
        "..oSHtTT", "..oSotTT", "...oobbb", "....otTT", "....oRRo", "....okko", "....oooo", "........"]},
    {"id": "elf_lady", "name": "She-Elf", "skin": "human", "hair": "hair", "rows": [
        "........", "......oo", ".....oHH", "....oggg", "...ooHSS", "..oSSSeS", "...oHSSS", "...oHtTT",
        "..oSHtTT", "..oSHtTT", "...ooRRR", "...otTTT", "...otTTT", "..otTTTT", "..oooooo", "........"]},
    {"id": "dark_elf", "name": "Dark Elf", "skin": "darkelf", "hair": "hair", "hairDefault": "white", "rows": [
        "........", "......oo", ".....oHH", "....oHHH", "...ooHSS", "..oSSSeS", "...oHSSS", "..oRHtTT",
        "..oTotTT", "..oTotTT", "..oTobbb", "..oTotTT", "....oTTo", "....okko", "....oooo", "........"],
     "extra": [(9, 1, "N"), (10, 1, "m"), (11, 1, "m"), (9, 14, "N"), (10, 14, "m"), (11, 14, "m")],
     "palette": {"e": "#e04050"}},
    {"id": "spider_lover", "name": "Spider Lover", "skin": "darkelf", "hair": "hair", "hairDefault": "white", "rows": [
        "........", "......oo", ".....oHH", "....oHHH", "...ooHSS", "..oSSSeS", "...oHSSS", "...oHSSS",
        "..oSHTTS", "..oSHSSS", "...oHSSS", "...otTTT", "....oSSo", "....okko", "....oooo", "........"],
     "extra": [(10, 13, "w"), (11, 13, "w"), (12, 12, "o"), (12, 13, "o"), (12, 14, "o"), (13, 12, "o"), (13, 14, "o"), (11, 12, "o"), (11, 14, "o"),
               (11, 7, "w"), (11, 8, "w")],
     "palette": {"e": "#e04050"}},
    {"id": "brave", "name": "Brave", "skin": "human", "hair": "hair", "skinDefault": "tan", "hairDefault": "black", "rows": [
        "........", "......oo", ".....oHH", "....oRRR", "...ooHSS", "..oSSSeS", "...oHSSS", "...oHtTT",
        "..oSHtTT", "..oSHtTT", "...otTTT", "...orRrR", "....oTTo", "....onno", "....oooo", "........"],
     "extra": [(0, 10, "w"), (1, 10, "w"), (0, 11, "k"),
               (5, 13, "n"), (6, 14, "n"), (7, 14, "n"), (8, 14, "n"), (9, 14, "n"), (10, 14, "n"), (11, 14, "n"), (12, 13, "n"),
               (6, 13, "w"), (7, 13, "w"), (8, 13, "w"), (9, 13, "w"), (10, 13, "w"), (11, 13, "w")]},
    {"id": "wolf_man", "name": "Wolf Man", "skin": None, "hair": "fur", "rows": [
        "....o...", "....oHo.", "....oHHH", "....oHHH", "....oHHH", "....oHeH", "....oHhh", "...oHHho",
        "..oHotTT", "..oHotTT", "...oobbb", "....oTTT", "....oTTo", "....oHHo", "....oooo", "........"],
     "palette": {"e": "#e8c040"}},
    {"id": "birdman", "name": "Birdman", "skin": None, "hair": "plumage", "rows": [
        ".......o", "......oH", ".....oHH", "....oHHH", "....oHHH", "....oHeH", "....oHgg", "....oHHg",
        "..oHotTT", ".oHHotTT", ".ohHobbb", "....otTT", ".....g..", "....ggg.", "........", "........"]},
    {"id": "dwarf", "name": "Dwarf", "skin": "human", "hair": "hair", "rows": [
        "........", "........", "........", ".....omm", "....oMMM", "....oSeS", "...oHSSS", "..oHHHHH",
        ".oSoHHHH", ".oSotHHH", "..ootTHH", "...oobbb", "...otTTT", "...okkko", "...ooooo", "........"]},
    {"id": "halfling", "name": "Halfling", "skin": "human", "hair": "hair", "rows": [
        "........", "........", "........", "........", "......oo", ".....oHH", "....oHHH", "....oHSS",
        "....oSeS", "...ootTT", "..oSotTT", "...oobbb", "....oRRo", "...oSSSo", "...ooooo", "........"]},
    {"id": "lizard_man", "name": "Lizard Man", "skin": "scales", "hair": None, "rows": [
        "........", "......oR", ".....oSS", "....oSSS", "....oSSS", "....oeSS", "....osSS", ".....oss",
        "..oSotTT", "..oSotTT", "...oobbb", "....osSS", "....oSSo", "....oSSo", "....oooo", "........"],
     "extra": [(12, 12, "S"), (13, 13, "S"), (14, 13, "s"), (14, 14, "s"), (0, 7, "R"), (1, 7, "R"), (0, 8, "R"), (2, 6, "R"), (2, 9, "R")],
     "palette": {"e": "#e8d040"}},
    {"id": "stone_man", "name": "Stone Man", "skin": "stone", "hair": None, "rows": [
        "........", "......oo", ".....oSS", "....oSsS", "....oSSS", "....oSeS", "...ooSSS", ".ooSSsSS",
        ".oSSoSsS", ".oSSoSSS", ".oSSoTTT", "..oooSSS", "...oSSso", "...oSSSo", "...ooooo", "........"],
     "extra": [(7, 7, "R"), (7, 8, "R"), (8, 6, "R"), (8, 9, "R")],
     "palette": {"e": "#ffd860"}},
    {"id": "log_man", "name": "Log Man", "skin": "wood", "hair": "leaf", "rows": [
        "......H.", ".....oHH", "....oSSS", "....oSsS", "....osSS", "....oSeS", "....osSS", "....otTT",
        ".ss.oSsS", "..s.oSSs", "....osSS", "....oSsS", "....oSso", "...ssSSo", "....oooo", "........"],
     "extra": [(0, 6, "."), (0, 10, "H"), (0, 11, "h"), (1, 11, "H")]},
    {"id": "devilman", "name": "Devilman", "skin": "demon", "hair": "hair", "hairDefault": "black", "rows": [
        "...w....", "...ww...", "....wooo", "....oHHH", "....oSSS", "....oSeS", "....oSSS", ".....oSH",
        "..oSotTT", "..oSotTT", "...oobRb", "....otTT", "....otTo", "....okko", "....oooo", "........"],
     "extra": [(12, 12, "S"), (11, 13, "S"), (10, 14, "S"), (9, 14, "S"), (8, 13, "S"), (8, 14, "S"), (8, 15, "S"), (7, 14, "S")],
     "palette": {"e": "#ffd860"}},
    {"id": "helling", "name": "Helling", "skin": "demon", "hair": "hair", "hairDefault": "black", "rows": [
        "...w....", "...ww...", "....woHH", "....oHHH", "...oHHSS", "...oHSeS", "k..oHSSS", "kk.oHtTT",
        "kkoSotTT", ".koSotTT", "...otTTT", "...otTTT", "....otTo", "....okko", "....oooo", "........"],
     "extra": [(12, 12, "S"), (13, 13, "S"), (13, 14, "S"), (12, 14, "S"), (14, 14, "S")],
     "palette": {"e": "#ffd860"}},
    {"id": "raider", "name": "Raider", "skin": "human", "hair": "hair", "rows": [
        "........", "......oo", ".....oHH", "....oHHH", "....oHSS", "...oHSeS", "...oHSSS", "...oHTSS",
        "..oSoTSS", "..oSoTSs", "...oobbb", "....oRRR", "....oRRo", "....okko", "....oooo", "........"],
     "extra": [(8, 12, "m"), (8, 13, "m"), (8, 14, "m"), (9, 13, "m"), (9, 14, "M"), (10, 13, "n")]},
    {"id": "swashbuckler", "name": "Swashbuckler", "skin": "human", "hair": "hair", "rows": [
        "........", "......oo", ".....oTT", "..oTTTTT", "....oHSS", "....oSeS", "....oSSS", "...owwSS",
        "..owowSS", "..oSowSS", "...oRRRR", "....otTT", "....okko", "....okko", "....oooo", "........"],
     "extra": [(1, 11, "w"), (0, 12, "w"), (1, 12, "R"), (4, 14, "m"), (5, 14, "m"), (6, 14, "m"), (7, 14, "m"), (8, 14, "m"), (9, 14, "m"), (10, 14, "m"),
               (11, 13, "g"), (11, 14, "g"), (11, 15, "g"), (12, 14, "N")]},
    {"id": "pirate", "name": "Pirate", "skin": "human", "hair": "hair", "hairDefault": "black", "rows": [
        "........", "......oo", "...oTTTw", "..ottTTT", "....oHSS", "....oSeS", "....oHHH", "...oRHHH",
        "..oSoRww", "..oSoRTT", "...oRbbb", "...oRtTT", "....otTo", "....okko", "....oooo", "........"],
     "extra": [(4, 5, "k"), (4, 6, "k"), (4, 7, "k"), (4, 8, "k"), (4, 9, "k"), (4, 10, "k"), (5, 6, "k"),
               (6, 1, "m"), (7, 1, "m"), (8, 1, "m"), (9, 1, "m"), (10, 0, "g"), (10, 1, "g"), (11, 1, "N")]},
    {"id": "tempest", "name": "Tempest", "skin": "human", "hair": "hair", "rows": [
        "........", "......oo", "...oTTTg", "..otTTTT", "...oHHSS", "...oHSeS", "...oHSSS", "...oHtww",
        "..oTotww", "..oSotRR", "...otRRR", "...otTTT", "....okko", "....okko", "....oooo", "........"],
     "extra": [(1, 10, "R"), (0, 11, "R"), (1, 11, "r"), (6, 14, "m"), (7, 14, "m"), (8, 14, "m"), (9, 13, "m"), (10, 13, "g"), (10, 14, "g"), (11, 13, "N")]},
    {"id": "riverman", "name": "Riverman", "skin": "human", "hair": "hair", "rows": [
        "........", "......oo", ".....oHH", "....oHHH", "....oHSS", "....oSeS", "....oSSS", "..otTTTT",
        ".oSotTRT", ".oSotTRT", "...otTTT", "....oRRR", "....oSSo", "....okko", "....oooo", "........"],
     "extra": [(5, 1, "N"), (6, 1, "N"), (7, 1, "N"), (8, 1, "N"), (9, 1, "N"), (10, 1, "N"), (4, 1, "n"), (3, 1, "n"), (11, 1, "n"), (12, 1, "n")]},
    {"id": "alchemist", "name": "Alchemist", "skin": "human", "hair": "hair", "rows": [
        "........", "......oo", ".....oHH", "....oHcm", "....oHSS", "....oSeS", "....oSSS", "...ootTT",
        "..oSotRR", "..oSotRR", "...otRRR", "...otRRR", "....oRRo", "....okko", "....oooo", "........"],
     "extra": [(6, 13, "w"), (7, 13, "c"), (8, 13, "G"), (8, 14, "G"), (9, 13, "G"), (9, 14, "G"), (7, 14, "o"), (10, 13, "o"), (10, 14, "o")]},
    {"id": "artificer", "name": "Artificer", "skin": "human", "hair": "hair", "rows": [
        "........", "......oo", ".....oHH", "....oHHH", "....oHSS", "....oSeS", "....oSSS", "...ootTT",
        "..oSotTT", "..oSotTT", "...oobgb", "....otTT", "....oRRo", "....okko", "....oooo", "........"],
     "extra": [(7, 2, "m"), (7, 3, "M"), (8, 3, "m"), (9, 3, "m"), (8, 2, "M"), (9, 2, "M"), (10, 3, "g"),
               (4, 9, "g"), (4, 10, "g"), (5, 10, "g"),
               (8, 13, "m"), (9, 13, "m"), (10, 13, "m"), (7, 13, "M"), (7, 14, "M"), (11, 13, "M"), (11, 14, "M")]},
]


def look_rows(look):
    """The look's full 16x16 grid: mirrored halves, then its extra pixels."""
    rows = list(look["rows"]) if look.get("full") else [row + row[::-1] for row in look["rows"]]
    grid = [list(row) for row in rows]
    for y, x, char in look.get("extra", []):
        grid[y][x] = char
    return ["".join(row) for row in grid]


def look_strip(look):
    """Five 16x16 frames side by side: the fixed colors, then the white masks of the main, trim, skin
    and hair channels (shade pixels a grey, so tinting keeps the shading)."""
    rows = look_rows(look)
    palette = {**LOOK_COLORS, **look.get("palette", {})}
    frames = []
    for channel in range(5):
        frame = []
        for y, row in enumerate(rows):
            pixels = []
            for x, char in enumerate(row):
                if char == ".":
                    pixels.append(TRANSPARENT)
                elif char in CHANNELS:
                    if CHANNELS[char] == channel:
                        pixels.append(rgba(MASK_SHADE if char.islower() else MASK_LIGHT))
                    else:
                        pixels.append(TRANSPARENT)
                elif channel == 0:
                    if char not in palette:
                        raise ValueError(f"look {look['id']} row {y} uses '{char}', which has no color")
                    pixels.append(rgba(palette[char]))
                else:
                    pixels.append(TRANSPARENT)
            frame.append(pixels)
        frames.append(frame)
    return [[pixel for frame in frames for pixel in frame[y]] for y in range(16)]


def write_look_list(path):
    """The game's list of looks (names, color sets and eye pixels), as C# so it ships with the code."""
    def text(value):
        return "null" if value is None else f'"{value}"'

    entries = []
    for look in LOOKS:
        rows = look_rows(look)
        if len(rows) != 16 or any(len(row) != 16 for row in rows):
            raise ValueError(f"look {look['id']} isn't 16x16")
        eyes = [(x, y) for y, row in enumerate(rows) for x, char in enumerate(row) if char == "e"][:2]
        eye_list = ", ".join(f"new({x}, {y})" for x, y in eyes)
        entries.append(f'        new("{look["id"]}", "{look["name"]}", {text(look["skin"])}, {text(look["hair"])}, [{eye_list}], '
                       f'{text(look.get("skinDefault"))}, {text(look.get("hairDefault"))}),')
    path.write_text(
        "// Generated by tools/build-assets.py from its LOOKS table; edit that and re-run it.\n"
        "namespace Woad.Scenes.Ui;\n\n"
        "public static partial class Looks\n{\n"
        "    public static IReadOnlyList<Look> All { get; } =\n    [\n"
        + "\n".join(entries) + "\n    ];\n}\n",
        encoding="utf-8", newline="\n")

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

    "ogre": (sprite([  # big, green and tusked
        "........",
        "....oooo",
        "...ossss",
        "..osssss",
        "..osesss",
        "..osssss",
        "..osswss",
        "...ossss",
        ".obbbbbb",
        "obsbbbbb",
        "osobbbbb",
        "osolllll",
        "..oppppp",
        "..oppo..",
        "..okko..",
        "..oooo..",
    ], {"o": "#1e140c", "s": "#c8844a", "e": "#ffe060", "w": "#f0ead8", "b": "#6a4a2a",
        "l": "#3a2a18", "p": "#5a4a6a", "k": "#2a1e14"}, mirror=True)),

    "ogre_champ": (sprite([  # bigger, fatter, shirtless and horned, with a great club
        ".............cc.",
        "..o.........occc",
        "...ho......oh.cc",
        "....hooooooh..cc",
        "....osssssso..cc",
        "....osesseso..cc",
        "....owsssswo..cc",
        "..oossssssssoocc",
        ".ossssssssssssso",
        "ossossddddssosso",
        "ossoddddddddosso",
        ".oo.bbbbbbbb.oo.",
        "....pppppppp....",
        "....ppp..ppp....",
        "....kkk..kkk....",
        "................",
    ], {"o": "#1e140c", "s": "#d0864a", "d": "#a8642e", "h": "#f0e6c8", "e": "#ff4030", "w": "#ffffff",
        "c": "#7a4a20", "b": "#5a3a1a", "p": "#4a3a5a", "k": "#2a1e14"})),

    "necromancer": (sprite([  # a hooded skull with glowing green eyes and hands
        "........",
        "......oo",
        ".....ohh",
        "....ohhh",
        "....ohww",
        "....ohew",
        "....ohww",
        "...ohhhh",
        "..ohhhgh",
        "..ohhhhh",
        ".oghhhhh",
        ".ohhhhhh",
        ".ohhhhhh",
        "ohhhhhhh",
        "oooooooo",
        "........",
    ], {"o": "#120c18", "h": "#3a2650", "w": "#e8e2d0", "e": "#60ff80", "g": "#60ff80"}, mirror=True)),

    "hexer": (sprite([  # a green-faced witch in a pointed hat
        ".......o",
        "......oh",
        "......oh",
        ".....ohh",
        "....ohhh",
        "..oooooo",
        "....osss",
        "....oses",
        "....osss",
        "...orrrr",
        "..orrrrr",
        "..orrrrr",
        ".orrrrrr",
        ".orrrrrr",
        ".ooooooo",
        "........",
    ], {"o": "#140e1a", "h": "#2a1a3a", "s": "#8ac070", "e": "#1a1410", "r": "#6a2a5a"}, mirror=True)),

    "gelatinous_cube": (sprite([  # a quivering block of jelly
        "........",
        "........",
        ".ooooooo",
        ".oliiiii",
        ".oliiiii",
        ".oiiiiii",
        ".oiiiiii",
        ".oiiiiii",
        ".oiiiiii",
        ".oiiiiii",
        ".oiiiiii",
        ".oiiiiii",
        ".odddddd",
        ".ooooooo",
        "........",
        "........",
    ], {"o": "#2a6a6a", "i": "#7ae0d8", "l": "#d8fff8", "d": "#4ab0a8"}, mirror=True)),

    "mimic": (sprite([  # a chest gaping open: teeth, a tongue, and eyes on the lid
        "........",
        "........",
        "..oooooo",
        ".owwewww",
        ".oddgddd",
        ".otototo",
        ".okkkkkk",
        ".okkrrrr",
        ".okkrrrr",
        ".otototo",
        ".owwwwww",
        ".oddgddd",
        ".owwwwww",
        ".ooooooo",
        "........",
        "........",
    ], {"o": "#2a1a10", "w": "#b07840", "d": "#8a5a2b", "g": "#e2c47a", "t": "#f0ead8",
        "k": "#1a0a08", "r": "#c83a4a", "e": "#ff5040"}, mirror=True)),

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

    "petty_thief": (sprite([
        "................",
        "......oooo......",
        ".....ohhhho.....",
        "....ohhhhhho....",
        "....ohsssshho...",
        "....ohsesesho...",
        "....ohhsssho....",
        "...ooohhhhoo....",
        "..occoccccoco...",
        "..occocccco.oyo.",
        "...oocccccooyyo.",
        "....ocooccooyyo.",
        "....oc.occo.oo..",
        "....ok..oko.....",
        "...ooo..ooo.....",
        "................",
    ], {"o": "#14100c", "h": "#4a3a5a", "s": "#d8b090", "e": "#14100c", "c": "#3a2e48", "k": "#2a2018", "y": "#c8a050"})),

    "mugger_fly": (sprite([
        "................",
        "................",
        "..ww........ww..",
        ".wwww.o..o.wwww.",
        ".wwwwwoooowwwww.",
        "..wwwoggggowww..",
        "....oreggeroo...",
        "....oggggggo....",
        "...ogkgkgkggo...",
        "...ogkgkgkggo...",
        "....oggggggo....",
        ".....oo..oo.....",
        "....o.y..o.o....",
        ".....yyy........",
        "......y.........",
        "................",
    ], {"o": "#0c100c", "w": "#cfe0e8", "g": "#3a6a3a", "k": "#1a2a1a", "r": "#d8434a", "e": "#0c100c", "y": "#f0c94a"})),

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

# The Sunken Temple's own monsters (its other monsters borrow the Emerald Keep's sprites).
# Monsters only the Emerald Keep has.
KEEP_ONLY = {"ogre", "ogre_champ", "necromancer", "hexer", "gelatinous_cube", "mimic"}

TEMPLE_MONSTERS = {
    "snapper_eel": sprite([
        "........", "........", "........",
        "......oo",
        ".....ogg",
        "....oggg",
        "....ogeg",
        "....oggg",
        "....oddd",
        "....owrr",
        "....oddd",
        "....oggg",
        "...ogggd",
        "..oggddd",
        ".bbbbbbb",
        "..bbbbbb",
    ], {"o": "#10202a", "g": "#3a8a6a", "d": "#24604a", "e": "#f0e040", "w": "#ffffff", "r": "#b03040", "b": "#5fb0d0"}, mirror=True),

    "drowned": sprite([
        "........",
        "......oo",
        ".....oww",
        "....ovww",
        "....owew",
        "....owww",
        ".....owt",
        "......oo",
        "...oowwo",
        "...ovowo",
        "...owowo",
        "....owvo",
        "....ow..",
        "....ov..",
        "...oww..",
        "........",
    ], {"o": "#1a2a2a", "w": "#b8d0c4", "e": "#40e0c0", "t": "#1a2a2a", "v": "#3a8a4a"}, mirror=True),

    "crab_knight": sprite([
        "........",
        "........",
        ".oo.....",
        "orro....",
        "or.ro...",
        "orrro...",
        ".orro.o.",
        "..oro.oe",
        "...ooomm",
        "..oRRRmM",
        ".oRRsRRR",
        ".oRRRRRR",
        "..oddddd",
        "..od.od.",
        ".od.od..",
        "........",
    ], {"o": "#2a0e0a", "r": "#d04a3a", "R": "#e8604a", "s": "#f8a080", "d": "#8a2a24", "e": "#101010",
        "m": "#b8c0cc", "M": "#6a7484"}, mirror=True),

    "crab_king": sprite([
        "........",
        "........",
        ".oo.....",
        "orro....",
        "or.ro...",
        "orrro...",
        ".orro.o.",
        "..oro.oe",
        "...oog.g",
        "..oRRggg",
        ".oRRsRRR",
        ".oRRRRRR",
        "..oddddd",
        "..od.od.",
        ".od.od..",
        "........",
    ], {"o": "#200a14", "r": "#b02a4a", "R": "#c83a5a", "s": "#f090a8", "d": "#701a30", "e": "#101010",
        "g": "#f0c94a"}, mirror=True),

    "bog_frog": sprite([
        "........", "........", "........", "........",
        "..ooo...",
        ".owkwo..",
        ".ogggooo",
        ".ogggggg",
        "oggooooo",
        "ogglllll",
        "ogglllll",
        ".oglllll",
        "oggo.ogg",
        "oooo.ooo",
        "........", "........",
    ], {"o": "#142010", "g": "#5aa040", "l": "#c8e070", "w": "#ffffff", "k": "#101010"}, mirror=True),

    "jellyfish": sprite([
        "........", "........",
        "......oo",
        "....oopp",
        "...oppll",
        "..opplll",
        "..oppppp",
        "..oppppp",
        "...p.p.p",
        "....p.p.",
        "...p.p.p",
        "....p...",
        "...p....",
        "........", "........", "........",
    ], {"o": "#4a2a6a", "p": "#c890f0e0", "l": "#f0d8ff"}, mirror=True),

    "leviathan": sprite([
        "..o.....",
        "..fo....",
        "..ffo.oo",
        "...ffogg",
        "....oggg",
        "...ogggg",
        "...ogegg",
        "...ogggg",
        "...odggg",
        "...owrrr",
        "...owrrr",
        "...odddd",
        "..oggggd",
        ".oggdd.o",
        "bbbbbbbb",
        "..bbbbbb",
    ], {"o": "#0a1a20", "g": "#2a7a8a", "d": "#1a4a5a", "f": "#e0705a", "e": "#ffd040", "w": "#ffffff",
        "r": "#a02a3a", "b": "#5fb0d0"}, mirror=True),
}

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
OBJECTS["chest_locked"] = sprite([  # iron bands and a gold lock plate with a keyhole
    "................", "................", "................",
    "..oooooooooooo..",
    ".owwwwwwwwwwwwo.",
    ".owddddddddddwo.",
    ".owddYYYYYYddwo.",
    ".ogggYyyyyYgggo.",
    ".owddYykkyYddwo.",
    ".owddYykkyYddwo.",
    ".owddYyykyYddwo.",
    ".owddYYYYYYddwo.",
    ".oggggggggggggo.",
    ".oooooooooooooo.",
    "................", "................",
], {**WOOD, "g": "#9aa4ad", "y": "#f0c94a", "Y": "#b08020", "k": "#1a1410"})

# The Boss Key's chest in the vault: crimson bands, gold horns, and a horned gold lock.
OBJECTS["chest_boss"] = sprite([
    "y..............y",
    "yY............Yy",
    ".yY..........Yy.",
    "..oooooooooooo..",
    ".owwwwwwwwwwwwo.",
    ".owdyddddddydwo.",
    ".owdyYYYYYYydwo.",
    ".ogggYyyyyYgggo.",
    ".owddYykkyYddwo.",
    ".owddYykkyYddwo.",
    ".owddYyykyYddwo.",
    ".owddYYYYYYddwo.",
    ".oggggggggggggo.",
    ".oooooooooooooo.",
    "................", "................",
], {**WOOD, "g": "#c8343e", "y": "#f0c94a", "Y": "#b08020", "w": "#c89040", "k": "#1a1410"})

# A den's grand chest: gilded and royal purple, a ruby on the lid and a heavy lock.
OBJECTS["chest_grand"] = sprite([
    "................",
    "...oooooooooo...",
    "..oyyyyyyyyyyo..",
    ".oyYppppppppYyo.",
    "oyYppppyyppppYyo",
    "oyppppyrryppppyo",
    "oyppppyrryppppyo",
    "oyYppppyyppppYyo",
    "oggggggYYggggggo",
    "oyppppYkkYppppyo",
    "oyppppYkkYppppyo",
    "oyppppYYYYppppyo",
    "oyYppppppppppYyo",
    "oyyyyyyyyyyyyyyo",
    "oooooooooooooooo",
    "................",
], {"o": "#2a1a10", "y": "#f0c94a", "Y": "#b08020", "p": "#6a2a8a", "r": "#e0304a", "g": "#b08020", "k": "#1a1410"})

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
ICONS["icon_gold"] = sprite([
    "............",
    "....oooo....",
    "...oyyyyo...",
    "..oyywyyyo..",
    "..oywyyyyo..",
    "..oyyyyyyo..",
    "..oyyyyydo..",
    "..oyyyyddo..",
    "...oyyddo...",
    "....oooo....",
    "............",
    "............",
], {"o": "#5a4424", "y": "#f0c94a", "w": "#fff4c0", "d": "#c8962a"})
ICONS["icon_boss_key"] = sprite(KEY_ROWS, {"o": "#e2c47a", "y": "#d8434a"})
ICONS["icon_defense"] = sprite([  # a kite shield, blue like the Defense dice
    "............",
    ".oooooooooo.",
    ".oggggggggo.",
    ".oglbbbbbgo.",
    ".oglbbbbbgo.",
    ".ogbbbbbbgo.",
    ".ogbbbbbbgo.",
    "..ogbbbbgo..",
    "..ogbbbbgo..",
    "...ogbbgo...",
    "....oggo....",
    ".....oo.....",
], {"o": OUTLINE, "g": "#e2c47a", "b": "#5d8de0", "l": "#a8c8f8"})
ICONS["icon_luck"] = sprite([  # a four-leaf clover
    ".ooo.ooo....",
    "olggolggo...",
    "ogggogggo...",
    "oggdoggdo...",
    ".ooooooo....",
    "olggolggo...",
    "ogggogggo...",
    "oggdoggdo...",
    ".ooo.oooso..",
    ".......oso..",
    "........oso.",
    ".........o..",
], {"o": OUTLINE, "g": "#5cc86a", "l": "#a8eca0", "d": "#2e8a3e", "s": "#3a7a3a"})


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


# Each theme's colors for the tiles that differ between themes.
KEEP_TILES = {
    "wall": ("#7a9a8a", "#a8c4b4", "#3d5a4c", "#24382f", "#141c18"),
    "pit": ("#2a3a33", "#1a2420", "#0e1412"),
    "stairs": ("#141c18", "#8aa898", "#4d6158"),
    "stairs_up": ("#2a3a33", "#c8dccf", "#6a8a7c"),
    "deep": ("#1d3a66", "#5f8fd0"),
    "shallow": ("#4a8ab8aa", "#c8e6f8d2"),
    "rubble": ("#8a8f86", "#b8bcb0", "#3a4038"),
}
TEMPLE_TILES = {
    "wall": ("#b8866e", "#e0b49a", "#7a4a3e", "#4a2a24", "#1e1210"),
    "pit": ("#5a4a3a", "#3a2e24", "#1e1812"),
    "stairs": ("#1e1812", "#d8c4a0", "#8a7458"),
    "stairs_up": ("#5a4a3a", "#ecdcbc", "#a08a68"),
    "deep": ("#11506a", "#3fa0b8"),
    "shallow": ("#3aa0a8aa", "#c0f0ecd2"),
    "rubble": ("#e07a6a", "#f8b0a0", "#8a3a34"),
}


def wall_tile(colors=KEEP_TILES):
    """A stone block seen from above and in front: lit top face, darker brick face below."""
    top, top_edge, face, mortar, outline = (rgba(color) for color in colors["wall"])
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


def cracked_wall_tile(colors=KEEP_TILES):
    """A wall block split by dark cracks: a bomb's blast breaks it."""
    tile = wall_tile(colors)
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


def pit_tile(colors=KEEP_TILES):
    """A hole: a crumbling rim fading into black."""
    rim = [rgba(color) for color in colors["pit"]]
    tile = blank(color=rgba("#05070a"))
    for depth, color in enumerate(rim):
        for i in range(depth, 16 - depth):
            tile[depth][i] = color
            tile[i][depth] = color
            tile[i][15 - depth] = color
    return tile


def stairs_tile(colors=KEEP_TILES):
    """Steps leading down: lit treads that shrink toward the dark bottom."""
    back, tread, riser = (rgba(color) for color in colors["stairs"])
    tile = blank(color=back)
    for step in range(5):
        y = 1 + step * 3
        inset = 1 + step
        for x in range(inset, 16 - inset):
            tile[y][x] = tread
            tile[y + 1][x] = riser
    return tile


def rubble_tile(colors=KEEP_TILES):
    """Broken stones (in the Temple, coral) scattered over the floor (drawn on top of it): rough ground, 2 steps a tile."""
    tile = blank()
    stone, light, dark = (rgba(color) for color in colors["rubble"])
    for left, top, width, height in [(1, 2, 4, 3), (7, 1, 3, 3), (11, 3, 4, 3), (3, 7, 3, 3),
                                     (8, 6, 4, 4), (1, 11, 3, 3), (6, 12, 4, 3), (12, 10, 3, 4)]:
        for y in range(top, top + height):
            for x in range(left, left + width):
                edge = y == top + height - 1 or x == left + width - 1
                tile[y][x] = dark if edge else light if y == top else stone
    return tile


def water_tile(deep, colors=KEEP_TILES):
    """Water, rippled with light crests: shallow shows a little floor through; deep is dark and opaque."""
    base, crest = (rgba(color) for color in colors["deep" if deep else "shallow"])
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


def stairs_up_tile(colors=KEEP_TILES):
    """Steps leading up: treads that widen toward a lit landing at the top."""
    back, tread, riser = (rgba(color) for color in colors["stairs_up"])
    tile = blank(color=back)
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


# ---- Woarld Map tiles and places (16x16) ----------------------------------------------------

GRASS = ("#5f9a4c", "#78b45c", "#467a3a")


def speckled(seed, base, specks, count=10):
    """A flat tile with a scatter of 1-2 pixel specks in the given colors."""
    tile = blank(color=rgba(base))
    generator = random.Random(seed)
    for _ in range(count):
        x, y = generator.randrange(0, 16), generator.randrange(0, 16)
        color = rgba(generator.choice(specks))
        tile[y][x] = color
        if generator.random() < 0.5 and x < 15:
            tile[y][x + 1] = color
    return tile


def grass_tile(seed):
    """Grass with tufts: a light blade over a dark root."""
    tile = speckled(seed, GRASS[0], [GRASS[2]], count=4)
    generator = random.Random(seed + 100)
    for _ in range(5):
        x, y = generator.randrange(1, 15), generator.randrange(1, 15)
        tile[y][x] = rgba(GRASS[1])
        tile[y + 1][x] = rgba(GRASS[2])
    return tile


def paint(tile, art, palette, dx=0, dy=0):
    """Draws sprite rows over a tile, '.' leaving it be."""
    for y, row in enumerate(art):
        for x, char in enumerate(row):
            if char != "." and 0 <= y + dy < 16 and 0 <= x + dx < 16:
                tile[y + dy][x + dx] = rgba(palette[char])
    return tile


TREE = ["..oooo..", ".oLLLGo.", "oLLGGGGo", "oLGGGGDo", "oGGGGDDo", ".oGDDDo.", "..ootoo.", "....t..."]
TREE_COLORS = {"o": "#1e3a1e", "L": "#6aa84e", "G": "#3f7a36", "D": "#2c5a2a", "t": "#5a3a20"}


def forest_tile():
    """Three trees crowded together on dark grass."""
    tile = speckled(7, "#3f6e34", ["#355e2c"], count=6)
    paint(tile, TREE, TREE_COLORS, 0, 0)
    paint(tile, TREE, TREE_COLORS, 8, 2)
    paint(tile, TREE, TREE_COLORS, 3, 8)
    return tile


def hills_tile():
    """Two grassy mounds, lit on the left."""
    tile = grass_tile(11)
    mound = ["...oooo...", "..oLLGGo..", ".oLLGGGDo.", "oLLGGGGDDo"]
    colors = {"o": "#34582a", "L": "#9ac470", "G": "#78a456", "D": "#5a8642"}
    paint(tile, mound, colors, 0, 4)
    paint(tile, mound, colors, 6, 10)
    return tile


def mountain_tile():
    """A grey peak with a snow cap, lit on the left."""
    tile = speckled(13, "#6f7f5c", ["#5f6e4e"], count=5)
    peak = [
        ".......o........",
        "......oWo.......",
        ".....oWWWo......",
        "....oWWLDDo.....",
        "....oLLLGDo.....",
        "...oLLLGGDDo....",
        "...oLLGGGDDo....",
        "..oLLLGGGDDDo...",
        "..oLLGGGGDDDo.o.",
        ".oLLLGGGGDDDDoWo",
        ".oLLGGGGGDDDDoDo",
        "oLLLGGGGGDDDDDDo",
        "oLLGGGGGGDDDDDDo",
        "oooooooooooooooo",
    ]
    return paint(tile, peak, {"o": "#2a2a34", "W": "#f0f2f8", "L": "#b8b8c4", "G": "#8a8a98", "D": "#5c5c6c"}, 0, 2)


def woarld_water_tile():
    """Open water, rippled with light crests."""
    tile = blank(color=rgba("#2c5c9c"))
    for row, offset in [(2, 1), (7, 5), (12, 2)]:
        for x in range(16):
            if (x + offset) % 8 in (0, 1, 2):
                tile[row][x] = rgba("#6a9ad8")
    return tile


def road_tile():
    """Packed dirt with pebbles."""
    return speckled(17, "#b8945e", ["#9a7a48", "#d0b07a"], count=12)


def sand_tile():
    return speckled(19, "#dcc88e", ["#c4ae74", "#f0e0ae"], count=10)


def bridge_tile():
    """Plank boards across the water, a rail along each side."""
    tile = woarld_water_tile()
    for y in range(2, 14):
        for x in range(16):
            tile[y][x] = rgba("#b07840" if x % 4 else "#6a4224")
    for x in range(16):
        tile[2][x] = tile[13][x] = rgba("#5a3a20")
        tile[3][x] = rgba("#8a5a2b")
    return tile


def cloud_tile(size=48):
    """Cloud cover over unexplored land: a haze of soft puffs, 3x3 tiles big so it tiles unseen.
    Each puff wraps round the edges, so the texture joins up with itself on every side."""
    tile = blank(size, size, rgba("#6e7890"))
    generator = random.Random(23)
    puffs = [(generator.randrange(size), generator.randrange(size), generator.randrange(5, 10)) for _ in range(16)]
    for layer, color in [(0, "#8a94aa"), (2, "#a2abbe")]:
        for cx, cy, radius in puffs:
            r = radius - layer
            for y in range(size):
                for x in range(size):
                    dx = min(abs(x - cx), size - abs(x - cx))
                    dy = min(abs(y - cy), size - abs(y - cy))
                    # Lit toward the top-left: the inner layer sits up and left of the puff's middle.
                    if (dx + layer / 2) ** 2 + (dy + layer / 2) ** 2 <= r * r if layer else dx * dx + dy * dy <= r * r:
                        tile[y][x] = rgba(color)
    return tile


def unknown_tile():
    """Parts Unknown: old dark parchment, cross-hatched."""
    tile = blank(color=rgba("#3a3226"))
    for y in range(16):
        for x in range(16):
            if (x + y) % 6 == 0 or (x - y) % 6 == 0:
                tile[y][x] = rgba("#4c4232")
    return tile


# Places, drawn over the tile they stand on. Halves mirrored.
PLACES = {
    "place_keep": sprite([
        "........",
        ".o.o.o..",
        ".ooooo..",
        ".oLLMo..",
        ".oLLMoo.",
        ".oLLMooo",
        ".oLLMLLM",
        ".oLLMLLM",
        ".oLLMLoo",
        ".oLLMLog",
        ".oLLMLog",
        ".oLLMLog",
        ".oooooog",
        "........",
        "........",
        "........",
    ], {"o": "#181420", "L": "#a8c4b4", "M": "#5f8a74", "g": "#2a1a10"}, mirror=True),
    "place_temple": sprite([
        "........",
        ".......o",
        ".....ooR",
        "...ooRRR",
        ".ooRRRRR",
        "oooooooo",
        ".oPo.oPo",
        ".oPo.oPo",
        ".oPo.oPo",
        ".oPo.oPo",
        ".oPo.oPo",
        "oooooooo",
        "~~~~~~~~",
        "w~~w~~~w",
        "........",
        "........",
    ], {"o": "#3a2418", "R": "#e0b49a", "P": "#ecdcbc", "~": "#3fa0b8", "w": "#c0f0ec"}, mirror=True),
    "place_inn": sprite([
        "........",
        "......oo",
        "....ooRR",
        "..ooRRRR",
        "ooRRRRRR",
        "oooooooo",
        ".oWWWWWW",
        ".oWyyoWW",
        ".oWyyoWW",
        ".oWWWWWo",
        ".oWWWWod",
        ".oWWWWod",
        ".ooooooo",
        "........",
        "........",
        "........",
    ], {"o": "#2a1a10", "R": "#a0423a", "W": "#c89a5e", "y": "#f0d060", "d": "#6a4224"}, mirror=True),
    "place_lock": sprite([
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "................",
        "...........ooo..",
        "..........o...o.",
        "..........o...o.",
        ".........ooooooo",
        ".........oyyyyyo",
        ".........oyyoyyo",
        ".........oyyyyyo",
        ".........ooooooo",
    ], {"o": "#181420", "y": "#e2c47a"}),
}


def build_woarld(directory):
    write_png(directory / "grass_a.png", grass_tile(1))
    write_png(directory / "grass_b.png", grass_tile(2))
    write_png(directory / "forest.png", forest_tile())
    write_png(directory / "hills.png", hills_tile())
    write_png(directory / "mountain.png", mountain_tile())
    write_png(directory / "water.png", woarld_water_tile())
    write_png(directory / "road.png", road_tile())
    write_png(directory / "bridge.png", bridge_tile())
    write_png(directory / "sand.png", sand_tile())
    write_png(directory / "cloud.png", cloud_tile())
    write_png(directory / "unknown.png", unknown_tile())
    for name, pixels in PLACES.items():
        write_png(directory / f"{name}.png", pixels)


# ---- Tavern tiles (16x16) -----------------------------------------------------------------

PLANK = ("#9a6a3c", "#b47e4a", "#6a4424", "#5a3a20")


def planks_tile(seed):
    """Wooden floor boards running across, with seams, staggered joints and a knot or two."""
    light, mid, seam, knot = (rgba(color) for color in (PLANK[1], PLANK[0], PLANK[2], PLANK[3]))
    tile = blank(color=mid)
    generator = random.Random(seed)
    for board in range(4):
        top = board * 4
        for x in range(16):
            tile[top][x] = light
            tile[top + 3][x] = seam
        joint = (board * 5 + seed * 3) % 16
        for y in range(top, top + 4):
            tile[y][joint] = seam
        x, y = generator.randrange(1, 15), top + generator.randrange(1, 3)
        tile[y][x] = knot
    return tile


def on_planks(art, palette, seed=1):
    return paint(planks_tile(seed), art, palette)


def tavern_wall_tile():
    """Timber-framed plaster: a dark beam along the bottom, uprights at the sides."""
    tile = blank(color=rgba("#d8c8a0"))
    for y in range(16):
        for x in range(16):
            if x in (0, 15) or y >= 12:
                tile[y][x] = rgba("#5a3a20")
            elif (x * 3 + y * 5) % 11 == 0:
                tile[y][x] = rgba("#c4b48c")
    for x in range(16):
        tile[12][x] = rgba("#3a2414")
    return tile


TAVERN_COLORS = {"o": "#2a1a10", "w": "#b07840", "d": "#8a5a2b", "l": "#d09a5a", "i": "#5a3a20",
                 "g": "#e2c47a", "r": "#a0423a", "R": "#c8604a", "f": "#f0a030", "F": "#f8e070", "s": "#5a5a68",
                 "S": "#8a8a98", "G": "#3a7a4a", "H": "#5a9a64", "p": "#d8d0bc", "b": "#3a2a50"}

TAVERN_TILES = {
    "bar": ["oooooooooooooooo", "llllllllllllllll", "wwwwwwwwwwwwwwww", "oooooooooooooooo",
            "dddddddddddddddd", "dwwddwwddwwddwwd", "dwwddwwddwwddwwd", "dwwddwwddwwddwwd",
            "dwwddwwddwwddwwd", "dwwddwwddwwddwwd", "dwwddwwddwwddwwd", "dddddddddddddddd",
            "dddddddddddddddd", "iiiiiiiiiiiiiiii", "oooooooooooooooo", "................"],
    "barrel": [
            "................", "....oooooooo....", "...oddddddddo...", "..odwwwwwwwwdo..",
            "..odwwwwwwwwdo..", "..oiiiiiiiiiio..", "..odwwwwwwwwdo..", "..odwwwwwwwwdo..",
            "..odwwwwwwwwdo..", "..odwwwwwwwwdo..", "..oiiiiiiiiiio..", "..odwwwwwwwwdo..",
            "...oddddddddo...", "....oooooooo....", "................", "................"],
    "hearth": ["ssssssssssssssss", "sSSSsSSSSsSSSSss", "ssssssssssssssss", "sSsoooooooooosSs",
               "sSo..........oSs", "sso..........oss", "sSo....f.....oSs", "sso...fFf..f.oss",
               "sSo..fFFfffFfoSs", "sso.fFFFFFFFfoss", "sSo.fFfFFfFFfoSs", "sso.iiiiiiiiioss",
               "sSooooooooooooSs", "ssssssssssssssss", "sSSSsSSSSsSSSSss", "ssssssssssssssss"],
    "stairs": ["oooooooooooooooo", "illllllllllllllo", "iddddddddddddddo", "oooooooooooooooo",
               "illllllllllllllo", "iwwwwwwwwwwwwwwo", "oooooooooooooooo", "illllllllllllllo",
               "iwwwwwwwwwwwwwwo", "oooooooooooooooo", "illllllllllllllo", "iwwwwwwwwwwwwwwo",
               "oooooooooooooooo", "illllllllllllllo", "iwwwwwwwwwwwwwwo", "oooooooooooooooo"],
    "door": ["oooooooooooooooo", "oddddddddddddddo", "odwwwwwwwwwwwwdo", "odwddddwwddddwdo",
             "odwdwwdwwdwwdwdo", "odwdwwdwwdwwdwdo", "odwddddwwddddwdo", "odwwwwwwwwwwwgdo",
             "odwwwwwwwwwwwgdo", "odwddddwwddddwdo", "odwdwwdwwdwwdwdo", "odwdwwdwwdwwdwdo",
             "odwddddwwddddwdo", "odwwwwwwwwwwwwdo", "oddddddddddddddo", "oooooooooooooooo"],
}

# Furniture stands on the planks.
TAVERN_FURNITURE = {
    "stool": ["................", "................", "................", "................",
              ".....oooooo.....", "....odwwwwdo....", "....oddddddo....", ".....oooooo.....",
              ".....o....o.....", ".....o....o.....", ".....o....o.....", "....oo....oo....",
              "................", "................", "................", "................"],
    "table": ["................", "...oooooooooo...", "..owwlllllwwwo..", ".owwllwwwwwwwwo.",
              ".owlwwwwwwwwwwo.", ".owwwwwwwwwwwwo.", ".owwwwwwwwwwwwo.", ".owwwwwwwwwwwdo.",
              ".odwwwwwwwwwwdo.", "..odwwwwwwwwdo..", "...oddddddddo...", "....oooooooo....",
              ".....oi..io.....", ".....oi..io.....", "....ooo..ooo....", "................"],
    "dice_table": ["................", "...oooooooooo...", "..odGGGGGGGGdo..", ".odGHGGGGGGGGdo.",
                   ".odGGGppGGGGGdo.", ".odGGGpbGGpGGdo.", ".odGGGGGGGbpGdo.", ".odGGGGGGGGGGdo.",
                   ".odGGGGGGGGGGdo.", "..odGGGGGGGGdo..", "...oddddddddo...", "....oooooooo....",
                   ".....oi..io.....", ".....oi..io.....", "....ooo..ooo....", "................"],
    "desk": [
            "oooooooooooooooo", "ollllllllllllllo", "owwwwwwwwwwppwwo", "owwwwwwwwwpbpwwo",
            "oooooooooooooooo", "oddddddddddddddo", "odwwwwddwwwwwddo", "odwwwwddwwwwwddo",
            "odwwwwddwwwwwddo", "odwwwwddwwwwwddo", "oddddddddddddddo", "odwwwwddwwwwwddo",
            "odwwwwddwwwwwddo", "oddddddddddddddo", "oooooooooooooooo", "................"],
    "stall": [
            "rRrRrRrRrRrRrRrR", "RrRrRrRrRrRrRrRr", "oooooooooooooooo", "..o..........o..",
            "..o..........o..", "oooooooooooooooo", "owSwgwrwSwgwSwwo", "oooooooooooooooo",
            "oddddddddddddddo", "odwwwwwwwwwwwwdo", "odwwwwwwwwwwwwdo", "odwwwwwwwwwwwwdo",
            "odwwwwwwwwwwwwdo", "oddddddddddddddo", "oooooooooooooooo", "................"],
    "rug": ["rrrrrrrrrrrrrrrr", "rggggggggggggggr", "rgRRRRRRRRRRRRgr", "rgRrrrrrrrrrrRgr",
            "rgRrRRRRRRRRrRgr", "rgRrRggggggRrRgr", "rgRrRgrrrrgRrRgr", "rgRrRgrRRrgRrRgr",
            "rgRrRgrRRrgRrRgr", "rgRrRgrrrrgRrRgr", "rgRrRggggggRrRgr", "rgRrRRRRRRRRrRgr",
            "rgRrrrrrrrrrrRgr", "rgRRRRRRRRRRRRgr", "rggggggggggggggr", "rrrrrrrrrrrrrrrr"],
}


def build_tavern(directory):
    write_png(directory / "planks_a.png", planks_tile(1))
    write_png(directory / "planks_b.png", planks_tile(2))
    write_png(directory / "wall.png", tavern_wall_tile())
    for name, rows in TAVERN_TILES.items():
        write_png(directory / f"{name}.png", on_planks(rows, TAVERN_COLORS))
    for name, rows in TAVERN_FURNITURE.items():
        write_png(directory / f"{name}.png", on_planks(rows, TAVERN_COLORS))


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


def laugh():
    """A big, low "HAR HAR HAR!": three buzzing syllables, each falling in pitch, the last held longest."""
    samples = []
    for start, length in [(150, 0.17), (138, 0.17), (126, 0.34)]:
        voice = tone(lambda t, f=start: f * (1 - 0.35 * t) + 6 * math.sin(t * 70), length, square, 0.2, release=0.06)
        breath = noise(length, 0.12, decay=6, seed=int(start), lowpass=0.6)
        samples += mix(voice, breath) + [0.0] * int(RATE * 0.05)
    return samples


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
        "laugh": laugh(),
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


def temple_theme():
    """The Sunken Temple: a slow, watery D minor with rippling arpeggios."""
    chords = ["Dm", "Dm", "A#", "C", "Dm", "Dm", "Gm", "A"] * 2
    song = Song(bpm=92, beats=64)
    song.voice("A4/2 D5/1 E5/1 | F5/3 E5/1 | D5/1.5 C5/0.5 A#4/2 | C5/2 E5/2 |"
               "F5/1 E5/1 D5/1 A4/1 | D5/3 -/1 | G5/1.5 F5/0.5 E5/1 D5/1 | C#5/2 E5/2 |"
               "A5/2 G5/1 F5/1 | E5/1 D5/1 E5/2 | F5/1.5 E5/0.5 D5/1 A#4/1 | C5/2 G5/2 |"
               "F5/1 A5/1 G5/1 F5/1 | D5/3 -/1 | A#4/1 D5/1 G5/1 F5/1 | E5/2 C#5/1 E5/1",
               triangle, 0.16, vibrato=True)
    song.arpeggio(chords, [0, 1, 2, 3, 2, 1] * 2 + [0, 1, 2, 3], sine, 0.05, step=0.25)
    song.bass(chords, [(0, 1.5), (7, 0.5), (12, 1), (7, 1)], 0.18)
    song.drums(16, "k.......h...k..h", 0.07)
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


OGRE_BEAT = [  # a heavy stomp: kick on the beat, a snare on two and four, toms rolling into each new phrase
    "c...s...k.k.s...", "k...s...k.k.s...", "k...s...k.k.s...", "k...s...k.k.s.ss",
    "c...s...k.k.s...", "k...s...k.k.s...", "k...s...k.k.s...", "k.k.s.k.s.s.sskk",
    "c...s...k.k.s...", "k...s...k.k.s...", "k...s...k.k.s...", "k...s...k.k.s.ss",
    "c...s...k.k.s...", "k...s...k.k.s...", "k...s...k.k.s...", "s.s.s.s.ssssskkk",
]


def ogre_theme():
    """The Ogre Champ's den: a brutish C minor march, chugging bass and a stomping beat."""
    chords = ["Cm", "Cm", "G#", "G", "Cm", "Cm", "F", "G", "G#", "A#", "Cm", "Cm", "F", "G#", "G", "G"]
    song = Song(bpm=150, beats=64)
    melody = ("C5/1 C5/0.5 D#5/0.5 F5/1 F#5/1 | G5/1.5 F5/0.5 D#5/1 C5/1 | G#5/1 G5/1 F5/1 D#5/1 | D5/2 G4/2 |"
              "C5/1 C5/0.5 D#5/0.5 F5/1 F#5/1 | G5/1 A#5/1 G5/1 F5/1 | D#5/1 F5/1 D5/1 B4/1 | C5/2 -/2 |"
              "G#5/1.5 G5/0.5 G#5/1 A#5/1 | C6/2 A#5/1 G#5/1 | G5/1.5 F5/0.5 D#5/1 F5/1 | G5/2 C5/2 |"
              "F5/1 G#5/1 C6/1 A#5/1 | G#5/1 G5/1 F5/1 D#5/1 | D5/1 D#5/1 F5/1 G5/1 | G5/2 B4/2")
    song.voice(melody, pulse(0.5), 0.09, transpose=-12)
    song.voice(melody, pulse(0.25), 0.05)
    song.bass(chords, [(0, 0.5), (0, 0.5), (12, 0.5), (0, 0.5), (0, 0.5), (0, 0.5), (12, 0.5), (10, 0.5)], 0.22)
    song.arpeggio(chords, [0, 2, 1, 2] * 4, pulse(0.125), 0.025, step=0.25)
    for bar, pattern in enumerate(OGRE_BEAT):
        song.drum_bar(bar, pattern, 0.15)
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
        "temple": temple_theme(),
        "boss": boss_theme(),
        "ogre": ogre_theme(),
        "victory": victory_tune(),
        "defeat": defeat_tune(),
    }
    for name, samples in songs.items():
        write_wav(directory / f"{name}.wav", samples)


# ---- Build --------------------------------------------------------------------------------

def build_theme(name, monsters, colors, floors):
    """A theme's folder: its monsters' sprites, objects and tiles in its colors."""
    theme = ASSETS / "themes" / name
    for monster, pixels in monsters.items():
        write_png(theme / "monsters" / f"{monster}.png", pixels)
    for item, pixels in OBJECTS.items():
        write_png(theme / "tiles" / f"{item}.png", pixels)

    write_png(theme / "tiles" / "floor_a.png", floor_tile(1, *floors[0]))
    write_png(theme / "tiles" / "floor_b.png", floor_tile(2, *floors[1]))
    write_png(theme / "tiles" / "wall.png", wall_tile(colors))
    write_png(theme / "tiles" / "cracked_wall.png", cracked_wall_tile(colors))
    write_png(theme / "tiles" / "spikes.png", spikes_tile())
    write_png(theme / "tiles" / "trapdoor.png", trapdoor_tile())
    write_png(theme / "tiles" / "pit.png", pit_tile(colors))
    write_png(theme / "tiles" / "stairs.png", stairs_tile(colors))
    write_png(theme / "tiles" / "stairs_up.png", stairs_up_tile(colors))
    write_png(theme / "tiles" / "rubble.png", rubble_tile(colors))
    write_png(theme / "tiles" / "gate.png", gate_tile())
    write_png(theme / "tiles" / "water_shallow.png", water_tile(False, colors))
    write_png(theme / "tiles" / "water_deep.png", water_tile(True, colors))
    write_png(theme / "tiles" / "exit_open.png", exit_tile(sealed=False))
    write_png(theme / "tiles" / "exit_sealed.png", exit_tile(sealed=True))


def main():
    build_theme("emerald_keep", MONSTERS, KEEP_TILES,
                [("#6fa48a", "#5a8f78", "#3d6555", "#4f7f6a"), ("#68a084", "#548a72", "#3a6152", "#4a7863")])
    build_theme("sunken_temple", {**{name: art for name, art in MONSTERS.items() if name not in KEEP_ONLY}, **TEMPLE_MONSTERS}, TEMPLE_TILES,
                [("#c9b48e", "#b39d78", "#7d6a4e", "#9c8664"), ("#c4ae88", "#ad9772", "#78664a", "#968060")])

    heroes = ASSETS / "heroes"
    for look in LOOKS:
        write_png(heroes / "looks" / f"{look['id']}.png", look_strip(look))
    write_look_list(ROOT / "scenes" / "ui" / "Looks.Generated.cs")

    ui = ASSETS / "ui"
    write_png(ui / "frame.png", frame("#b08d4a", "#5a4424", "#1a1626"))
    write_png(ui / "frame_hover.png", frame("#e2c47a", "#8a6a34", "#2a2338"))
    write_png(ui / "frame_pressed.png", frame("#e2c47a", "#8a6a34", "#3a2f4d"))
    write_png(ui / "frame_disabled.png", frame("#4a4030", "#2a2418", "#120f1c"))
    write_png(ui / "heart_full.png", sprite(HEART_ROWS, {"o": "#2a0a10", "r": "#d8434a", "l": "#ffb0a0"}))
    write_png(ui / "heart_empty.png", sprite(HEART_ROWS, {"o": "#2a0a10", "r": "#3a2a30", "l": "#4a3a40"}))
    for name, pixels in ICONS.items():
        write_png(ui / f"{name}.png", pixels)

    build_woarld(ASSETS / "woarld")
    build_tavern(ASSETS / "tavern")
    build_font(ASSETS / "fonts", "woad_gothic")
    build_sounds(ASSETS / "audio")
    build_music(ASSETS / "audio" / "music")
    print(f"Assets written to {ASSETS}")


if __name__ == "__main__":
    main()
