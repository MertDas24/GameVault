using System.Net;
using AngleSharp.Dom;
using GameVault.Entities;
using GameVault.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Tests;

public sealed class SearchTests(GameVaultFactory factory) : IClassFixture<GameVaultFactory>
{
    private async Task<AppClient> SeededUserAsync(int count)
    {
        var client = factory.CreateAppClient();
        var email = $"search-{Guid.NewGuid():N}@example.test";
        await client.RegisterAsync(email);

        await factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email);
            var pc = await db.Platforms.SingleAsync(p => p.Name == "PC");
            var ps5 = await db.Platforms.SingleAsync(p => p.Name == "PlayStation 5");
            var rpg = await db.Genres.SingleAsync(g => g.Name == "Rol Yapma (RPG)");
            var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (var i = 1; i <= count; i++)
            {
                db.LibraryGames.Add(new LibraryGame
                {
                    Id = Guid.NewGuid(),
                    UserId = user.Id,
                    Title = $"Oyun {i:D2}" + (i % 5 == 0 ? " Ejderha" : string.Empty),
                    PlatformId = i % 2 == 0 ? ps5.Id : pc.Id,
                    Status = i % 3 == 0 ? GameStatus.Completed : GameStatus.Playing,
                    IsFavorite = i % 4 == 0,
                    Rating = i % 10 + 1,
                    PlaytimeHours = i * 3,
                    Genres = i % 5 == 0 ? [rpg] : [],
                    DateAdded = start.AddDays(i),
                    UpdatedAtUtc = start.AddDays(i)
                });
            }

            await db.SaveChangesAsync();
        });

        return client;
    }

    private static List<string> Titles(IDocument doc) =>
        doc.QuerySelectorAll(".card-title").Select(e => e.TextContent.Trim()).ToList();

    [Fact]
    public async Task Default_listing_is_paginated_newest_first()
    {
        using var client = await SeededUserAsync(30);

        var (_, page1) = await client.GetPageAsync("/Library?size=12");
        var titles = Titles(page1);
        Assert.Equal(12, titles.Count);
        Assert.Equal("Oyun 30 Ejderha", titles[0]);
        Assert.Contains("Sayfa 1 / 3", page1.QuerySelector(".result-count")!.TextContent, StringComparison.Ordinal);

        var (_, page3) = await client.GetPageAsync("/Library?size=12&page=3");
        Assert.Equal(6, Titles(page3).Count);
        Assert.Null(page3.QuerySelector("a[rel=next]"));
        Assert.Equal("/Library?size=12&page=2", page3.QuerySelector("a[rel=prev]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task Out_of_range_or_invalid_paging_values_fall_back_safely()
    {
        using var client = await SeededUserAsync(5);

        var (r1, huge) = await client.GetPageAsync("/Library?page=999999999&size=100000");
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(5, Titles(huge).Count); // son sayfaya çekilir, izin verilmeyen boyut varsayılana döner

        var (r2, junk) = await client.GetPageAsync("/Library?page=-1&size=abc&sort=DROP%20TABLE&status=%27%20OR%201%3D1--");
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal(5, Titles(junk).Count);
    }

    [Fact]
    public async Task Search_matches_title_substring_and_treats_wildcards_literally()
    {
        using var client = await SeededUserAsync(20);

        var (_, doc) = await client.GetPageAsync("/Library?q=ejderha");
        Assert.Equal(["Oyun 20 Ejderha", "Oyun 15 Ejderha", "Oyun 10 Ejderha", "Oyun 05 Ejderha"], Titles(doc));

        var (_, wildcard) = await client.GetPageAsync("/Library?q=%25");
        Assert.Empty(Titles(wildcard));
        Assert.Contains("bulunamadı", wildcard.QuerySelector(".result-count")!.TextContent, StringComparison.Ordinal);

        var (_, injection) = await client.GetPageAsync("/Library?q=%27%20OR%20%271%27%3D%271");
        Assert.Empty(Titles(injection));
    }

    [Fact]
    public async Task Filters_can_be_combined()
    {
        using var client = await SeededUserAsync(20);
        var ps5 = await factory.WithDbAsync(db => db.Platforms.Where(p => p.Name == "PlayStation 5").Select(p => p.Id).SingleAsync());
        var rpg = await factory.WithDbAsync(db => db.Genres.Where(g => g.Name == "Rol Yapma (RPG)").Select(g => g.Id).SingleAsync());

        var (_, completed) = await client.GetPageAsync("/Library?status=completed&size=48");
        Assert.Equal(6, Titles(completed).Count); // 3,6,9,12,15,18

        var (_, combo) = await client.GetPageAsync($"/Library?status=completed&platform={ps5}&size=48");
        Assert.Equal(["Oyun 18", "Oyun 12", "Oyun 06"], Titles(combo));

        var (_, favorites) = await client.GetPageAsync("/Library?fav=1&size=48");
        Assert.Equal(5, Titles(favorites).Count); // 4,8,12,16,20

        var (_, genre) = await client.GetPageAsync($"/Library?genre={rpg}&size=48");
        Assert.Equal(4, Titles(genre).Count);
    }

    [Theory]
    [InlineData("title_asc", "Oyun 01")]
    [InlineData("title_desc", "Oyun 12")]
    [InlineData("added_asc", "Oyun 01")]
    [InlineData("playtime_desc", "Oyun 12")]
    [InlineData("rating_desc", "Oyun 09")]
    public async Task Sorting_orders_results(string sort, string expectedFirst)
    {
        using var client = await SeededUserAsync(12);
        var (_, doc) = await client.GetPageAsync($"/Library?sort={sort}");
        Assert.Equal(expectedFirst, Titles(doc)[0]);
    }

    [Fact]
    public async Task Stats_reflect_only_the_users_real_data()
    {
        using var client = await SeededUserAsync(12);
        using var empty = factory.CreateAppClient();
        await empty.RegisterAsync($"empty-{Guid.NewGuid():N}@example.test");

        var (statsResponse, stats) = await client.GetPageAsync("/Stats");
        Assert.Equal(HttpStatusCode.OK, statsResponse.StatusCode);
        var tiles = stats.QuerySelectorAll(".stat-value").Select(e => e.TextContent.Trim()).ToList();
        Assert.Equal("12", tiles[0]);            // toplam oyun
        Assert.Equal("234 sa", tiles[1]);        // 3+6+...+36
        Assert.Equal("3", tiles[4]);             // favori: 4, 8, 12

        var (emptyResponse, emptyStats) = await empty.GetPageAsync("/Stats");
        Assert.Equal(HttpStatusCode.OK, emptyResponse.StatusCode);
        Assert.Empty(emptyStats.QuerySelectorAll(".stat-value"));
        Assert.Contains("Henüz istatistik yok", emptyStats.Body!.TextContent, StringComparison.Ordinal);
    }
}
