using System.Net;
using GameVault.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Tests;

/// <summary>
/// Aynı adlı oyun eklenirken uyarı ve onay. Bu bir yasak değildir (aynı oyun farklı platformda ayrı
/// kayıt olabilir); kullanıcı onaylarsa eklenir. Kontrol yalnızca kullanıcının kendi kütüphanesine bakar.
/// </summary>
public sealed class DuplicateTitleTests(GameVaultFactory factory) : IClassFixture<GameVaultFactory>
{
    private async Task<(AppClient Client, int PlatformId, Guid UserId)> SignedInAsync()
    {
        var email = $"dup-{Guid.NewGuid():N}@example.test";
        var client = factory.CreateAppClient();
        await client.RegisterAsync(email);
        var userId = await factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        return (client, await client.GetPlatformIdAsync(), userId);
    }

    private Task<int> CountAsync(Guid userId) =>
        factory.WithDbAsync(db => db.LibraryGames.CountAsync(g => g.UserId == userId));

    [Theory]
    [InlineData("Hollow Knight")]
    [InlineData("  hollow KNIGHT  ")]
    public async Task Same_title_shows_a_warning_and_does_not_save(string secondTitle)
    {
        var (client, platformId, userId) = await SignedInAsync();
        using (client)
        {
            var firstId = await client.CreateGameAsync("Hollow Knight", platformId);

            var response = await client.PostFormAsync("/Library/New", AppClient.GameFields(secondTitle, platformId));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var doc = await client.ParseAsync(response);
            var warning = doc.QuerySelector("#same-title-warning")!;
            Assert.Contains("aynı adlı bir oyun var", warning.TextContent, StringComparison.Ordinal);
            Assert.NotNull(warning.QuerySelector($"a[href='/Library/{firstId}']"));
            Assert.Contains("PC", warning.TextContent, StringComparison.Ordinal);
            Assert.Equal(1, await CountAsync(userId));

            // Enter tuşu formdaki ilk gönder düğmesini kullanır: bu normal kaydetme düğmesi olmalı, onay değil.
            var submitButtons = doc.QuerySelectorAll("form[method=post] button[type=submit]");
            Assert.Null(submitButtons[0].GetAttribute("name"));
            Assert.Equal("ConfirmDuplicateTitle", submitButtons[^1].GetAttribute("name"));
        }
    }

    [Fact]
    public async Task Confirming_adds_the_game_anyway()
    {
        var (client, platformId, userId) = await SignedInAsync();
        using (client)
        {
            await client.CreateGameAsync("Celeste", platformId);
            var switchId = await client.GetPlatformIdAsync("Nintendo Switch");

            var fields = AppClient.GameFields("Celeste", switchId, "NotStarted", ("ConfirmDuplicateTitle", "true"));
            var response = await client.PostFormAsync("/Library/New", fields);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(2, await CountAsync(userId));
        }
    }

    [Fact]
    public async Task Other_users_games_with_the_same_title_do_not_trigger_the_warning()
    {
        var (alice, alicePlatform, _) = await SignedInAsync();
        var (bob, bobPlatform, bobId) = await SignedInAsync();
        using (alice)
        using (bob)
        {
            await alice.CreateGameAsync("Ortak Ad", alicePlatform);

            // Uyarı Alice'in oyununu sızdırmamalı; Bob için ekleme doğrudan gerçekleşir.
            await bob.CreateGameAsync("Ortak Ad", bobPlatform);
            Assert.Equal(1, await CountAsync(bobId));
        }
    }

    [Fact]
    public async Task Different_titles_are_saved_without_a_warning()
    {
        var (client, platformId, userId) = await SignedInAsync();
        using (client)
        {
            await client.CreateGameAsync("Hades", platformId);
            await client.CreateGameAsync("Hades II", platformId);
            Assert.Equal(2, await CountAsync(userId));
        }
    }
}
