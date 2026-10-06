using GameVault.Business.Security;
using GameVault.Entities;
using GameVault.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Account;

/// <summary>
/// Girişin ikinci adımı: doğrulayıcı uygulamadaki kod. Yalnızca parolası doğrulanmış ve ara çerezi (5 dk) olan bir
/// kullanıcı için çalışır. Hatalı kodlar Identity'nin hesap kilidine sayılır; aynı kod ikinci kez kullanılamaz.
/// </summary>
[AllowAnonymous]
public sealed class LoginWith2faModel(
    AppSignInManager signIn,
    UserManager<ApplicationUser> users,
    DbRateLimiter limiter,
    TotpReplayGuard replay) : AppPageModel
{
    public const string WrongCodeError = "Kod hatalı ya da süresi geçmiş. Uygulamadaki güncel kodu gir.";
    public const string LockedOutError = "Çok fazla hatalı deneme yapıldı. Hesabın 15 dakika boyunca kilitlendi.";
    public const string ExpiredError = "Doğrulama süresi doldu. Lütfen yeniden giriş yap.";

    [BindProperty]
    public TwoFactorCodeInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(bool rememberMe)
    {
        if (await signIn.GetTwoFactorAuthenticationUserAsync() is null)
        {
            return RedirectToPage("/Account/Login");
        }

        Input.RememberMe = rememberMe;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            FlashError(ExpiredError);
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

        var code = TotpQrCode.NormalizeCode(Input.Code);
        if (await replay.WasUsedAsync(user, code))
        {
            // Az önce kullanılmış bir kod: geçerli olsa bile kabul edilmez ve hatalı deneme sayılır.
            await users.AccessFailedAsync(user);
            Error = await users.IsLockedOutAsync(user) ? LockedOutError : WrongCodeError;
            return Page();
        }

        var result = await signIn.TwoFactorAuthenticatorSignInAsync(code, Input.RememberMe, rememberClient: false);
        if (result.Succeeded)
        {
            await replay.MarkUsedAsync(user, code);
            return LocalRedirect(SafeLocalUrl(ReturnUrl, "/Library"));
        }

        // Kilidi tetikleyen denemede Identity "Failed" döner; kullanıcı kilitlendiğini hemen öğrensin.
        Error = result.IsLockedOut || await users.IsLockedOutAsync(user) ? LockedOutError : WrongCodeError;
        return Page();
    }
}
