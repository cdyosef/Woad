"""Pictures for the cards: one 32x32 pixel-art picture for every item, drawn with a few shapes and an
outline, in the same style as the rest of the game's art. build-assets.py calls build_cards(); run this
file on its own to redraw just the cards (and a contact sheet to look them over)."""

import importlib.util
import math
from pathlib import Path

SIZE = 32
OUTLINE = "#181420"

# Materials, light to dark.
STEEL = ("#e8eef2", "#b8c4cc", "#7a8892")
IRON = ("#a0a8b0", "#6c747c", "#444a52")
GOLD = ("#fff0a0", "#f0c94a", "#a87a20")
BRASS = ("#f0d890", "#c8a048", "#8a6a28")
WOOD = ("#c8955a", "#9a6a3a", "#6a4424")
LEATHER = ("#b07848", "#8a5430", "#5a3418")
CLOTH = ("#f0e0a0", "#d8b860", "#a08030")
PARCHMENT = ("#fff4d8", "#ead6a4", "#c4a670")
STONE = ("#b0a898", "#888070", "#5a5448")
ICE = ("#f0fcff", "#a8ecff", "#5ab4d8")
RED = ("#ff9a90", "#d8434a", "#8a1e28")
GREEN = ("#a8eca0", "#4cc85a", "#1e7a3a")
BLUE = ("#b0d8ff", "#4a8ad8", "#24508a")
BLACK = ("#5a5868", "#34303e", "#1e1a26")
WHITE = ("#ffffff", "#e8e4f0", "#b4b0c4")
FLAME = ("#fff2a0", "#ffb03a", "#e0502a")

# What each element's scroll seal and runes look like (the dice colors).
ELEMENTS = {
    "flame": "#ff7a3a", "frost": "#a8ecff", "venom": "#7fd04a", "acid": "#d4ec3a", "grave": "#8f78b0",
    "gale": "#6fa8dc", "sun": "#fff2b0", "bloom": "#3fbf62", "quake": "#b08850", "spark": "#ffe03a",
    "mind": "#e878e8", "shadow": "#5a5878",
}


def rgba(color):
    color = color.lstrip("#")
    return (int(color[0:2], 16), int(color[2:4], 16), int(color[4:6], 16), 255)


