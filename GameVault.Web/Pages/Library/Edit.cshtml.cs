using GameVault.Business.Library;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Library;

public sealed class EditModel(LibraryService library, ReferenceDataService referenceData, CollectionService collections)
    : GameFormPageModel(referenceData, collections)
{
    public Guid Id { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var input = await library.GetInputAsync(CurrentUserId, id, ct);
        if (input is null)
        {
            return NotFound();
        }

        Id = id;
        Input = input;
        await LoadReferenceDataAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        Id = id;
        if (!ModelState.IsValid)
        {
            // Başkasına ait bir kaydın varlığı, doğrulama hatası üzerinden bile belli olmasın.
            if (await library.GetAsync(CurrentUserId, id, ct) is null)
            {
                return NotFound();
            }

            await LoadReferenceDataAsync(ct);
            return Page();
        }

        var result = await library.UpdateAsync(CurrentUserId, id, Input, ct);
        if (result.NotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            AddErrors(result.Errors, nameof(Input));
            await LoadReferenceDataAsync(ct);
            return Page();
        }

        Flash("Değişiklikler kaydedildi.");
        return LocalRedirect($"/Library/{id}");
    }
}
