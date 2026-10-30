using Woad.Core.Content;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Core.Randomness;
using Woad.Core.Rules;

namespace Woad.Core.Dungeon;

/// <summary>
/// A graveyard (see <see cref="GraveyardDefinition"/>): one huge, dark room. A ragged ring of stone pillars
/// walls it in, with a few gaps leading out and a few more walled up in cracked stone a bomb can open.
/// The party comes in at the bottom, the gate rusting shut behind them. A ruined temple stands in the
/// middle; walled crypts dot the ground, some sharing a wall with a door through it, their sarcophagi
/// along the back; plots of graves lie in rows between. The horde gathers thicker toward the middle and
/// in the crypts; a few strays wander.
/// </summary>
public static partial class DungeonGenerator
{
    /// <summary>The graveyard's one room.</summary>
    public const string GraveyardRoomId = "graves";

    private const char Open = '.';
    private const char Stone = '#';

    private static RunSetup CreateGraveyard(ContentCatalog content, ThemeDefinition theme, IReadOnlyList<HeroSetup> heroes, ulong seed)
    {
        GraveyardDefinition yard = theme.Graveyard!;
        var random = new Pcg32(seed, Stream);
        var plan = new GraveyardPlan(yard.Size);
        plan.Ring(random);
        plan.Gate(heroes.Count);
        plan.Temple();
        plan.Crypts(yard.Crypts, random);
        plan.Exits(yard.Exits, cracked: false, random);
        plan.Exits(yard.CrackedExits, cracked: true, random);
        plan.Graves(yard.Graves.Count, random);
        plan.Sarcophagi(yard.Sarcophagi.Count, random);
        plan.Scatter(random);
        List<GridPosition> braziers = plan.Braziers(yard.Braziers, random);
        plan.Populate(theme, content, yard.Horde, horde: true, random);
        plan.Populate(theme, content, yard.Strays, horde: false, random);

        // What each grave and sarcophagus holds.
        var looks = new Dictionary<GridPosition, ChestLook>();
        var empty = new List<GridPosition>();
        var risers = new Dictionary<GridPosition, string>();
        var traps = new Dictionary<GridPosition, TrapSetup>();
        var locks = new Dictionary<GridPosition, int>();
        foreach ((List<GridPosition> chests, ChestLook look, GraveContents contents) in new[] { (plan.GraveTiles, ChestLook.Grave, yard.Graves), (plan.SarcophagusTiles, ChestLook.Sarcophagus, yard.Sarcophagi) })
        {
            foreach (GridPosition chest in chests)
            {
                looks[chest] = look;
                int roll = random.NextInt(0, 100);
                if (roll < contents.Treasure)
                {
                    // A card, as a chest holds.
                }
                else if (roll < contents.Treasure + contents.Trap)
                {
                    traps[chest] = new TrapSetup(TrapKind.Needle, GameRules.TrapDifficulty(1));
                }
                else if (roll < contents.Treasure + contents.Trap + contents.Risen && contents.Risers.Count > 0)
                {
                    risers[chest] = contents.Risers[random.NextInt(0, contents.Risers.Count)];
                }
                else
                {
                    empty.Add(chest);
                }

                if (!empty.Contains(chest) && random.NextInt(0, 100) < contents.Locked)
                {
                    locks[chest] = GameRules.ChestLockDifficulty(1);
                }
            }
        }

        RoomTemplate template = RoomTemplate.Parse(plan.Rows(), content.FindMonsterByGlyph);
        List<GridPosition> lit = [.. braziers.OrderBy(_ => random.NextInt(0, 1 << 30)).Take(yard.LitBraziers)];
        var room = new RoomSetup(GraveyardRoomId, template, Role: RoomRole.Combat, Dark: true, Features: new RoomFeatures
        {
            ChestLooks = looks,
            EmptyChests = empty,
            Risers = risers,
            ChestTraps = traps,
            LockedChests = locks,
            LitBraziers = lit,
            RelicChest = plan.Reliquary,
        });
        return new RunSetup
        {
            Seed = seed,
            ThemeId = theme.Id,
            Heroes = heroes,
            Rooms = [room],
            StartRoomId = GraveyardRoomId,
        };
    }

