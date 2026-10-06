using GameVault.DataAccess;
using GameVault.Entities;
using GameVault.Models.Stats;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Business.Stats;

/// <summary>Yalnızca çağıran kullanıcının gerçek kayıtlarından hesaplanan istatistikler.</summary>
public sealed class StatsService(GameVaultDbContext db)
{
    /// <param name="today">Kullanıcının gösterim saat dilimindeki bugünü; aylık etkinliğin son ayını belirler.</param>
    public async Task<LibraryStats> GetAsync(Guid userId, DateOnly today, CancellationToken ct = default)
    {
        var games = db.LibraryGames.AsNoTracking().Where(g => g.UserId == userId);

        var byStatus = await games
            .GroupBy(g => g.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        var total = byStatus.Values.Sum();
        var favorites = await games.CountAsync(g => g.IsFavorite, ct);
        var playtime = await games.SumAsync(g => (long)g.PlaytimeHours, ct);
        var ratedCount = await games.CountAsync(g => g.Rating != null, ct);
        var averageRating = ratedCount == 0 ? (double?)null : await games.Where(g => g.Rating != null).AverageAsync(g => (double)g.Rating!.Value, ct);

        // İstek listesindekiler henüz sahip olunmayan oyunlar; tamamlanma oranına katılmaz.
        var owned = total - byStatus.GetValueOrDefault(GameStatus.Wishlist);
        var completionRate = owned == 0 ? (double?)null : byStatus.GetValueOrDefault(GameStatus.Completed) / (double)owned;

        var topPlatforms = (await games
            .GroupBy(g => g.Platform.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name)
            .Take(5)
            .ToListAsync(ct))
            .Select(x => new NamedCount(x.Name, x.Count))
            .ToList();

        var topGenres = (await games
            .SelectMany(g => g.Genres)
            .GroupBy(g => g.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).ThenBy(x => x.Name)
            .Take(5)
            .ToListAsync(ct))
            .Select(x => new NamedCount(x.Name, x.Count))
            .ToList();

        var mostPlayed = await games
            .Where(g => g.PlaytimeHours > 0)
            .OrderByDescending(g => g.PlaytimeHours).ThenBy(g => g.Title)
            .Take(5)
            .Select(g => new NamedCount(g.Title, g.PlaytimeHours))
            .ToListAsync(ct);

        var ratingCounts = await games
            .Where(g => g.Rating != null)
            .GroupBy(g => g.Rating!.Value)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Rating, x => x.Count, ct);
        var ratingDistribution = Enumerable.Range(1, LibraryStats.RatingScale).Select(r => ratingCounts.GetValueOrDefault(r)).ToList();

        var monthlyActivity = await GetMonthlyActivityAsync(games, today, ct);

        var fullStatus = Enum.GetValues<GameStatus>().ToDictionary(s => s, s => byStatus.GetValueOrDefault(s));

        return new LibraryStats(total, fullStatus, favorites, playtime, averageRating, ratedCount, completionRate,
            topPlatforms, topGenres, mostPlayed, ratingDistribution, monthlyActivity);
    }

    /// <summary>
    /// Son <see cref="LibraryStats.ActivityMonths"/> ayda (bu ay dahil) başlanan ve bitirilen oyunlar.
    /// Kullanıcının girdiği tarihlerden (DateOnly) hesaplanır; boş aylar da sıfırla listelenir.
    /// </summary>
    private static async Task<IReadOnlyList<MonthActivity>> GetMonthlyActivityAsync(IQueryable<LibraryGame> games, DateOnly today, CancellationToken ct)
    {
        var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-(LibraryStats.ActivityMonths - 1));
        var end = first.AddMonths(LibraryStats.ActivityMonths);

        var started = await CountByMonthAsync(games.Where(g => g.StartDate >= first && g.StartDate < end).Select(g => g.StartDate!.Value), ct);
        var completed = await CountByMonthAsync(games.Where(g => g.CompletionDate >= first && g.CompletionDate < end).Select(g => g.CompletionDate!.Value), ct);

        return Enumerable.Range(0, LibraryStats.ActivityMonths)
            .Select(i => first.AddMonths(i))
            .Select(m => new MonthActivity(m, started.GetValueOrDefault(m), completed.GetValueOrDefault(m)))
            .ToList();
    }

    private static async Task<Dictionary<DateOnly, int>> CountByMonthAsync(IQueryable<DateOnly> dates, CancellationToken ct) =>
        (await dates
            .GroupBy(d => new { d.Year, d.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToListAsync(ct))
        .ToDictionary(x => new DateOnly(x.Year, x.Month, 1), x => x.Count);
}
