using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages;

[AllowAnonymous]
public sealed class IndexModel : AppPageModel
{
    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? LocalRedirect("/Library") : Page();
}
