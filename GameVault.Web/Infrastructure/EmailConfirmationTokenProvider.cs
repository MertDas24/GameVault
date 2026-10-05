using GameVault.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GameVault.Web.Infrastructure;

public sealed class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public EmailConfirmationTokenProviderOptions()
    {
        Name = EmailConfirmationTokenProvider.ProviderName;
        TokenLifespan = EmailConfirmationTokenProvider.Lifespan;
    }
}

/// <summary>
/// E-posta doğrulama tokenları için ayrı sağlayıcı: parola sıfırlamadan (30 dk) bağımsız, daha uzun bir
/// geçerlilik süresi kullanır. Token güvenlik damgasına bağlıdır; doğrulama sonrası damga yenilenir.
/// </summary>
public sealed class EmailConfirmationTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<EmailConfirmationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger)
{
    public const string ProviderName = "GameVaultEmailConfirmation";
    public static readonly TimeSpan Lifespan = TimeSpan.FromHours(24);
}
