using GameVault.Business.Catalog;
using GameVault.Business.Library;
using GameVault.Business.Security;
using GameVault.Models.Catalog;
using GameVault.Web.Infrastructure;
using GameVault.Web.Pages.Account;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Library;

/// <summary>
/// RAWG'da isimle oyun arama. Sonuçlardan biri seçilince "Oyun ekle" formu RAWG bilgileriyle
/// doldurulur. RAWG'a yalnızca arama metni gönderilir; kullanıcıya ait bilgi gönderilmez.
/// </summary>
public sealed class SearchModel(GameCatalog catalog, LibraryService library, DbRateLimiter limiter) : AppPageModel
{
    public bool Enabled => catalog.IsEnabled;

    public string? Query { get; private set; }

    public string? Message { get; private set; }

    public CatalogSearchResult? Result { get; private set; }

    /// <summary>Sonuçlardan kullanıcının kütüphanesinde zaten olanlar (RAWG kimliği → oyun kimliği).</summary>
    public IReadOnlyDictionary<int, Guid> Owned { get; private set; } = new Dictionary<int, Guid>();

    public async Task<IActionResult> OnGetAsync(string? q, CancellationToken ct)
    {
        Query = q?.Trim();
        if (!Enabled || string.IsNullOrEmpty(Query))
        {
            return Page();
        }

        var normalized = GameCatalog.NormalizeQuery(Query);
        if (normalized is null)
        {
            Message = $"Aramak için {GameCatalog.MinQueryLength} ile {GameCatalog.MaxQueryLength} karakter arasında bir oyun adı gir.";
            return Page();
        }

        Query = normalized;
        if (!await limiter.TryAcquireAsync("catalog-user", limiter.Options.CatalogPerUser, ClientPartition.ForUser(CurrentUserId), ct))
        {
            Message = LoginModel.RateLimitedError;
            return PageWithStatus(StatusCodes.Status429TooManyRequests);
        }

        Result = await catalog.SearchAsync(normalized, ct);
        if (Result.Status == CatalogStatus.Ok && Result.Games.Count > 0)
        {
            Owned = await library.GetOwnedRawgIdsAsync(CurrentUserId, Result.Games.Select(g => g.RawgId).ToList(), ct);
        }

        return Page();
    }
}
