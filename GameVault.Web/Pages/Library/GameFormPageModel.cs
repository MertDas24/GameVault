using GameVault.Business.Library;
using GameVault.Models.Library;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Library;

/// <summary>Ekleme ve düzenleme sayfalarının ortak form mantığı.</summary>
public abstract class GameFormPageModel(ReferenceDataService referenceData, CollectionService collections) : AppPageModel
{
    [BindProperty]
    public GameInput Input { get; set; } = new();

    public IReadOnlyList<ReferenceItem> Platforms { get; private set; } = [];
    public IReadOnlyList<ReferenceItem> Genres { get; private set; } = [];

    /// <summary>Yalnızca geçerli kullanıcının koleksiyonları.</summary>
    public IReadOnlyList<CollectionSummary> Collections { get; private set; } = [];

    protected async Task LoadReferenceDataAsync(CancellationToken ct)
    {
        Platforms = await referenceData.GetPlatformsAsync(ct);
        Genres = await referenceData.GetGenresAsync(ct);
        Collections = await collections.ListAsync(CurrentUserId, ct);
    }
}
