using GameVault.Business.Security;
using GameVault.Entities;
using GameVault.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Account;

[AllowAnonymous]
public sealed class ForgotPasswordModel(
    UserManager<ApplicationUser> users,
    DbRateLimiter limiter,
    AccountEmails emails) : AppPageModel
{
    [BindProperty]
    public EmailInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public bool Sent { get; set; }

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var o = limiter.Options;
        if (!await limiter.TryAcquireAsync("forgot-ip", o.ForgotPasswordPerIp, ClientPartition.ForIp(HttpContext), ct)
            || !await limiter.TryAcquireAsync("forgot-account", o.ForgotPasswordPerAccount, ClientPartition.ForEmail(Input.Email), ct))
        {
            Error = LoginModel.RateLimitedError;
            return PageWithStatus(StatusCodes.Status429TooManyRequests);
        }

        var user = await users.FindByEmailAsync(Input.Email);
        if (user?.Email is not null)
        {
            await emails.SendPasswordResetAsync(user, ct);
        }

        // Hesap olsun ya da olmasın aynı yanıt (PRG ile).
        return RedirectToPage(new { sent = true });
    }
}
