using Godot;
using Woad.Core.Grid;
using Woad.Core.Model;
using Woad.Scenes.Ui;

namespace Woad.Scenes.Game;

/// <summary>
/// The room's walls and doors. A thin stone rim, in the dungeon's own stone, runs round the room's
/// outline (along the rock of round, cross and L-shaped rooms too), broken only by doorways and
/// opened breaches. Each door sits in a stone archway in the rim: open (dark, its leaf swung back),
/// locked (iron-bound wood and a keyhole), the Boss Door (crimson, gold horns round the frame and
/// the keyhole), a found secret door (a stone panel standing ajar), walled up (cracked stone), or a
/// sealed trap room's red portcullis. A secret door no one has found is just more rim.
/// </summary>
public partial class BoardView
{
    /// <summary>How thick the rim is, in board pixels.</summary>
    private const float Rim = 4;

    /// <summary>The rim's stone, its lit edge and its dark outline, per dungeon.</summary>
    private (Color Stone, Color Light, Color Dark) RimColors => ThemeId switch
    {
        "sunken_temple" => (new Color("6a3a30"), new Color("e0b49a"), new Color("1e1210")),
        "unhallowed_graves" => (new Color("3a3a4a"), new Color("9a9aae"), new Color("101016")),
        _ => (new Color("27392f"), new Color("a8c4b4"), new Color("0e1410")),
    };

    private static readonly Direction[] Sides = [Direction.North, Direction.East, Direction.South, Direction.West];