class Canvas:
    """A 32x32 picture: pixels set by shapes, then outlined in one pass."""

    def __init__(self):
        self.px = [[None] * SIZE for _ in range(SIZE)]

    def dot(self, x, y, color):
        x, y = int(round(x)), int(round(y))
        if 0 <= x < SIZE and 0 <= y < SIZE:
            self.px[y][x] = color

    def rect(self, x, y, w, h, color):
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                self.dot(xx, yy, color)

    def line(self, x0, y0, x1, y1, color, width=1):
        steps = max(abs(x1 - x0), abs(y1 - y0), 1) * 2
        for i in range(int(steps) + 1):
            t = i / steps
            x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
            for ox in range(width):
                for oy in range(width):
                    self.dot(x + ox - (width - 1) / 2, y + oy - (width - 1) / 2, color)

    def poly(self, points, color):
        ys = [p[1] for p in points]
        for y in range(int(min(ys)), int(max(ys)) + 1):
            for x in range(SIZE):
                if self._inside(x + 0.5, y + 0.5, points):
                    self.dot(x, y, color)

    @staticmethod
    def _inside(x, y, points):
        inside = False
        for i in range(len(points)):
            (x0, y0), (x1, y1) = points[i], points[(i + 1) % len(points)]
            if (y0 > y) != (y1 > y) and x < (x1 - x0) * (y - y0) / (y1 - y0) + x0:
                inside = not inside
        return inside

    def circle(self, cx, cy, r, color):
        for y in range(SIZE):
            for x in range(SIZE):
                if (x + 0.5 - cx) ** 2 + (y + 0.5 - cy) ** 2 <= r * r:
                    self.dot(x, y, color)

    def ring(self, cx, cy, r, color, width=1.2):
        for y in range(SIZE):
            for x in range(SIZE):
                d = math.hypot(x + 0.5 - cx, y + 0.5 - cy)
                if r - width <= d <= r:
                    self.dot(x, y, color)

    def outline(self, color=OUTLINE):
        filled = [[c is not None for c in row] for row in self.px]
        for y in range(SIZE):
            for x in range(SIZE):
                if not filled[y][x] and any(
                        0 <= x + dx < SIZE and 0 <= y + dy < SIZE and filled[y + dy][x + dx]
                        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                    self.px[y][x] = color

    def pixels(self):
        return [[(0, 0, 0, 0) if c is None else rgba(c) for c in row] for row in self.px]


# ---- Pieces ------------------------------------------------------------------------------------

def blade(c, x0, y0, x1, y1, metal, width=2):
    """A blade from its hilt (x0, y0) to its point, lit along one edge."""
    c.line(x0, y0, x1, y1, metal[1], width)
    c.line(x0 - 1, y0 - 1, x1 - 1, y1, metal[0], 1)


def hilt(c, x, y, guard, grip, pommel, guard_size=4):
    """A crossguard across a blade pointing up-right from (x, y), then the grip and pommel down-left."""
    c.line(x - guard_size, y - guard_size, x + guard_size, y + guard_size, guard[1], 2)
    c.line(x - 1, y + 1, x - 5, y + 5, grip[1], 2)
    c.circle(x - 6, y + 6, 1.8, pommel[1])


def haft(c, x0, y0, x1, y1, wood=WOOD, width=2):
    c.line(x0, y0, x1, y1, wood[1], width)
    c.line(x0, y0 - 1, x1, y1 - 1, wood[0], 1)


# Each element's rune on its scroll: 7x7, "#" inked in the element's color, "+" in a darker shade.
RUNES = {
    "flame": ["...#...", "..##...", "..###..", ".#####.", ".##+##.", "##+++##", ".#####."],
    "frost": ["...#...", ".#.#.#.", "..###..", "#######", "..###..", ".#.#.#.", "...#..."],
    "venom": ["..###..", ".#####.", "##+#+##", "#######", ".#####.", "..#.#..", "..#.#.."],
    "acid": ["...#...", "...#...", "..###..", ".##+##.", ".#+++#.", ".##+##.", "..###.."],
    "grave": [".#####.", "#######", "#+##+##", "#######", ".##.##.", ".#####.", ".#.#.#."],
    "gale": [".####..", "#....#.", "..##..#", ".#..#.#", ".#.##.#", "..#...#", "...###."],
    "sun": ["#..#..#", ".#.#.#.", "..###..", "###+###", "..###..", ".#.#.#.", "#..#..#"],
    "bloom": ["....##.", "...###.", "..####.", ".####..", ".#+#...", "#.#....", "#......"],
    "quake": ["#......", ".#...#.", "..#.#.#", "...#..#", "..#....", ".#.....", "###...."],
    "spark": ["...###.", "..###..", ".###...", "#####..", "..###..", ".##....", ".#....."],
    "mind": [".#####.", "#.....#", "#.###.#", "#.#+#.#", "#.##..#", "#.....#", ".######"],
    "shadow": ["..###..", ".##....", "##.....", "##.....", "##.....", ".##....", "..###.."],
}


def scroll(c, element):
    """A rolled parchment, tied with its element's seal, its rune inked across the middle."""
    seal = ELEMENTS[element]
    c.rect(7, 7, 18, 18, PARCHMENT[1])
    c.rect(7, 7, 18, 2, PARCHMENT[0])
    c.circle(7, 16, 3.2, PARCHMENT[2])
    c.circle(25, 16, 3.2, PARCHMENT[2])
    dark = "#" + "".join(f"{max(0, int(seal[k:k + 2], 16) - 70):02x}" for k in (1, 3, 5))
    for y, row in enumerate(RUNES[element]):
        for x, mark in enumerate(row):
            if mark != ".":
                c.dot(13 + x, 10 + y, dark if mark == "+" else seal)
    c.circle(16, 23, 2.6, seal)


def potion(c, glass, liquid, round_body=True):
    if round_body:
        c.circle(16, 20, 8, liquid[1])
        c.circle(16, 20, 8, liquid[1])
        c.rect(9, 13, 14, 4, glass)
        c.circle(13, 17, 2.2, liquid[0])
    c.rect(13, 6, 6, 6, glass)
    c.rect(12, 4, 8, 3, WOOD[1])


def helmet(c, metal, x=16, y=17):
    c.circle(x, y, 9, metal[1])
    c.rect(x - 9, y, 19, 9, None)
    for yy in range(y, y + 9):
        for xx in range(x - 9, x + 10):
            c.px[yy][xx] = None if 0 <= yy < SIZE and 0 <= xx < SIZE else None
    c.rect(x - 10, y, 21, 3, metal[2])
    c.circle(x - 3, y - 4, 2.4, metal[0])


# ---- The items -------------------------------------------------------------------------------

def sword(c, metal, guard, grip, pommel, length=1.0, width=2):
    tip = (6 + 20 * length, 26 - 20 * length)
    blade(c, 11, 21, tip[0], tip[1], metal, width)
    hilt(c, 11, 21, guard, grip, pommel)


def draw(item):
    c = Canvas()
    i = item
    if i == "wooden_sword":
        sword(c, WOOD, WOOD, LEATHER, WOOD, 0.85)
    elif i == "longsword":
        sword(c, STEEL, IRON, LEATHER, IRON, 1.1)
    elif i == "knights_sword":
        sword(c, STEEL, GOLD, BLUE, GOLD, 1.05)
        c.dot(11, 21, BLUE[0])
    elif i == "rapier":
        blade(c, 11, 21, 28, 4, STEEL, 1)
        c.ring(11, 21, 4.5, GOLD[1], 1.4)
        c.line(10, 22, 6, 26, LEATHER[1], 2)
        c.circle(5, 27, 1.6, GOLD[1])
    elif i == "frost_brand":
        sword(c, ICE, STEEL, BLUE, ICE, 1.05)
        for x, y in ((17, 11), (22, 8), (14, 17)):
            c.dot(x + 2, y, "#ffffff")
            c.dot(x + 3, y - 1, ICE[0])
    elif i == "dagger":
        blade(c, 13, 19, 24, 8, STEEL, 2)
        hilt(c, 13, 19, IRON, LEATHER, IRON, 3)
    elif i == "trident":
        haft(c, 5, 27, 22, 10)
        c.line(19, 13, 27, 5, STEEL[1], 2)
        c.line(17, 9, 23, 3, STEEL[1], 2)
        c.line(23, 15, 29, 9, STEEL[1], 2)
        c.line(17, 9, 23, 15, STEEL[2], 2)
    elif i == "spear":
        haft(c, 4, 28, 21, 11)
        c.poly([(20, 12), (24, 4), (28, 3), (27, 8), (21, 13)], STEEL[1])
        c.line(22, 10, 26, 5, STEEL[0])
    elif i == "great_axe":
        haft(c, 6, 28, 22, 6)
        c.poly([(14, 8), (24, 3), (28, 12), (20, 18)], STEEL[1])
        c.poly([(20, 14), (26, 7), (27, 12)], STEEL[0])
        c.poly([(16, 12), (10, 8), (12, 16)], STEEL[2])
    elif i == "war_hammer":
        haft(c, 6, 28, 20, 10)
        c.poly([(15, 9), (21, 3), (28, 10), (22, 16)], IRON[1])
        c.line(18, 6, 24, 12, IRON[0])
    elif i == "maul":
        haft(c, 6, 28, 19, 12, WOOD, 3)
        c.poly([(12, 9), (20, 1), (30, 11), (22, 19)], IRON[1])
        c.poly([(14, 9), (20, 3), (22, 5), (16, 11)], IRON[0])
    elif i == "quake_hammer":
        haft(c, 6, 28, 20, 11)
        c.poly([(13, 9), (21, 2), (29, 10), (21, 17)], STONE[1])
        c.line(19, 6, 22, 10, STONE[2])
        c.line(22, 10, 20, 13, STONE[2])
        c.line(16, 9, 18, 7, STONE[0])
    elif i in ("short_bow", "longbow"):
        tall = i == "longbow"
        top, bottom = (3, 29) if tall else (6, 26)
        for y in range(top, bottom):
            t = (y - top) / (bottom - top)
            x = 10 + int(8 * math.sin(t * math.pi))
            c.dot(x, y, WOOD[1])
            c.dot(x + 1, y, WOOD[2] if tall else WOOD[1])
        c.line(10, top, 10, bottom - 1, WHITE[1])
        c.rect(17, 14, 2, 4, LEATHER[1])
    elif i in ("crossbow", "heavy_crossbow"):
        heavy = i == "heavy_crossbow"
        stock = IRON if heavy else WOOD
        c.line(16, 8, 16, 29, stock[1], 3)
        for x in range(4, 29):
            t = (x - 4) / 24
            c.dot(x, 11 - int(4 * math.sin(t * math.pi)), (IRON if heavy else WOOD)[2])
        c.line(4, 11, 28, 11, WHITE[1])
        c.line(16, 6, 16, 11, STEEL[0], 1)
        if heavy:
            c.rect(14, 18, 5, 3, IRON[0])
    elif i == "boomerang":
        c.line(5, 22, 15, 8, WOOD[1], 3)
        c.line(15, 8, 27, 18, WOOD[1], 3)
        c.line(6, 21, 15, 9, WOOD[0])
        c.dot(15, 10, RED[1])
    elif i == "buckler":
        c.circle(16, 16, 11, WOOD[1])
        c.ring(16, 16, 11, IRON[1], 1.6)
        c.circle(16, 16, 3.5, IRON[0])
    elif i == "tower_shield":
        c.rect(8, 3, 16, 26, IRON[1])
        c.rect(10, 5, 12, 22, IRON[2])
        c.rect(15, 5, 2, 22, IRON[0])
        c.rect(10, 15, 12, 2, IRON[0])
    elif i == "knights_shield":
        c.poly([(6, 4), (26, 4), (26, 15), (16, 29), (6, 15)], BLUE[1])
        c.rect(14, 6, 4, 17, GOLD[1])
        c.rect(9, 11, 14, 4, GOLD[1])
        c.line(7, 5, 7, 14, BLUE[0])
    elif i == "leather_cap":
        c.circle(16, 18, 10, LEATHER[1])
        c.rect(5, 18, 23, 12, None)
        _clear_below(c, 18)
        c.rect(5, 17, 22, 3, LEATHER[2])
        c.circle(12, 13, 2.5, LEATHER[0])
    elif i == "diving_helm":
        c.circle(16, 16, 12, BRASS[1])
        c.circle(16, 16, 6, BLUE[1])
        c.ring(16, 16, 6.5, BRASS[2], 1.5)
        c.circle(14, 14, 1.8, BLUE[0])
        c.rect(6, 25, 20, 3, BRASS[2])
    elif i == "lucky_helmet":
        c.circle(16, 18, 10, IRON[1])
        _clear_below(c, 19)
        c.rect(5, 18, 22, 3, IRON[2])
        clover(c, 16, 12, 2.2)
    elif i == "cowards_cloak":
        c.poly([(16, 3), (25, 10), (28, 29), (4, 29), (7, 10)], CLOTH[1])
        c.poly([(12, 9), (20, 9), (19, 17), (13, 17)], CLOTH[2])
        c.line(16, 18, 16, 28, CLOTH[2])
        c.line(9, 12, 7, 27, CLOTH[0])
    elif i in ("leather_armor", "studded_armor", "chain_mail", "wardens_plate", "storm_mail"):
        material = {"leather_armor": LEATHER, "studded_armor": LEATHER, "chain_mail": IRON, "wardens_plate": STEEL, "storm_mail": BLUE}[i]
        c.poly([(9, 4), (13, 4), (16, 7), (19, 4), (23, 4), (28, 9), (25, 13), (24, 28), (8, 28), (7, 13), (4, 9)], material[1])
        c.line(16, 8, 16, 27, material[2])
        if i == "studded_armor":
            for x, y in ((11, 13), (21, 13), (11, 19), (21, 19), (13, 24), (19, 24)):
                c.dot(x, y, IRON[0])
        if i == "chain_mail":
            for y in range(9, 27, 2):
                for x in range(9 + (y // 2) % 2, 24, 2):
                    c.dot(x, y, IRON[0])
        if i == "wardens_plate":
            c.rect(9, 16, 14, 1, STEEL[2])
            for x, y in ((10, 9), (22, 9), (10, 25), (22, 25)):
                c.dot(x, y, GOLD[1])
        if i == "storm_mail":
            c.line(18, 9, 13, 18, "#ffe03a", 1)
            c.line(13, 18, 19, 18, "#ffe03a", 1)
            c.line(19, 18, 14, 27, "#ffe03a", 1)
        c.line(10, 6, 8, 26, material[0])
    elif i == "dash_boots":
        c.poly([(12, 5), (20, 5), (20, 20), (27, 23), (27, 28), (10, 28), (10, 20)], LEATHER[1])
        c.rect(10, 25, 18, 3, LEATHER[2])
        c.rect(12, 5, 8, 3, LEATHER[0])
        for y in (11, 15, 19):
            c.line(2, y, 7, y, WHITE[1])
    elif i.endswith("_scroll"):
        scroll(c, i.removesuffix("_scroll"))
    elif i == "frost_wand":
        c.line(6, 27, 20, 12, WOOD[1], 2)
        c.poly([(19, 13), (23, 3), (27, 13), (23, 16)], ICE[1])
        c.line(23, 5, 23, 14, ICE[0])
        c.dot(26, 6, "#ffffff")
    elif i == "torch":
        c.line(10, 28, 18, 14, WOOD[1], 3)
        c.rect(15, 12, 6, 4, LEATHER[1])
        c.poly([(14, 12), (17, 2), (19, 6), (22, 3), (22, 12)], FLAME[2])
        c.poly([(16, 12), (18, 5), (20, 12)], FLAME[1])
        c.dot(18, 10, FLAME[0])
    elif i == "lockpicks":
        # A ring with two picks hanging from it, hooked and raked at the tips.
        c.ring(9, 8, 5, IRON[1], 1.5)
        c.line(11, 12, 15, 27, STEEL[1], 1)
        c.line(15, 27, 17, 25, STEEL[1], 1)
        c.line(13, 11, 24, 24, STEEL[1], 1)
        c.line(24, 24, 25, 22, STEEL[1], 1)
        c.line(23, 23, 24, 21, STEEL[1], 1)
        c.rect(10, 12, 3, 4, LEATHER[1])
        c.rect(13, 12, 4, 3, LEATHER[1])
    elif i == "bomb":
        c.circle(15, 19, 9, BLACK[1])
        c.circle(12, 16, 2.5, BLACK[0])
        c.rect(19, 9, 4, 4, IRON[1])
        c.line(22, 9, 26, 4, WOOD[1])
        c.circle(27, 3, 1.8, FLAME[1])
    elif i == "autosmith_hammer":
        # A smith's hammer with a brass cog on its head: it mends things by itself.
        haft(c, 8, 28, 16, 14)
        c.poly([(9, 12), (21, 5), (25, 11), (13, 18)], IRON[1])
        c.poly([(9, 12), (21, 5), (22, 7), (10, 14)], IRON[0])
        c.circle(23, 22, 5, BRASS[1])
        for k in range(8):
            a = k * math.pi / 4
            c.dot(23 + 6 * math.cos(a), 22 + 6 * math.sin(a), BRASS[1])
        c.circle(23, 22, 1.8, BRASS[2])
    elif i == "eye_drops":
        c.rect(12, 12, 8, 14, BLUE[0])
        c.rect(12, 18, 8, 8, BLUE[1])
        c.rect(14, 6, 4, 6, WHITE[1])
        c.circle(16, 5, 2.5, RED[1])
        c.dot(16, 29, BLUE[1])
    elif i == "holy_water":
        c.poly([(13, 4), (19, 4), (19, 10), (25, 16), (25, 28), (7, 28), (7, 16), (13, 10)], WHITE[0])
        c.rect(8, 17, 16, 10, BLUE[0])
        c.rect(15, 15, 2, 10, GOLD[1])
        c.rect(12, 18, 8, 2, GOLD[1])
        c.rect(12, 2, 8, 3, WOOD[1])
    elif i == "health_potion":
        potion(c, WHITE[0], RED)
    elif i == "loose_coins":
        for x, y in ((9, 22), (17, 24), (23, 20), (13, 16), (20, 14)):
            c.circle(x, y, 4, GOLD[1])
            c.dot(x - 1, y - 1, GOLD[0])
            c.dot(x + 1, y + 1, GOLD[2])
    elif i == "coin_pouch":
        c.poly([(9, 12), (23, 12), (27, 22), (23, 29), (9, 29), (5, 22)], LEATHER[1])
        c.poly([(11, 6), (21, 6), (19, 12), (13, 12)], LEATHER[0])
        c.rect(11, 11, 10, 2, GOLD[2])
        c.circle(16, 22, 3.5, GOLD[1])
    elif i == "emerald":
        c.poly([(10, 6), (22, 6), (27, 13), (16, 28), (5, 13)], GREEN[1])
        c.poly([(10, 6), (22, 6), (19, 13), (13, 13)], GREEN[0])
        c.line(5, 13, 27, 13, GREEN[2])
    elif i == "pearl":
        c.circle(16, 17, 9, WHITE[1])
        c.circle(13, 14, 3, WHITE[0])
        c.ring(16, 17, 9, WHITE[2], 1.3)
    elif i == "golden_idol":
        c.circle(16, 8, 5, GOLD[1])
        c.poly([(10, 12), (22, 12), (24, 26), (8, 26)], GOLD[1])
        c.rect(7, 26, 18, 3, GOLD[2])
        c.dot(14, 8, GOLD[2])
        c.dot(18, 8, GOLD[2])
        c.line(11, 14, 10, 24, GOLD[0])
    else:
        raise ValueError(f"No picture for '{item}'")

    c.outline()
    return c.pixels()


# ---- Ability cards (the hands under each dice pool) and new accessories ------------------------

SKIN = ("#f4c8a0", "#d89a6a", "#a06a40")
MOTION = "#e8e4f0"


def fist(c, x, y, skin=SKIN):
    """A clenched fist, knuckles to the right."""
    c.rect(x, y, 10, 9, skin[1])
    for k in range(4):
        c.rect(x + 8, y + k * 2 + 1, 3, 2, skin[1] if k % 2 else skin[0])
    c.rect(x + 2, y + 7, 5, 3, skin[2])
    c.rect(x - 5, y + 2, 6, 6, LEATHER[1])


def ability(c, name):
    if name == "fist":
        fist(c, 10, 11)
        for y in (9, 15, 21):
            c.line(2, y, 7, y, MOTION)
    elif name == "shove":
        fist(c, 4, 6)
        fist(c, 4, 17)
        c.poly([(22, 10), (29, 16), (22, 22)], RED[1])
        c.rect(17, 14, 6, 4, RED[1])
    elif name == "wend":
        for x, y in ((7, 24), (13, 17), (19, 22), (24, 12)):
            c.circle(x, y, 2.6, LEATHER[1])
            c.dot(x - 1, y - 3, LEATHER[0])
            c.dot(x + 1, y - 3, LEATHER[0])
    elif name == "leap":
        for k in range(14):
            t = k / 13
            c.dot(4 + 22 * t, 25 - 18 * math.sin(t * math.pi), MOTION)
        c.poly([(22, 22), (29, 25), (24, 29)], MOTION)
        c.circle(15, 7, 3, LEATHER[1])
    elif name == "prone":
        c.circle(7, 22, 3, SKIN[1])
        c.rect(10, 20, 14, 5, LEATHER[1])
        c.rect(24, 21, 5, 3, LEATHER[2])
        c.rect(2, 26, 28, 2, STONE[2])
    elif name == "search":
        c.ring(13, 13, 8, BRASS[1], 2)
        c.circle(13, 13, 6, ICE[0])
        c.dot(10, 10, "#ffffff")
        c.line(19, 19, 27, 27, WOOD[1], 3)
    elif name == "steal":
        c.poly([(4, 20), (14, 14), (22, 16), (22, 20), (12, 24)], SKIN[1])
        for k in range(3):
            c.line(14 + k * 3, 14, 18 + k * 3, 8, SKIN[1], 2)
        c.circle(23, 8, 4, GOLD[1])
        c.dot(22, 7, GOLD[0])
    elif name == "pick_lock":
        c.rect(8, 6, 16, 20, BRASS[1])
        c.circle(16, 13, 3, BLACK[2])
        c.rect(15, 14, 2, 6, BLACK[2])
        c.line(16, 18, 28, 28, STEEL[1], 1)
        c.line(16, 18, 17, 16, STEEL[1], 1)
    elif name == "disarm":
        for k in range(4):
            c.poly([(6 + k * 6, 26), (9 + k * 6, 15), (12 + k * 6, 26)], IRON[1])
        c.line(5, 6, 27, 28, RED[1], 3)
        c.line(27, 6, 5, 28, RED[1], 3)
    elif name == "rouse":
        c.circle(11, 11, 5, RED[1])
        c.circle(20, 11, 5, RED[1])
        c.poly([(6, 13), (25, 13), (16, 25)], RED[1])
        c.dot(10, 9, RED[0])
        c.rect(22, 18, 8, 2, GREEN[1])
        c.rect(25, 15, 2, 8, GREEN[1])
    elif name == "tend":
        c.poly([(3, 16), (16, 8), (29, 16), (16, 24)], WHITE[0])
        c.circle(16, 16, 5, BLUE[1])
        c.circle(16, 16, 2, BLACK[2])
        c.circle(25, 25, 2.5, BLUE[0])
        c.dot(25, 21, BLUE[0])
    elif name == "retreat":
        c.poly([(3, 16), (12, 7), (12, 12), (26, 12), (26, 20), (12, 20), (12, 25)], WHITE[1])
        for y in (9, 16, 23):
            c.line(27, y, 30, y, MOTION)
    elif name == "learn":
        c.poly([(3, 9), (15, 7), (15, 27), (3, 26)], PARCHMENT[1])
        c.poly([(17, 7), (29, 9), (29, 26), (17, 27)], PARCHMENT[0])
        c.rect(15, 7, 2, 20, LEATHER[1])
        for y in (12, 16, 20):
            c.line(5, y, 12, y - 1, PARCHMENT[2])
        c.circle(23, 16, 3, "#b07ce8")
    elif name == "dig_out":
        c.line(6, 4, 18, 18, WOOD[1], 2)
        c.poly([(16, 16), (24, 14), (26, 24), (18, 26)], IRON[1])
        c.circle(10, 27, 4, STONE[1])
        c.circle(25, 29, 3, STONE[2])
    elif name == "drop_roll":
        c.poly([(9, 24), (12, 10), (16, 16), (19, 6), (24, 24)], FLAME[2])
        c.poly([(12, 24), (16, 15), (21, 24)], FLAME[1])
        for k in range(10):
            a = k / 10 * 2 * math.pi
            c.dot(16 + 13 * math.cos(a), 17 + 12 * math.sin(a), MOTION)
    elif name == "cast":
        c.ring(16, 16, 12, "#b07ce8", 1.6)
        pts = [(16 + 10 * math.cos(math.radians(-90 + 72 * k)), 16 + 10 * math.sin(math.radians(-90 + 72 * k))) for k in range(5)]
        for k in range(5):
            (x0, y0), (x1, y1) = pts[k], pts[(k + 2) % 5]
            c.line(x0, y0, x1, y1, "#e8d8ff", 1)
    elif name == "jackalope_foot":
        c.poly([(13, 3), (19, 3), (21, 20), (11, 20)], "#c8b090")
        c.poly([(9, 20), (23, 20), (26, 27), (6, 27)], "#c8b090")
        c.line(13, 3, 9, 1, WOOD[1])
        c.line(19, 3, 23, 1, WOOD[1])
        c.rect(12, 9, 8, 2, GOLD[1])
        clover(c, 25, 9, 1.6)
    elif name == "shield_cufflinks":
        for x in (10, 22):
            c.poly([(x - 5, 9), (x + 5, 9), (x + 5, 15), (x, 22), (x - 5, 15)], STEEL[1])
            c.rect(x - 1, 11, 2, 7, GOLD[1])
        c.line(14, 15, 18, 15, GOLD[2])
    elif name == "icarus_wings":
        feathers = ["#ffffff", "#f0ead8", "#e0d6bc", "#d0c4a0", "#c0b088"]
        for side in (-1, 1):
            for k in range(5):
                x0 = 16 + side * 2
                c.line(x0, 10 + k, x0 + side * (13 - k * 2), 6 + k * 4, feathers[k], 2)
        c.rect(14, 8, 4, 12, BRASS[1])
        c.circle(23, 26, 2.2, FLAME[1])
        c.circle(9, 27, 1.6, FLAME[2])
    else:
        raise ValueError(f"No picture for ability '{name}'")


# The backs of cards: an empty slot shows the slot's mark, and the bag its own.
BACK = ("#7a6a90", "#5a4a6a", "#3a3048")


def back(c, slot):
    if slot == "head":
        c.circle(16, 17, 9, BACK[1])
        _clear_below(c, 18)
        c.rect(6, 17, 21, 3, BACK[0])
    elif slot == "body":
        c.poly([(9, 5), (13, 5), (16, 8), (19, 5), (23, 5), (28, 10), (25, 13), (24, 27), (8, 27), (7, 13), (4, 10)], BACK[1])
    elif slot == "main_hand":
        blade(c, 11, 21, 27, 5, BACK, 2)
        hilt(c, 11, 21, BACK, BACK, BACK)
    elif slot == "off_hand":
        c.poly([(7, 5), (25, 5), (25, 15), (16, 28), (7, 15)], BACK[1])
    elif slot == "feet":
        c.poly([(12, 5), (20, 5), (20, 20), (27, 23), (27, 28), (10, 28), (10, 20)], BACK[1])
    elif slot == "accessory":
        c.ring(16, 19, 8, BACK[0], 2.4)
        c.poly([(12, 6), (20, 6), (22, 9), (16, 14), (10, 9)], BACK[1])
    elif slot == "belt":
        c.rect(3, 13, 26, 7, BACK[1])
        c.rect(12, 11, 9, 11, BACK[0])
        c.rect(15, 14, 3, 5, BACK[2])
    elif slot == "bag":
        c.poly([(9, 11), (23, 11), (27, 22), (23, 29), (9, 29), (5, 22)], BACK[1])
        c.poly([(11, 5), (21, 5), (19, 11), (13, 11)], BACK[0])
        c.rect(11, 10, 10, 2, BACK[2])
    else:
        raise ValueError(f"No card back for '{slot}'")


ABILITIES = [
    "fist", "shove", "wend", "leap", "prone", "search", "steal", "pick_lock", "disarm", "rouse", "tend",
    "retreat", "learn", "dig_out", "drop_roll", "cast", "jackalope_foot", "shield_cufflinks", "icarus_wings",
]

BACKS = ["head", "body", "main_hand", "off_hand", "feet", "accessory", "belt", "bag"]


def draw_ability(name):
    c = Canvas()
    ability(c, name)
    c.outline()
    return c.pixels()


def draw_back(slot):
    c = Canvas()
    back(c, slot)
    c.outline()
    return c.pixels()


def _clear_below(c, y):
    for yy in range(y, SIZE):
        for xx in range(SIZE):
            c.px[yy][xx] = None


def clover(c, x, y, r):
    for dx, dy in ((-1, -1), (1, -1), (-1, 1), (1, 1)):
        c.circle(x + dx * r * 0.9, y + dy * r * 0.9, r, GREEN[1])
    c.dot(x, y, GREEN[2])


ITEMS = [
    "wooden_sword", "longsword", "knights_sword", "trident", "short_bow", "great_axe", "maul", "war_hammer",
    "spear", "dagger", "rapier", "crossbow", "heavy_crossbow", "frost_brand", "quake_hammer", "longbow",
    "buckler", "tower_shield", "knights_shield", "leather_cap", "diving_helm", "lucky_helmet", "cowards_cloak",
    "leather_armor", "studded_armor", "chain_mail", "wardens_plate", "storm_mail", "dash_boots",
    "flame_scroll", "frost_scroll", "venom_scroll", "acid_scroll", "grave_scroll", "gale_scroll", "sun_scroll",
    "bloom_scroll", "quake_scroll", "spark_scroll", "mind_scroll", "shadow_scroll", "frost_wand", "boomerang",
    "torch", "lockpicks", "bomb", "autosmith_hammer", "eye_drops", "holy_water", "health_potion",
    "loose_coins", "coin_pouch", "emerald", "pearl", "golden_idol",
]


def build_cards(out_dir, write_png):
    for item in ITEMS:
        write_png(Path(out_dir) / f"{item}.png", draw(item))

    for name in ABILITIES:
        write_png(Path(out_dir) / f"{name}.png", draw_ability(name))

    for slot in BACKS:
        write_png(Path(out_dir) / f"back_{slot}.png", draw_back(slot))


def contact_sheet(path, write_png, scale=3, columns=11, background="#2a2338"):
    """Every picture on a card-colored panel, enlarged, to look them over."""
    pictures = [draw(item) for item in ITEMS] + [draw_ability(name) for name in ABILITIES] + [draw_back(slot) for slot in BACKS]
    cell = SIZE * scale + 8
    rows = (len(pictures) + columns - 1) // columns
    bg = rgba(background)
    sheet = [[bg] * (columns * cell) for _ in range(rows * cell)]
    for n, pixels in enumerate(pictures):
        ox, oy = n % columns * cell + 4, n // columns * cell + 4
        for y in range(SIZE * scale):
            for x in range(SIZE * scale):
                p = pixels[y // scale][x // scale]
                if p[3]:
                    sheet[oy + y][ox + x] = p
    write_png(Path(path), sheet)


if __name__ == "__main__":
    import sys
    spec = importlib.util.spec_from_file_location("build_assets", Path(__file__).with_name("build-assets.py"))
    assets = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(assets)
    build_cards(assets.ASSETS / "cards", assets.write_png)
    if len(sys.argv) > 1:
        contact_sheet(sys.argv[1], assets.write_png)
