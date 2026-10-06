using System.Net;
using GameVault.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Tests;

/// <summary>
/// İki adımlı doğrulama (TOTP): kurulum, girişte ikinci adım, tekrar kullanılan kod, kurtarma kodları, kilit,
/// kapatma ve diğer oturumların kapatılması. Kodlar bağımsız bir RFC 6238 uygulamasıyla (<see cref="Totp"/>) üretilir.
/// </summary>
public sealed class TwoFactorTests(GameVaultFactory factory) : IClassFixture<GameVaultFactory>
{
    private sealed record Enabled(string Key, IReadOnlyList<string> RecoveryCodes, string UsedCode);

    private static string NewEmail() => $"twofa-{Guid.NewGuid():N}@example.test";

    private async Task<(AppClient Client, string Email)> NewUserAsync()
    {
        var email = NewEmail();
        var client = factory.CreateAppClient();
        await client.RegisterAsync(email);
        return (client, email);
    }

    private static async Task<string> SetupKeyAsync(AppClient client)
    {
        var setup = await client.PostFormAsync("/Account/TwoFactor?handler=Setup", [], "/Account/TwoFactor");
        Assert.Equal(HttpStatusCode.Redirect, setup.StatusCode);

        var (_, doc) = await client.GetPageAsync("/Account/TwoFactor");
        var key = doc.QuerySelector("#shared-key")!.TextContent.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        Assert.Matches("^[A-Z2-7]{32,}$", key);
        return key;
    }

    private static async Task<Enabled> EnableAsync(AppClient client)
    {
        var key = await SetupKeyAsync(client);
        var code = Totp.Now(key);
        var response = await client.PostFormAsync("/Account/TwoFactor?handler=Enable",
            [new("Enable.Code", code), new("Enable.Password", AppClient.DefaultPassword)], "/Account/TwoFactor");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await client.ParseAsync(response);
        var codes = doc.QuerySelectorAll("#recovery-codes code").Select(c => c.TextContent.Trim()).ToList();
        Assert.Equal(10, codes.Count);
        Assert.Contains("Açık", doc.QuerySelector("#twofa-status")!.TextContent);
        return new Enabled(key, codes, code);
    }

    /// <summary>Şu anki koddan farklı ve henüz kullanılmamış geçerli bir kod.</summary>
    private static string FreshCode(Enabled state)
    {
        var now = Totp.Now(state.Key);
        return now != state.UsedCode ? now : Totp.Previous(state.Key);
    }

    private static async Task<HttpResponseMessage> PasswordStepAsync(AppClient client, string email)
    {
        var response = await client.LoginAsync(email);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Account/LoginWith2fa", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        return response;
    }

    private static Task<HttpResponseMessage> SubmitCodeAsync(AppClient client, string code) =>
        client.PostFormAsync("/Account/LoginWith2fa?returnUrl=%2FLibrary", [new("Input.Code", code)], "/Account/LoginWith2fa");

