using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GameVault.DataAccess;
using Microsoft.AspNetCore.DataProtection;

namespace GameVault.Web.Infrastructure;

public sealed class CertificateReference
{
    /// <summary>PKCS#12 (.pfx) dosya yolu. Veritabanı dışında, salt okunur bir sır olarak bağlanmalı (ör. Docker/Kubernetes secret).</summary>
    public string? Path { get; set; }

    /// <summary>PFX parolası. Ortam değişkeni ya da sır yöneticisiyle verilir; asla depoya yazılmaz.</summary>
    public string? Password { get; set; }
}

public sealed class DataProtectionKeyOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>Yeni anahtarları şifrelemek için kullanılan güncel sertifika.</summary>
    public CertificateReference Certificate { get; set; } = new();

    /// <summary>Rotasyon sonrası eski anahtarları çözmek için tutulan önceki sertifikalar.</summary>
    public List<CertificateReference> PreviousCertificates { get; set; } = [];
}

/// <summary>
/// Data Protection anahtar halkası veritabanında (tüm instance'lar paylaşır) ve saklama sırasında bir
/// X.509 sertifikasıyla şifrelenmiş olarak tutulur. Sertifika veritabanında değil, ayrı bir sır olarak durur;
/// böylece yalnızca veritabanı dökümüne erişen biri çerez ya da token üretemez.
/// </summary>
public static class DataProtectionSetup
{
    public static void Configure(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var options = config.GetSection(DataProtectionKeyOptions.SectionName).Get<DataProtectionKeyOptions>() ?? new DataProtectionKeyOptions();
        var builder = services.AddDataProtection()
            .SetApplicationName("GameVault")
            .PersistKeysToDbContext<GameVaultDbContext>();

        if (string.IsNullOrWhiteSpace(options.Certificate.Path))
        {
            // Yalnızca Production dışında: StartupValidation Production'da sertifikasız başlamayı reddeder.
            return;
        }

        var current = Load(options.Certificate);
        var previous = options.PreviousCertificates.Where(c => !string.IsNullOrWhiteSpace(c.Path)).Select(Load).ToArray();
        builder.ProtectKeysWithCertificate(current);
        builder.UnprotectKeysWithAnyCertificate([current, .. previous]);
        _ = env;
    }

    public static X509Certificate2 Load(CertificateReference reference) =>
        X509CertificateLoader.LoadPkcs12FromFile(reference.Path!, reference.Password, X509KeyStorageFlags.EphemeralKeySet);

    /// <summary>Başlangıç doğrulaması için: sertifika yüklenebiliyor mu, RSA özel anahtarı var mı?</summary>
    public static string? Validate(CertificateReference reference, string key)
    {
        if (!File.Exists(reference.Path))
        {
            return $"{key}:Path ile belirtilen sertifika dosyası bulunamadı.";
        }

        try
        {
            using var cert = Load(reference);
            using var rsa = cert.GetRSAPrivateKey();
            return rsa is null ? $"{key} sertifikası RSA özel anahtarı içermiyor." : null;
        }
        catch (CryptographicException)
        {
            return $"{key} sertifikası açılamadı (dosya biçimi ya da parola hatalı).";
        }
    }
}
