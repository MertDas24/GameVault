using System.Net;
using System.Text.RegularExpressions;
using GameVault.Tests.Infrastructure;
using GameVault.Web.Pages.Account;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Tests;

public sealed partial class PasswordResetTests(GameVaultFactory factory) : IClassFixture<GameVaultFactory>
{
    private static string NewEmail() => $"reset-{Guid.NewGuid():N}@example.test";

    [GeneratedRegex(@"http://localhost/Account/ResetPassword\?uid=[0-9a-f]{32}&token=[A-Za-z0-9_\-]+")]
    private static partial Regex ResetLink();

    private async Task<string> RequestResetLinkAsync(AppClient client, string email, int expectedCount = 1)
    {
        var response = await client.PostFormAsync("/Account/ForgotPassword", new Dictionary<string, string> { ["Input.Email"] = email });
        response.AssertRedirectTo("/Account/ForgotPassword?sent=True");
        var message = await factory.Emails.WaitForAsync(email, "parola sıfırlama", expectedCount);
        var match = ResetLink().Match(message.TextBody);
        Assert.True(match.Success, "E-postada sıfırlama bağlantısı bulunamadı.");
        return match.Value["http://localhost".Length..];
    }

    [Fact]
    public async Task Forgot_password_response_is_the_same_for_known_and_unknown_emails()
    {
        var email = NewEmail();
        using (var setup = factory.CreateAppClient())
        {
            await setup.RegisterAsync(email);
        }

        using var client = factory.CreateAppClient();
        var known = await client.PostFormAsync("/Account/ForgotPassword", new Dictionary<string, string> { ["Input.Email"] = email });
        var unknownEmail = NewEmail();
        var unknown = await client.PostFormAsync("/Account/ForgotPassword", new Dictionary<string, string> { ["Input.Email"] = unknownEmail });

        known.AssertRedirectTo("/Account/ForgotPassword?sent=True");
        unknown.AssertRedirectTo("/Account/ForgotPassword?sent=True");
        await factory.Emails.WaitForAsync(email, "parola sıfırlama");
        Assert.Equal(0, factory.Emails.CountFor(unknownEmail));
    }

    [Fact]
    public async Task Reset_changes_password_revokes_all_sessions_and_token_is_single_use()
    {
        var email = NewEmail();
        using var existingSession = factory.CreateAppClient();
        await existingSession.RegisterAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await existingSession.Http.GetAsync("/Library")).StatusCode);

        using var client = factory.CreateAppClient();
        var link = await RequestResetLinkAsync(client, email);

        // Bağlantı sayfası tokenı gizli alana koyar; parola burada belirlenir.
        var (page, doc) = await client.GetPageAsync(link);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var fields = new Dictionary<string, string>
        {
            ["Input.UserId"] = doc.QuerySelector("input[name='Input.UserId']")!.GetAttribute("value")!,
            ["Input.Token"] = doc.QuerySelector("input[name='Input.Token']")!.GetAttribute("value")!,
            ["Input.Password"] = "yepyeni-parola-456",
            ["Input.ConfirmPassword"] = "yepyeni-parola-456"
        };

        var reset = await client.PostFormAsync("/Account/ResetPassword", fields, tokenPage: link);
        reset.AssertRedirectTo("/Account/Login");

        // Politika: sıfırlama sonrası mevcut tüm oturumlar kapanır.
        (await existingSession.Http.GetAsync("/Library")).AssertRedirectToLogin();
        var userSessions = await factory.WithDbAsync(db =>
            db.UserSessions.CountAsync(s => db.Users.Any(u => u.Id == s.UserId && u.Email == email)));
        Assert.Equal(0, userSessions);

        // Eski parola çalışmaz, yenisi çalışır.
        using var login = factory.CreateAppClient();
        Assert.Equal(HttpStatusCode.OK, (await login.LoginAsync(email)).StatusCode);
        (await login.LoginAsync(email, "yepyeni-parola-456")).AssertRedirectTo("/Library");

        // Aynı token ikinci kez kullanılamaz.
        fields["Input.Password"] = fields["Input.ConfirmPassword"] = "ucuncu-parola-789";
        var reuse = await client.PostFormAsync("/Account/ResetPassword", fields, tokenPage: link);
        Assert.Equal(HttpStatusCode.OK, reuse.StatusCode);
        Assert.Contains(ResetPasswordModel.InvalidLinkError, await reuse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tampered_or_foreign_tokens_are_rejected()
    {
        var victim = NewEmail();
        var attacker = NewEmail();
        using (var setup = factory.CreateAppClient())
        {
            await setup.RegisterAsync(victim);
            await setup.LogoutAsync();
            await setup.RegisterAsync(attacker);
        }

        using var client = factory.CreateAppClient();
        var attackerLink = await RequestResetLinkAsync(client, attacker);
        var (_, doc) = await client.GetPageAsync(attackerLink);
        var attackerToken = doc.QuerySelector("input[name='Input.Token']")!.GetAttribute("value")!;
        var victimId = await factory.WithDbAsync(db => db.Users.Where(u => u.Email == victim).Select(u => u.Id).SingleAsync());

        // Saldırganın kendi tokenı kurbanın hesabında geçersizdir.
        var response = await client.PostFormAsync("/Account/ResetPassword", new Dictionary<string, string>
        {
            ["Input.UserId"] = victimId.ToString("N"),
            ["Input.Token"] = attackerToken,
            ["Input.Password"] = "ele-gecirme-123",
            ["Input.ConfirmPassword"] = "ele-gecirme-123"
        }, tokenPage: attackerLink);
        Assert.Contains(ResetPasswordModel.InvalidLinkError, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var victimLogin = factory.CreateAppClient();
        (await victimLogin.LoginAsync(victim)).AssertRedirectTo("/Library");

        // Bozuk token.
        var garbage = await client.PostFormAsync("/Account/ResetPassword", new Dictionary<string, string>
        {
            ["Input.UserId"] = victimId.ToString("N"),
            ["Input.Token"] = "bozuk!!token",
            ["Input.Password"] = "ele-gecirme-123",
            ["Input.ConfirmPassword"] = "ele-gecirme-123"
        }, tokenPage: attackerLink);
        Assert.Contains(ResetPasswordModel.InvalidLinkError, await garbage.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reset_link_uses_configured_public_base_url_not_the_request_host()
    {
        var email = NewEmail();
        using (var setup = factory.CreateAppClient())
        {
            await setup.RegisterAsync(email);
        }

        using var client = factory.CreateAppClient();
        var token = await client.GetTokenAsync("/Account/ForgotPassword");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Account/ForgotPassword")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = email,
                ["__RequestVerificationToken"] = token
            })
        };
        request.Headers.Host = "attacker.example";
        await client.Http.SendAsync(request);

        var message = await factory.Emails.WaitForAsync(email, "parola sıfırlama");
        Assert.DoesNotContain("attacker.example", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("http://localhost/Account/ResetPassword", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reset_page_without_link_parameters_shows_error()
    {
        using var client = factory.CreateAppClient();
        var (response, doc) = await client.GetPageAsync("/Account/ResetPassword");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(doc.QuerySelector("input[name='Input.Password']"));
        Assert.Contains(ResetPasswordModel.InvalidLinkError, doc.Body!.TextContent, StringComparison.Ordinal);
    }
}
