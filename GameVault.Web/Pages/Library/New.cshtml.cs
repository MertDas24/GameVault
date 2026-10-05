using GameVault.Business.Library;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Library;

public sealed class NewModel(LibraryService library, ReferenceDataService referenceData) : GameFormPageModel(referenceData)
{
    public async Task OnGetAsync(CancellationToken ct) => await LoadReferenceDataAsync(ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            await LoadReferenceDataAsync(ct);
            return Page();
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
