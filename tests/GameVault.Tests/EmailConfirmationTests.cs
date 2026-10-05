using System.Net;
using GameVault.Tests.Infrastructure;
using GameVault.Web.Pages.Account;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests;

public sealed class EmailConfirmationTests(GameVaultFactory factory) : IClassFixture<GameVaultFactory>
{
    private static string NewEmail() => $"confirm-{Guid.NewGuid():N}@example.test";

    [Fact]
    public async Task Unconfirmed_account_cannot_sign_in_and_link_confirms_only_via_post_once()
    {
        var email = NewEmail();
        using var client = factory.CreateAppClient();
        (await client.SubmitRegistrationAsync(email)).AssertRedirectTo("/Account/Register?submitted=True");

        // Doğrulanmamış hesap: doğru parolayla bile giriş yok; yanlış parolada genel mesaj.
        var wrong = await client.LoginAsync(email, "yanlis-parola-999");
        Assert.Equal(LoginModel.GenericError, (await client.ParseAsync(wrong)).QuerySelector(".alert-error")!.TextContent.Trim());
        var unconfirmed = await client.LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, unconfirmed.StatusCode);
        Assert.Contains(LoginModel.UnconfirmedError, await unconfirmed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, await factory.WithDbAsync(db => db.UserSessions.CountAsync(s => db.Users.Any(u => u.Id == s.UserId && u.Email == email))));

        // Bağlantıyı açmak (GET) hesabı doğrulamaz — e-posta tarayıcıları bağlantıları önceden açabilir.
        var link = await client.GetConfirmationLinkAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync(link)).StatusCode);
        Assert.False(await factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.EmailConfirmed).SingleAsync()));

        (await client.ConfirmEmailAsync(email, link)).AssertRedirectTo("/Account/Login");
        Assert.True(await factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.EmailConfirmed).SingleAsync()));

        // Tek kullanımlık: aynı bağlantı ikinci kez çalışmaz.
        var reuse = await client.ConfirmEmailAsync(email, link);
        Assert.Contains(ConfirmEmailModel.InvalidLinkError, await reuse.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        (await client.LoginAsync(email)).AssertRedirectTo("/Library");
    }

    [Fact]
    public async Task Registering_an_existing_address_returns_the_same_response_and_does_not_change_the_account()
    {
        var existing = NewEmail();
        using (var owner = factory.CreateAppClient())
        {
            await owner.RegisterAsync(existing);
        }

        using var attacker = factory.CreateAppClient();
        var forExisting = await attacker.SubmitRegistrationAsync(existing.ToUpperInvariant(), "saldirgan-parola-1");
        var forNew = await attacker.SubmitRegistrationAsync(NewEmail(), "saldirgan-parola-1");

        Assert.Equal(forNew.StatusCode, forExisting.StatusCode);
        Assert.Equal(forNew.Headers.Location, forExisting.Headers.Location);
        var (_, pageExisting) = await attacker.GetPageAsync(forExisting.Headers.Location!.OriginalString);
        var (_, pageNew) = await attacker.GetPageAsync(forNew.Headers.Location!.OriginalString);
        Assert.Equal(pageNew.QuerySelector("main")!.TextContent.Trim(), pageExisting.QuerySelector("main")!.TextContent.Trim());

        // Hesap devralınamaz: yeni kullanıcı oluşmaz, parola değişmez; gerçek sahibine bilgi e-postası gider.
        var normalized = existing.ToUpperInvariant();
        Assert.Equal(1, await factory.WithDbAsync(db => db.Users.CountAsync(u => u.NormalizedEmail == normalized)));
        await factory.Emails.WaitForAsync(existing, "zaten bir hesabın var");
        using var login = factory.CreateAppClient();
        Assert.Equal(HttpStatusCode.OK, (await login.LoginAsync(existing, "saldirgan-parola-1")).StatusCode);
        (await login.LoginAsync(existing)).AssertRedirectTo("/Library");
    }

    [Fact]
    public async Task Password_rules_are_applied_before_checking_whether_the_address_exists()
    {
        var existing = NewEmail();
        using (var owner = factory.CreateAppClient())
        {
            await owner.RegisterAsync(existing);
        }

        using var client = factory.CreateAppClient();
        var weakExisting = await client.SubmitRegistrationAsync(existing, "aaaaaaaaaa");
        var weakNew = await client.SubmitRegistrationAsync(NewEmail(), "aaaaaaaaaa");

        Assert.Equal(HttpStatusCode.OK, weakExisting.StatusCode);
        Assert.Equal(HttpStatusCode.OK, weakNew.StatusCode);
        var a = (await client.ParseAsync(weakExisting)).QuerySelector("#Input_Password-error")!.TextContent.Trim();
        var b = (await client.ParseAsync(weakNew)).QuerySelector("#Input_Password-error")!.TextContent.Trim();
        Assert.NotEmpty(a);
        Assert.Equal(b, a);
    }

    [Fact]
    public async Task Re_registering_an_unconfirmed_address_sends_a_new_confirmation_link()
    {
        var email = NewEmail();
        using var client = factory.CreateAppClient();
        await client.SubmitRegistrationAsync(email);
        await factory.Emails.WaitForAsync(email, "doğrula");
        await factory.WithDbAsync(db => db.OutboxEmails.Where(o => o.Kind == "confirm-email").ExecuteUpdateAsync(u => u.SetProperty(o => o.DedupKey, (string?)null)));

        await client.SubmitRegistrationAsync(email);
        await factory.Emails.WaitForAsync(email, "doğrula", expectedCount: 2);
        Assert.Equal(0, factory.Emails.CountFor(email, "zaten bir hesabın var"));
    }

    [Fact]
    public async Task Resend_confirmation_responds_identically_and_only_mails_unconfirmed_accounts()
    {
        var unconfirmed = NewEmail();
        var confirmed = NewEmail();
        var unknown = NewEmail();
        using var client = factory.CreateAppClient();
        await client.SubmitRegistrationAsync(unconfirmed);
        await factory.Emails.WaitForAsync(unconfirmed, "doğrula");
        await factory.WithDbAsync(db => db.OutboxEmails.ExecuteUpdateAsync(u => u.SetProperty(o => o.DedupKey, (string?)null)));
        using (var other = factory.CreateAppClient())
        {
            await other.RegisterAsync(confirmed);
        }

        var responses = new List<HttpResponseMessage>();
        foreach (var email in new[] { unconfirmed, confirmed, unknown })
        {
            responses.Add(await client.PostFormAsync("/Account/ResendConfirmation", new Dictionary<string, string> { ["Input.Email"] = email }));
        }

        Assert.All(responses, r => r.AssertRedirectTo("/Account/ResendConfirmation?sent=True"));
        await factory.Emails.WaitForAsync(unconfirmed, "doğrula", expectedCount: 2);
        await Task.Delay(1500);
        Assert.Equal(1, factory.Emails.CountFor(confirmed, "doğrula"));
        Assert.Equal(0, factory.Emails.CountFor(unknown));
    }

    [Fact]
    public async Task Password_reset_confirms_an_unconfirmed_address()
    {
        var email = NewEmail();
        using var client = factory.CreateAppClient();
        await client.SubmitRegistrationAsync(email);
        await client.PostFormAsync("/Account/ForgotPassword", new Dictionary<string, string> { ["Input.Email"] = email });
        var message = await factory.Emails.WaitForAsync(email, "parola sıfırlama");
        var link = message.TextBody.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith("http://localhost/Account/ResetPassword", StringComparison.Ordinal))["http://localhost".Length..];
        var (_, doc) = await client.GetPageAsync(link);
        var reset = await client.PostFormAsync("/Account/ResetPassword", new Dictionary<string, string>
        {
            ["Input.UserId"] = doc.QuerySelector("input[name='Input.UserId']")!.GetAttribute("value")!,
            ["Input.Token"] = doc.QuerySelector("input[name='Input.Token']")!.GetAttribute("value")!,
            ["Input.Password"] = "sifirlanmis-parola-1",
            ["Input.ConfirmPassword"] = "sifirlanmis-parola-1"
        }, tokenPage: link);
        reset.AssertRedirectTo("/Account/Login");

        (await client.LoginAsync(email, "sifirlanmis-parola-1")).AssertRedirectTo("/Library");
    }

    [Fact]
    public async Task Tampered_confirmation_links_are_rejected()
    {
        var email = NewEmail();
        using var client = factory.CreateAppClient();
        await client.SubmitRegistrationAsync(email);
        var link = await client.GetConfirmationLinkAsync(email);
        var tampered = link[..^4] + "AAAA";

        var response = await client.ConfirmEmailAsync(email, tampered);
        Assert.Contains(ConfirmEmailModel.InvalidLinkError, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(await factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.EmailConfirmed).SingleAsync()));

        var (_, missing) = await client.GetPageAsync("/Account/ConfirmEmail");
        Assert.Null(missing.QuerySelector("button[type=submit]"));
    }
}

