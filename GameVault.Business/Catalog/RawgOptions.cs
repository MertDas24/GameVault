using System.ComponentModel.DataAnnotations;

namespace GameVault.Business.Catalog;

/// <summary>
/// RAWG (https://rawg.io/apidocs) entegrasyonu ayarları. API anahtarı kaynak kodda ya da
/// appsettings dosyalarında tutulmaz: yerelde User Secrets, diğer ortamlarda ortam değişkeni
/// (Rawg__ApiKey) ile verilir. Anahtar yoksa entegrasyon kapalıdır ve uygulama normal çalışır.
/// </summary>
public sealed class RawgOptions
{
    public const string SectionName = "Rawg";

    /// <summary>IHttpClientFactory'deki adlandırılmış istemci.</summary>
    public const string HttpClientName = "rawg";

    /// <summary>RAWG kapak görsellerinin sunulduğu alan adı (CSP ve kapak izin listesine eklenir).</summary>
    public const string CoverHost = "media.rawg.io";

    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://api.rawg.io/api/";

    /// <summary>Tek bir RAWG isteği için en uzun bekleme süresi.</summary>
    [Range(1, 30)]
    public int TimeoutSeconds { get; set; } = 5;

    /// <summary>Bir hatadan sonra RAWG'a yeniden istek atılmadan beklenecek süre (basit devre kesici).</summary>
    [Range(0, 3600)]
    public int FailureCooldownSeconds { get; set; } = 30;

    [Range(1, 1440)]
    public int SearchCacheMinutes { get; set; } = 60;

    [Range(1, 10080)]
    public int GameCacheMinutes { get; set; } = 1440;

    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);
}
