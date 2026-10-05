using System.Net;
using GameVault.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Tests;

public sealed class AccountTests(GameVaultFactory factory) : IClassFixture<GameVaultFactory>
{
    private static string NewEmail() => $"acct-{Guid.NewGuid():N}@example.test";

    [Fact]
    public async Task Delete_account_requires_password_and_confirmation()
    {
        var email = NewEmail();
        using var client = factory.CreateAppClient();
        await client.RegisterAsync(email);

        var wrongPassword = await client.PostFormAsync("/Account/Delete", new Dictionary<string, string>
        {
            ["Input.Password"] = "yanlis-parola-999",
            ["Input.ConfirmEmail"] = email,
            ["Input.Acknowledge"] = "true"
        });
        Assert.Equal(HttpStatusCode.OK, wrongPassword.StatusCode);
        Assert.NotNull((await client.ParseAsync(wrongPassword)).QuerySelector("#Input_Password[aria-invalid=true]"));

        var wrongEmail = await client.PostFormAsync("/Account/Delete", new Dictionary<string, string>
        {
            ["Input.Password"] = AppClient.DefaultPassword,
            ["Input.ConfirmEmail"] = "baska@example.test",
            ["Input.Acknowledge"] = "true"
        });
        Assert.NotNull((await client.ParseAsync(wrongEmail)).QuerySelector("#Input_ConfirmEmail[aria-invalid=true]"));

        var noAck = await client.PostFormAsync("/Account/Delete", new Dictionary<string, string>
        {
            ["Input.Password"] = AppClient.DefaultPassword,
            ["Input.ConfirmEmail"] = email
        });
        Assert.Contains("Devam etmek için bu kutuyu işaretle.", await noAck.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.True(await factory.WithDbAsync(db => db.Users.AnyAsync(u => u.Email == email)));
    }

    [Fact]
    public async Task Delete_account_removes_user_games_and_sessions()
    {
        var email = NewEmail();
        using var client = factory.CreateAppClient();
        await client.RegisterAsync(email);
        var platformId = await client.GetPlatformIdAsync();
        var genreId = await client.GetGenreIdAsync("Aksiyon");
        await client.CreateGameAsync("Silinecek 1", platformId, "Playing", ("Input.GenreIds", genreId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        await client.CreateGameAsync("Silinecek 2", platformId);

        using var otherDevice = factory.CreateAppClient();
        (await otherDevice.LoginAsync(email)).AssertRedirectTo("/Library");

        var userId = await factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());

        var response = await client.PostFormAsync("/Account/Delete", new Dictionary<string, string>
        {
            ["Input.Password"] = AppClient.DefaultPassword,
            ["Input.ConfirmEmail"] = email.ToUpperInvariant(),
            ["Input.Acknowledge"] = "true"
        });
        response.AssertRedirectTo("/");

        Assert.False(await factory.WithDbAsync(db => db.Users.AnyAsync(u => u.Id == userId)));
        Assert.Equal(0, await factory.WithDbAsync(db => db.LibraryGames.CountAsync(g => g.UserId == userId)));
        Assert.Equal(0, await factory.WithDbAsync(db => db.UserSessions.CountAsync(s => s.UserId == userId)));
        var orphanGenreLinks = await factory.WithDbAsync(db => db.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS [Value] FROM [LibraryGameGenres] lgg
            WHERE NOT EXISTS (SELECT 1 FROM [LibraryGames] lg WHERE lg.[Id] = lgg.[LibraryGameId])
            """).SingleAsync());
        Assert.Equal(0, orphanGenreLinks);

        (await client.Http.GetAsync("/Library")).AssertRedirectToLogin();
        (await otherDevice.Http.GetAsync("/Library")).AssertRedirectToLogin();

        using var login = factory.CreateAppClient();
        Assert.Equal(HttpStatusCode.OK, (await login.LoginAsync(email)).StatusCode);
    }

    [Fact]
    public async Task Delete_account_removes_archived_legacy_rows_and_queued_emails_of_that_user_only()
    {
        var email = NewEmail();
        using var client = factory.CreateAppClient();
        await client.RegisterAsync(email);
        var userId = await factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        var otherUserId = Guid.NewGuid();

        // Eski şemadan yükseltilmiş bir veritabanını taklit eder (UserOwnedLibrary arşivi).
        await factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            IF OBJECT_ID(N'[Legacy_UserGames]', N'U') IS NULL
                CREATE TABLE [Legacy_UserGames] ([Id] uniqueidentifier NOT NULL, [UserId] uniqueidentifier NOT NULL, [Notes] nvarchar(2000) NULL);
            EXEC sp_executesql N'INSERT INTO [Legacy_UserGames] ([Id], [UserId], [Notes]) VALUES (NEWID(), @a, N''kişisel not''), (NEWID(), @b, N''başkasının notu'')',
                N'@a uniqueidentifier, @b uniqueidentifier', @a = {userId}, @b = {otherUserId};
            """));
        Assert.True(await factory.WithDbAsync(db => db.OutboxEmails.AnyAsync(e => e.UserId == userId)));

        var response = await client.PostFormAsync("/Account/Delete", new Dictionary<string, string>
        {
            ["Input.Password"] = AppClient.DefaultPassword,
            ["Input.ConfirmEmail"] = email,
            ["Input.Acknowledge"] = "true"
        });
        response.AssertRedirectTo("/");

        Assert.Equal(0, await LegacyRowsAsync(userId));
        Assert.Equal(1, await LegacyRowsAsync(otherUserId));
        Assert.False(await factory.WithDbAsync(db => db.OutboxEmails.AnyAsync(e => e.UserId == userId)));

        Task<int> LegacyRowsAsync(Guid id) => factory.WithDbAsync(db => db.Database
            .SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [Legacy_UserGames] WHERE [UserId] = {id}").SingleAsync());
    }

    [Fact]
    public async Task Change_password_keeps_current_session_and_revokes_others()
    {
        var email = NewEmail();
        using var client = factory.CreateAppClient();
        await client.RegisterAsync(email);
        using var otherDevice = factory.CreateAppClient();
        await otherDevice.LoginAsync(email);

        var wrong = await client.PostFormAsync("/Account/Settings?handler=Password", new Dictionary<string, string>
        {
            ["Password.CurrentPassword"] = "yanlis-parola-999",
            ["Password.NewPassword"] = "yeni-parola-12345",
            ["Password.ConfirmPassword"] = "yeni-parola-12345"
        }, tokenPage: "/Account/Settings");
        Assert.Contains("Mevcut parola hatalı.", await wrong.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var ok = await client.PostFormAsync("/Account/Settings?handler=Password", new Dictionary<string, string>
        {
            ["Password.CurrentPassword"] = AppClient.DefaultPassword,
            ["Password.NewPassword"] = "yeni-parola-12345",
            ["Password.ConfirmPassword"] = "yeni-parola-12345"
        }, tokenPage: "/Account/Settings");
        ok.AssertRedirectTo("/Account/Settings");

        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/Library")).StatusCode);
        (await otherDevice.Http.GetAsync("/Library")).AssertRedirectToLogin();
    }

    [Fact]
    public async Task Profile_update_changes_display_name_only()
    {
        var email = NewEmail();
        using var client = factory.CreateAppClient();
        await client.RegisterAsync(email);

        var response = await client.PostFormAsync("/Account/Settings?handler=Profile", new Dictionary<string, string>
        {
            ["Profile.DisplayName"] = "Yeni Ad",
            ["Email"] = "ele-gecirme@example.test",
            ["Profile.Email"] = "ele-gecirme@example.test"
        }, tokenPage: "/Account/Settings");
        response.AssertRedirectTo("/Account/Settings");

        var user = await factory.WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Email == email));
        Assert.Equal("Yeni Ad", user.DisplayName);
        var (_, doc) = await client.GetPageAsync("/Library");
        Assert.Equal("Yeni Ad", doc.QuerySelector(".sidebar-user")!.TextContent.Trim());
    }

    [Fact]
    public async Task Sign_out_other_sessions_keeps_current_one()
    {
        var email = NewEmail();
        using var client = factory.CreateAppClient();
        await client.RegisterAsync(email);
        using var otherDevice = factory.CreateAppClient();
        await otherDevice.LoginAsync(email);

        var response = await client.PostFormAsync("/Account/Settings?handler=SignOutOthers", [], tokenPage: "/Account/Settings");
        response.AssertRedirectTo("/Account/Settings");

        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/Library")).StatusCode);
        (await otherDevice.Http.GetAsync("/Library")).AssertRedirectToLogin();
    }
}
