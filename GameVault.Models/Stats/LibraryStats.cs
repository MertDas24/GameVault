using GameVault.Entities;

namespace GameVault.Models.Stats;

public sealed record LibraryStats(
    int TotalGames,
    IReadOnlyDictionary<GameStatus, int> ByStatus,
    int Favorites,
    long TotalPlaytimeHours,
    double? AverageRating,
    int RatedGames,
    double? CompletionRate,
    IReadOnlyList<NamedCount> TopPlatforms,
    IReadOnlyList<NamedCount> TopGenres,
    IReadOnlyList<NamedCount> MostPlayed);

public sealed record NamedCount(string Name, long Count);
