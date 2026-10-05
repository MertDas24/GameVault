using System.Net;
using GameVault.Entities;
using GameVault.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Tests;

public sealed class LibraryTests(GameVaultFactory factory) : IClassFixture<GameVaultFactory>
{
    private string _lastEmail = string.Empty;

    private async Task<(AppClient Client, int PlatformId)> SignedInAsync()
    {
        var client = factory.CreateAppClient();
        _lastEmail = $"lib-{Guid.NewGuid():N}@example.test";
        await client.RegisterAsync(_lastEmail);
        return (client, await client.GetPlatformIdAsync());
    }

    [Fact]
    public async Task Create_view_edit_and_delete_game()
    {
        var (client, platformId) = await SignedInAsync();
        using (client)
        {
            var genreId = await client.GetGenreIdAsync("Macera");
            var id = await client.CreateGameAsync("Yıldız Tozu", platformId, "Playing",
                ("Input.Rating", "8"), ("Input.PlaytimeHours", "12"), ("Input.Notes", "İkinci bölümde kaldım"),
                ("Input.GenreIds", genreId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("Input.CoverChoice", "builtin:nebula"), ("Input.StartDate", "2025-01-15"), ("Input.IsFavorite", "true"));

            var (details, doc) = await client.GetPageAsync($"/Library/{id}");
            Assert.Equal(HttpStatusCode.OK, details.StatusCode);
            Assert.Equal("Yıldız Tozu", doc.QuerySelector("h1")!.TextContent.Trim());
            Assert.Contains("İkinci bölümde kaldım", doc.Body!.TextContent, StringComparison.Ordinal);
            Assert.Contains("Macera", doc.Body.TextContent, StringComparison.Ordinal);
            Assert.NotNull(doc.QuerySelector("img[src='/covers/nebula.svg']"));

            var edit = await client.PostFormAsync($"/Library/{id}/Edit",
                AppClient.GameFields("Yıldız Tozu: Tam Sürüm", platformId, "Completed", ("Input.Rating", "10"), ("Input.PlaytimeHours", "40")));
            edit.AssertRedirectTo($"/Library/{id}");

            var saved = await factory.WithDbAsync(db => db.LibraryGames.Include(g => g.Genres).AsNoTracking().SingleAsync(g => g.Id == id));
            Assert.Equal("Yıldız Tozu: Tam Sürüm", saved.Title);
            Assert.Equal(GameStatus.Completed, saved.Status);
            Assert.Equal(10, saved.Rating);
            Assert.Equal(40, saved.PlaytimeHours);
            Assert.Empty(saved.Genres);
            Assert.Null(saved.CoverImageUrl);
            Assert.False(saved.IsFavorite);

            var (confirm, confirmDoc) = await client.GetPageAsync($"/Library/{id}/Delete");
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
            Assert.Contains("geri alınamaz", confirmDoc.Body!.TextContent, StringComparison.Ordinal);

            var delete = await client.PostFormAsync($"/Library/{id}/Delete", []);
            delete.AssertRedirectTo("/Library");
            Assert.Equal(HttpStatusCode.NotFound, (await client.Http.GetAsync($"/Library/{id}")).StatusCode);
            Assert.False(await factory.WithDbAsync(db => db.LibraryGames.AnyAsync(g => g.Id == id)));
        }
    }

    [Fact]
    public async Task Favorite_toggle_uses_post_and_returns_to_local_url_only()
    {
        var (client, platformId) = await SignedInAsync();
        using (client)
        {
            var id = await client.CreateGameAsync("Favori Deneme", platformId);

            var toggle = await client.PostFormAsync($"/Library/{id}?handler=Favorite",
                new Dictionary<string, string> { ["returnUrl"] = "/Library?fav=1" }, tokenPage: $"/Library/{id}");
            toggle.AssertRedirectTo("/Library?fav=1");
            Assert.True(await factory.WithDbAsync(db => db.LibraryGames.Where(g => g.Id == id).Select(g => g.IsFavorite).SingleAsync()));

            var external = await client.PostFormAsync($"/Library/{id}?handler=Favorite",
                new Dictionary<string, string> { ["returnUrl"] = "https://evil.example" }, tokenPage: $"/Library/{id}");
            external.AssertRedirectTo($"/Library/{id}");
            Assert.False(await factory.WithDbAsync(db => db.LibraryGames.Where(g => g.Id == id).Select(g => g.IsFavorite).SingleAsync()));
        }
    }

    public static TheoryData<string, string, string> InvalidInputs => new()
    {
        { "Input.Title", "", "Oyun adı zorunludur." },
        { "Input.Title", "   ", "Oyun adı zorunludur." },
        { "Input.Title", new string('x', 201), "en fazla 200" },
        { "Input.Rating", "11", "Puan 1 ile 10 arasında olmalı." },
        { "Input.Rating", "0", "Puan 1 ile 10 arasında olmalı." },
        { "Input.PlaytimeHours", "-5", "0 ile 100000" },
        { "Input.PlaytimeHours", "abc", "geçersiz" },
        { "Input.Status", "99", "geçersiz" },
        { "Input.Status", "Hacked", "geçersiz" },
        { "Input.PlatformId", "999999", "Geçersiz platform." },
        { "Input.GenreIds", "999999", "Geçersiz tür seçimi." },
        { "Input.Notes", new string('n', 4001), "en fazla 4000" },
        { "Input.StartDate", "1900-01-01", "1970 ile bugün" },
        { "Input.CoverChoice", "builtin:../../etc/passwd", "Geçersiz hazır kapak" },
        { "Input.CoverChoice", "javascript:alert(1)", "Geçersiz kapak" }
    };

    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public async Task Invalid_input_is_rejected_with_field_error(string field, string value, string expectedMessage)
    {
        var (client, platformId) = await SignedInAsync();
        using (client)
        {
            var fields = AppClient.GameFields("Geçerli Ad", platformId);
            fields[field] = value;

            var response = await client.PostFormAsync("/Library/New", fields);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains(expectedMessage, System.Net.WebUtility.HtmlDecode(html), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, await factory.WithDbAsync(db => db.LibraryGames.CountAsync(g => g.User.Email == _lastEmail)));
        }
    }

    [Theory]
    [InlineData("http://images.example.com/a.png")]           // https değil
    [InlineData("https://evil.example/a.png")]                // izinli değil
    [InlineData("https://images.example.com.evil.example/a")] // alt alan hilesi
    [InlineData("https://user@images.example.com/a.png")]     // kullanıcı bilgisi
    [InlineData("https://images.example.com:8443/a.png")]     // standart dışı port
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")]
    public async Task External_cover_urls_outside_the_allow_list_are_rejected(string url)
    {
        var (client, platformId) = await SignedInAsync();
        using (client)
        {
            var response = await client.PostFormAsync("/Library/New",
                AppClient.GameFields("Kapak Deneme", platformId, "NotStarted", ("Input.CoverChoice", "url"), ("Input.CoverUrl", url)));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull((await client.ParseAsync(response)).QuerySelector("#Input_CoverUrl[aria-invalid=true]"));
        }
    }

    [Fact]
    public async Task Allowed_external_cover_is_accepted_and_rendered()
    {
        var (client, platformId) = await SignedInAsync();
        using (client)
        {
            var id = await client.CreateGameAsync("Harici Kapak", platformId, "NotStarted",
                ("Input.CoverChoice", "url"), ("Input.CoverUrl", "https://images.example.com/covers/1.jpg"));
            var (_, doc) = await client.GetPageAsync($"/Library/{id}");
            Assert.NotNull(doc.QuerySelector("img[src='https://images.example.com/covers/1.jpg'][referrerpolicy='no-referrer']"));
        }
    }

    [Fact]
    public async Task User_content_is_rendered_as_plain_text()
    {
        var (client, platformId) = await SignedInAsync();
        using (client)
        {
            const string payload = "<img src=x onerror=alert(1)><script>alert('xss')</script>";
            var id = await client.CreateGameAsync(payload, platformId, "NotStarted", ("Input.Notes", payload), ("Input.Description", payload));

            var html = await client.Http.GetStringAsync($"/Library/{id}");
            Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
            Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);

            var list = await client.Http.GetStringAsync("/Library");
            Assert.DoesNotContain("<script>alert", list, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Long_titles_are_stored_and_listed()
    {
        var (client, platformId) = await SignedInAsync();
        using (client)
        {
            var title = string.Concat(Enumerable.Repeat("Çok Uzun Oyun Adı ", 12)).Trim()[..200];
            var id = await client.CreateGameAsync(title, platformId);
            var (_, doc) = await client.GetPageAsync("/Library");
            var card = doc.QuerySelector($"a[href='/Library/{id}'] .card-title")!;
            Assert.Equal(title, card.GetAttribute("title"));
        }
    }

}