    [Fact]
    public async Task Setup_shows_an_svg_qr_code_and_wrong_codes_do_not_enable()
    {
        var (client, email) = await NewUserAsync();
        using (client)
        {
            var key = await SetupKeyAsync(client);
            var (_, doc) = await client.GetPageAsync("/Account/TwoFactor");
            var qr = doc.QuerySelector("svg.qr[role=img]");
            Assert.NotNull(qr);
            Assert.Empty(doc.QuerySelectorAll("[style]"));

            var wrong = await client.PostFormAsync("/Account/TwoFactor?handler=Enable",
                [new("Enable.Code", "000000"), new("Enable.Password", AppClient.DefaultPassword)], "/Account/TwoFactor");
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

            var wrongPassword = await client.PostFormAsync("/Account/TwoFactor?handler=Enable",
                [new("Enable.Code", Totp.Now(key)), new("Enable.Password", "yanlis-parola-99")], "/Account/TwoFactor");
            Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);

            Assert.False(await factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.TwoFactorEnabled).SingleAsync()));
        }
    }

    [Fact]
    public async Task Login_requires_a_valid_code_after_enabling_and_the_owner_is_notified()
    {
        var (client, email) = await NewUserAsync();
        using (client)
        {
            var state = await EnableAsync(client);
            await factory.Emails.WaitForAsync(email, "iki adımlı doğrulama açıldı");

            await client.LogoutAsync();
            await PasswordStepAsync(client, email);

            // Parola tek başına oturum açtırmaz.
            (await client.Http.GetAsync("/Library")).AssertRedirectToLogin();

            var wrong = await SubmitCodeAsync(client, "123456" == FreshCode(state) ? "654321" : "123456");
            Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
            Assert.Contains("Kod hatalı", (await client.ParseAsync(wrong)).QuerySelector("[role=alert]")!.TextContent);

            var ok = await SubmitCodeAsync(client, FreshCode(state));
            ok.AssertRedirectTo("/Library");
            Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/Library")).StatusCode);
        }
    }

    [Fact]
    public async Task A_code_that_was_already_used_is_rejected()
    {
        var (client, email) = await NewUserAsync();
        using (client)
        {
            var state = await EnableAsync(client);
            await client.LogoutAsync();
            await PasswordStepAsync(client, email);

            // Kurulumda kullanılan kod hâlâ zaman penceresinde olsa da ikinci kez kabul edilmez.
            var replay = await SubmitCodeAsync(client, state.UsedCode);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            (await client.Http.GetAsync("/Library")).AssertRedirectToLogin();

            var fresh = FreshCode(state);
            (await SubmitCodeAsync(client, fresh)).AssertRedirectTo("/Library");

            await client.LogoutAsync();
            await PasswordStepAsync(client, email);
            Assert.Equal(HttpStatusCode.OK, (await SubmitCodeAsync(client, fresh)).StatusCode);
            (await client.Http.GetAsync("/Library")).AssertRedirectToLogin();
        }
    }

    [Fact]
    public async Task Recovery_codes_work_once()
    {
        var (client, email) = await NewUserAsync();
        using (client)
        {
            var state = await EnableAsync(client);
            await client.LogoutAsync();
            await PasswordStepAsync(client, email);

            var used = state.RecoveryCodes[0];
            var ok = await client.PostFormAsync("/Account/LoginWithRecoveryCode?returnUrl=%2FLibrary",
                [new("Input.Code", " " + used.ToLowerInvariant() + " ")], "/Account/LoginWithRecoveryCode");
            ok.AssertRedirectTo("/Library");

            await client.LogoutAsync();
            await PasswordStepAsync(client, email);
            var again = await client.PostFormAsync("/Account/LoginWithRecoveryCode", [new("Input.Code", used)], "/Account/LoginWithRecoveryCode");
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            Assert.Contains("daha önce kullanılmış", (await client.ParseAsync(again)).QuerySelector("[role=alert]")!.TextContent);
        }
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_account_even_if_the_password_is_entered_again()
    {
        var (client, email) = await NewUserAsync();
        using (client)
        {
            var state = await EnableAsync(client);
            await client.LogoutAsync();

            await PasswordStepAsync(client, email);
            for (var i = 0; i < 3; i++)
            {
                await SubmitCodeAsync(client, "000000");
            }

            // Parolayı yeniden girmek ikinci adımdaki hatalı deneme sayacını sıfırlamaz.
            await PasswordStepAsync(client, email);
            await SubmitCodeAsync(client, "000000");
            var fifth = await SubmitCodeAsync(client, "000000");
            Assert.Contains("kilitlendi", (await client.ParseAsync(fifth)).QuerySelector("[role=alert]")!.TextContent);

            // Kilitliyken doğru kod da kabul edilmez.
            var correct = await SubmitCodeAsync(client, FreshCode(state));
            Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
            (await client.Http.GetAsync("/Library")).AssertRedirectToLogin();
        }
    }

    [Fact]
    public async Task Second_step_pages_without_a_pending_login_redirect_to_login()
    {
        using var client = factory.CreateAppClient();

        var get = await client.Http.GetAsync("/Account/LoginWith2fa");
        get.AssertRedirectTo("/Account/Login");
        var recovery = await client.Http.GetAsync("/Account/LoginWithRecoveryCode");
        recovery.AssertRedirectTo("/Account/Login");
    }

    [Fact]
    public async Task Enabling_signs_out_other_sessions_but_keeps_the_current_one()
    {
        var email = NewEmail();
        using var first = factory.CreateAppClient();
        await first.RegisterAsync(email);
        using var second = factory.CreateAppClient();
        Assert.Equal(HttpStatusCode.Redirect, (await second.LoginAsync(email)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.Http.GetAsync("/Library")).StatusCode);

        await EnableAsync(first);

        Assert.Equal(HttpStatusCode.OK, (await first.Http.GetAsync("/Library")).StatusCode);
        (await second.Http.GetAsync("/Library")).AssertRedirectToLogin();
    }

    [Fact]
    public async Task Disabling_requires_the_password_and_resets_the_key()
    {
        var (client, email) = await NewUserAsync();
        using (client)
        {
            var state = await EnableAsync(client);

            var wrong = await client.PostFormAsync("/Account/TwoFactor?handler=Disable", [new("Confirm.Password", "yanlis-parola-99")], "/Account/TwoFactor");
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            Assert.True(await factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.TwoFactorEnabled).SingleAsync()));

            var ok = await client.PostFormAsync("/Account/TwoFactor?handler=Disable", [new("Confirm.Password", AppClient.DefaultPassword)], "/Account/TwoFactor");
            ok.AssertRedirectTo("/Account/TwoFactor");
            await factory.Emails.WaitForAsync(email, "iki adımlı doğrulama kapatıldı");

            // Kapatınca giriş yine tek adım; eski anahtar da artık geçerli değil.
            await client.LogoutAsync();
            (await client.LoginAsync(email)).AssertRedirectTo("/Library");
            Assert.NotEqual(state.Key, await SetupKeyAsync(client));
        }
    }

    [Fact]
    public async Task Regenerating_recovery_codes_invalidates_the_old_ones()
    {
        var (client, email) = await NewUserAsync();
        using (client)
        {
            var state = await EnableAsync(client);
            var response = await client.PostFormAsync("/Account/TwoFactor?handler=RecoveryCodes", [new("Confirm.Password", AppClient.DefaultPassword)], "/Account/TwoFactor");
            var fresh = (await client.ParseAsync(response)).QuerySelectorAll("#recovery-codes code").Select(c => c.TextContent.Trim()).ToList();
            Assert.Equal(10, fresh.Count);
            Assert.Empty(fresh.Intersect(state.RecoveryCodes));

            await client.LogoutAsync();
            await PasswordStepAsync(client, email);
            var old = await client.PostFormAsync("/Account/LoginWithRecoveryCode", [new("Input.Code", state.RecoveryCodes[1])], "/Account/LoginWithRecoveryCode");
            Assert.Equal(HttpStatusCode.OK, old.StatusCode);
            var newer = await client.PostFormAsync("/Account/LoginWithRecoveryCode", [new("Input.Code", fresh[0])], "/Account/LoginWithRecoveryCode");
            newer.AssertRedirectTo("/Library");
        }
    }

    // ---------- Saklama biçimi (AppUserStore) ----------

    private const string Provider = "[AspNetUserStore]";

    private Task<string?> StoredTokenAsync(string email, string name) =>
        factory.WithDbAsync(db => db.UserTokens
            .Where(t => t.LoginProvider == Provider && t.Name == name && db.Users.Any(u => u.Id == t.UserId && u.Email == email))
            .Select(t => t.Value).SingleOrDefaultAsync());

    private Task SetStoredTokenAsync(string email, string name, string value) =>
        factory.WithDbAsync(async db =>
        {
            var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            var token = await db.UserTokens.SingleAsync(t => t.UserId == userId && t.LoginProvider == Provider && t.Name == name);
            token.Value = value;
            await db.SaveChangesAsync();
        });

    [Fact]
    public async Task Database_holds_no_readable_authenticator_key_or_recovery_code()
    {
        var (client, email) = await NewUserAsync();
        using (client)
        {
            var state = await EnableAsync(client);

            var key = await StoredTokenAsync(email, "AuthenticatorKey");
            Assert.StartsWith("dp1:", key, StringComparison.Ordinal);
            Assert.DoesNotContain(state.Key, key!, StringComparison.OrdinalIgnoreCase);

            var codes = await StoredTokenAsync(email, "RecoveryCodes");
            Assert.StartsWith("pbkdf2-sha256$", codes, StringComparison.Ordinal);
            Assert.All(state.RecoveryCodes, c => Assert.DoesNotContain(c, codes!, StringComparison.OrdinalIgnoreCase));

            // Tekrar kullanım koruması da kodun kendisini değil özetini tutar.
            var lastUsed = await factory.WithDbAsync(db => db.UserTokens
                .Where(t => t.LoginProvider == "GameVault" && t.Name == "LastAuthenticatorCode" && db.Users.Any(u => u.Id == t.UserId && u.Email == email))
                .Select(t => t.Value).SingleAsync());
            var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(state.UsedCode)));
            Assert.StartsWith(expectedHash + "|", lastUsed, StringComparison.Ordinal);

            // Şifreli saklama girişi bozmaz.
            await client.LogoutAsync();
            await PasswordStepAsync(client, email);
            (await SubmitCodeAsync(client, FreshCode(state))).AssertRedirectTo("/Library");
        }
    }

    [Fact]
    public async Task Legacy_plaintext_secrets_keep_working_and_are_converted_on_first_use()
    {
        var (client, email) = await NewUserAsync();
        using (client)
        {
            await EnableAsync(client);
            // Bu değişiklikten önce kurulmuş bir hesabı taklit et: Identity'nin varsayılan düz metin biçimi.
            const string legacyKey = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
            await SetStoredTokenAsync(email, "AuthenticatorKey", legacyKey);
            await SetStoredTokenAsync(email, "RecoveryCodes", "BCDFG-HJKMN;PQRTV-WXY23");

            await client.LogoutAsync();
            await PasswordStepAsync(client, email);
            (await SubmitCodeAsync(client, Totp.Now(legacyKey))).AssertRedirectTo("/Library");
            Assert.StartsWith("dp1:", await StoredTokenAsync(email, "AuthenticatorKey"), StringComparison.Ordinal);

            await client.LogoutAsync();
            await PasswordStepAsync(client, email);
            var recovery = await client.PostFormAsync("/Account/LoginWithRecoveryCode?returnUrl=%2FLibrary",
                [new("Input.Code", "pqrtv-wxy23")], "/Account/LoginWithRecoveryCode");
            recovery.AssertRedirectTo("/Library");

            var codes = await StoredTokenAsync(email, "RecoveryCodes");
            Assert.StartsWith("pbkdf2-sha256$", codes, StringComparison.Ordinal);
            Assert.DoesNotContain("BCDFG", codes!, StringComparison.Ordinal);
            Assert.Single(codes!.Split('$')[3].Split(','));
        }
    }

    [Fact]
    public async Task An_undecryptable_key_does_not_lock_the_user_out()
    {
        var (client, email) = await NewUserAsync();
        using (client)
        {
            var state = await EnableAsync(client);
            // Data Protection anahtar halkası kaybolmuş gibi: kayıt artık çözülemiyor.
            await SetStoredTokenAsync(email, "AuthenticatorKey", "dp1:CfDJ8bozuk-ve-cozulemeyen-veri");

            await client.LogoutAsync();
            await PasswordStepAsync(client, email);
            var totp = await SubmitCodeAsync(client, FreshCode(state));
            Assert.Equal(HttpStatusCode.OK, totp.StatusCode);

            // Kurtarma kodları Data Protection'a bağlı değil: kullanıcı girer ve 2FA'yı yeniden kurabilir.
            var recovery = await client.PostFormAsync("/Account/LoginWithRecoveryCode?returnUrl=%2FLibrary",
                [new("Input.Code", state.RecoveryCodes[0])], "/Account/LoginWithRecoveryCode");
            recovery.AssertRedirectTo("/Library");
            var (page, _) = await client.GetPageAsync("/Account/TwoFactor");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains(factory.Logs.Lines, m => m.Contains("doğrulayıcı anahtarı çözülemedi", StringComparison.Ordinal));
        }
    }
}
