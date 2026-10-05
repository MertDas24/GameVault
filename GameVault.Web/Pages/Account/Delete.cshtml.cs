using GameVault.Business.Account;
using GameVault.Business.Library;
using GameVault.Business.Security;
using GameVault.Entities;
using GameVault.Web.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Account;

public sealed class DeleteModel(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    AccountService accounts,
    LibraryService library,
    DbRateLimiter limiter) : AppPageModel
{
    [BindProperty]
    public DeleteAccountInput Input { get; set; } = new();

    public int GameCount { get; private set; }

    public async Task OnGetAsync(CancellationToken ct) =>
        GameCount = await library.CountAsync(CurrentUserId, ct);

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        GameCount = await library.CountAsync(user.Id, ct);
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!await limiter.TryAcquireAsync("sensitive-user", limiter.Options.SensitivePerUser, ClientPartition.ForUser(user.Id), ct))
        {
            ModelState.AddModelError(string.Empty, LoginModel.RateLimitedError);
            return PageWithStatus(StatusCodes.Status429TooManyRequests);
        }

        // Yeniden kimlik doğrulama: başarısız denemeler hesap kilidi sayacına eklenir.
        var check = await signIn.CheckPasswordSignInAsync(user, Input.Password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            ModelState.AddModelError("Input.Password", "Parola hatalı.");
            return Page();
        }

        if (!string.Equals(Input.ConfirmEmail.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Input.ConfirmEmail", "Yazdığın adres hesabının e-posta adresiyle eşleşmiyor.");
            return Page();
        }

        if (!await accounts.DeleteAccountAsync(user.Id, ct))
        {
            ModelState.AddModelError(string.Empty, "Hesap silinemedi. Lütfen tekrar dene.");
            return Page();
        }

        await signIn.SignOutAsync();
        Flash("Hesabın ve tüm verilerin kalıcı olarak silindi.");
        return LocalRedirect("/");
    }
}
