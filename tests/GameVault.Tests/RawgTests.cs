using System.Net;
using GameVault.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Tests;

/// <summary>RAWG entegrasyonu: arama, form doldurma, RAWG kimliğinin saklanması, hata dayanıklılığı ve veri gizliliği.</summary>
public sealed class RawgTests(RawgFactory factory) : IClassFixture<RawgFactory>
{
    private const int HollowKnightId = 9767;
    private const string CropCover = "https://media.rawg.io/media/crop/600/400/games/4cf/4cfc6b7f1850590a4634b08bfab308ab.jpg";

    private async Task<(AppClient Client, string Email)> NewUserAsync()
    {
        var email = $"rawg-{Guid.NewGuid():N}@example.test";
        var client = factory.CreateAppClient();
        await client.RegisterAsync(email);
        return (client, email);
    }

    private static Dictionary<string, string> RawgGameFields(int platformId, int rawgId = HollowKnightId) =>
        AppClient.GameFields("Hollow Knight", platformId, "Playing",
            ("Input.RawgId", rawgId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Input.ReleaseDate", "2017-02-24"),
            ("Input.CoverChoice", "url"),
            ("Input.CoverUrl", CropCover));

    [Fact]
    public async Task Search_shows_metadata_and_sends_only_the_search_term_to_rawg()
    {
        var (client, email) = await NewUserAsync();
        using (client)
        {
            var (response, doc) = await client.GetPageAsync("/Library/Search?q=Hollow%20%20Knight");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var items = doc.QuerySelectorAll(".catalog-item");
            Assert.Equal(2, items.Length);
            var first = items[0];
            Assert.Equal("Hollow Knight", first.QuerySelector(".catalog-title")!.TextContent);
            Assert.Equal("24 Şubat 2017", first.QuerySelector(".catalog-date")!.TextContent);
            Assert.Contains("Nintendo Switch", first.TextContent, StringComparison.Ordinal);
            Assert.Contains("Platformer", first.TextContent, StringComparison.Ordinal);
            Assert.Equal(CropCover, first.QuerySelector("img")!.GetAttribute("src"));
            Assert.Equal($"/Library/New?rawgId={HollowKnightId}", first.QuerySelector("a.btn")!.GetAttribute("href"));
            Assert.Contains("rawg.io", doc.QuerySelector(".attribution")!.InnerHtml, StringComparison.Ordinal);

            // Güvenilmeyen RAWG verisi: betik kodlanır, http/yabancı görsel ve geçersiz tarih atılır.
            // Sunucunun gönderdiği ham HTML kontrol edilir (AngleSharp yeniden yazarken kodlamayı çözer).
            var html = await (await client.Http.GetAsync("/Library/Search?q=Hollow%20%20Knight")).Content.ReadAsStringAsync();
            Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
            Assert.Contains("&lt;script&gt;alert", html, StringComparison.Ordinal);
            Assert.DoesNotContain("evil.example", html, StringComparison.Ordinal);
            Assert.Null(items[1].QuerySelector("img"));
            Assert.Equal("Çıkış tarihi bilinmiyor", items[1].QuerySelector(".catalog-date")!.TextContent);

            // RAWG'a giden istek: yalnızca arama metni, sayfa boyutu ve API anahtarı. Çerez, kimlik ya da e-posta yok.
            var request = factory.Rawg.Requests.Last(r => r.RequestUri!.Query.Contains("Hollow", StringComparison.Ordinal));
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            Assert.Equal(["key", "page_size", "search"], query.AllKeys.Order(StringComparer.Ordinal));
            Assert.Equal("Hollow Knight", query["search"]);
            Assert.Equal("https", request.RequestUri.Scheme);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.Null(request.Headers.Authorization);
            Assert.DoesNotContain(email, request.RequestUri.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Repeated_searches_and_selecting_a_result_are_served_from_cache()
    {
        var (client, _) = await NewUserAsync();
        using (client)
        {
            await client.GetPageAsync("/Library/Search?q=onbellek-testi");
            await client.GetPageAsync("/Library/Search?q=ONBELLEK-TESTI");
            Assert.Equal(1, factory.Rawg.CountRequests("search=onbellek-testi"));

            // Sonuç arama sırasında önbelleğe alındı; oyunu seçmek RAWG'a yeni istek atmaz.
            var before = factory.Rawg.CountRequests($"/games/{HollowKnightId}");
            var (response, _) = await client.GetPageAsync($"/Library/New?rawgId={HollowKnightId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(before, factory.Rawg.CountRequests($"/games/{HollowKnightId}"));
        }
    }

    [Fact]
    public async Task Selecting_a_result_prefills_the_form_with_mapped_values()
    {
        var (client, _) = await NewUserAsync();
        using (client)
        {
            var pcId = await client.GetPlatformIdAsync("PC");
            var actionId = await client.GetGenreIdAsync("Aksiyon");
            var platformerId = await client.GetGenreIdAsync("Platform");

            var (response, doc) = await client.GetPageAsync($"/Library/New?rawgId={HollowKnightId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Hollow Knight", doc.QuerySelector("#Input_Title")!.GetAttribute("value"));
            Assert.Equal("2017-02-24", doc.QuerySelector("#Input_ReleaseDate")!.GetAttribute("value"));
            Assert.Equal(HollowKnightId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                doc.QuerySelector("input[type=hidden][name='Input.RawgId']")!.GetAttribute("value"));
            Assert.Equal(CropCover, doc.QuerySelector("#Input_CoverUrl")!.GetAttribute("value"));
            Assert.NotNull(doc.QuerySelector("input[name='Input.CoverChoice'][value=url][checked]"));

            // PC, RAWG'daki üç platform arasında önceliklidir.
            var selectedPlatform = doc.QuerySelector("select[name='Input.PlatformId'] option[selected]")!;
            Assert.Equal(pcId.ToString(System.Globalization.CultureInfo.InvariantCulture), selectedPlatform.GetAttribute("value"));

            // "Indie" GameVault'ta karşılığı olmadığı için eşlenmez.
            var checkedGenres = doc.QuerySelectorAll("input[name='Input.GenreIds'][checked]")
                .Select(i => int.Parse(i.GetAttribute("value")!, System.Globalization.CultureInfo.InvariantCulture))
                .Order()
                .ToList();
            Assert.Equal(new[] { actionId, platformerId }.Order(), checkedGenres);
        }
    }

    [Fact]
    public async Task Adding_from_rawg_stores_the_rawg_id_and_the_same_game_cannot_be_added_twice()
    {
        var (client, _) = await NewUserAsync();
        var (other, _) = await NewUserAsync();
        using (client)
        using (other)
        {
            var platformId = await client.GetPlatformIdAsync();

            var created = await client.PostFormAsync("/Library/New", RawgGameFields(platformId));
            Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
            var gameId = Guid.Parse(created.Headers.Location!.OriginalString.Split('/').Last());

            var game = await factory.WithDbAsync(db => db.LibraryGames.AsNoTracking().SingleAsync(g => g.Id == gameId));
            Assert.Equal(HollowKnightId, game.RawgId);
            Assert.Equal(new DateOnly(2017, 2, 24), game.ReleaseDate);
            Assert.Equal(CropCover, game.CoverImageUrl);

            // İkinci ekleme reddedilir ve kayıt sayısı değişmez.
            var duplicate = await client.PostFormAsync("/Library/New", RawgGameFields(platformId));
            Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
            var page = await client.ParseAsync(duplicate);
            Assert.Contains("Bu oyun zaten kütüphanende.", page.QuerySelector(".alert-error")!.TextContent, StringComparison.Ordinal);
            var userId = game.UserId;
            Assert.Equal(1, await factory.WithDbAsync(db => db.LibraryGames.CountAsync(g => g.UserId == userId && g.RawgId == HollowKnightId)));

            // Seç bağlantısı mevcut kayda yönlendirir; arama sonucunda "Kütüphanende" görünür.
            var select = await client.Http.GetAsync($"/Library/New?rawgId={HollowKnightId}");
            select.AssertRedirectTo($"/Library/{gameId}");
            var (_, search) = await client.GetPageAsync("/Library/Search?q=Hollow");
            Assert.Equal($"/Library/{gameId}", search.QuerySelectorAll(".catalog-item")[0].QuerySelector("a.btn")!.GetAttribute("href"));

            // Kural kullanıcı başınadır: başka bir kullanıcı aynı oyunu ekleyebilir.
            var otherCreated = await other.PostFormAsync("/Library/New", RawgGameFields(platformId));
            Assert.Equal(HttpStatusCode.Redirect, otherCreated.StatusCode);
        }
    }

    [Fact]
    public async Task Database_rejects_duplicate_rawg_ids_even_without_the_service_check()
    {
        var (client, _) = await NewUserAsync();
        using (client)
        {
            var platformId = await client.GetPlatformIdAsync();
            var gameId = await client.CreateGameAsync("Benzersiz", platformId, "NotStarted", ("Input.RawgId", "4242"));
            var userId = await factory.WithDbAsync(db => db.LibraryGames.Where(g => g.Id == gameId).Select(g => g.UserId).SingleAsync());

            await Assert.ThrowsAsync<DbUpdateException>(() => factory.WithDbAsync(async db =>
            {
                db.LibraryGames.Add(new Entities.LibraryGame
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    Title = "Kopya",
                    PlatformId = platformId,
                    RawgId = 4242,
                    DateAdded = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }));
        }
    }

    [Fact]
    public async Task Rawg_id_cannot_be_changed_or_injected_on_edit()
    {
        var (client, _) = await NewUserAsync();
        using (client)
        {
            var platformId = await client.GetPlatformIdAsync();
            var created = await client.PostFormAsync("/Library/New", RawgGameFields(platformId));
            var gameId = Guid.Parse(created.Headers.Location!.OriginalString.Split('/').Last());
            var manual = await client.CreateGameAsync("Elle eklenen", platformId);

            var fields = AppClient.GameFields("Yeni ad", platformId, "Completed", ("Input.RawgId", "1"));
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync($"/Library/{gameId}/Edit", fields, tokenPage: "/Library/New")).StatusCode);
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync($"/Library/{manual}/Edit", fields, tokenPage: "/Library/New")).StatusCode);

            Assert.Equal(HollowKnightId, await factory.WithDbAsync(db => db.LibraryGames.Where(g => g.Id == gameId).Select(g => g.RawgId).SingleAsync()));
            Assert.Null(await factory.WithDbAsync(db => db.LibraryGames.Where(g => g.Id == manual).Select(g => g.RawgId).SingleAsync()));
        }
    }

    [Theory]
    [InlineData("rawg-500")]
    [InlineData("rawg-slow")]
    [InlineData("rawg-badjson")]
    public async Task Rawg_failures_show_a_message_and_the_rest_of_the_app_keeps_working(string query)
    {
        var (client, _) = await NewUserAsync();
        using (client)
        {
            var (response, doc) = await client.GetPageAsync($"/Library/Search?q={query}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("RAWG'a şu an ulaşılamıyor", doc.QuerySelector(".alert-error")!.TextContent, StringComparison.Ordinal);
            Assert.Empty(doc.QuerySelectorAll(".catalog-item"));

            // Kütüphane ve elle ekleme RAWG'dan bağımsız çalışır.
            Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/Library")).StatusCode);
            var platformId = await client.GetPlatformIdAsync();
            await client.CreateGameAsync("RAWG olmadan eklenen", platformId);
        }
    }

    [Fact]
    public async Task Unknown_rawg_id_falls_back_to_an_empty_form()
    {
        var (client, _) = await NewUserAsync();
        using (client)
        {
            var (response, doc) = await client.GetPageAsync("/Library/New?rawgId=123456");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("bulunamadı", doc.QuerySelector(".alert-error")!.TextContent, StringComparison.Ordinal);
            Assert.Equal(string.Empty, doc.QuerySelector("#Input_Title")!.GetAttribute("value") ?? string.Empty);
            Assert.Null(doc.QuerySelector("input[name='Input.RawgId']"));
        }
    }

    [Theory]
    [InlineData("a")]
    [InlineData("   ")]
    public async Task Invalid_queries_are_not_sent_to_rawg(string query)
    {
        var (client, _) = await NewUserAsync();
        using (client)
        {
            var before = factory.Rawg.Requests.Count;
            var (response, _) = await client.GetPageAsync($"/Library/Search?q={Uri.EscapeDataString(query)}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(before, factory.Rawg.Requests.Count);
        }
    }

    [Fact]
    public async Task Api_key_never_appears_in_logs_or_pages()
    {
        var (client, _) = await NewUserAsync();
        using (client)
        {
            var (_, search) = await client.GetPageAsync("/Library/Search?q=gizli-anahtar-testi");
            var (_, failed) = await client.GetPageAsync("/Library/Search?q=rawg-500");
            var (_, form) = await client.GetPageAsync($"/Library/New?rawgId={HollowKnightId}");

            Assert.Contains(factory.Rawg.Requests, r => r.RequestUri!.Query.Contains(RawgFactory.TestApiKey, StringComparison.Ordinal));
            foreach (var html in new[] { search, failed, form }.Select(d => d.DocumentElement.OuterHtml))
            {
                Assert.DoesNotContain(RawgFactory.TestApiKey, html, StringComparison.Ordinal);
            }

            Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains(RawgFactory.TestApiKey, StringComparison.Ordinal));
            // .NET zaten URL'deki query'yi loglarda maskeler; ikinci katman olarak RAWG istemcisi hiç istek logu yazmaz.
            Assert.DoesNotContain(factory.Logs.Lines, line => line.Contains("System.Net.Http.HttpClient.rawg", StringComparison.Ordinal));
            Assert.Contains(factory.Logs.Lines, line => line.Contains("RAWG isteği başarısız: HTTP 500", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Csp_allows_rawg_cover_images()
    {
        using var client = factory.CreateAppClient();
        var response = await client.Http.GetAsync("/Account/Login");
        Assert.Contains("img-src 'self' https://images.example.com https://media.rawg.io",
            response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_page_requires_sign_in()
    {
        using var client = factory.CreateAppClient();
        var before = factory.Rawg.Requests.Count;
        (await client.Http.GetAsync("/Library/Search?q=anonim-arama")).AssertRedirectToLogin();
        Assert.Equal(before, factory.Rawg.Requests.Count);
    }
}

/// <summary>API anahtarı yoksa entegrasyon görünmez ve RAWG'a hiçbir istek gitmez.</summary>
public sealed class RawgDisabledTests(GameVaultFactory factory) : IClassFixture<GameVaultFactory>
{
    [Fact]
    public async Task Without_an_api_key_the_feature_is_hidden_and_the_app_works()
    {
        using var client = factory.CreateAppClient();
        await client.RegisterAsync($"norawg-{Guid.NewGuid():N}@example.test");

        var (searchResponse, search) = await client.GetPageAsync("/Library/Search?q=Hollow");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        Assert.Contains("RAWG entegrasyonu bu kurulumda kapalı", search.Body!.TextContent, StringComparison.Ordinal);
        Assert.Null(search.QuerySelector("form[role=search]"));

        var (_, form) = await client.GetPageAsync("/Library/New?rawgId=9767");
        Assert.Null(form.QuerySelector("a[href='/Library/Search']"));
        Assert.Null(form.QuerySelector("input[name='Input.RawgId']"));
        Assert.Equal(string.Empty, form.QuerySelector("#Input_Title")!.GetAttribute("value") ?? string.Empty);

        var response = await client.Http.GetAsync("/Library");
        Assert.DoesNotContain("media.rawg.io", response.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
    }
}