/// <summary>Kısa token ömrüyle yapılandırılmış uygulama: süre dolumu testleri için.</summary>
public sealed class ShortTokenFactory : GameVaultFactory
{
    protected override void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        services.Configure<GameVault.Web.Infrastructure.EmailConfirmationTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromSeconds(2));
        services.Configure<Microsoft.AspNetCore.Identity.DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromSeconds(2));
    }
}

public sealed class TokenExpiryTests(ShortTokenFactory factory) : IClassFixture<ShortTokenFactory>
{
    [Fact]
    public async Task Expired_confirmation_and_reset_links_are_rejected()
    {
        var email = $"expiry-{Guid.NewGuid():N}@example.test";
        using var client = factory.CreateAppClient();
        await client.SubmitRegistrationAsync(email);
        var confirmLink = await client.GetConfirmationLinkAsync(email);
        await client.PostFormAsync("/Account/ForgotPassword", new Dictionary<string, string> { ["Input.Email"] = email });
        var resetMail = await factory.Emails.WaitForAsync(email, "parola sıfırlama");
        var resetLink = resetMail.TextBody.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith("http://localhost/Account/ResetPassword", StringComparison.Ordinal))["http://localhost".Length..];
        var (_, resetDoc) = await client.GetPageAsync(resetLink);

        await Task.Delay(TimeSpan.FromSeconds(3));

        var confirm = await client.ConfirmEmailAsync(email, confirmLink);
        Assert.Contains(ConfirmEmailModel.InvalidLinkError, await confirm.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var reset = await client.PostFormAsync("/Account/ResetPassword", new Dictionary<string, string>
        {
            ["Input.UserId"] = resetDoc.QuerySelector("input[name='Input.UserId']")!.GetAttribute("value")!,
            ["Input.Token"] = resetDoc.QuerySelector("input[name='Input.Token']")!.GetAttribute("value")!,
            ["Input.Password"] = "sure-dolmus-parola-1",
            ["Input.ConfirmPassword"] = "sure-dolmus-parola-1"
        }, tokenPage: resetLink);
        Assert.Contains(ResetPasswordModel.InvalidLinkError, await reset.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
