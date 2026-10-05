using GameVault.Business.Security;
using GameVault.Entities;
using GameVault.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Account;

/// <summary>
/// Kayıt. Yanıt, adresin daha önce kayıtlı olup olmamasından bağımsızdır: her iki durumda da aynı
/// "e-postanı kontrol et" sayfasına yönlendirilir. Kayıtlı adrese bilgilendirme (ya da doğrulanmamışsa
/// yeni doğrulama) e-postası gider. Parola kuralları, adres kontrolünden önce herkes için uygulanır.
/// </summary>
[AllowAnonymous]
public sealed class RegisterModel(
    UserManager<ApplicationUser> users,
    AccountEmails emails,
    DbRateLimiter limiter,
    LoginTiming timing,
    TimeProvider time) : AppPageModel
{
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public bool Submitted { get; set; }

    public string? Error { get; private set; }

    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? LocalRedirect("/Library") : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!await limiter.TryAcquireAsync("register-ip", limiter.Options.RegisterPerIp, ClientPartition.ForIp(HttpContext), ct))
        {
            Error = LoginModel.RateLimitedError;
            return PageWithStatus(StatusCodes.Status429TooManyRequests);
        }

        var email = Input.Email.Trim();
        var candidate = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(Input.DisplayName) ? null : Input.DisplayName.Trim(),
            CreatedAtUtc = time.GetUtcNow().UtcDateTime
        };

        // Parola kuralları hesap varlığından bağımsız olarak önce uygulanır.
        var passwordErrors = new List<IdentityError>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, candidate, Input.Password);
            passwordErrors.AddRange(result.Errors);
        }

        if (passwordErrors.Count > 0)
        {
            foreach (var error in passwordErrors)
            {
                ModelState.AddModelError("Input.Password", error.Description);
            }

            return Page();
        }

        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            // Yeni hesap oluştururken yapılan parola özeti maliyeti burada da harcanır (zamanlama farkını azaltır).
            timing.Equalize(Input.Password);
            if (existing.EmailConfirmed)
            {
                await emails.SendAccountExistsAsync(existing, ct);
            }
            else
            {
                await emails.SendConfirmationAsync(existing, ct);
            }

            return RedirectToPage(new { submitted = true });
        }

        var created = await users.CreateAsync(candidate, Input.Password);
        if (!created.Succeeded)
        {
            // Eşzamanlı aynı adresle kayıt dışında beklenmez; ayrıntı verilmeden aynı yanıt döner.
            if (created.Errors.All(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)))
            {
                return RedirectToPage(new { submitted = true });
            }

            foreach (var error in created.Errors)
            {
                ModelState.AddModelError("Input.Email", error.Description);
            }

            return Page();
        }

        await emails.SendConfirmationAsync(candidate, ct);
        return RedirectToPage(new { submitted = true });
    }
}
