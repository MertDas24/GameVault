using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests.Infrastructure;

/// <summary>
/// RAWG API'sini taklit eder; testler gerçek RAWG'a hiçbir zaman istek atmaz. Gelen istekler
/// (adres ve başlıklar) kaydedilir, böylece RAWG'a ne gönderildiği doğrulanabilir.
/// Özel arama metinleri hata senaryolarını tetikler: "rawg-500", "rawg-slow", "rawg-badjson".
/// </summary>
public sealed class FakeRawgHandler : HttpMessageHandler
{
    public const string HollowKnightJson = """
        {
          "id": 9767,
          "name": "Hollow Knight",
          "released": "2017-02-24",
          "playtime": 26,
          "background_image": "https://media.rawg.io/media/games/4cf/4cfc6b7f1850590a4634b08bfab308ab.jpg",
          "platforms": [
            { "platform": { "id": 4, "slug": "pc", "name": "PC" } },
            { "platform": { "id": 7, "slug": "nintendo-switch", "name": "Nintendo Switch" } },
            { "platform": { "id": 18, "slug": "playstation4", "name": "PlayStation 4" } }
          ],
          "genres": [
            { "id": 4, "slug": "action", "name": "Action" },
            { "id": 83, "slug": "platformer", "name": "Platformer" },
            { "id": 51, "slug": "indie", "name": "Indie" }
          ]
        }
        """;

    // Güvenilmeyen veri: betik içeren ad, http ve yabancı alan adlı görsel, geçersiz tarih, geçersiz slug.
    private const string HostileJson = """
        {
          "id": 666,
          "name": "<script>alert('x')</script> Kötü Oyun",
          "released": "tba",
          "background_image": "http://evil.example/cover.jpg",
          "platforms": [ { "platform": { "slug": "PC; DROP TABLE", "name": "PC" } } ],
          "genres": []
        }
        """;

    private static readonly string SearchJson = $$"""{ "count": 2, "results": [ {{HollowKnightJson}}, {{HostileJson}} ] }""";

    private readonly ConcurrentQueue<HttpRequestMessage> _requests = new();

    public IReadOnlyCollection<HttpRequestMessage> Requests => _requests;

    public int CountRequests(string pathAndQueryFragment) =>
        _requests.Count(r => r.RequestUri!.PathAndQuery.Contains(pathAndQueryFragment, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _requests.Enqueue(request);
        var uri = request.RequestUri!;
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var search = query["search"];

        if (search == "rawg-slow")
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        }

        if (search == "rawg-500")
        {
            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }

        if (search == "rawg-badjson")
        {
            return Json("<html>bu json değil</html>");
        }

        if (uri.AbsolutePath.EndsWith("/games", StringComparison.Ordinal) && search is not null)
        {
            return Json(SearchJson);
        }

        if (uri.AbsolutePath.EndsWith("/games/9767", StringComparison.Ordinal))
        {
            return Json(HollowKnightJson);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

/// <summary>RAWG entegrasyonu etkin, istekleri <see cref="FakeRawgHandler"/>'a giden uygulama.</summary>
public sealed class RawgFactory : GameVaultFactory
{
    /// <summary>Gerçek bir anahtar değildir; loglara ya da sayfalara sızıp sızmadığı kontrol edilir.</summary>
    public const string TestApiKey = "test-rawg-key-not-real-7c1e";

    public FakeRawgHandler Rawg { get; } = new();

    protected override IDictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["Rawg:ApiKey"] = TestApiKey;
            settings["Rawg:TimeoutSeconds"] = "1";
            // Devre kesici testlerin birbirini etkilememesi için kapalı; ayrı bir birim testinde denenir.
            settings["Rawg:FailureCooldownSeconds"] = "0";
            settings["RateLimiting:CatalogPerUser:PermitLimit"] = "10000";
            return settings;
        }
    }

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddHttpClient(GameVault.Business.Catalog.RawgOptions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => Rawg);
}
