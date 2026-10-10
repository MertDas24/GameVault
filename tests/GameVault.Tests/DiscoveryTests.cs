using System.Globalization;
using System.Net;
using GameVault.Business.Catalog;
using GameVault.Tests.Infrastructure;

namespace GameVault.Tests;

/// <summary>"Ne oynasam?" keşif sekmesi: RAWG'dan kütüphanede olmayan öneriler, filtreler, zevke göre tür ve veri gizliliği.</summary>
public sealed class DiscoveryTests(RawgFactory factory) : IClassFixture<RawgFactory>
{
    private async Task<(AppClient Client, string Email)> NewUserAsync()
    {
        var email = $"discover-{Guid.NewGuid():N}@example.test";
        var client = factory.CreateAppClient();
        await client.RegisterAsync(email);
        return (client, email);
    }

    private static async Task<List<string>> DiscoveredTitlesAsync(AppClient client, string query = "")
    {
        var (response, doc) = await client.GetPageAsync("/Suggest?source=rawg" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return doc.QuerySelectorAll(".discover-card .card-title").Select(t => t.TextContent.Trim()).Order(StringComparer.Ordinal).ToList();
    }

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

    [Fact]
    public async Task Discover_suggests_only_games_that_are_not_in_the_library()
    {
        var (client, _) = await NewUserAsync();
        using (client)
        {
            var pc = await client.GetPlatformIdAsync();
            Assert.Equal(["Celeste", "Hollow Knight", "Süresiz Oyun", "Uzun RPG"], await DiscoveredTitlesAsync(client));

            // RAWG kimliğiyle eklenmiş oyun da, aynı adla elle eklenmiş oyun da önerilmez.
            await client.CreateGameAsync("Hollow Knight", pc, "Playing", ("Input.RawgId", "9767"));
            await client.CreateGameAsync("celeste", pc);
            Assert.Equal(["Süresiz Oyun", "Uzun RPG"], await DiscoveredTitlesAsync(client));

            var (_, doc) = await client.GetPageAsync("/Suggest?source=rawg");
            Assert.Equal("/Library/New?rawgId=1002", doc.QuerySelectorAll(".discover-card a.btn")
                .Select(a => a.GetAttribute("href")).Single(h => h!.EndsWith("1002", StringComparison.Ordinal)));
            Assert.Equal("page", doc.QuerySelector(".segmented a[href^='/Suggest?source=rawg']")!.GetAttribute("aria-current"));
            Assert.Null(doc.QuerySelector("#s-wishlist"));
        }
    }

    [Fact]
    public async Task Max_hours_filter_drops_long_and_unknown_games()
    {
        var (client, _) = await NewUserAsync();
        using (client)
        {
            Assert.Equal(["Celeste", "Hollow Knight"], await DiscoveredTitlesAsync(client, "&maxHours=30"));
        }
    }

    [Fact]
    public async Task Genre_comes_from_the_filter_or_from_the_users_favorite_games()
    {
        var (client, email) = await NewUserAsync();
        using (client)
        {
            var pc = await client.GetPlatformIdAsync();
            var rpg = await client.GetGenreIdAsync("Rol Yapma (RPG)");
            var strategy = await client.GetGenreIdAsync("Strateji");

            // Zevk: 8+ puanlı RPG ve favori RPG, puansız strateji oyunundan ağır basar.
            await client.CreateGameAsync("Sevilen RPG", pc, "Playing", ("Input.GenreIds", Id(rpg)), ("Input.Rating", "9"));
            await client.CreateGameAsync("Favori RPG", pc, "Playing", ("Input.GenreIds", Id(rpg)), ("Input.IsFavorite", "true"));
            await client.CreateGameAsync("Sıradan Strateji", pc, "Playing", ("Input.GenreIds", Id(strategy)));

            var (_, doc) = await client.GetPageAsync("/Suggest?source=rawg");
            Assert.Contains("Sevdiğin türe göre: Rol Yapma (RPG)", doc.QuerySelector(".discover-head")!.TextContent, StringComparison.Ordinal);
            var request = factory.Rawg.Requests.Last(r => r.RequestUri!.Query.Contains("ordering", StringComparison.Ordinal));
            Assert.Equal("role-playing-games-rpg", System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["genres"]);

            // Filtre seçilirse filtre geçerlidir; platform RAWG platform kimliğine çevrilir.
            (_, doc) = await client.GetPageAsync($"/Suggest?source=rawg&genre={Id(strategy)}&platform={Id(pc)}");
            Assert.Contains("Tür: Strateji", doc.QuerySelector(".discover-head")!.TextContent, StringComparison.Ordinal);
            request = factory.Rawg.Requests.Last(r => r.RequestUri!.Query.Contains("genres=strategy", StringComparison.Ordinal));
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            Assert.Equal("4", query["platforms"]);
            Assert.DoesNotContain(email, request.RequestUri.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Discover_sends_only_genre_platform_and_paging_to_rawg()
    {
        var (client, email) = await NewUserAsync();
        using (client)
        {
            await client.GetPageAsync("/Suggest?source=rawg");

            var request = factory.Rawg.Requests.Last(r => r.RequestUri!.Query.Contains("ordering", StringComparison.Ordinal));
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            Assert.Empty(query.AllKeys.Except(["key", "ordering", "page", "page_size", "genres", "platforms"], StringComparer.Ordinal));
            Assert.Equal("https", request.RequestUri.Scheme);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.DoesNotContain(email, request.RequestUri.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Library_tab_keeps_working_and_links_to_discover_with_the_same_filters()
    {
        var (client, _) = await NewUserAsync();
        using (client)
        {
            var (response, doc) = await client.GetPageAsync("/Suggest?maxHours=10");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("page", doc.QuerySelector(".segmented a[href^='/Suggest?maxHours']")!.GetAttribute("aria-current"));
            Assert.NotNull(doc.QuerySelector(".segmented a[href='/Suggest?source=rawg&maxHours=10']"));
            Assert.Empty(doc.QuerySelectorAll(".discover-card"));
        }
    }

    [Fact]
    public void Genre_and_platform_mapping_covers_reference_names()
    {
        Assert.Equal("action", RawgMapping.GenreSlugFor("Aksiyon"));
        Assert.Null(RawgMapping.GenreSlugFor("Bilinmeyen Tür"));
        Assert.Equal("3,21", RawgMapping.PlatformIdsFor("Mobil"));
        Assert.Null(RawgMapping.PlatformIdsFor(null));
    }
}
