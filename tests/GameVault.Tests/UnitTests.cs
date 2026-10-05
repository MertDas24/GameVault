using GameVault.Business.Covers;
using GameVault.Business.Security;
using GameVault.Entities;
using GameVault.Models.Library;
using GameVault.Web.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GameVault.Tests;

public sealed class LibraryQueryTests
{
    [Fact]
    public void Defaults_are_applied_for_missing_values()
    {
        var q = LibraryQuery.FromRaw(null, null, null, null, null, null, null, null);
        Assert.Null(q.Search);
        Assert.Equal(1, q.Page);
        Assert.Equal(LibraryQuery.DefaultPageSize, q.PageSize);
        Assert.Equal(LibrarySort.AddedDesc, q.Sort);
        Assert.False(q.HasFilters);
    }

    [Theory]
    [InlineData("0", 1)]
    [InlineData("-4", 1)]
    [InlineData("abc", 1)]
    [InlineData("1e3", 1)]
    [InlineData("99999999999", 1)]
    [InlineData("20000", LibraryQuery.MaxPage)]
    [InlineData("3", 3)]
    public void Page_is_clamped(string raw, int expected) =>
        Assert.Equal(expected, LibraryQuery.FromRaw(null, null, null, null, null, null, raw, null).Page);

    [Theory]
    [InlineData("12", 12)]
    [InlineData("48", 48)]
    [InlineData("1000", LibraryQuery.DefaultPageSize)]
    [InlineData("13", LibraryQuery.DefaultPageSize)]
    public void Page_size_is_limited_to_allowed_values(string raw, int expected) =>
        Assert.Equal(expected, LibraryQuery.FromRaw(null, null, null, null, null, null, null, raw).PageSize);

    [Fact]
    public void Search_is_trimmed_and_truncated()
    {
        var q = LibraryQuery.FromRaw("  " + new string('a', 300) + "  ", null, null, null, null, null, null, null);
        Assert.Equal(LibraryQuery.MaxSearchLength, q.Search!.Length);
        Assert.Null(LibraryQuery.FromRaw("   ", null, null, null, null, null, null, null).Search);
    }

    [Fact]
    public void Unknown_status_and_sort_are_ignored()
    {
        var q = LibraryQuery.FromRaw(null, "hacked", "x", "-1", "maybe", "DROP TABLE", null, null);
        Assert.Null(q.Status);
        Assert.Null(q.PlatformId);
        Assert.Null(q.GenreId);
        Assert.False(q.FavoritesOnly);
        Assert.Equal(LibrarySort.AddedDesc, q.Sort);
        Assert.Equal(GameStatus.OnHold, LibraryQuery.FromRaw(null, "on-hold", null, null, null, null, null, null).Status);
    }

    [Fact]
    public void Every_status_has_a_label_and_round_trips_through_its_key()
    {
        foreach (var status in Enum.GetValues<GameStatus>())
        {
            Assert.NotEqual("Bilinmiyor", StatusLabels.Label(status));
            Assert.Equal(status, StatusLabels.FromKey(StatusLabels.Key(status)));
        }

        Assert.Equal(Enum.GetValues<GameStatus>().Length, StatusLabels.Ordered.Count);
    }
}

public sealed class CoverPolicyTests
{
    private static CoverPolicy Policy(params string[] hosts) =>
        new(Options.Create(new CoverOptions { AllowedHosts = [.. hosts] }));

    [Fact]
    public void Built_in_covers_resolve_to_local_paths()
    {
        var result = Policy().Resolve("builtin:forest", null);
        Assert.True(result.Succeeded);
        Assert.Equal("/covers/forest.svg", result.Value);
        Assert.True(Policy().IsDisplayable("/covers/forest.svg"));
        Assert.False(Policy().IsDisplayable("/covers/../appsettings.json"));
    }

    [Fact]
    public void External_urls_are_rejected_when_no_hosts_are_allowed()
    {
        var result = Policy().Resolve("url", "https://images.example.com/a.png");
        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("https://images.example.com/a.png", true)]
    [InlineData("https://IMAGES.example.com/a.png", true)]
    [InlineData("http://images.example.com/a.png", false)]
    [InlineData("https://images.example.com:444/a.png", false)]
    [InlineData("https://a@images.example.com/a.png", false)]
    [InlineData("https://images.example.com/a.png#x", false)]
    [InlineData("https://images.example.com.evil.test/a.png", false)]
    [InlineData("https://evil.test/?u=https://images.example.com", false)]
    [InlineData("//images.example.com/a.png", false)]
    [InlineData("/covers/../../secret", false)]
    public void External_urls_must_match_allow_list_exactly(string url, bool allowed)
    {
        var policy = Policy("images.example.com");
        Assert.Equal(allowed, policy.Resolve("url", url).Succeeded);
        Assert.Equal(allowed, policy.IsDisplayable(url));
    }

