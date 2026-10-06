using System.Globalization;
using GameVault.Entities;
using Microsoft.AspNetCore.Identity;

namespace GameVault.Web.Infrastructure;

/// <summary>
/// Doğrulayıcı uygulama kodlarının tekrar kullanılmasını engeller. Identity bir kodu, saat kaymasına tolerans için
/// birkaç dakika boyunca geçerli sayar; bu sürede kodu gören biri (omuz üstünden, kayıttan) aynı kodu ikinci kez
/// kullanabilirdi. Son başarılı kod ve zamanı kullanıcının Identity token tablosunda tutulur; tüm uygulama
/// örnekleri aynı veriyi görür.
/// </summary>
public sealed class TotpReplayGuard(UserManager<ApplicationUser> users, TimeProvider time)
{
    private const string Provider = "GameVault";
    private const string Name = "LastAuthenticatorCode";

    /// <summary>Identity'nin kabul penceresinden (±2 adım × 30 sn) uzun.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    public async Task<bool> WasUsedAsync(ApplicationUser user, string code)
    {
        var stored = await users.GetAuthenticationTokenAsync(user, Provider, Name);
        if (stored?.Split('|') is not [var usedCode, var usedAt]
            || !long.TryParse(usedAt, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        var age = time.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds);
        return age < Window && string.Equals(usedCode, code, StringComparison.Ordinal);
    }

    public Task MarkUsedAsync(ApplicationUser user, string code) =>
        users.SetAuthenticationTokenAsync(user, Provider, Name,
            $"{code}|{time.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}");
}