    /// <summary>The rim, all round the room: wherever an open tile meets rock or the room's edge.</summary>
    private void DrawRim()
    {
        (Color stone, Color light, Color dark) = RimColors;
        HashSet<(GridPosition, Direction)> gaps = RimGaps();
        foreach (GridPosition tile in ShownTiles())
        {
            if (_room.Layout[tile] is TileKind.Rock or TileKind.Wall or TileKind.CrackedWall)
            {
                continue;
            }

            Rect2 rect = TileRect(tile);
            foreach (Direction side in Sides)
            {
                if (!Closed(tile.Step(side)) || gaps.Contains((tile, side)))
                {
                    continue;
                }

                Rect2 band = side switch
                {
                    Direction.North => new Rect2(rect.Position, new Vector2(TileSize, Rim)),
                    Direction.South => new Rect2(rect.Position.X, rect.End.Y - Rim, TileSize, Rim),
                    Direction.West => new Rect2(rect.Position, new Vector2(Rim, TileSize)),
                    _ => new Rect2(rect.End.X - Rim, rect.Position.Y, Rim, TileSize),
                };
                DrawRect(band, stone);

                // Lit where it meets the rock, shadowed where it meets the floor.
                (Vector2 outerA, Vector2 outerB, Vector2 innerA, Vector2 innerB) = side switch
                {
                    Direction.North => (band.Position, band.Position + new Vector2(band.Size.X, 0), band.Position + new Vector2(0, Rim), band.End),
                    Direction.South => (band.Position + new Vector2(0, Rim), band.End, band.Position, band.Position + new Vector2(band.Size.X, 0)),
                    Direction.West => (band.Position, band.Position + new Vector2(0, band.Size.Y), band.Position + new Vector2(Rim, 0), band.End),
                    _ => (band.Position + new Vector2(Rim, 0), band.End, band.Position, band.Position + new Vector2(0, band.Size.Y)),
                };
                DrawLine(outerA, outerB, light, 1);
                DrawLine(innerA, innerB, dark, 1);
            }

            // An inside corner (rock only diagonally, as along a round room's curve) gets a small block.
            foreach ((int dx, int dy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
            {
                var diagonal = new GridPosition(tile.X + dx, tile.Y + dy);
                if (Closed(diagonal) && !Closed(new GridPosition(tile.X + dx, tile.Y)) && !Closed(new GridPosition(tile.X, tile.Y + dy)))
                {
                    var corner = new Rect2(dx < 0 ? rect.Position.X : rect.End.X - Rim, dy < 0 ? rect.Position.Y : rect.End.Y - Rim, Rim, Rim);
                    DrawRect(corner, stone);
                    DrawRect(corner, dark, filled: false, width: 1);
                }
            }
        }
    }

    /// <summary>Outside the room, or solid rock: where the rim runs.</summary>
    private bool Closed(GridPosition tile) => !_room!.Layout.Contains(tile) || _room.Layout[tile] == TileKind.Rock;

    /// <summary>Where the rim is broken: every doorway with a door someone can see, and breaches blown open.</summary>
    private HashSet<(GridPosition, Direction)> RimGaps()
    {
        var gaps = new HashSet<(GridPosition, Direction)>();
        foreach ((Direction side, string _) in _room!.Doors)
        {
            if (_room.PassageOn(side) is not { IsHidden: true })
            {
                gaps.Add((_room.DoorTile(side), side));
            }
        }

        foreach (GridPosition breach in _room.Breaches.Keys.Where(tile => _room.Layout[tile] != TileKind.CrackedWall))
        {
            foreach (Direction side in Sides.Where(side => !_room.Layout.Contains(breach.Step(side))))
            {
                gaps.Add((breach, side));
            }
        }

        return gaps;
    }

    /// <summary>
    /// A door in its archway, drawn facing north (the wall at the top of the tile) and turned to its side.
    /// A revealed trap on it gets its red warning.
    /// </summary>
    private void DrawDoor(Direction side, Passage? passage, bool sealedShut)
    {
        GridPosition doorTile = _room!.DoorTile(side);
        Rect2 tile = TileRect(doorTile);
        float turn = side switch { Direction.East => Mathf.Pi / 2, Direction.South => Mathf.Pi, Direction.West => -Mathf.Pi / 2, _ => 0 };
        DrawSetTransform(tile.GetCenter(), turn, Vector2.One);
        var o = new Vector2(-TileSize / 2f, -TileSize / 2f);
        (Color stone, Color light, Color dark) = RimColors;

        // The archway: two posts and a lintel, a little deeper than the rim.
        var opening = new Rect2(o + new Vector2(6, Rim), new Vector2(20, 13));
        Color gold = Palette.TrimBright, wood = new("7a4a24"), woodDark = new("4a2a14"), iron = new("3a3a44");
        bool boss = passage is { NeedsBossKey: true, IsLocked: true };

        switch (passage)
        {
            case { IsBlocked: true }:
                // Walled up: rough stone with cracks, until a bomb breaks it.
                DrawRect(opening, stone.Darkened(0.15f));
                DrawPolyline([opening.Position + new Vector2(5, 0), opening.Position + new Vector2(8, 4), opening.Position + new Vector2(6, 7), opening.Position + new Vector2(10, 11)], dark, 1);
                DrawPolyline([opening.Position + new Vector2(13, 1), opening.Position + new Vector2(11, 5), opening.Position + new Vector2(14, 11)], dark, 1);
                break;

            case { IsLocked: true }:
                DrawRect(opening, boss ? new Color("6a1a20") : wood);
                for (float x = 3; x < opening.Size.X; x += 4)
                {
                    DrawLine(opening.Position + new Vector2(x, 0), opening.Position + new Vector2(x, opening.Size.Y), boss ? new Color("3a0a10") : woodDark, 1);
                }

                DrawRect(new Rect2(opening.Position + new Vector2(0, 1.5f), new Vector2(opening.Size.X, 1.5f)), iron);
                DrawRect(new Rect2(opening.Position + new Vector2(0, 10), new Vector2(opening.Size.X, 1.5f)), iron);
                DrawKeyhole(opening.GetCenter() + new Vector2(0, 0.5f), boss);
                break;

            default:
                // Open (or a found secret door): a dark way through, lit at the threshold.
                DrawRect(opening, new Color("0a0c10"));
                DrawRect(new Rect2(opening.Position + new Vector2(0, opening.Size.Y - 1.5f), new Vector2(opening.Size.X, 1.5f)), gold with { A = 0.55f });
                if (passage is { Kind: PassageKind.Secret })
                {
                    // A stone panel swung half open on its pivot.
                    DrawColoredPolygon([opening.Position, opening.Position + new Vector2(9, 2), opening.Position + new Vector2(9, opening.Size.Y), opening.Position + new Vector2(0, opening.Size.Y)], stone);
                    DrawPolyline([opening.Position, opening.Position + new Vector2(9, 2), opening.Position + new Vector2(9, opening.Size.Y)], dark, 1);
                }
                else
                {
                    // The door leaf, swung back against the right-hand post.
                    DrawRect(new Rect2(opening.End.X - 4, opening.Position.Y, 4, opening.Size.Y - 1.5f), wood);
                    DrawLine(new Vector2(opening.End.X - 4, opening.Position.Y), new Vector2(opening.End.X - 4, opening.End.Y - 1.5f), woodDark, 1);
                }

                if (sealedShut)
                {
                    // A trap room's portcullis, dropped until the monsters are dead.
                    Color bars = Palette.Danger.Darkened(0.2f);
                    for (float x = 2; x < opening.Size.X; x += 3.5f)
                    {
                        DrawLine(opening.Position + new Vector2(x, 0), opening.Position + new Vector2(x, opening.Size.Y), bars, 1.5f);
                    }

                    DrawLine(opening.Position + new Vector2(0, 3), opening.Position + new Vector2(opening.Size.X, 3), bars, 1.5f);
                    DrawLine(opening.Position + new Vector2(0, 8), opening.Position + new Vector2(opening.Size.X, 8), bars, 1.5f);
                }

                break;
        }

        // Posts and lintel over everything.
        foreach (float x in new[] { 1f, 26f })
        {
            var post = new Rect2(o + new Vector2(x, 0), new Vector2(5, Rim + 14));
            DrawRect(post, stone);
            DrawRect(post, dark, filled: false, width: 1);
            DrawLine(post.Position + new Vector2(1, 1), post.Position + new Vector2(1, post.Size.Y - 1), light, 1);
        }

        var lintel = new Rect2(o + new Vector2(1, 0), new Vector2(30, Rim));
        DrawRect(lintel, stone);
        DrawLine(lintel.Position, lintel.Position + new Vector2(lintel.Size.X, 0), light, 1);

        // The arch: stone fills the opening's corners on the wall side, leaving a round top.
        float radius = opening.Size.X / 2;
        Vector2 arch = new(opening.GetCenter().X, opening.Position.Y + radius * 0.8f);
        Vector2[] curve = [.. Enumerable.Range(0, 13).Select(i => arch + new Vector2(-Mathf.Cos(Mathf.Pi * i / 12) * radius, -Mathf.Sin(Mathf.Pi * i / 12) * radius * 0.8f))];
        DrawColoredPolygon([opening.Position, .. curve[..7]], stone);
        DrawColoredPolygon([new Vector2(opening.End.X, opening.Position.Y), .. curve[6..].Reverse()], stone);
        DrawPolyline(curve, dark, 1, antialiased: true);

        if (boss)
        {
            // Gold horns curling out from the posts.
            foreach (float flip in new[] { -1f, 1f })
            {
                Vector2 root = o + new Vector2(16 + flip * 12.5f, Rim + 13);
                DrawPolyline([root, root + new Vector2(flip * 2, 2), root + new Vector2(flip * 2, 5), root + new Vector2(flip * 0.5f, 7), root + new Vector2(-flip * 1, 6)], gold, 1.5f, antialiased: true);
            }
        }

        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        if (passage?.Trap is { Revealed: true })
        {
            DrawWarning(doorTile);
        }
    }

    /// <summary>A gold lock plate with a keyhole; the Boss Door's is ringed by little gold horns and its keyhole is red.</summary>
    private void DrawKeyhole(Vector2 center, bool boss)
    {
        var plate = new Rect2(center - new Vector2(3.5f, 3.5f), new Vector2(7, 7));
        DrawRect(plate, Palette.TrimBright);
        DrawRect(plate, Palette.Ink, filled: false, width: 1);
        Color hole = boss ? Palette.Danger : Palette.Ink;
        DrawCircle(center + new Vector2(0, -0.8f), 1.1f, hole);
        DrawRect(new Rect2(center + new Vector2(-0.5f, -0.5f), new Vector2(1, 2.3f)), hole);
        if (boss)
        {
            foreach (float flip in new[] { -1f, 1f })
            {
                Vector2 root = center + new Vector2(flip * 3, -2);
                DrawPolyline([root, root + new Vector2(flip * 2, -1), root + new Vector2(flip * 3, -3)], Palette.TrimBright, 1, antialiased: true);
            }
        }
    }
}
