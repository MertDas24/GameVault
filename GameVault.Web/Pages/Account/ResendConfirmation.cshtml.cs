using GameVault.Business.Security;
using GameVault.Entities;
using GameVault.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Account;

/// <summary>Doğrulama e-postasını yeniden gönderir; yanıt hesabın varlığını ya da durumunu belli etmez.</summary>
[AllowAnonymous]
public sealed class ResendConfirmationModel(
    UserManager<ApplicationUser> users,
    AccountEmails emails,
    DbRateLimiter limiter) : AppPageModel
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
        if (!await limiter.TryAcquireAsync("confirm-ip", o.ConfirmationPerIp, ClientPartition.ForIp(HttpContext), ct)
            || !await limiter.TryAcquireAsync("confirm-account", o.ConfirmationPerAccount, ClientPartition.ForEmail(Input.Email), ct))
        {
            Error = LoginModel.RateLimitedError;
            return PageWithStatus(StatusCodes.Status429TooManyRequests);
        }

        var user = await users.FindByEmailAsync(Input.Email);
        if (user is { EmailConfirmed: false })
        {
            await emails.SendConfirmationAsync(user, ct);
        }

        return RedirectToPage(new { sent = true });
    }
}
