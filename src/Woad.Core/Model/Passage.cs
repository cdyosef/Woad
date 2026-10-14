using Woad.Core.Grid;

namespace Woad.Core.Model;

public enum PassageKind
{
    Open,

    /// <summary>Opened with a key, or by picking the lock (Od Hand).</summary>
    Locked,

    /// <summary>The Boss Door: only the Boss Key opens it. The only way into the boss room.</summary>
    BossLocked,

    /// <summary>Hidden until a search finds it; then an ordinary door.</summary>
    Secret,

    /// <summary>A cracked wall in the doorway until a bomb's blast breaks it open.</summary>
    Cracked,
}

/// <summary>
/// The door between two rooms, shared by both sides so unlocking, finding or blasting it from
/// one side opens it from the other. A door can also hide a dart trap that fires on the first
/// hero through.
/// </summary>
public sealed class Passage
{
    internal Passage(string roomA, Direction sideA, string roomB, PassageKind kind, int difficulty, Trap? trap)
    {
        RoomA = roomA;
        SideA = sideA;
        RoomB = roomB;
        Kind = kind;
        Difficulty = difficulty;
        Trap = trap;
        IsLocked = kind is PassageKind.Locked or PassageKind.BossLocked;
        IsHidden = kind == PassageKind.Secret;
        IsBlocked = kind == PassageKind.Cracked;
    }

    public string RoomA { get; }

    /// <summary>The side of <see cref="RoomA"/> the door is on; it's on the opposite side of <see cref="RoomB"/>.</summary>
    public Direction SideA { get; }

    public string RoomB { get; }

    /// <summary>What the door was when the run began; see <see cref="IsLocked"/>, <see cref="IsHidden"/> and <see cref="IsBlocked"/> for now.</summary>
    public PassageKind Kind { get; }

    /// <summary>Od Hand needed to pick its lock, or to find it when it's secret.</summary>
    public int Difficulty { get; }

    public bool IsLocked { get; internal set; }

    public bool NeedsBossKey => Kind == PassageKind.BossLocked;

    /// <summary>A secret door nobody has found yet: it doesn't show and can't be used.</summary>
    public bool IsHidden { get; internal set; }

    /// <summary>A cracked wall still standing in the doorway.</summary>
    public bool IsBlocked { get; internal set; }

    /// <summary>A dart trap that fires on the next hero through, until disarmed.</summary>
    public Trap? Trap { get; internal set; }

    /// <summary>Heroes can walk through it now.</summary>
    public bool IsOpen => !IsLocked && !IsHidden && !IsBlocked;

    public string OtherRoom(string roomId) => roomId == RoomA ? RoomB : RoomA;

    public Direction SideIn(string roomId) => roomId == RoomA ? SideA : SideA.Opposite();
}
