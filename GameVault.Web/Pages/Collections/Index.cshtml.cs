using GameVault.Business.Library;
using GameVault.Models.Library;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Collections;

/// <summary>Koleksiyonları listeleme, oluşturma, yeniden adlandırma ve silme.</summary>
public sealed class IndexModel(CollectionService collections) : AppPageModel
{
    public IReadOnlyList<CollectionSummary> Items { get; private set; } = [];

    /// <summary>Oluşturma formunda hata olursa girilen ad korunur.</summary>
    public string? NewName { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) => Items = await collections.ListAsync(CurrentUserId, ct);

    public async Task<IActionResult> OnPostCreateAsync([FromForm] string? name, CancellationToken ct)
    {
        var result = await collections.CreateAsync(CurrentUserId, name, ct);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("name", result.Errors.Values.First());
            NewName = name;
            Items = await collections.ListAsync(CurrentUserId, ct);
            return PageWithStatus(StatusCodes.Status400BadRequest);
        }

        Flash("Koleksiyon oluşturuldu.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRenameAsync(int id, [FromForm] string? name, CancellationToken ct)
    {
        var result = await collections.RenameAsync(CurrentUserId, id, name, ct);
        if (result.NotFound)
        {
            return NotFound();
        }

        if (result.Succeeded)
        {
            Flash("Koleksiyonun adı değiştirildi.");
        }
        else
        {
            FlashError(result.Errors.Values.First());
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken ct)
    {
        if (!await collections.DeleteAsync(CurrentUserId, id, ct))
        {
            return NotFound();
        }

        Flash("Koleksiyon silindi. İçindeki oyunlar kütüphanende duruyor.");
        return RedirectToPage();
    }
}
