using Woad.Core.Dungeon;

namespace Woad.Core.Content;

/// <summary>A theme's room layout after parsing and checking, ready for the dungeon generator.</summary>
public sealed record ThemeRoom(string Id, IReadOnlyList<RoomRole> Roles, RoomTemplate Template);
