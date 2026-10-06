using GameVault.Business.Account;
using GameVault.Business.Security;
using GameVault.Entities;
using GameVault.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Account;

public sealed class SettingsModel(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    AccountService accounts,
    DbRateLimiter limiter) : AppPageModel
{
    public string Email { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public int ActiveSessions { get; private set; }
    public bool TwoFactorEnabled { get; private set; }

    [BindProperty]
    public ProfileInput Profile { get; set; } = new();

    [BindProperty]
    public ChangePasswordInput Password { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        Profile.DisplayName = user.DisplayName;
        await LoadAsync(user, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostProfileAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        // Yalnızca bu formun alanları doğrulanır.
        ClearStateExcept(nameof(Profile));
        if (!ModelState.IsValid)
        {
            await LoadAsync(user, ct);
            return Page();
        }

        user.DisplayName = string.IsNullOrWhiteSpace(Profile.DisplayName) ? null : Profile.DisplayName.Trim();
        await users.UpdateAsync(user);
        await signIn.RefreshSignInAsync(user); // görünen ad oturum bilgisinde de güncellenir
        Flash("Profilin güncellendi.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPasswordAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        ClearStateExcept(nameof(Password));
        if (!ModelState.IsValid)
        {
            await LoadAsync(user, ct);
            return Page();
        }

        if (!await limiter.TryAcquireAsync("sensitive-user", limiter.Options.SensitivePerUser, ClientPartition.ForUser(user.Id), ct))
        {
            ModelState.AddModelError("Password.CurrentPassword", LoginModel.RateLimitedError);
            await LoadAsync(user, ct);
            return PageWithStatus(StatusCodes.Status429TooManyRequests);
        }

        var result = await users.ChangePasswordAsync(user, Password.CurrentPassword, Password.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                var key = error.Code == nameof(IdentityErrorDescriber.PasswordMismatch)
                    ? "Password.CurrentPassword"
                    : "Password.NewPassword";
                ModelState.AddModelError(key, error.Code == nameof(IdentityErrorDescriber.PasswordMismatch) ? "Mevcut parola hatalı." : error.Description);
            }

            await LoadAsync(user, ct);
            return Page();
        }

        // Politika: parola değişince diğer tüm oturumlar kapatılır, bu oturum yenilenir.
        await accounts.RevokeSessionsAsync(user.Id, await CurrentSessionIdAsync(), ct);
        await signIn.RefreshSignInAsync(user);
        Flash("Parolan değişti. Diğer cihazlardaki oturumların kapatıldı.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSignOutOthersAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var removed = await accounts.RevokeSessionsAsync(user.Id, await CurrentSessionIdAsync(), ct);
        Flash(removed == 0 ? "Başka açık oturum yoktu." : $"{removed} oturum kapatıldı.");
        return RedirectToPage();
    }

    private async Task<Guid?> CurrentSessionIdAsync() =>
        DbTicketStore.CurrentSessionId(await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme));

    private async Task LoadAsync(ApplicationUser user, CancellationToken ct)
    {
        Email = user.Email ?? string.Empty;
        CreatedAtUtc = user.CreatedAtUtc;
        ActiveSessions = await accounts.CountActiveSessionsAsync(user.Id, ct);
        TwoFactorEnabled = await users.GetTwoFactorEnabledAsync(user);
    }

    private void ClearStateExcept(string prefix)
    {
        foreach (var key in ModelState.Keys.Where(k => !k.StartsWith(prefix + ".", StringComparison.Ordinal)).ToList())
        {
            ModelState.Remove(key);
        }
    }
}
