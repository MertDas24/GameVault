using GameVault.Business.Security;
using GameVault.Entities;
using GameVault.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Account;

/// <summary>
/// Doğrulayıcı uygulamaya erişilemediğinde ikinci adım için tek kullanımlık kurtarma kodu. Kullanılan kod silinir
/// (Identity); az kod kaldıysa kullanıcı yenilerini oluşturması için uyarılır.
/// </summary>
[AllowAnonymous]
public sealed class LoginWithRecoveryCodeModel(
    AppSignInManager signIn,
    UserManager<ApplicationUser> users,
    DbRateLimiter limiter) : AppPageModel
{
    public const string WrongCodeError = "Kurtarma kodu hatalı ya da daha önce kullanılmış.";

    /// <summary>Bu sayıda ya da daha az kod kaldığında uyarı gösterilir.</summary>
    public const int LowCodeWarning = 3;

    [BindProperty]
    public RecoveryCodeInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync() =>
        await signIn.GetTwoFactorAuthenticationUserAsync() is null ? RedirectToPage("/Account/Login") : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            FlashError(LoginWith2faModel.ExpiredError);
            return RedirectToPage("/Account/Login", new { returnUrl = SafeLocalUrl(ReturnUrl, "/Library") });
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var o = limiter.Options;
        if (!await limiter.TryAcquireAsync("2fa-ip", o.LoginPerIp, ClientPartition.ForIp(HttpContext), ct)
            || !await limiter.TryAcquireAsync("2fa-account", o.LoginPerAccount, ClientPartition.ForUser(user.Id), ct))
        {
            Error = LoginModel.RateLimitedError;
            return PageWithStatus(StatusCodes.Status429TooManyRequests);
        }

        // Kodlar "ABCDE-12345" biçimindedir; boşluklar atılır, tire korunur.
        var code = string.Concat(Input.Code.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        var result = await signIn.TwoFactorRecoveryCodeSignInAsync(code);
        if (result.Succeeded)
        {
            var left = await users.CountRecoveryCodesAsync(user);
            if (left <= LowCodeWarning)
            {
                FlashError(left == 0
                    ? "Son kurtarma kodunu kullandın. Hesap ayarlarından hemen yeni kodlar oluştur."
                    : $"Yalnızca {left} kurtarma kodun kaldı. Hesap ayarlarından yeni kodlar oluşturabilirsin.");
            }

            return LocalRedirect(SafeLocalUrl(ReturnUrl, "/Library"));
        }

        Error = result.IsLockedOut || await users.IsLockedOutAsync(user) ? LoginWith2faModel.LockedOutError : WrongCodeError;
        return Page();
    }
}
