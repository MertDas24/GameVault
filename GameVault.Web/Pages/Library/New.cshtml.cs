using GameVault.Business.Catalog;
using GameVault.Business.Library;
using GameVault.Business.Security;
using GameVault.Models.Catalog;
using GameVault.Models.Library;
using GameVault.Web.Infrastructure;
using GameVault.Web.Pages.Account;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Library;

public sealed class NewModel(
    LibraryService library,
    ReferenceDataService referenceData,
    GameCatalog catalog,
    CollectionService collections,
    DbRateLimiter limiter) : GameFormPageModel(referenceData, collections)
{
    public bool CatalogEnabled => catalog.IsEnabled;

    /// <summary>Form RAWG'dan seçilen oyunla dolduruldu.</summary>
    public CatalogGame? FromCatalog { get; private set; }

    /// <summary>RAWG'dan doldurma başarısız olduysa kullanıcıya gösterilecek açıklama.</summary>
    public string? CatalogMessage { get; private set; }

    /// <summary>Kullanıcı, aynı adlı oyun uyarısına rağmen eklemeyi onayladı.</summary>
    [BindProperty]
    public bool ConfirmDuplicateTitle { get; set; }

    /// <summary>Kütüphanede aynı adı taşıyan oyunlar; doluysa kaydetmeden önce onay istenir.</summary>
    public IReadOnlyList<SameTitleGame> SameTitleGames { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? rawgId, CancellationToken ct)
    {
        await LoadReferenceDataAsync(ct);
        if (rawgId is not { } id || !catalog.IsEnabled)
        {
            return Page();
        }

        var owned = await library.GetOwnedRawgIdsAsync(CurrentUserId, [id], ct);
        if (owned.TryGetValue(id, out var existingId))
        {
            Flash(LibraryService.DuplicateRawgGameError);
            return LocalRedirect($"/Library/{existingId}");
        }

        if (!await limiter.TryAcquireAsync("catalog-user", limiter.Options.CatalogPerUser, ClientPartition.ForUser(CurrentUserId), ct))
        {
            CatalogMessage = LoginModel.RateLimitedError;
            return Page();
        }

        // RAWG verisi doğrudan kaydedilmez: formu doldurur, kullanıcı kontrol edip kaydeder.
        var result = await catalog.GetGameAsync(id, ct);
        if (result.Status == CatalogStatus.Ok && result.Game is { } game)
        {
            FromCatalog = game;
            Input = RawgMapping.ToGameInput(game, Platforms, Genres);
        }
        else
        {
            CatalogMessage = result.Status == CatalogStatus.NotFound
                ? "Seçilen oyun RAWG'da bulunamadı. Bilgileri elle girebilirsin."
                : "RAWG'a şu an ulaşılamıyor. Bilgileri elle girebilirsin.";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadReferenceDataAsync(ct);
            return Page();
        }

        if (!ConfirmDuplicateTitle)
        {
            var sameTitle = await library.FindSameTitleAsync(CurrentUserId, Input.Title, ct);
            // Aynı RAWG oyunu zaten varsa uyarı gösterilmez; servis eklemeyi kesin olarak reddeder.
            var sameRawgGame = Input.RawgId is not null && sameTitle.Any(g => g.RawgId == Input.RawgId);
            if (sameTitle.Count > 0 && !sameRawgGame)
            {
                SameTitleGames = sameTitle;
                await LoadReferenceDataAsync(ct);
                return Page();
            }
        }

        var result = await library.CreateAsync(CurrentUserId, Input, ct);
        if (!result.Succeeded)
        {
            AddErrors(result.Errors, nameof(Input));
            await LoadReferenceDataAsync(ct);
            return Page();
        }

        Flash($"“{Input.Title.Trim()}” kütüphanene eklendi.");
        return LocalRedirect($"/Library/{result.Value}");
    }
}
