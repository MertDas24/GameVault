using System.Globalization;
using System.Net;
using GameVault.Business.Library;
using GameVault.Entities;
using GameVault.Models.Library;
using GameVault.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests;

/// <summary>"Ne oynasam?": yalnızca sıradaki oyunlar, filtreler, "başka öner", izolasyon ve "oynamaya başla".</summary>
public sealed class SuggestionTests(GameVaultFactory factory) : IClassFixture<GameVaultFactory>
{
    private sealed record User(AppClient Client, Guid Id, int Pc, int Switch);

    private async Task<User> NewUserAsync()
    {
        var email = $"suggest-{Guid.NewGuid():N}@example.test";
        var client = factory.CreateAppClient();
        await client.RegisterAsync(email);
        var id = await factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        return new User(client, id, await client.GetPlatformIdAsync("PC"), await client.GetPlatformIdAsync("Nintendo Switch"));
    }

    private static Task<Guid> AddAsync(User user, string title, string status, int? platform = null, int? estimate = null, int? genre = null)
    {
        var extra = new List<(string, string)>();
        if (estimate is { } e)
        {
            extra.Add(("Input.EstimatedHours", e.ToString(CultureInfo.InvariantCulture)));
        }

        if (genre is { } g)
        {
            extra.Add(("Input.GenreIds", g.ToString(CultureInfo.InvariantCulture)));
        }

        return user.Client.CreateGameAsync(title, platform ?? user.Pc, status, [.. extra]);
    }

    private static async Task<string?> SuggestedTitleAsync(AppClient client, string query = "")
    {
        var (response, doc) = await client.GetPageAsync("/Suggest" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return doc.QuerySelector("#suggestion-title")?.TextContent.Trim();
    }

    private async Task<SuggestionResult> SuggestDirectAsync(Guid userId, SuggestionFilter filter, Guid? exclude = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SuggestionService>().SuggestAsync(userId, filter, exclude);
    }

    [Fact]
    public async Task Only_not_started_and_on_hold_games_are_suggested()
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            await AddAsync(user, "Oynanıyor", "Playing");
            await AddAsync(user, "Bitti", "Completed");
            await AddAsync(user, "Bırakıldı", "Dropped");
            await AddAsync(user, "İstek", "Wishlist");
            await AddAsync(user, "Sırada", "NotStarted");
            await AddAsync(user, "Ara verildi", "OnHold");

            var seen = new HashSet<string>();
            for (var i = 0; i < 40; i++)
            {
                var result = await SuggestDirectAsync(user.Id, SuggestionFilter.None);
                Assert.Equal(2, result.BacklogCount);
                seen.Add(result.Game!.Title);
            }

            // 40 denemede iki adayın da görülmeme olasılığı ~2e-12: seçim gerçekten rastgele ve yalnızca bu ikisinden.
            Assert.Equal(["Ara verildi", "Sırada"], seen.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task Wishlist_is_included_only_when_requested()
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            await AddAsync(user, "İstek listesindeki", "Wishlist");

            Assert.Null(await SuggestedTitleAsync(user.Client));
            Assert.Equal("İstek listesindeki", await SuggestedTitleAsync(user.Client, "?wishlist=true"));
        }
    }

    [Fact]
    public async Task Platform_genre_and_max_hours_filters_are_applied()
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            var rpg = await user.Client.GetGenreIdAsync("Rol Yapma (RPG)");
            await AddAsync(user, "Kısa PC", "NotStarted", user.Pc, estimate: 8);
            await AddAsync(user, "Uzun PC RPG", "NotStarted", user.Pc, estimate: 80, genre: rpg);
            await AddAsync(user, "Kısa Switch", "NotStarted", user.Switch, estimate: 5);
            await AddAsync(user, "Süresi bilinmeyen", "NotStarted", user.Pc);

            var pcShort = await SuggestDirectAsync(user.Id, SuggestionFilter.Normalize(user.Pc, null, 10, false));
            Assert.Equal("Kısa PC", pcShort.Game!.Title);
            Assert.Equal(1, pcShort.MatchCount);
            Assert.Equal(1, pcShort.UnknownEstimateCount);

            var rpgOnly = await SuggestDirectAsync(user.Id, SuggestionFilter.Normalize(null, rpg, null, false));
            Assert.Equal("Uzun PC RPG", rpgOnly.Game!.Title);

            var switchGames = await SuggestDirectAsync(user.Id, SuggestionFilter.Normalize(user.Switch, null, null, false));
            Assert.Equal("Kısa Switch", switchGames.Game!.Title);

