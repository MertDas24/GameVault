using GameVault.Business.Catalog;
using GameVault.Business.Library;
using GameVault.Business.Security;
using GameVault.Models.Library;
using GameVault.Web.Infrastructure;
using GameVault.Web.Pages.Account;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Suggest;

/// <summary>
/// "Ne oynasam?": sırada bekleyen oyunlardan filtrelere uyan rastgele bir öneri ya da
/// (keşif sekmesi) RAWG'daki popüler oyunlardan kütüphanede olmayanlar.
/// </summary>
public sealed class IndexModel(
    SuggestionService suggestions,
    DiscoveryService discovery,
    LibraryService library,
    ReferenceDataService referenceData,
    DbRateLimiter limiter,
    Display display,
    TimeProvider time) : AppPageModel
{
    public const string DiscoverSource = "rawg";

    public SuggestionFilter Filter { get; private set; } = SuggestionFilter.None;

    public SuggestionResult Result { get; private set; } = new(null, 0, 0, 0);

    /// <summary>Keşif sekmesi açıksa true; öneriler kütüphane yerine RAWG'dan gelir.</summary>
    public bool Discover { get; private set; }

    public DiscoveryResult? Discovery { get; private set; }

    public string? Message { get; private set; }

    public IReadOnlyList<ReferenceItem> Platforms { get; private set; } = [];

    public IReadOnlyList<ReferenceItem> Genres { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(
        int? platform, int? genre, int? maxHours, bool wishlist, Guid? exclude, string? source, CancellationToken ct)
    {
        Discover = source == DiscoverSource;
        // Sorgu dizesindeki geçersiz değerler hata sayfası değil, filtresiz öneri üretir.
        // İstek listesi keşifte anlamsızdır: kütüphanedeki oyunlar zaten önerilmez.
        Filter = SuggestionFilter.Normalize(platform, genre, maxHours, wishlist && !Discover);
        Platforms = await referenceData.GetPlatformsAsync(ct);
        Genres = await referenceData.GetGenresAsync(ct);

        if (!Discover)
        {
            Result = await suggestions.SuggestAsync(CurrentUserId, Filter, exclude, ct);
            return Page();
        }

        if (!await limiter.TryAcquireAsync("catalog-user", limiter.Options.CatalogPerUser, ClientPartition.ForUser(CurrentUserId), ct))
        {
            Message = LoginModel.RateLimitedError;
            return PageWithStatus(StatusCodes.Status429TooManyRequests);
        }

        Discovery = await discovery.DiscoverAsync(CurrentUserId, Filter, Platforms, Genres, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostStartAsync(Guid id, CancellationToken ct)
    {
        if (!await library.StartPlayingAsync(CurrentUserId, id, display.Today(time.GetUtcNow()), ct))
        {
            return NotFound();
        }

        Flash("İyi eğlenceler! Oyun “Oynuyorum” olarak işaretlendi.");
        return LocalRedirect($"/Library/{id}");
    }

    /// <summary>Aynı filtrelerle yeni öneri adresi; az önce önerilen oyun hariç tutulur.</summary>
    public string AnotherUrl(Guid excludeId) => FilterUrl(Discover, excludeId);

    /// <summary>Filtreleri koruyarak kütüphane ya da keşif sekmesinin adresi.</summary>
    public string SourceUrl(bool discover) => FilterUrl(discover, null);

    /// <summary>Geçerli sekmenin filtresiz adresi.</summary>
    public string SourceUrlWithoutFilters() => Discover ? "/Suggest?source=" + DiscoverSource : "/Suggest";

    private string FilterUrl(bool discover, Guid? excludeId)
    {
        var query = new Dictionary<string, string?>
        {
            ["source"] = discover ? DiscoverSource : null,
            ["platform"] = Filter.PlatformId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["genre"] = Filter.GenreId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxHours"] = Filter.MaxHours?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["wishlist"] = Filter.IncludeWishlist && !discover ? "true" : null,
            ["exclude"] = excludeId?.ToString()
        };
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/Suggest", query.Where(p => p.Value is not null));
    }
}
