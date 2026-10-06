using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GameVault.DataAccess;
using GameVault.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace GameVault.Web.Infrastructure;

/// <summary>
/// Identity'nin EF Core kullanıcı deposu; yalnızca iki adımlı doğrulama sırlarının saklanma biçimi değişir.
/// Identity varsayılan olarak ikisini de <c>AspNetUserTokens</c> tablosunda düz metin tutar:
/// <list type="bullet">
/// <item><b>TOTP anahtarı</b> Data Protection ile şifrelenir. Sunucu kodu hesaplamak için anahtarın kendisine
/// ihtiyaç duyar, bu yüzden özet değil geri çözülebilir şifreleme gerekir. Data Protection anahtarları üretimde
/// veritabanı dışındaki bir sertifikayla korunur; yalnızca veritabanı dökümü anahtarı vermez.</item>
/// <item><b>Kurtarma kodları</b> tek yönlü özetlenir (PBKDF2-SHA256, kullanıcıya özel tuz). Yalnızca karşılaştırılırlar,
/// geri okunmaları gerekmez. Data Protection'a bağlı değildir: anahtar halkası kaybolsa bile kurtarma kodları çalışır
/// ve kullanıcı hesabına girip 2FA'yı yeniden kurabilir.</item>
/// </list>
/// Eski (düz metin) kayıtlar ilk okunduklarında yeni biçime çevrilir.
/// </summary>
public sealed partial class AppUserStore(
    GameVaultDbContext context,
    IDataProtectionProvider dataProtection,
    ILogger<AppUserStore> logger,
    IdentityErrorDescriber? describer = null)
    : UserStore<ApplicationUser, IdentityRole<Guid>, GameVaultDbContext, Guid>(context, describer)
{
    // Identity'nin kendi kullandığı sağlayıcı ve token adları; tablo yapısı değişmez.
    private const string InternalLoginProvider = "[AspNetUserStore]";
    private const string AuthenticatorKeyTokenName = "AuthenticatorKey";
    private const string RecoveryCodeTokenName = "RecoveryCodes";

    private const string ProtectedKeyPrefix = "dp1:";
    private const string HashedCodesPrefix = "pbkdf2-sha256$";
    internal const string ProtectorPurpose = "GameVault.TwoFactor.AuthenticatorKey.v1";

    /// <summary>
    /// Tek bir doğrulama ~30-60 ms. Kodlar ~50 bit rastgelelik taşır; bu maliyetle çevrimdışı deneme pratik değildir.
    /// Tuz kod kümesi başına tektir; bu sayede girilen kod bir kez özetlenip on özetle karşılaştırılır.
    /// </summary>
    private const int Iterations = 100_000;

    private readonly IDataProtector _protector = dataProtection.CreateProtector(ProtectorPurpose);

    public override Task SetAuthenticatorKeyAsync(ApplicationUser user, string key, CancellationToken cancellationToken) =>
        SetTokenAsync(user, InternalLoginProvider, AuthenticatorKeyTokenName, ProtectedKeyPrefix + _protector.Protect(key), cancellationToken);

    public override async Task<string?> GetAuthenticatorKeyAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var stored = await GetTokenAsync(user, InternalLoginProvider, AuthenticatorKeyTokenName, cancellationToken);
        if (string.IsNullOrEmpty(stored))
        {
            return stored;
        }

        if (stored.StartsWith(ProtectedKeyPrefix, StringComparison.Ordinal))
        {
            try
            {
                return _protector.Unprotect(stored[ProtectedKeyPrefix.Length..]);
            }
            catch (CryptographicException)
            {
                // Anahtarın kendisi loglanmaz; yalnızca operatörün fark etmesi için kullanıcı kimliği.
                LogUnreadableKey(logger, user.Id);
                // Anahtar halkası kayıp ya da kayıt bozuk: anahtar yokmuş gibi davranılır. Uygulamadaki kod doğrulanamaz;
                // kullanıcı kurtarma koduyla girip 2FA'yı yeniden kurabilir.
                return null;
            }
        }

        // Eski biçim (düz metin base32): değeri kullan ve hemen şifreli olarak yeniden yaz.
        if (LegacyKey().IsMatch(stored))
        {
            await SetAuthenticatorKeyAsync(user, stored, cancellationToken);
            await SaveChanges(cancellationToken);
            return stored;
        }

        LogUnreadableKey(logger, user.Id);
        return null;
    }

    public override Task ReplaceCodesAsync(ApplicationUser user, IEnumerable<string> recoveryCodes, CancellationToken cancellationToken) =>
        SetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, HashCodes(recoveryCodes), cancellationToken);

    public override async Task<bool> RedeemCodeAsync(ApplicationUser user, string code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        var set = await LoadCodesAsync(user, cancellationToken);
        if (set is null)
        {
            return false;
        }

        var candidate = Pbkdf2(Normalize(code), set.Salt, set.Iterations);
        var match = set.Hashes.FindIndex(h => CryptographicOperations.FixedTimeEquals(h, candidate));
        if (match < 0)
        {
            return false;
        }

        // Kullanılan kod silinir; kalanlar aynı tuzla saklanmaya devam eder. Kaydı UserManager kaydeder.
        set.Hashes.RemoveAt(match);
        await SetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, Format(set), cancellationToken);
        return true;
    }

    public override async Task<int> CountCodesAsync(ApplicationUser user, CancellationToken cancellationToken) =>
        (await LoadCodesAsync(user, cancellationToken))?.Hashes.Count ?? 0;

    private sealed record CodeSet(int Iterations, byte[] Salt, List<byte[]> Hashes);

    /// <summary>Kayıtlı kodları okur; eski düz metin listeyi bulursa özetleyip hemen yeniden yazar.</summary>
    private async Task<CodeSet?> LoadCodesAsync(ApplicationUser user, CancellationToken ct)
    {
        var stored = await GetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, ct);
        if (string.IsNullOrEmpty(stored))
        {
            return null;
        }

        if (!stored.StartsWith(HashedCodesPrefix, StringComparison.Ordinal))
        {
            var legacy = stored.Split(';', StringSplitOptions.RemoveEmptyEntries);
            var hashed = HashCodes(legacy);
            await SetTokenAsync(user, InternalLoginProvider, RecoveryCodeTokenName, hashed, ct);
            await SaveChanges(ct);
            stored = hashed;
        }

        // Biçim: pbkdf2-sha256$<tekrar>$<tuz>$<özet1>,<özet2>,...
        var parts = stored.Split('$');
        // Tekrar sayısı kayıttan okunur: ileride artırılırsa eski kodlar çalışmaya devam eder, yeni kodlar yeni değeri alır.
        if (parts.Length != 4
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var iterations)
            || iterations < 10_000)
        {
            return null;
        }

        try
        {
            var hashes = parts[3].Length == 0 ? [] : parts[3].Split(',').Select(Convert.FromBase64String).ToList();
            return new CodeSet(iterations, Convert.FromBase64String(parts[2]), hashes);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string HashCodes(IEnumerable<string> codes)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return Format(new CodeSet(Iterations, salt, codes.Select(c => Pbkdf2(Normalize(c), salt, Iterations)).ToList()));
    }

    private static string Format(CodeSet set) =>
        $"{HashedCodesPrefix}{set.Iterations}${Convert.ToBase64String(set.Salt)}${string.Join(',', set.Hashes.Select(Convert.ToBase64String))}";

    private static byte[] Pbkdf2(string code, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(code), salt, iterations, HashAlgorithmName.SHA256, 32);

    /// <summary>Kodlar büyük harfle üretilir; kullanıcı küçük harf ya da baştaki/sondaki boşlukla girse de eşleşir.</summary>
    private static string Normalize(string code) => code.Trim().ToUpperInvariant();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kullanıcı {UserId} için doğrulayıcı anahtarı çözülemedi (Data Protection anahtar halkası değişmiş ya da kayıt bozuk). Kullanıcı kurtarma koduyla girip 2FA'yı yeniden kurmalı.")]
    private static partial void LogUnreadableKey(ILogger logger, Guid userId);

    [GeneratedRegex("^[A-Z2-7]{16,}$")]
    private static partial Regex LegacyKey();
}
