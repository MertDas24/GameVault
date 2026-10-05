using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GameVault.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Tests;

internal static class TestCertificates
{
    /// <summary>Testler için geçici, kendinden imzalı RSA sertifikası (.pfx) üretir.</summary>
    public static (string Path, string Password) Create(string name)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"gv-test-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
        return (path, password);
    }
}

public sealed class CertificateFactory((string Path, string Password) certificate, string connectionString, bool ownsDatabase)
    : GameVaultFactory(connectionString, ownsDatabase)
{
    protected override IDictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["DataProtection:Certificate:Path"] = certificate.Path;
            settings["DataProtection:Certificate:Password"] = certificate.Password;
            return settings;
        }
    }
}

public sealed class DataProtectionTests : IAsyncLifetime
{
    private readonly (string Path, string Password) _cert = TestCertificates.Create("GameVault DP Test");
    private readonly (string Path, string Password) _otherCert = TestCertificates.Create("GameVault DP Other");
    private CertificateFactory _app = null!;

    public async ValueTask InitializeAsync()
    {
        _app = new CertificateFactory(_cert, TestDatabase.CreateConnectionString(), ownsDatabase: true);
        await _app.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        File.Delete(_cert.Path);
        File.Delete(_otherCert.Path);
    }

    private async Task<string> SignInAndGetSessionCookieAsync(GameVaultFactory factory)
    {
        var email = $"dp-{Guid.NewGuid():N}@example.test";
        using (var setup = factory.CreateAppClient())
        {
            await setup.RegisterAsync(email);
        }

        using var raw = factory.CreateRawClient();
        var page = await raw.GetAsync("/Account/Login");
        var response = await raw.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Headers = { { "Cookie", AuthTests.CookieHeader(page) } },
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = email,
                ["Input.Password"] = AppClient.DefaultPassword,
                ["__RequestVerificationToken"] = await AuthTests.ExtractTokenAsync(page)
            })
        });
        response.AssertRedirectTo("/Library");
        return AuthTests.CookieHeader(response);
    }

    [Fact]
    public async Task Keys_are_encrypted_at_rest_with_the_certificate()
    {
        await SignInAndGetSessionCookieAsync(_app);

        var xml = await _app.WithDbAsync(db => db.DataProtectionKeys.Select(k => k.Xml!).ToListAsync());
        Assert.NotEmpty(xml);
        Assert.All(xml, k =>
        {
            Assert.Contains("encryptedSecret", k, StringComparison.Ordinal);
            Assert.Contains("X509Data", k, StringComparison.Ordinal);
            Assert.DoesNotContain("<value>", k, StringComparison.Ordinal); // şifrelenmemiş anahtar biçimi
        });
    }

    [Fact]
    public async Task Second_instance_with_the_same_certificate_accepts_existing_sessions()
    {
        var cookie = await SignInAndGetSessionCookieAsync(_app);

        await using var second = new CertificateFactory(_cert, _app.ConnectionString, ownsDatabase: false);
        await second.InitializeAsync();
        using var raw = second.CreateRawClient();
        Assert.Equal(HttpStatusCode.OK, (await AuthTests.GetWithCookie(raw, "/Library", cookie)).StatusCode);
    }

    [Fact]
    public async Task Instance_with_a_different_certificate_cannot_use_existing_keys()
    {
        var cookie = await SignInAndGetSessionCookieAsync(_app);

        await using var other = new CertificateFactory(_otherCert, _app.ConnectionString, ownsDatabase: false);
        await other.InitializeAsync();
        using var raw = other.CreateRawClient();
        (await AuthTests.GetWithCookie(raw, "/Library", cookie)).AssertRedirectToLogin();
    }

    [Fact]
    public async Task Rotated_certificate_keeps_old_keys_readable_when_previous_certificate_is_configured()
    {
        var cookie = await SignInAndGetSessionCookieAsync(_app);

        await using var rotated = new RotatedFactory(_otherCert, _cert, _app.ConnectionString);
        await rotated.InitializeAsync();
        using var raw = rotated.CreateRawClient();
        Assert.Equal(HttpStatusCode.OK, (await AuthTests.GetWithCookie(raw, "/Library", cookie)).StatusCode);
    }

    private sealed class RotatedFactory((string Path, string Password) current, (string Path, string Password) previous, string connectionString)
        : GameVaultFactory(connectionString, ownsDatabase: false)
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                settings["DataProtection:Certificate:Path"] = current.Path;
                settings["DataProtection:Certificate:Password"] = current.Password;
                settings["DataProtection:PreviousCertificates:0:Path"] = previous.Path;
                settings["DataProtection:PreviousCertificates:0:Password"] = previous.Password;
                return settings;
            }
        }
    }

    [Fact]
    public async Task Without_a_certificate_keys_are_stored_unencrypted_which_is_only_allowed_outside_production()
    {
        await using var plain = new GameVaultFactory();
        await plain.InitializeAsync();
        using (var client = plain.CreateAppClient())
        {
            await client.Http.GetAsync("/Account/Login");
        }

        var xml = await plain.WithDbAsync(db => db.DataProtectionKeys.Select(k => k.Xml!).FirstAsync());
        Assert.Contains("<value>", xml, StringComparison.Ordinal);
    }
}
