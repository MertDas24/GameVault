using GameVault.Business.Security;
using GameVault.Entities;
using GameVault.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Account;

/// <summary>
/// E-posta doğrulama. Bağlantıyı açmak (GET) hiçbir şeyi değiştirmez; doğrulama düğmeyle (POST) yapılır.
/// Böylece e-posta güvenlik tarayıcılarının bağlantıyı önceden açması tokenı tüketmez.
/// Başarılı doğrulamadan sonra güvenlik damgası yenilenir: aynı bağlantı ikinci kez çalışmaz.
/// </summary>
[AllowAnonymous]
public sealed class ConfirmEmailModel(UserManager<ApplicationUser> users, DbRateLimiter limiter) : AppPageModel
{
    public const string InvalidLinkError = "Doğrulama bağlantısı geçersiz, süresi dolmuş ya da daha önce kullanılmış.";

    [BindProperty(SupportsGet = true)]
    public string? Uid { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    public string? Error { get; private set; }

    public bool LinkLooksValid =>
        !string.IsNullOrEmpty(Uid) && !string.IsNullOrEmpty(Token) && Uid.Length <= 64 && Token.Length <= 2048;

    public void OnGet()
    {
        if (!LinkLooksValid)
        {
            Error = InvalidLinkError;
        }
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!await limiter.TryAcquireAsync("confirm-ip", limiter.Options.ConfirmationPerIp, ClientPartition.ForIp(HttpContext), ct))
        {
            Error = LoginModel.RateLimitedError;
            return PageWithStatus(StatusCodes.Status429TooManyRequests);
        }

        if (!LinkLooksValid
            || !Guid.TryParseExact(Uid, "N", out var userId)
            || await users.FindByIdAsync(userId.ToString()) is not { } user
            || !AccountEmails.TryDecode(Token!, out var token))
        {
            Error = InvalidLinkError;
            return Page();
        }

        var result = await users.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            Error = InvalidLinkError;
            return Page();
        }

        await users.UpdateSecurityStampAsync(user);
        Flash("E-posta adresin doğrulandı. Artık giriş yapabilirsin.");
        return LocalRedirect("/Account/Login");
    }
}
