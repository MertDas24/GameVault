using GameVault.Business.Library;
using GameVault.Models.Library;
using GameVault.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Suggest;

/// <summary>"Ne oynasam?": sırada bekleyen oyunlardan filtrelere uyan rastgele bir öneri.</summary>
public sealed class IndexModel(
    SuggestionService suggestions,
    LibraryService library,
    ReferenceDataService referenceData,
    Display display,
    TimeProvider time) : AppPageModel
{
    public SuggestionFilter Filter { get; private set; } = SuggestionFilter.None;

    public SuggestionResult Result { get; private set; } = new(null, 0, 0, 0);

    public IReadOnlyList<ReferenceItem> Platforms { get; private set; } = [];

    public IReadOnlyList<ReferenceItem> Genres { get; private set; } = [];

    public async Task OnGetAsync(int? platform, int? genre, int? maxHours, bool wishlist, Guid? exclude, CancellationToken ct)
    {
        // Sorgu dizesindeki geçersiz değerler hata sayfası değil, filtresiz öneri üretir.
        Filter = SuggestionFilter.Normalize(platform, genre, maxHours, wishlist);
        Platforms = await referenceData.GetPlatformsAsync(ct);
        Genres = await referenceData.GetGenresAsync(ct);
        Result = await suggestions.SuggestAsync(CurrentUserId, Filter, exclude, ct);
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
    public string AnotherUrl(Guid excludeId)
    {
        var query = new Dictionary<string, string?>
        {
            ["platform"] = Filter.PlatformId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["genre"] = Filter.GenreId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["maxHours"] = Filter.MaxHours?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["wishlist"] = Filter.IncludeWishlist ? "true" : null,
            ["exclude"] = excludeId.ToString()
        };
        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/Suggest", query.Where(p => p.Value is not null));
    }
}
