using System.Net;
using GameVault.Business.Catalog;
using GameVault.Business.Covers;
using GameVault.Business.Email;

namespace GameVault.Web.Infrastructure;

public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>
    /// E-postadaki bağlantılar bu adresle kurulur. İstekteki Host başlığına güvenilmez
    /// (parola sıfırlama bağlantısı zehirlenmesine karşı).
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Tarihlerin gösterildiği saat dilimi (IANA). Veriler her zaman UTC saklanır.</summary>
    public string DisplayTimeZone { get; set; } = "Europe/Istanbul";
}

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>HTTPS yönlendirmesi. Üretimde varsayılan olarak açıktır.</summary>
    public bool UseHttpsRedirection { get; set; } = true;

    /// <summary>HSTS yalnızca Production ortamında ve HTTPS isteklerinde gönderilir.</summary>
    public bool EnableHsts { get; set; } = true;
}

public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    /// <summary>X-Forwarded-* başlıklarına güvenilecek proxy IP adresleri.</summary>
    public List<string> KnownProxies { get; set; } = [];

    /// <summary>X-Forwarded-* başlıklarına güvenilecek ağlar (CIDR, ör. 10.0.0.0/8).</summary>
    public List<string> KnownNetworks { get; set; } = [];

    public bool IsConfigured => KnownProxies.Count > 0 || KnownNetworks.Count > 0;
}

/// <summary>
/// Uygulama başlamadan önce yapılandırmayı doğrular ve eksik/geçersiz değerleri tek
/// bir anlaşılır hata mesajında toplar. Sır değerleri mesaja yazılmaz.
/// </summary>
public static class StartupValidation
{
    public static void Validate(IConfiguration config, IHostEnvironment env)
    {
        var errors = new List<string>();
        var isProduction = env.IsProduction();

        if (string.IsNullOrWhiteSpace(config.GetConnectionString("DefaultConnection")))
        {
            errors.Add("ConnectionStrings:DefaultConnection tanımlı değil (ortam değişkeni: ConnectionStrings__DefaultConnection).");
        }

        var app = config.GetSection(AppOptions.SectionName).Get<AppOptions>() ?? new AppOptions();
        if (!Uri.TryCreate(app.PublicBaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            errors.Add("App:PublicBaseUrl mutlak bir http(s) adresi olmalı (ör. https://gamevault.example.com).");
        }
        else if (isProduction && baseUri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("Üretimde App:PublicBaseUrl https olmalı.");
        }

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(app.DisplayTimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            errors.Add($"App:DisplayTimeZone geçersiz: '{app.DisplayTimeZone}'.");
        }

        var email = config.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
        if (string.IsNullOrWhiteSpace(email.Host))
        {
            errors.Add("Email:Host tanımlı değil.");
        }

        if (string.IsNullOrWhiteSpace(email.FromAddress))
        {
            errors.Add("Email:FromAddress tanımlı değil.");
        }

        if (isProduction && email.Security == SmtpSecurity.None)
        {
            errors.Add("Üretimde Email:Security 'None' olamaz (StartTls veya SslOnConnect kullanın).");
        }

        if (env.IsDevelopment() && !email.AllowNonLocalSmtpInDevelopment && !IsLocalHost(email.Host))
        {
            errors.Add("Development ortamında Email:Host yerel bir SMTP (localhost, 127.0.0.1, mailpit) olmalı; gerçek alıcılara e-posta gitmesin. Bilerek uzak SMTP kullanacaksanız Email:AllowNonLocalSmtpInDevelopment=true yapın.");
        }

        var dataProtection = config.GetSection(DataProtectionKeyOptions.SectionName).Get<DataProtectionKeyOptions>() ?? new DataProtectionKeyOptions();
        if (string.IsNullOrWhiteSpace(dataProtection.Certificate.Path))
        {
            if (isProduction)
            {
                errors.Add("Üretimde DataProtection:Certificate:Path (ve gerekiyorsa :Password) tanımlanmalı; Data Protection anahtarları şifrelenmeden saklanamaz.");
            }
        }
        else if (DataProtectionSetup.Validate(dataProtection.Certificate, "DataProtection:Certificate") is { } certError)
        {
            errors.Add(certError);
        }

        for (var i = 0; i < dataProtection.PreviousCertificates.Count; i++)
        {
            if (DataProtectionSetup.Validate(dataProtection.PreviousCertificates[i], $"DataProtection:PreviousCertificates:{i}") is { } prevError)
            {
                errors.Add(prevError);
            }
        }

        var covers = config.GetSection(CoverOptions.SectionName).Get<CoverOptions>() ?? new CoverOptions();
        foreach (var host in covers.AllowedHosts)
        {
            if (Uri.CheckHostName(host) != UriHostNameType.Dns || host.StartsWith('*'))
            {
                errors.Add($"Covers:AllowedHosts geçersiz alan adı içeriyor: '{host}'.");
            }
        }

        // Anahtar isteğe bağlıdır (yoksa entegrasyon kapalı). Adres ise anahtarın gideceği yer olduğundan https olmalı.
        var rawg = config.GetSection(RawgOptions.SectionName).Get<RawgOptions>() ?? new RawgOptions();
        if (!Uri.TryCreate(rawg.BaseUrl, UriKind.Absolute, out var rawgUri)
            || rawgUri.Scheme != Uri.UriSchemeHttps
            || !rawg.BaseUrl.EndsWith('/'))
        {
            errors.Add("Rawg:BaseUrl '/' ile biten mutlak bir https adresi olmalı.");
        }

        var proxy = config.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>() ?? new ReverseProxyOptions();
        foreach (var ip in proxy.KnownProxies)
        {
            if (!IPAddress.TryParse(ip, out _))
            {
                errors.Add($"ReverseProxy:KnownProxies geçersiz IP içeriyor: '{ip}'.");
            }
        }

        foreach (var network in proxy.KnownNetworks)
        {
            if (!System.Net.IPNetwork.TryParse(network, out _))
            {
                errors.Add($"ReverseProxy:KnownNetworks geçersiz CIDR içeriyor: '{network}'.");
            }
        }

        if (isProduction && config.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
        {
            errors.Add("Database:ApplyMigrationsOnStartup üretimde kullanılamaz; migration'ları dağıtımdan önce ayrı adımda uygulayın.");
        }

        if (errors.Count > 0)
        {
            errors = errors.Distinct().ToList();
            throw new InvalidOperationException(
                "Game Vault yapılandırması geçersiz:" + Environment.NewLine + " - " +
                string.Join(Environment.NewLine + " - ", errors));
        }
    }

    private static bool IsLocalHost(string host) =>
        host is "localhost" or "127.0.0.1" or "::1" or "mailpit";
}