            // Sayfa: eşleşme yoksa açıklama ve süresi bilinmeyen oyun sayısı gösterilir.
            var (_, doc) = await user.Client.GetPageAsync($"/Suggest?platform={user.Switch}&maxHours=2");
            Assert.Null(doc.QuerySelector("#suggestion-title"));
            Assert.Contains("Bu filtrelere uyan oyun yok", doc.Body!.TextContent, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Another_suggestion_skips_the_previous_game_when_possible()
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            var first = await AddAsync(user, "Birinci", "NotStarted");
            var second = await AddAsync(user, "İkinci", "NotStarted");

            for (var i = 0; i < 10; i++)
            {
                Assert.Equal(second, (await SuggestDirectAsync(user.Id, SuggestionFilter.None, exclude: first)).Game!.Id);
            }

            // Tek aday kaldığında hariç tutma yok sayılır; boş sonuç yerine aynı oyun önerilir.
            await user.Client.PostFormAsync($"/Library/{second}/Delete", [], tokenPage: "/Library/New");
            Assert.Equal(first, (await SuggestDirectAsync(user.Id, SuggestionFilter.None, exclude: first)).Game!.Id);

            // Tek seçenek kaldığında "Başka öner" bağlantısı gösterilmez.
            var (_, doc) = await user.Client.GetPageAsync("/Suggest");
            Assert.Equal("Birinci", doc.QuerySelector("#suggestion-title")!.TextContent.Trim());
            Assert.Null(doc.QuerySelector("a[href*='exclude=']"));
        }
    }

    [Fact]
    public async Task Another_link_keeps_the_filters()
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            await AddAsync(user, "A", "NotStarted", estimate: 5);
            await AddAsync(user, "B", "NotStarted", estimate: 6);

            var (_, doc) = await user.Client.GetPageAsync($"/Suggest?platform={user.Pc}&maxHours=50&wishlist=true");
            var href = doc.QuerySelector("a[href*='exclude=']")!.GetAttribute("href")!;
            Assert.Contains($"platform={user.Pc}", href, StringComparison.Ordinal);
            Assert.Contains("maxHours=50", href, StringComparison.Ordinal);
            Assert.Contains("wishlist=true", href, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Other_users_games_are_never_suggested_or_started()
    {
        var alice = await NewUserAsync();
        var bob = await NewUserAsync();
        using (alice.Client)
        using (bob.Client)
        {
            var aliceGame = await AddAsync(alice, "Alice'in sıradaki oyunu", "NotStarted");

            var (_, empty) = await bob.Client.GetPageAsync($"/Suggest?exclude={aliceGame}");
            Assert.Contains("Sırada bekleyen oyunun yok", empty.Body!.TextContent, StringComparison.Ordinal);
            Assert.DoesNotContain("Alice", empty.Body.TextContent, StringComparison.Ordinal);

            var start = await bob.Client.PostFormAsync($"/Suggest?handler=Start&id={aliceGame}", [], tokenPage: "/Library/New");
            Assert.Equal(HttpStatusCode.NotFound, start.StatusCode);
            var status = await factory.WithDbAsync(db => db.LibraryGames.Where(g => g.Id == aliceGame).Select(g => g.Status).SingleAsync());
            Assert.Equal(GameStatus.NotStarted, status);
        }
    }

    [Fact]
    public async Task Start_playing_marks_the_game_and_sets_the_start_date_once()
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            var fresh = await AddAsync(user, "Yeni başlanacak", "NotStarted");
            var resumed = await user.Client.CreateGameAsync("Devam edilecek", user.Pc, "OnHold", ("Input.StartDate", "2025-03-01"));

            var response = await user.Client.PostFormAsync($"/Suggest?handler=Start&id={fresh}", [], tokenPage: "/Suggest");
            response.AssertRedirectTo($"/Library/{fresh}");
            await user.Client.PostFormAsync($"/Suggest?handler=Start&id={resumed}", [], tokenPage: "/Suggest");

            var games = await factory.WithDbAsync(db => db.LibraryGames.AsNoTracking()
                .Where(g => g.Id == fresh || g.Id == resumed).ToDictionaryAsync(g => g.Id));
            Assert.Equal(GameStatus.Playing, games[fresh].Status);
            Assert.NotNull(games[fresh].StartDate);
            Assert.Equal(GameStatus.Playing, games[resumed].Status);
            Assert.Equal(new DateOnly(2025, 3, 1), games[resumed].StartDate); // mevcut tarih korunur
        }
    }

    [Theory]
    [InlineData("?platform=abc&genre=-1&maxHours=999999&wishlist=maybe")]
    [InlineData("?maxHours=0&exclude=not-a-guid")]
    public async Task Invalid_query_values_fall_back_to_no_filter(string query)
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            await AddAsync(user, "Tek oyun", "NotStarted");
            Assert.Equal("Tek oyun", await SuggestedTitleAsync(user.Client, query));
        }
    }

    [Fact]
    public void Filter_normalization_drops_out_of_range_values()
    {
        var filter = SuggestionFilter.Normalize(0, -3, GameInput.MaxEstimatedHours + 1, false);
        Assert.False(filter.HasFilters);
        Assert.Equal([GameStatus.NotStarted, GameStatus.OnHold], filter.Statuses);
        Assert.Contains(GameStatus.Wishlist, SuggestionFilter.Normalize(null, null, null, true).Statuses);
    }

    [Fact]
    public async Task Estimated_hours_are_validated_and_shown_on_details()
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            var invalid = await user.Client.PostFormAsync("/Library/New",
                AppClient.GameFields("Geçersiz süre", user.Pc, "NotStarted", ("Input.EstimatedHours", "5000")));
            Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
            var page = await user.Client.ParseAsync(invalid);
            Assert.Contains("1 ile 1000", page.QuerySelector("#Input_EstimatedHours-error")!.TextContent, StringComparison.Ordinal);

            var id = await AddAsync(user, "Süreli", "NotStarted", estimate: 42);
            var (_, details) = await user.Client.GetPageAsync($"/Library/{id}");
            Assert.Contains("~42 saat", details.Body!.TextContent, StringComparison.Ordinal);
        }
    }
}