    [Fact]
    public void Stored_values_round_trip_to_form_choices()
    {
        Assert.Equal(("builtin:ocean", (string?)null), CoverPolicy.ToChoice("/covers/ocean.svg"));
        Assert.Equal(("none", (string?)null), CoverPolicy.ToChoice(null));
        Assert.Equal(("url", "https://x.test/a.png"), CoverPolicy.ToChoice("https://x.test/a.png"));
    }
}

public sealed class InfrastructureTests
{
    [Fact]
    public void Rate_limit_keys_do_not_contain_raw_partition_values()
    {
        var key = DbRateLimiter.BuildKey("login-account", "email:USER@EXAMPLE.TEST");
        Assert.StartsWith("login-account:", key, StringComparison.Ordinal);
        Assert.DoesNotContain("EXAMPLE", key, StringComparison.Ordinal);
        Assert.True(key.Length <= 128);
    }

    [Theory]
    [InlineData("203.0.113.7", "ip:203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "ip:203.0.113.7")]
    [InlineData("2001:db8:1:2:3:4:5:6", "ip:2001:db8:1:2::")]
    public void Client_ip_partition_normalizes_addresses(string ip, string expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
        Assert.Equal(expected, ClientPartition.ForIp(context));
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "GameVault.Web";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static readonly (string Path, string Password) ProductionCert = TestCertificates.Create("GameVault Unit");

    private static Dictionary<string, string?> ValidProduction() => new()
    {
        ["DataProtection:Certificate:Path"] = ProductionCert.Path,
        ["DataProtection:Certificate:Password"] = ProductionCert.Password,
        ["ConnectionStrings:DefaultConnection"] = "Server=db;Database=x;User Id=u;Password=p",
        ["App:PublicBaseUrl"] = "https://gamevault.example.com",
        ["App:DisplayTimeZone"] = "Europe/Istanbul",
        ["Email:Host"] = "smtp.example.com",
        ["Email:FromAddress"] = "no-reply@example.com",
        ["Email:Security"] = "StartTls"
    };

    [Fact]
    public void Valid_production_configuration_passes() =>
        StartupValidation.Validate(Config(ValidProduction()), new Env("Production"));

    [Fact]
    public void Production_refuses_to_start_without_a_usable_data_protection_certificate()
    {
        var missing = ValidProduction();
        missing.Remove("DataProtection:Certificate:Path");
        var ex = Assert.Throws<InvalidOperationException>(() => StartupValidation.Validate(Config(missing), new Env("Production")));
        Assert.Contains("DataProtection:Certificate:Path", ex.Message, StringComparison.Ordinal);

        var wrongPassword = ValidProduction();
        wrongPassword["DataProtection:Certificate:Password"] = "yanlis-sertifika-parolasi";
        ex = Assert.Throws<InvalidOperationException>(() => StartupValidation.Validate(Config(wrongPassword), new Env("Production")));
        Assert.Contains("sertifikası açılamadı", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("yanlis-sertifika-parolasi", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ProductionCert.Password, ex.Message, StringComparison.Ordinal);

        var nonexistent = ValidProduction();
        nonexistent["DataProtection:Certificate:Path"] = Path.Combine(Path.GetTempPath(), "yok-" + Guid.NewGuid() + ".pfx");
        ex = Assert.Throws<InvalidOperationException>(() => StartupValidation.Validate(Config(nonexistent), new Env("Production")));
        Assert.Contains("bulunamadı", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_rejects_non_local_smtp_unless_explicitly_allowed()
    {
        var values = ValidProduction();
        values["Email:Host"] = "smtp.gercek-saglayici.example";
        var ex = Assert.Throws<InvalidOperationException>(() => StartupValidation.Validate(Config(values), new Env("Development")));
        Assert.Contains("Email:Host yerel", ex.Message, StringComparison.Ordinal);

        values["Email:AllowNonLocalSmtpInDevelopment"] = "true";
        StartupValidation.Validate(Config(values), new Env("Development"));

        values["Email:Host"] = "localhost";
        values["Email:AllowNonLocalSmtpInDevelopment"] = "false";
        StartupValidation.Validate(Config(values), new Env("Development"));
    }

    [Fact]
    public void Missing_or_unsafe_production_configuration_fails_with_clear_message_without_secrets()
    {
        var values = ValidProduction();
        values["ConnectionStrings:DefaultConnection"] = "";
        values["App:PublicBaseUrl"] = "http://gamevault.example.com";
        values["Email:Security"] = "None";
        values["Email:Password"] = "super-secret-value";
        values["Database:ApplyMigrationsOnStartup"] = "true";
        values["Covers:AllowedHosts:0"] = "*.example.com";

        var ex = Assert.Throws<InvalidOperationException>(() => StartupValidation.Validate(Config(values), new Env("Production")));
        Assert.Contains("ConnectionStrings:DefaultConnection", ex.Message, StringComparison.Ordinal);
        Assert.Contains("https", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Email:Security", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ApplyMigrationsOnStartup", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Covers:AllowedHosts", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-value", ex.Message, StringComparison.Ordinal);
    }
}
