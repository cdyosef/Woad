namespace Woad.Core.Characters;

/// <summary>What a run changed for a character: levels gained, and card ids gained and lost.</summary>
public sealed record RunReward(int LevelsGained, IReadOnlyList<string> CardsGained, IReadOnlyList<string> CardsLost);
