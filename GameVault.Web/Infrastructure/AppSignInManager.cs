using GameVault.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GameVault.Web.Infrastructure;

/// <summary>
/// Giriş sayfası parolayı kendisi doğruladığı için (bkz. LoginModel) Identity'nin PasswordSignInAsync akışı
/// kullanılmaz. Bu sınıf, Identity'nin "giriş yap ya da ikinci adımı iste" mantığını (korumalı
/// <see cref="SignInManager{TUser}.SignInOrTwoFactorAsync"/>) açar; böylece iki adımlı doğrulama çerezi
/// Identity'nin kendi biçimiyle oluşturulur ve TwoFactorAuthenticatorSignInAsync onu okuyabilir.
/// </summary>
public sealed class AppSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    /// <summary>
    /// Parolası doğrulanmış kullanıcıyı oturum açtırır; iki adımlı doğrulama açıksa yalnızca kısa ömürlü
    /// ara çerezi yazar ve <see cref="SignInResult.RequiresTwoFactor"/> döner.
    /// </summary>
    public Task<SignInResult> SignInOrRequireTwoFactorAsync(ApplicationUser user, bool isPersistent) =>
        SignInOrTwoFactorAsync(user, isPersistent, loginProvider: null, bypassTwoFactor: false);
}
