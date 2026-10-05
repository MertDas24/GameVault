using GameVault.Business.Library;
using GameVault.Models.Library;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Library;

public sealed class DetailsModel(LibraryService library) : AppPageModel
{
    public GameDetails Game { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        var game = await library.GetAsync(CurrentUserId, id, ct);
        if (game is null)
        {
            return NotFound();
        }

        Game = game;
        return Page();
    }

    public async Task<IActionResult> OnPostFavoriteAsync(Guid id, string? returnUrl, CancellationToken ct)
    {
        var state = await library.ToggleFavoriteAsync(CurrentUserId, id, ct);
        if (state is null)
        {
            return NotFound();
        }

        Flash(state.Value ? "Favorilere eklendi." : "Favorilerden çıkarıldı.");
        return LocalRedirect(SafeLocalUrl(returnUrl, $"/Library/{id}"));
    }
}
