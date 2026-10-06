using GameVault.Business.Security;
using GameVault.Entities;
using GameVault.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Account;

/// <summary>
/// Giriş. Hesap yoksa, kilitliyse ya da parola yanlışsa aynı mesaj döner ve her durumda bir parola özeti
/// doğrulaması yapılır. Hesabın doğrulanmamış olduğu bilgisi yalnızca doğru parolayla verilir (bu bilgiye
/// yalnızca parolayı bilen biri ulaşabilir). Identity'nin PasswordSignInAsync akışı, doğrulanmamış hesabı
/// parola kontrolünden önce bildirdiği için kullanılmaz.
/// </summary>
[AllowAnonymous]
public sealed class LoginModel(
    AppSignInManager signIn,
    UserManager<ApplicationUser> users,
    DbRateLimiter limiter,
    LoginTiming timing) : AppPageModel
{
    public const string GenericError = "E-posta veya parola hatalı.";
    public const string RateLimitedError = "Çok fazla deneme yapıldı. Lütfen birkaç dakika sonra tekrar dene.";
    public const string UnconfirmedError = "E-posta adresin henüz doğrulanmadı. Gelen kutundaki bağlantıyı aç ya da yeni bir bağlantı iste.";

    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public bool ShowResendLink { get; private set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(SafeLocalUrl(ReturnUrl, "/Library"));
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var o = limiter.Options;
        if (!await limiter.TryAcquireAsync("login-ip", o.LoginPerIp, ClientPartition.ForIp(HttpContext), ct)
            || !await limiter.TryAcquireAsync("login-account", o.LoginPerAccount, ClientPartition.ForEmail(Input.Email), ct))
        {
            Error = RateLimitedError;
            return PageWithStatus(StatusCodes.Status429TooManyRequests);
        }

        var user = await users.FindByEmailAsync(Input.Email);
        if (user is null || await users.IsLockedOutAsync(user))
        {
            timing.Equalize(Input.Password);
            Error = GenericError;
            return Page();
        }

        // CheckPasswordAsync gerekirse eski (düşük iterasyonlu) özeti yeniden hesaplar.
        if (!await users.CheckPasswordAsync(user, Input.Password))
        {
            await users.AccessFailedAsync(user); // eşiğe ulaşınca hesabı kilitler
            Error = GenericError;
            return Page();
        }

        if (!user.EmailConfirmed)
        {
            Error = UnconfirmedError;
            ShowResendLink = true;
            return Page();
        }

        // İki adımlı doğrulama açıksa hatalı deneme sayacı burada sıfırlanmaz: parolayı bilen biri, her seferinde
        // parolayı yeniden girerek ikinci adımdaki kod denemelerinin kilidini sıfırlayamasın.
        var twoFactor = await users.GetTwoFactorEnabledAsync(user);
        if (!twoFactor)
        {
            await users.ResetAccessFailedCountAsync(user);
        }

        var result = await signIn.SignInOrRequireTwoFactorAsync(user, Input.RememberMe);
        if (result.RequiresTwoFactor)
        {
            var returnUrl = SafeLocalUrl(ReturnUrl, "/Library");
            return LocalRedirect($"/Account/LoginWith2fa?returnUrl={Uri.EscapeDataString(returnUrl)}&rememberMe={Input.RememberMe}");
        }

        return LocalRedirect(SafeLocalUrl(ReturnUrl, "/Library"));
    }
}
