using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameVault.Web.Pages;

[AllowAnonymous]
public sealed class PrivacyModel : PageModel
{
    public void OnGet()
    {
    }
}
