using GameVault.DataAccess;
using GameVault.Entities;
using GameVault.Models.Catalog;
using GameVault.Models.Library;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Business.Catalog;

/// <summary>
/// "Ne oynasam?" keşif sekmesi: RAWG'daki popüler oyunlardan kullanıcının kütüphanesinde olmayanları önerir.
/// <list type="bullet">
/// <item>Tür seçilmemişse, kullanıcının favori, yüksek puanlı (8+) ya da bitirdiği oyunlarında en sık geçen tür kullanılır.</item>
/// <item>RAWG'a yalnızca tür kısa adı, platform kimlikleri ve sayfa numarası gider; kütüphane bilgisi sunucuda kalır.</item>
/// <item>Kütüphanedeki oyunlar hem RAWG kimliğiyle hem de adıyla elenir (elle eklenmiş oyunlar da yakalanır).</item>
/// </list>
/// </summary>
public sealed class DiscoveryService(GameVaultDbContext db, GameCatalog catalog)
{
    public const int Count = 6;
    public const int FavoriteRatingThreshold = 8;

    public async Task<DiscoveryResult> DiscoverAsync(
        Guid userId, SuggestionFilter filter, IReadOnlyList<ReferenceItem> platforms, IReadOnlyList<ReferenceItem> genres,
        CancellationToken ct = default)
    {
        if (!catalog.IsEnabled)
        {
            return new DiscoveryResult(CatalogStatus.Disabled, []);
        }

        var genreName = genres.FirstOrDefault(g => g.Id == filter.GenreId)?.Name;
        var fromTaste = false;
        if (genreName is null)
        {
            genreName = await FavoriteGenreAsync(userId, ct);
            fromTaste = genreName is not null;
        }

        var genreSlug = RawgMapping.GenreSlugFor(genreName);
        if (genreSlug is null)
        {
            // RAWG'da karşılığı olmayan tür: tür olmadan genel popüler oyunlar.
            genreName = null;
            fromTaste = false;
        }

        var platformIds = RawgMapping.PlatformIdsFor(platforms.FirstOrDefault(p => p.Id == filter.PlatformId)?.Name);

        var page = Random.Shared.Next(1, GameCatalog.DiscoverMaxPage + 1);
        var result = await catalog.DiscoverAsync(genreSlug, platformIds, page, ct);
        if (result.Status == CatalogStatus.NotFound && page > 1)
        {
            // Az sonuçlu tür/platform: rastgele sayfa yoksa ilk sayfaya dön.
            result = await catalog.DiscoverAsync(genreSlug, platformIds, 1, ct);
        }

        if (result.Status != CatalogStatus.Ok)
        {
            return new DiscoveryResult(result.Status == CatalogStatus.NotFound ? CatalogStatus.Ok : result.Status, [], genreName, fromTaste);
        }

        var candidates = result.Games
            .Where(g => filter.MaxHours is not { } max || g.EstimatedHours <= max)
            .ToList();

        var rawgIds = candidates.Select(g => g.RawgId).ToList();
        var names = candidates.Select(g => g.Name).ToList();
        var owned = await db.LibraryGames.AsNoTracking()
            .Where(g => g.UserId == userId && ((g.RawgId != null && rawgIds.Contains(g.RawgId.Value)) || names.Contains(g.Title)))
            .Select(g => new { g.RawgId, g.Title })
            .ToListAsync(ct);
        var ownedIds = owned.Where(o => o.RawgId is not null).Select(o => o.RawgId!.Value).ToHashSet();
        var ownedTitles = owned.Select(o => o.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var picks = candidates
            .Where(g => !ownedIds.Contains(g.RawgId) && !ownedTitles.Contains(g.Name))
            .OrderBy(_ => Random.Shared.Next())
            .Take(Count)
            .ToList();

        return new DiscoveryResult(CatalogStatus.Ok, picks, genreName, fromTaste);
    }

    private Task<string?> FavoriteGenreAsync(Guid userId, CancellationToken ct) =>
        db.LibraryGames.AsNoTracking()
            .Where(g => g.UserId == userId
                && (g.IsFavorite || g.Rating >= FavoriteRatingThreshold || g.Status == GameStatus.Completed))
            .SelectMany(g => g.Genres)
            .GroupBy(x => x.Name)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key)
            .Select(x => x.Key)
            .FirstOrDefaultAsync(ct);
}
