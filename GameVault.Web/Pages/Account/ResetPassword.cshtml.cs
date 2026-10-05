using GameVault.Business.Account;
using GameVault.Business.Security;
using GameVault.DataAccess;
using GameVault.Entities;
using GameVault.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Web.Pages.Account;

[AllowAnonymous]
public sealed class ResetPasswordModel(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    AccountService accounts,
    GameVaultDbContext db,
    DbRateLimiter limiter) : AppPageModel
{
    public const string InvalidLinkError = "Bağlantı geçersiz ya da süresi dolmuş. Yeni bir sıfırlama bağlantısı iste.";

    [BindProperty]
    public ResetPasswordInput Input { get; set; } = new();

    public bool LinkMissing { get; private set; }

    public string? Error { get; private set; }

    public void OnGet(string? uid, string? token)
    {
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(token) || uid.Length > 64 || token.Length > 2048)
        {
            LinkMissing = true;
            return;
        }

        Input = new ResetPasswordInput { UserId = uid, Token = token };
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!await limiter.TryAcquireAsync("reset-ip", limiter.Options.ResetPasswordPerIp, ClientPartition.ForIp(HttpContext), ct))
        {
            Error = LoginModel.RateLimitedError;
            return PageWithStatus(StatusCodes.Status429TooManyRequests);
        }

        if (!Guid.TryParseExact(Input.UserId, "N", out var userId)
            || await users.FindByIdAsync(userId.ToString()) is not { } user
            || !AccountEmails.TryDecode(Input.Token, out var token))
        {
            Error = InvalidLinkError;
            return Page();
        }

        IdentityResult? result = null;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // ResetPasswordAsync güvenlik damgasını yeniler: aynı token ikinci kez kullanılamaz.
            result = await users.ResetPasswordAsync(user, token, Input.Password);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return;
            }

            // Politika: parola sıfırlanınca tüm cihazlardaki oturumlar kapatılır ve kilit kaldırılır.
            await accounts.RevokeSessionsAsync(user.Id, ct: ct);
            await users.SetLockoutEndDateAsync(user, null);
            // Sıfırlama bağlantısını açabilmek e-posta sahipliğini kanıtlar; doğrulanmamış hesap doğrulanmış sayılır.
            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
                await users.UpdateAsync(user);
            }

            await users.ResetAccessFailedCountAsync(user);
            await transaction.CommitAsync(ct);
        });

        if (result is not { Succeeded: true })
        {
            var passwordErrors = result?.Errors.Where(e => e.Code.StartsWith("Password", StringComparison.Ordinal)).ToList() ?? [];
            if (passwordErrors.Count > 0)
            {
                foreach (var error in passwordErrors)
                {
                    ModelState.AddModelError("Input.Password", error.Description);
                }
            }
            else
            {
                Error = InvalidLinkError;
            }

            return Page();
        }

        // Bu tarayıcıda başka bir oturum açıksa onu da kapat.
        if (User.Identity?.IsAuthenticated == true)
        {
            await signIn.SignOutAsync();
        }

        Flash("Parolan güncellendi ve tüm oturumların kapatıldı. Yeni parolanla giriş yapabilirsin.");
        return LocalRedirect("/Account/Login");
    }
}
