using GameVault.Business.Library;
using GameVault.Models.Library;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Library;

public sealed class DeleteModel(LibraryService library) : AppPageModel
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

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken ct)
    {
        var game = await library.GetAsync(CurrentUserId, id, ct);
        if (game is null || !await library.DeleteAsync(CurrentUserId, id, ct))
        {
            return NotFound();
        }

        Flash($"“{game.Title}” kütüphanenden silindi.");
        return LocalRedirect("/Library");
    }
}
