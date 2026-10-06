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
    IReadOnlyList<NamedCount> MostPlayed,
    IReadOnlyList<int> RatingDistribution,
    IReadOnlyList<MonthActivity> MonthlyActivity)
{
    /// <summary>Aylık etkinlik grafiğinin kapsadığı ay sayısı (içinde bulunulan ay dahil).</summary>
    public const int ActivityMonths = 12;

    /// <summary>Puan dağılımındaki dilim sayısı; <see cref="RatingDistribution"/>[i], i + 1 puanlı oyun sayısıdır.</summary>
    public const int RatingScale = 10;
}

public sealed record NamedCount(string Name, long Count);

/// <summary>Bir ayda başlanan (StartDate) ve bitirilen (CompletionDate) oyun sayısı. <see cref="Month"/> ayın ilk günüdür.</summary>
public sealed record MonthActivity(DateOnly Month, int Started, int Completed);
