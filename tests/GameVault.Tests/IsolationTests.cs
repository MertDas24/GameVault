using System.Net;
using System.Text.Json;
using GameVault.Business.Library;
using GameVault.Business.Stats;
using GameVault.Entities;
using GameVault.Models.Library;
using GameVault.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests;

/// <summary>Kullanıcı A'nın verileri kullanıcı B tarafından okunamaz, değiştirilemez, silinemez, dışa aktarılamaz.</summary>
public sealed class IsolationTests(GameVaultFactory factory) : IClassFixture<GameVaultFactory>
{
    private async Task<(AppClient Alice, AppClient Bob, Guid AliceGameId, int PlatformId)> SetupAsync()
    {
        var alice = factory.CreateAppClient();
        await alice.RegisterAsync($"alice-{Guid.NewGuid():N}@example.test");
        var platformId = await alice.GetPlatformIdAsync();
        var gameId = await alice.CreateGameAsync("Alice'in Gizli Oyunu", platformId, "Playing",
            ("Input.Notes", "Alice'e özel not"), ("Input.Rating", "9"));

        var bob = factory.CreateAppClient();
        await bob.RegisterAsync($"bob-{Guid.NewGuid():N}@example.test");
        return (alice, bob, gameId, platformId);
    }

    [Fact]
    public async Task Other_user_cannot_read_game_details_or_edit_form()
    {
        var (alice, bob, gameId, _) = await SetupAsync();
        using (alice)
        using (bob)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await bob.Http.GetAsync($"/Library/{gameId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await bob.Http.GetAsync($"/Library/{gameId}/Edit")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await bob.Http.GetAsync($"/Library/{gameId}/Delete")).StatusCode);

            var list = await bob.Http.GetStringAsync("/Library?q=Gizli");
            Assert.DoesNotContain("Alice'in Gizli Oyunu", list, StringComparison.Ordinal);
            Assert.DoesNotContain("Alice&#x27;in Gizli Oyunu", list, StringComparison.Ordinal);

            Assert.Equal(HttpStatusCode.OK, (await alice.Http.GetAsync($"/Library/{gameId}")).StatusCode);
        }
    }

    [Fact]
    public async Task Other_user_cannot_update_game()
    {
        var (alice, bob, gameId, platformId) = await SetupAsync();
        using (alice)
        using (bob)
        {
            var response = await bob.PostFormAsync($"/Library/{gameId}/Edit",
                AppClient.GameFields("Bob değiştirdi", platformId, "Dropped"), tokenPage: "/Library/New");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var title = await factory.WithDbAsync(db => db.LibraryGames.Where(g => g.Id == gameId).Select(g => g.Title).SingleAsync());
            Assert.Equal("Alice'in Gizli Oyunu", title);

            // Geçersiz girdiyle de varlık bilgisi sızmamalı.
            var invalid = await bob.PostFormAsync($"/Library/{gameId}/Edit",
                AppClient.GameFields(string.Empty, platformId), tokenPage: "/Library/New");
            Assert.Equal(HttpStatusCode.NotFound, invalid.StatusCode);
        }
    }

    [Fact]
    public async Task Other_user_cannot_delete_or_favorite_game()
    {
        var (alice, bob, gameId, _) = await SetupAsync();
        using (alice)
        using (bob)
        {
            var delete = await bob.PostFormAsync($"/Library/{gameId}/Delete", [], tokenPage: "/Library/New");
            Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

            var favorite = await bob.PostFormAsync($"/Library/{gameId}?handler=Favorite", [], tokenPage: "/Library/New");
            Assert.Equal(HttpStatusCode.NotFound, favorite.StatusCode);

            var game = await factory.WithDbAsync(db => db.LibraryGames.AsNoTracking().SingleAsync(g => g.Id == gameId));
            Assert.False(game.IsFavorite);
        }
    }

