using GameVault.DataAccess;
using GameVault.Entities;
using GameVault.Models.Stats;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Business.Stats;

/// <summary>Yalnızca çağıran kullanıcının gerçek kayıtlarından hesaplanan istatistikler.</summary>
public sealed class StatsService(GameVaultDbContext db)
{
    public async Task<LibraryStats> GetAsync(Guid userId, CancellationToken ct = default)
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

        var fullStatus = Enum.GetValues<GameStatus>().ToDictionary(s => s, s => byStatus.GetValueOrDefault(s));

        return new LibraryStats(total, fullStatus, favorites, playtime, averageRating, ratedCount, completionRate,
            topPlatforms, topGenres, mostPlayed);
    }
}
