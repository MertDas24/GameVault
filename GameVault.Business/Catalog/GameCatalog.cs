using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GameVault.Models.Catalog;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameVault.Business.Catalog;

/// <summary>
/// Harici oyun kataloğu (RAWG). Tüm çağrılar IHttpClientFactory'nin "rawg" istemcisiyle yapılır.
/// <list type="bullet">
/// <item>RAWG'a yalnızca arama metni / oyun kimliği ve API anahtarı gider; kullanıcıya ait hiçbir bilgi gönderilmez.</item>
/// <item>Her istek zaman aşımıyla sınırlıdır. Hata, zaman aşımı ya da bozuk yanıt <see cref="CatalogStatus.Unavailable"/>
/// döner, istisna fırlatmaz; uygulamanın geri kalanı etkilenmez.</item>
/// <item>Bir hatadan sonra kısa bir süre RAWG'a hiç istek atılmaz (basit devre kesici).</item>
/// <item>Başarılı yanıtlar boyutu sınırlı bir bellek önbelleğinde tutulur. Aramadan seçilen oyun
/// önbellekten gelir, ek çağrı yapılmaz.</item>
/// <item>API anahtarı URL'de taşındığı için istek adresi hiçbir zaman loglanmaz.</item>
/// </list>
/// </summary>
public sealed partial class GameCatalog(
    IHttpClientFactory httpClients,
    IOptions<RawgOptions> options,
    TimeProvider time,
    ILogger<GameCatalog> logger) : IDisposable
{
    public const int MinQueryLength = 2;
    public const int MaxQueryLength = 100;
    public const int PageSize = 12;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    // Uygulama genelindeki önbellekten ayrı ve boyutu sınırlı: çok sayıda farklı arama belleği şişiremez.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 2000 });
    private long _unavailableUntilTicks;

    public bool IsEnabled => options.Value.Enabled;

    /// <summary>Arama metnini sadeleştirir; geçersizse (çok kısa/uzun) null döner.</summary>
    public static string? NormalizeQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var withoutControl = new string(query.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        var normalized = string.Join(' ', withoutControl.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length is >= MinQueryLength and <= MaxQueryLength ? normalized : null;
    }

    public async Task<CatalogSearchResult> SearchAsync(string query, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            return CatalogSearchResult.Of(CatalogStatus.Disabled);
        }

        var normalized = NormalizeQuery(query);
        if (normalized is null)
        {
            return CatalogSearchResult.Of(CatalogStatus.NotFound);
        }

        var cacheKey = "search:" + normalized.ToLowerInvariant();
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<CatalogGame>? cached) && cached is not null)
        {
            return new CatalogSearchResult(CatalogStatus.Ok, cached);
        }

        var (status, response) = await GetJsonAsync<RawgSearchResponse>(
            $"games?search={Uri.EscapeDataString(normalized)}&page_size={PageSize}", ct);
        if (status != CatalogStatus.Ok)
        {
            return CatalogSearchResult.Of(status == CatalogStatus.NotFound ? CatalogStatus.NotFound : CatalogStatus.Unavailable);
        }

        var games = (response!.Results ?? [])
            .Select(RawgMapping.ToCatalogGame)
            .OfType<CatalogGame>()
            .DistinctBy(g => g.RawgId)
            .Take(PageSize)
            .ToList();

        Store(cacheKey, (IReadOnlyList<CatalogGame>)games, TimeSpan.FromMinutes(options.Value.SearchCacheMinutes));
        foreach (var game in games)
        {
            Store(GameKey(game.RawgId), game, TimeSpan.FromMinutes(options.Value.GameCacheMinutes));
        }

        return new CatalogSearchResult(CatalogStatus.Ok, games);
    }

    public async Task<CatalogGameResult> GetGameAsync(int rawgId, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            return new(CatalogStatus.Disabled, null);
        }

        if (rawgId <= 0)
        {
            return new(CatalogStatus.NotFound, null);
        }

        if (_cache.TryGetValue(GameKey(rawgId), out CatalogGame? cached) && cached is not null)
        {
            return new(CatalogStatus.Ok, cached);
        }

        var (status, response) = await GetJsonAsync<RawgGame>($"games/{rawgId}", ct);
        if (status != CatalogStatus.Ok)
        {
            return new(status, null);
        }

        var game = RawgMapping.ToCatalogGame(response);
        if (game is null || game.RawgId != rawgId)
        {
            return new(CatalogStatus.NotFound, null);
        }

        Store(GameKey(rawgId), game, TimeSpan.FromMinutes(options.Value.GameCacheMinutes));
        return new(CatalogStatus.Ok, game);
    }

    public void Dispose() => _cache.Dispose();

    private static string GameKey(int rawgId) => "game:" + rawgId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private void Store<T>(string key, T value, TimeSpan ttl) =>
        _cache.Set(key, value, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = ttl });

    private bool InCooldown => time.GetUtcNow().UtcTicks < Interlocked.Read(ref _unavailableUntilTicks);

    private void TripBreaker(TimeSpan duration)
    {
        if (duration > TimeSpan.Zero)
        {
            Interlocked.Exchange(ref _unavailableUntilTicks, time.GetUtcNow().Add(duration).UtcTicks);
        }
    }

    private async Task<(CatalogStatus Status, T? Body)> GetJsonAsync<T>(string relativeUrl, CancellationToken ct)
        where T : class
    {
        if (InCooldown)
        {
            return (CatalogStatus.Unavailable, null);
        }

        var o = options.Value;
        var cooldown = TimeSpan.FromSeconds(o.FailureCooldownSeconds);
        var separator = relativeUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        var url = $"{relativeUrl}{separator}key={Uri.EscapeDataString(o.ApiKey!)}";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSeconds));
        try
        {
            var client = httpClients.CreateClient(RawgOptions.HttpClientName);
            using var response = await client.GetAsync(new Uri(url, UriKind.Relative), timeout.Token);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return (CatalogStatus.NotFound, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode;
                LogHttpFailure(logger, code);
                // Anahtar reddedildiyse (401/403) ya da kota aşıldıysa (429) daha uzun bekle.
                TripBreaker(code is 401 or 403 or 429 ? cooldown * 10 : cooldown);
                return (CatalogStatus.Unavailable, null);
            }

            var body = await response.Content.ReadFromJsonAsync<T>(Json, timeout.Token);
            if (body is null)
            {
                LogInvalidResponse(logger, "empty");
                TripBreaker(cooldown);
                return (CatalogStatus.Unavailable, null);
            }

            return (CatalogStatus.Ok, body);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            LogTimeout(logger, o.TimeoutSeconds);
            TripBreaker(cooldown);
            return (CatalogStatus.Unavailable, null);
        }
        catch (HttpRequestException ex)
        {
            // Mesaj değil yalnızca hata türü loglanır; istek adresi (ve içindeki anahtar) loga girmez.
            LogRequestFailure(logger, ex.HttpRequestError.ToString());
            TripBreaker(cooldown);
            return (CatalogStatus.Unavailable, null);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            LogInvalidResponse(logger, ex.GetType().Name);
            TripBreaker(cooldown);
            return (CatalogStatus.Unavailable, null);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAWG isteği başarısız: HTTP {StatusCode}.")]
    private static partial void LogHttpFailure(ILogger logger, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAWG isteği {TimeoutSeconds} sn içinde yanıt vermedi.")]
    private static partial void LogTimeout(ILogger logger, int timeoutSeconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAWG'a bağlanılamadı ({ErrorType}).")]
    private static partial void LogRequestFailure(ILogger logger, string errorType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RAWG yanıtı okunamadı ({Reason}).")]
    private static partial void LogInvalidResponse(ILogger logger, string reason);
}