    [Fact]
    public async Task Export_contains_only_the_callers_data()
    {
        var (alice, bob, gameId, platformId) = await SetupAsync();
        using (alice)
        using (bob)
        {
            await bob.CreateGameAsync("Bob'un Oyunu", platformId);

            var response = await bob.PostFormAsync("/Account/Export", []);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
            Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);

            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(gameId.ToString(), json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Alice", json, StringComparison.Ordinal);

            using var doc = JsonDocument.Parse(json);
            var games = doc.RootElement.GetProperty("games");
            Assert.Equal(1, games.GetArrayLength());
            Assert.Equal("Bob'un Oyunu", games[0].GetProperty("title").GetString());
            Assert.StartsWith("bob-", doc.RootElement.GetProperty("account").GetProperty("email").GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("securityStamp", json, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Sahiplik kontrolü sayfalarda değil, servis katmanında yapılır. Sayfalar atlanıp servisler
    /// başka bir kullanıcının kimliğiyle çağrıldığında da hiçbir kayıt okunamaz ya da değiştirilemez.
    /// </summary>
    [Fact]
    public async Task Service_layer_enforces_ownership_without_the_page_layer()
    {
        var aliceEmail = $"alice-{Guid.NewGuid():N}@example.test";
        var bobEmail = $"bob-{Guid.NewGuid():N}@example.test";
        using var alice = factory.CreateAppClient();
        using var bob = factory.CreateAppClient();
        await alice.RegisterAsync(aliceEmail);
        await bob.RegisterAsync(bobEmail);
        var platformId = await alice.GetPlatformIdAsync();
        var gameId = await alice.CreateGameAsync("Servis Katmanı Oyunu", platformId, "Playing", ("Input.Rating", "8"));

        var aliceId = await factory.WithDbAsync(db => db.Users.Where(u => u.Email == aliceEmail).Select(u => u.Id).SingleAsync());
        var bobId = await factory.WithDbAsync(db => db.Users.Where(u => u.Email == bobEmail).Select(u => u.Id).SingleAsync());

        await using var scope = factory.Services.CreateAsyncScope();
        var library = scope.ServiceProvider.GetRequiredService<LibraryService>();
        var stats = scope.ServiceProvider.GetRequiredService<StatsService>();

        // Kontrol: sahibi kaydı görebiliyor (test boşuna geçmiyor).
        Assert.NotNull(await library.GetAsync(aliceId, gameId));

        Assert.Null(await library.GetAsync(bobId, gameId));
        Assert.Null(await library.GetInputAsync(bobId, gameId));
        Assert.Null(await library.ToggleFavoriteAsync(bobId, gameId));
        Assert.False(await library.DeleteAsync(bobId, gameId));
        var update = await library.UpdateAsync(bobId, gameId,
            new GameInput { Title = "Bob değiştirdi", PlatformId = platformId, Status = GameStatus.Dropped });
        Assert.True(update.NotFound);

        var all = LibraryQuery.FromRaw(null, null, null, null, null, null, null, null);
        Assert.Equal(0, (await library.SearchAsync(bobId, all)).TotalCount);
        Assert.Equal(0, await library.CountAsync(bobId));
        var bobStats = await stats.GetAsync(bobId);
        Assert.Equal(0, bobStats.TotalGames);
        Assert.Equal(0, bobStats.TotalPlaytimeHours);
        Assert.Empty(bobStats.MostPlayed);

        var game = await factory.WithDbAsync(db => db.LibraryGames.AsNoTracking().SingleAsync(g => g.Id == gameId));
        Assert.Equal("Servis Katmanı Oyunu", game.Title);
        Assert.Equal(GameStatus.Playing, game.Status);
        Assert.False(game.IsFavorite);
        Assert.Equal(aliceId, game.UserId);
    }

    [Fact]
    public async Task Client_supplied_owner_fields_are_ignored_on_create()
    {
        var (alice, bob, _, platformId) = await SetupAsync();
        using (alice)
        using (bob)
        {
            var aliceId = await factory.WithDbAsync(db => db.LibraryGames.Select(g => g.UserId).FirstAsync());
            var bobGame = await bob.CreateGameAsync("Sahipliği değiştirme denemesi", platformId, "NotStarted",
                ("UserId", aliceId.ToString()), ("Input.UserId", aliceId.ToString()), ("Input.Id", Guid.Empty.ToString()),
                ("Input.DateAdded", "2000-01-01"));

            var game = await factory.WithDbAsync(db => db.LibraryGames.AsNoTracking().SingleAsync(g => g.Id == bobGame));
            Assert.NotEqual(aliceId, game.UserId);
            Assert.True(game.DateAdded > new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
    }
}
