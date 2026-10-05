using GameVault.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Account;

/// <summary>Çıkış yalnızca POST (antiforgery korumalı) ile yapılır.</summary>
[AllowAnonymous]
public sealed class LogoutModel(SignInManager<ApplicationUser> signIn) : AppPageModel
{
    public IActionResult OnGet() => LocalRedirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            // Çerez silinir ve sunucu tarafı oturum kaydı (DbTicketStore) kaldırılır.
            await signIn.SignOutAsync();
            Flash("Çıkış yaptın.");
        }

        return LocalRedirect("/Account/Login");
    }
}
