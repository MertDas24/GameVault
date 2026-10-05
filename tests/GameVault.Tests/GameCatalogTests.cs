using System.Net;
using System.Text;
using GameVault.Business.Catalog;
using GameVault.Models.Catalog;
using GameVault.Models.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GameVault.Tests;

/// <summary>GameCatalog ve RawgMapping için veritabanı ve web katmanı olmadan birim testleri.</summary>
public sealed class GameCatalogTests
{
    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.rawg.test/api/") };
    }

    private static GameCatalog Catalog(HttpMessageHandler handler, TimeProvider time, int cooldownSeconds = 30, string? apiKey = "k") =>
        new(new SingleClientFactory(handler),
            Options.Create(new RawgOptions { ApiKey = apiKey, FailureCooldownSeconds = cooldownSeconds }),
            time,
            NullLogger<GameCatalog>.Instance);

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task After_a_failure_rawg_is_not_called_again_until_the_cooldown_passes()
    {
        var fail = true;
        var handler = new ScriptedHandler(_ => fail ? new HttpResponseMessage(HttpStatusCode.BadGateway) : Ok("""{ "results": [] }"""));
        var time = new ManualTime(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero));
        using var catalog = Catalog(handler, time, cooldownSeconds: 30);

        Assert.Equal(CatalogStatus.Unavailable, (await catalog.SearchAsync("ilk deneme")).Status);
        Assert.Equal(1, handler.Calls);

        fail = false;
        time.Now = time.Now.AddSeconds(10);
        Assert.Equal(CatalogStatus.Unavailable, (await catalog.SearchAsync("ikinci deneme")).Status);
        Assert.Equal(1, handler.Calls); // devre açık: istek atılmadı

        time.Now = time.Now.AddSeconds(25);
        Assert.Equal(CatalogStatus.Ok, (await catalog.SearchAsync("üçüncü deneme")).Status);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Rejected_api_key_opens_the_breaker_for_longer()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var time = new ManualTime(DateTimeOffset.UnixEpoch);
        using var catalog = Catalog(handler, time, cooldownSeconds: 30);

        await catalog.SearchAsync("anahtar");
        time.Now = time.Now.AddMinutes(4);
        await catalog.SearchAsync("anahtar 2");

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Disabled_catalog_never_calls_rawg()
    {
        var handler = new ScriptedHandler(_ => Ok("{}"));
        using var catalog = Catalog(handler, TimeProvider.System, apiKey: " ");

        Assert.False(catalog.IsEnabled);
        Assert.Equal(CatalogStatus.Disabled, (await catalog.SearchAsync("Hollow Knight")).Status);
        Assert.Equal(CatalogStatus.Disabled, (await catalog.GetGameAsync(1)).Status);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("  Hollow   Knight ", "Hollow Knight")]
    [InlineData("Ori\tand the\nForest", "Ori and the Forest")]
    [InlineData("a", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void Queries_are_normalized(string? input, string? expected) =>
        Assert.Equal(expected, GameCatalog.NormalizeQuery(input));

    [Fact]
    public void Overlong_queries_are_rejected() =>
        Assert.Null(GameCatalog.NormalizeQuery(new string('x', GameCatalog.MaxQueryLength + 1)));

    [Theory]
    [InlineData("https://media.rawg.io/media/games/abc/def.jpg", "https://media.rawg.io/media/crop/600/400/games/abc/def.jpg")]
    [InlineData("https://media.rawg.io/media/screenshots/a/b.jpg", "https://media.rawg.io/media/screenshots/a/b.jpg")]
    [InlineData("http://media.rawg.io/media/games/a/b.jpg", null)]
    [InlineData("https://media.rawg.io.evil.example/a.jpg", null)]
    [InlineData("https://user@media.rawg.io/media/games/a/b.jpg", null)]
    [InlineData("https://media.rawg.io:8443/media/games/a/b.jpg", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("", null)]
    public void Only_https_images_from_the_rawg_media_host_are_accepted(string input, string? expected) =>
        Assert.Equal(expected, RawgMapping.SafeCoverUrl(input));

    [Fact]
    public void Untrusted_fields_are_cleaned()
    {
        var game = RawgMapping.ToCatalogGame(new RawgGame(
            5,
            "  Ad\u0000\u0007 " + new string('x', 300),
            "2020-13-45",
            null,
            [new RawgPlatformEntry(new RawgNamed("pc", "PC")), new RawgPlatformEntry(new RawgNamed("PC; DROP", "Kötü")), new RawgPlatformEntry(null)],
            [new RawgNamed("action", "Action"), new RawgNamed("action", "Action")]));

        Assert.NotNull(game);
        Assert.StartsWith("Ad x", game.Name, StringComparison.Ordinal);
        Assert.True(game.Name.Length <= GameInput.TitleMaxLength);
        Assert.Null(game.Released);
        Assert.Null(game.CoverUrl);
        Assert.Equal(["pc"], game.Platforms.Select(p => p.Slug));
        Assert.Single(game.Genres);

        Assert.Null(RawgMapping.ToCatalogGame(new RawgGame(0, "Kimliksiz", null, null, null, null)));
        Assert.Null(RawgMapping.ToCatalogGame(new RawgGame(7, "   ", null, null, null, null)));
    }

    [Theory]
    [InlineData(62, 62)]
    [InlineData(1, 1)]
    [InlineData(0, null)]      // RAWG'da 0 "bilinmiyor" demektir
    [InlineData(-5, null)]
    [InlineData(5000, null)]   // makul olmayan değer
    [InlineData(null, null)]
    public void Rawg_playtime_becomes_the_estimated_hours(int? playtime, int? expected)
    {
        var game = RawgMapping.ToCatalogGame(new RawgGame(1, "Süre", null, null, null, null, playtime));
        Assert.Equal(expected, game!.EstimatedHours);
        Assert.Equal(expected, RawgMapping.ToGameInput(game, [], []).EstimatedHours);
    }

    [Fact]
    public void Mapping_to_game_input_uses_gamevault_reference_data_only()
    {
        ReferenceItem[] platforms = [new(1, "PC"), new(2, "PlayStation 5"), new(3, "Mobil")];
        ReferenceItem[] genres = [new(10, "Aksiyon"), new(11, "Macera"), new(12, "Rol Yapma (RPG)"), new(13, "Strateji"), new(14, "Nişancı"), new(15, "Spor")];
        var game = new CatalogGame(
            42,
            "Örnek",
            "https://media.rawg.io/media/crop/600/400/games/a/b.jpg",
            new DateOnly(2023, 5, 1),
            [new("android", "Android"), new("playstation5", "PS5"), new("unknown-console", "Bilinmeyen")],
            [new("indie", "Indie"), new("action", "Action"), new("adventure", "Adventure"), new("role-playing-games-rpg", "RPG"),
             new("strategy", "Strategy"), new("shooter", "Shooter"), new("sports", "Sports")]);

        var input = RawgMapping.ToGameInput(game, platforms, genres);

        Assert.Equal("Örnek", input.Title);
        Assert.Equal(42, input.RawgId);
        Assert.Equal(new DateOnly(2023, 5, 1), input.ReleaseDate);
        Assert.Equal(2, input.PlatformId); // PlayStation 5, Mobil'den önceliklidir
        Assert.Equal([10, 11, 12, 13, 14], input.GenreIds); // en fazla 5, eşlenmeyen "indie" atlanır
        Assert.Equal(CoverChoices.Url, input.CoverChoice);
        Assert.Null(input.Rating);
        Assert.Null(input.Notes);
    }

    [Fact]
    public void Unmapped_platforms_leave_the_choice_to_the_user()
    {
        var game = new CatalogGame(1, "Retro", null, null, [new("sega-saturn", "SEGA Saturn")], []);
        var input = RawgMapping.ToGameInput(game, [new ReferenceItem(1, "PC")], []);

        Assert.Null(input.PlatformId);
        Assert.Empty(input.GenreIds);
        Assert.Equal(CoverChoices.None, input.CoverChoice);
    }
}