    /// <summary>The graveyard drawn as rows of template characters while it's laid out.</summary>
    private sealed class GraveyardPlan(int size)
    {
        private readonly char[,] _tiles = Filled(size);

        /// <summary>Tiles kept for buildings and their surroundings: nothing else is laid there.</summary>
        private readonly bool[,] _kept = new bool[size, size];

        private readonly List<(int X, int Y, int W, int H)> _crypts = [];

        /// <summary>How deep the pillar ring runs at each place along each side (top, bottom, left, right).</summary>
        private readonly int[][] _depth = [new int[size], new int[size], new int[size], new int[size]];

        private GridPosition _gate;

        public List<GridPosition> GraveTiles { get; } = [];

        public List<GridPosition> SarcophagusTiles { get; } = [];

        /// <summary>The temple's reliquary, before the pulpit.</summary>
        public GridPosition Reliquary { get; private set; }

        private GridPosition Center => new(size / 2, size / 2);

        private static char[,] Filled(int size)
        {
            var tiles = new char[size, size];
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y < size; y++)
                {
                    tiles[x, y] = Open;
                }
            }

            return tiles;
        }

        private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < size && y < size;

        private bool IsOpen(int x, int y) => Inside(x, y) && _tiles[x, y] == Open && !_kept[x, y];

        /// <summary>Every tile of the rectangle (and <paramref name="margin"/> around it) is open, unkept ground.</summary>
        private bool Clear(int x, int y, int w, int h, int margin) =>
            Enumerable.Range(x - margin, w + 2 * margin).All(xx => Enumerable.Range(y - margin, h + 2 * margin).All(yy => IsOpen(xx, yy)));

        private void Keep(int x, int y, int w, int h, int margin)
        {
            for (int xx = x - margin; xx < x + w + margin; xx++)
            {
                for (int yy = y - margin; yy < y + h + margin; yy++)
                {
                    if (Inside(xx, yy))
                    {
                        _kept[xx, yy] = true;
                    }
                }
            }
        }

        /// <summary>The ragged ring of stone pillars, two to five deep, wandering along each side.</summary>
        public void Ring(IRandomSource random)
        {
            foreach (int[] depth in _depth)
            {
                int d = 3;
                for (int i = 0; i < size; i++)
                {
                    d = Math.Clamp(d + random.NextInt(-1, 2), 2, 5);
                    depth[i] = d;
                }
            }

            for (int i = 0; i < size; i++)
            {
                for (int d = 0; d < _depth[0][i]; d++)
                {
                    _tiles[i, d] = Stone;
                }

                for (int d = 0; d < _depth[1][i]; d++)
                {
                    _tiles[i, size - 1 - d] = Stone;
                }

                for (int d = 0; d < _depth[2][i]; d++)
                {
                    _tiles[d, i] = Stone;
                }

                for (int d = 0; d < _depth[3][i]; d++)
                {
                    _tiles[size - 1 - d, i] = Stone;
                }
            }
        }

        /// <summary>Where the party comes in: just inside the bottom of the ring, in the middle, the gate shut behind them.</summary>
        public void Gate(int heroes)
        {
            int y = size - 1 - _depth[1].Skip(size / 2 - 3).Take(7).Max() - 1;
            _gate = new GridPosition(size / 2, y);
            for (int x = size / 2 - 3; x <= size / 2 + 3; x++)
            {
                for (int yy = y - 2; yy <= y; yy++)
                {
                    _tiles[x, yy] = Open;
                }
            }

            for (int seat = 0; seat < Math.Min(heroes, 4); seat++)
            {
                _tiles[size / 2 - 1 + seat % 3, y - seat / 3] = (char)('1' + seat);
            }

            Keep(size / 2 - 6, y - 8, 13, 9, 0);
        }

        /// <summary>
        /// The ruined temple in the middle: walls with a wide doorway at the bottom, two rows of pillars,
        /// braziers either side of the pulpit at the top and a chest before it.
        /// </summary>
        public void Temple()
        {
            const int w = 17, h = 15;
            int left = size / 2 - w / 2, top = size / 2 - h / 2;
            for (int x = left; x < left + w; x++)
            {
                for (int y = top; y < top + h; y++)
                {
                    bool edge = x == left || x == left + w - 1 || y == top || y == top + h - 1;
                    _tiles[x, y] = edge ? Stone : Open;
                }
            }

            for (int x = size / 2 - 1; x <= size / 2 + 1; x++)
            {
                _tiles[x, top + h - 1] = Open;
            }

            for (int y = top + 3; y < top + h - 3; y += 3)
            {
                _tiles[left + 3, y] = Stone;
                _tiles[left + w - 4, y] = Stone;
            }

            _tiles[size / 2 - 2, top + 1] = '^';
            _tiles[size / 2 + 2, top + 1] = '^';
            _tiles[size / 2, top + 2] = 'C';
            Reliquary = new GridPosition(size / 2, top + 2);
            Keep(left, top, w, h, 3);
        }

        /// <summary>Walled crypts, a doorway each; about a third built onto another, a door through the wall they share.</summary>
        public void Crypts(int count, IRandomSource random)
        {
            for (int attempt = 0; attempt < count * 60 && _crypts.Count < count; attempt++)
            {
                int w = random.NextInt(7, 12), h = random.NextInt(6, 10);
                if (_crypts.Count > 0 && random.NextInt(0, 100) < 35)
                {
                    (int px, int py, int pw, int ph) = _crypts[random.NextInt(0, _crypts.Count)];
                    bool sideways = random.NextInt(0, 2) == 0;
                    int x = sideways ? (random.NextInt(0, 2) == 0 ? px + pw - 1 : px - w + 1) : px + random.NextInt(-w / 2, pw - w / 2);
                    int y = sideways ? py + random.NextInt(-h / 2, ph - h / 2) : (random.NextInt(0, 2) == 0 ? py + ph - 1 : py - h + 1);
                    if (TryCrypt(x, y, w, h, (px, py, pw, ph), random))
                    {
                        continue;
                    }
                }

                TryCrypt(random.NextInt(8, size - 8 - w), random.NextInt(8, size - 8 - h), w, h, null, random);
            }
        }

        private bool TryCrypt(int x, int y, int w, int h, (int X, int Y, int W, int H)? onto, IRandomSource random)
        {
            if (x < 8 || y < 8 || x + w > size - 8 || y + h > size - 8)
            {
                return false;
            }

            // Clear of everything but the crypt it's built onto (whose shared wall stays).
            for (int xx = x - 2; xx < x + w + 2; xx++)
            {
                for (int yy = y - 2; yy < y + h + 2; yy++)
                {
                    bool ontoIt = onto is { } other && xx >= other.X - 2 && xx < other.X + other.W + 2 && yy >= other.Y - 2 && yy < other.Y + other.H + 2;
                    if (!ontoIt && !IsOpen(xx, yy))
                    {
                        return false;
                    }
                }
            }

            if (onto is { } shared)
            {
                // The overlap must be along one shared wall, three tiles at least, with no other crypt in the way.
                int overlapX = Math.Min(x + w, shared.X + shared.W) - Math.Max(x, shared.X);
                int overlapY = Math.Min(y + h, shared.Y + shared.H) - Math.Max(y, shared.Y);
                if (!((overlapX == 1 && overlapY >= 5) || (overlapY == 1 && overlapX >= 5)))
                {
                    return false;
                }
            }

            for (int xx = x; xx < x + w; xx++)
            {
                for (int yy = y; yy < y + h; yy++)
                {
                    bool edge = xx == x || xx == x + w - 1 || yy == y || yy == y + h - 1;
                    _tiles[xx, yy] = edge ? Stone : Open;
                }
            }

            // A door through the shared wall, in the middle of the overlap.
            if (onto is { } joined)
            {
                int ox = Math.Max(x, joined.X), oy = Math.Max(y, joined.Y);
                int ow = Math.Min(x + w, joined.X + joined.W) - ox, oh = Math.Min(y + h, joined.Y + joined.H) - oy;
                _tiles[ox + ow / 2, oy + oh / 2] = Open;
            }

            // Its own doorway, on a side with open ground beyond.
            List<(int X, int Y, int Dx, int Dy)> doors =
            [
                (x + w / 2, y, 0, -1), (x + w / 2, y + h - 1, 0, 1), (x, y + h / 2, -1, 0), (x + w - 1, y + h / 2, 1, 0),
            ];
            List<(int X, int Y, int Dx, int Dy)> fits = [.. doors.Where(door => IsOpen(door.X + door.Dx, door.Y + door.Dy) && IsOpen(door.X + 2 * door.Dx, door.Y + 2 * door.Dy))];
            if (fits.Count > 0)
            {
                (int dx, int dy, int ddx, int ddy) = fits[random.NextInt(0, fits.Count)];
                _tiles[dx, dy] = Open;
            }

            // Sarcophagi along the wall facing the door, a tile in from it.
            int lids = random.NextInt(1, 4);
            for (int i = 0; i < lids; i++)
            {
                int sx = x + 2 + i * 2, sy = y + 1;
                if (sx < x + w - 2 && _tiles[sx, sy] == Open)
                {
                    _tiles[sx, sy] = 'C';
                    SarcophagusTiles.Add(new GridPosition(sx, sy));
                }
            }

            _crypts.Add((x, y, w, h));
            Keep(x, y, w, h, 1);
            return true;
        }

        /// <summary>Ways out through the ring: on the left, right and top sides, a passage cut through it to an exit at the very edge, or walled up in cracked stone.</summary>
        public void Exits(int count, bool cracked, IRandomSource random)
        {
            for (int made = 0, attempt = 0; made < count && attempt < 50; attempt++)
            {
                int side = new[] { 0, 2, 3 }[random.NextInt(0, 3)];
                int along = random.NextInt(size / 5, size * 4 / 5);
                int depth = _depth[side][along];
                GridPosition Cell(int inward) => side switch
                {
                    0 => new GridPosition(along, inward),
                    2 => new GridPosition(inward, along),
                    _ => new GridPosition(size - 1 - inward, along),
                };

                GridPosition inner = Cell(depth);
                if (!IsOpen(inner.X, inner.Y) || Enumerable.Range(0, depth).Any(d => _tiles[Cell(d).X, Cell(d).Y] != Stone))
                {
                    continue;
                }

                for (int d = 0; d < depth; d++)
                {
                    GridPosition cell = Cell(d);
                    _tiles[cell.X, cell.Y] = d == 0 ? '*' : cracked && d == depth - 1 ? 'X' : Open;
                }

                Keep(inner.X - 2, inner.Y - 2, 5, 5, 0);
                made++;
            }
        }

        /// <summary>Plots of graves in rows: a grave every other tile, rows three apart, so there's always a way between.</summary>
        public void Graves(int count, IRandomSource random)
        {
            for (int attempt = 0; attempt < 2000 && GraveTiles.Count < count; attempt++)
            {
                int columns = random.NextInt(3, 8), rows = random.NextInt(2, 5);
                int w = columns * 2 - 1, h = rows * 3 - 2;
                int x = random.NextInt(6, size - 6 - w), y = random.NextInt(6, size - 6 - h);
                if (!Clear(x, y, w, h, 1))
                {
                    continue;
                }

                for (int c = 0; c < columns && GraveTiles.Count < count; c++)
                {
                    for (int r = 0; r < rows && GraveTiles.Count < count; r++)
                    {
                        _tiles[x + c * 2, y + r * 3] = 'C';
                        GraveTiles.Add(new GridPosition(x + c * 2, y + r * 3));
                    }
                }

                Keep(x, y, w, h, 1);
            }
        }

        /// <summary>The sarcophagi the crypts didn't hold stand in the open, alone, toward the middle.</summary>
        public void Sarcophagi(int count, IRandomSource random)
        {
            for (int attempt = 0; attempt < 1000 && SarcophagusTiles.Count < count; attempt++)
            {
                var tile = new GridPosition(random.NextInt(size / 4, size * 3 / 4), random.NextInt(size / 4, size * 3 / 4));
                if (Clear(tile.X, tile.Y, 1, 1, 1))
                {
                    _tiles[tile.X, tile.Y] = 'C';
                    SarcophagusTiles.Add(tile);
                    Keep(tile.X, tile.Y, 1, 1, 1);
                }
            }
        }

        /// <summary>Lone pillars and rubble across the open ground, never boxing anything in.</summary>
        public void Scatter(IRandomSource random)
        {
            for (int i = 0; i < size * size / 150; i++)
            {
                int x = random.NextInt(6, size - 6), y = random.NextInt(6, size - 6);
                if (Clear(x, y, 1, 1, 1))
                {
                    _tiles[x, y] = random.NextInt(0, 3) == 0 ? ':' : Stone;
                }
            }
        }

        /// <summary>Braziers: one in each crypt, the rest out in the open. Returns them all, the temple's too.</summary>
        public List<GridPosition> Braziers(int count, IRandomSource random)
        {
            List<GridPosition> braziers = [new(size / 2 - 2, size / 2 - 15 / 2 + 1), new(size / 2 + 2, size / 2 - 15 / 2 + 1)];
            foreach ((int x, int y, int w, int h) in _crypts)
            {
                var spot = new GridPosition(x + w - 2, y + h - 2);
                if (_tiles[spot.X, spot.Y] == Open)
                {
                    _tiles[spot.X, spot.Y] = '^';
                    braziers.Add(spot);
                }
            }

            for (int attempt = 0; attempt < 2000 && braziers.Count < count; attempt++)
            {
                int x = random.NextInt(6, size - 6), y = random.NextInt(6, size - 6);
                if (Clear(x, y, 1, 1, 1))
                {
                    _tiles[x, y] = '^';
                    braziers.Add(new GridPosition(x, y));
                }
            }

            return braziers;
        }

        /// <summary>
        /// The horde (or the strays), on open ground away from the gate: the horde gathers thicker toward
        /// the middle and in the crypts; strays wander anywhere.
        /// </summary>
        public void Populate(ThemeDefinition theme, ContentCatalog content, int count, bool horde, IRandomSource random)
        {
            List<RosterEntry> roster = [.. theme.Roster.Where(entry => entry.Horde == horde)];
            int total = roster.Sum(entry => entry.Weight);
            if (total == 0)
            {
                return;
            }

            float reach = size / 2f;
            for (int placed = 0, attempt = 0; placed < count && attempt < count * 400; attempt++)
            {
                int x = random.NextInt(1, size - 1), y = random.NextInt(1, size - 1);
                bool inCrypt = _crypts.Any(crypt => x > crypt.X && x < crypt.X + crypt.W - 1 && y > crypt.Y && y < crypt.Y + crypt.H - 1);
                if (_tiles[x, y] != Open || new GridPosition(x, y).ChebyshevDistanceTo(_gate) < 13
                    || new GridPosition(x, y).SurroundingTiles().Any(tile => Inside(tile.X, tile.Y) && char.IsLetter(_tiles[tile.X, tile.Y]) && _tiles[tile.X, tile.Y] is not ('C' or 'X')))
                {
                    continue;
                }

                // Deeper in, and in the crypts, the dead lie thicker.
                int chance = horde ? (inCrypt ? 100 : 15 + (int)(85 * (1 - new GridPosition(x, y).ChebyshevDistanceTo(Center) / reach))) : 100;
                if (random.NextInt(0, 100) >= chance)
                {
                    continue;
                }

                int pick = random.NextInt(0, total);
                RosterEntry entry = roster.First(candidate => (pick -= candidate.Weight) < 0);
                _tiles[x, y] = content.GetMonster(entry.Monster).Glyph;
                placed++;
            }
        }

        public IEnumerable<string> Rows() =>
            Enumerable.Range(0, size).Select(y => new string([.. Enumerable.Range(0, size).Select(x => _tiles[x, y])]));
    }
}
