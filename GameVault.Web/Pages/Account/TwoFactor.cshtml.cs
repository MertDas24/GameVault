using GameVault.Business.Account;
using GameVault.Business.Security;
using GameVault.Entities;
using GameVault.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Account;

/// <summary>
/// İki adımlı doğrulama (TOTP) kurulumu ve yönetimi. Açmak, kapatmak ve yeni kurtarma kodu üretmek mevcut parolayı
/// ister: çalınmış bir oturum, hesap sahibini dışarıda bırakacak şekilde 2FA'yı açamaz ya da kapatamaz.
/// Kurtarma kodları yalnızca üretildikleri yanıtta bir kez gösterilir; TempData'ya ya da loglara yazılmaz.
/// </summary>
public sealed class TwoFactorModel(
    UserManager<ApplicationUser> users,
    AppSignInManager signIn,
    AccountService accounts,
    AccountEmails emails,
    DbRateLimiter limiter,
    TotpReplayGuard replay) : AppPageModel
{
    public const string Issuer = "Game Vault";
    public const int RecoveryCodeCount = 10;

    public bool IsEnabled { get; private set; }

    public int RecoveryCodesLeft { get; private set; }

    /// <summary>Kurulum başlatıldıysa elle girilecek anahtar (4'lü gruplar).</summary>
    public string? SharedKey { get; private set; }

    public IHtmlContent? QrCode { get; private set; }

    /// <summary>Yalnızca üretildikleri yanıtta dolu.</summary>
    public IReadOnlyList<string>? NewRecoveryCodes { get; private set; }

    [BindProperty]
    public EnableTwoFactorInput Enable { get; set; } = new();

    [BindProperty]
    public PasswordConfirmInput Confirm { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        await LoadAsync(user);
        return Page();
    }

    /// <summary>
    /// Yeni bir doğrulayıcı anahtarı üretir. Identity bu işlemde güvenlik damgasını da yeniler; bu yüzden GET değil
    /// POST'tur ve bu oturum yenilenir.
    /// </summary>
    public async Task<IActionResult> OnPostSetupAsync()
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        if (await users.GetTwoFactorEnabledAsync(user))
        {
            return RedirectToPage();
        }

        await users.ResetAuthenticatorKeyAsync(user);
        await signIn.RefreshSignInAsync(user);
        return RedirectToPage(null, null, "setup");
    }

    public async Task<IActionResult> OnPostEnableAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        KeepOnly(nameof(Enable));
        if (await users.GetTwoFactorEnabledAsync(user) || string.IsNullOrEmpty(await users.GetAuthenticatorKeyAsync(user)))
        {
            return RedirectToPage();
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(user);
            return PageWithStatus(StatusCodes.Status400BadRequest);
        }

        if (await PasswordFailureAsync(user, Enable.Password, "Enable.Password", ct) is { } failure)
        {
            await LoadAsync(user);
            return PageWithStatus(failure);
        }

        var code = TotpQrCode.NormalizeCode(Enable.Code);
        if (!await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
        {
            ModelState.AddModelError("Enable.Code", "Kod doğrulanamadı. Uygulamadaki güncel kodu gir; telefonunun saatinin doğru olduğundan emin ol.");
            await LoadAsync(user);
            return PageWithStatus(StatusCodes.Status400BadRequest);
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        NewRecoveryCodes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))?.ToList();
        await replay.MarkUsedAsync(user, code);

        // 2FA'dan önce açılmış diğer oturumlar kapatılır; bu oturum yenilenir.
        await accounts.RevokeSessionsAsync(user.Id, await CurrentSessionIdAsync(), ct);
        await signIn.RefreshSignInAsync(user);
        await emails.SendTwoFactorChangedAsync(user, enabled: true, ct);

        Enable = new();
        await LoadAsync(user);
        return Page();
    }

    public async Task<IActionResult> OnPostDisableAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        KeepOnly(nameof(Confirm));
        if (!await users.GetTwoFactorEnabledAsync(user))
        {
            return RedirectToPage();
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(user);
            return PageWithStatus(StatusCodes.Status400BadRequest);
        }

        if (await PasswordFailureAsync(user, Confirm.Password, "Confirm.Password", ct) is { } failure)
        {
            await LoadAsync(user);
            return PageWithStatus(failure);
        }

        await users.SetTwoFactorEnabledAsync(user, false);
        // Eski anahtar geçersiz olsun: tekrar açılırsa yeni bir QR kodu okutulur.
        await users.ResetAuthenticatorKeyAsync(user);
        await signIn.RefreshSignInAsync(user);
        await emails.SendTwoFactorChangedAsync(user, enabled: false, ct);
        Flash("İki adımlı doğrulama kapatıldı.");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRecoveryCodesAsync(CancellationToken ct)
    {
        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        KeepOnly(nameof(Confirm));
        if (!await users.GetTwoFactorEnabledAsync(user))
        {
            return RedirectToPage();
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(user);
            return PageWithStatus(StatusCodes.Status400BadRequest);
        }

        if (await PasswordFailureAsync(user, Confirm.Password, "Confirm.Password", ct) is { } failure)
        {
            await LoadAsync(user);
            return PageWithStatus(failure);
        }

        NewRecoveryCodes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount))?.ToList();
        Confirm = new();
        await LoadAsync(user);
        return Page();
    }

    /// <summary>
    /// Oran sınırı + parola kontrolü (hatalı parola hesap kilidine sayılır). Başarılıysa null, değilse yanıt durum kodu;
    /// hata mesajı ModelState'e yazılır.
    /// </summary>
    private async Task<int?> PasswordFailureAsync(ApplicationUser user, string password, string field, CancellationToken ct)
    {
        if (!await limiter.TryAcquireAsync("sensitive-user", limiter.Options.SensitivePerUser, ClientPartition.ForUser(user.Id), ct))
        {
            ModelState.AddModelError(field, LoginModel.RateLimitedError);
            return StatusCodes.Status429TooManyRequests;
        }

        var check = await signIn.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            ModelState.AddModelError(field, check.IsLockedOut ? LoginWith2faModel.LockedOutError : "Parola hatalı.");
            return StatusCodes.Status400BadRequest;
        }

        return null;
    }

    private async Task LoadAsync(ApplicationUser user)
    {
        IsEnabled = await users.GetTwoFactorEnabledAsync(user);
        if (IsEnabled)
        {
            RecoveryCodesLeft = await users.CountRecoveryCodesAsync(user);
            return;
        }

        var key = await users.GetAuthenticatorKeyAsync(user);
        if (!string.IsNullOrEmpty(key))
        {
            SharedKey = TotpQrCode.FormatKey(key);
            QrCode = TotpQrCode.Svg(
                TotpQrCode.AuthenticatorUri(Issuer, user.Email ?? user.Id.ToString(), key),
                "Doğrulayıcı uygulamayla okutulacak QR kodu");
        }
    }

    private async Task<Guid?> CurrentSessionIdAsync() =>
        DbTicketStore.CurrentSessionId(await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme));

    private void KeepOnly(string prefix)
    {
        foreach (var key in ModelState.Keys.Where(k => !k.StartsWith(prefix + ".", StringComparison.Ordinal)).ToList())
        {
            ModelState.Remove(key);
        }
    }
}
