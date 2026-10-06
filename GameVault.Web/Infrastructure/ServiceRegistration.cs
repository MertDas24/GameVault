using System.Globalization;
using GameVault.Business.Account;
using GameVault.Business.Catalog;
using GameVault.Business.Covers;
using GameVault.Business.Email;
using GameVault.Business.Library;
using GameVault.Business.Security;
using GameVault.Business.Stats;
using GameVault.DataAccess;
using GameVault.Entities;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameVault.Web.Infrastructure;

public static class ServiceRegistration
{
    public const int MaxRequestBodyBytes = 128 * 1024;

    public static WebApplicationBuilder AddGameVault(this WebApplicationBuilder builder)
    {
        StartupValidation.Validate(builder.Configuration, builder.Environment);

        var services = builder.Services;
        var config = builder.Configuration;
        var isProduction = builder.Environment.IsProduction();

        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
            k.Limits.MaxRequestBodySize = MaxRequestBodyBytes;
        });
        services.Configure<FormOptions>(o =>
        {
            o.ValueCountLimit = 100;
            o.ValueLengthLimit = 16 * 1024;
            o.KeyLengthLimit = 256;
            o.MultipartBodyLengthLimit = MaxRequestBodyBytes;
        });

        services.Configure<AppOptions>(config.GetSection(AppOptions.SectionName));
        services.Configure<SecurityOptions>(config.GetSection(SecurityOptions.SectionName));
        services.Configure<CoverOptions>(config.GetSection(CoverOptions.SectionName));
        services.AddOptions<RawgOptions>().Bind(config.GetSection(RawgOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        // RAWG etkinse kapak görsellerinin sunulduğu alan adı kapak izin listesine (ve dolayısıyla CSP img-src'ye) eklenir.
        services.AddOptions<CoverOptions>().PostConfigure<IOptions<RawgOptions>>((covers, rawg) =>
        {
            if (rawg.Value.Enabled && !covers.AllowedHosts.Contains(RawgOptions.CoverHost, StringComparer.OrdinalIgnoreCase))
            {
                covers.AllowedHosts.Add(RawgOptions.CoverHost);
            }
        });
        services.Configure<RateLimitOptions>(config.GetSection(RateLimitOptions.SectionName));
        services.AddOptions<LibraryOptions>().Bind(config.GetSection(LibraryOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<EmailOptions>().Bind(config.GetSection(EmailOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<EmailOutboxOptions>().Bind(config.GetSection(EmailOutboxOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();

        services.AddDbContext<GameVaultDbContext>(o =>
            o.UseSqlServer(config.GetConnectionString("DefaultConnection"), sql => sql.EnableRetryOnFailure(3)));

        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        // --- Kimlik doğrulama (ASP.NET Core Identity) ---
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(o =>
            {
                // NIST SP 800-63B: uzunluk öncelikli, karakter sınıfı zorlaması yok.
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredUniqueChars = 4;

                o.User.RequireUniqueEmail = true;

                // Politika: e-posta adresi doğrulanmamış hesaplar giriş yapamaz (bkz. LoginModel).
                o.SignIn.RequireConfirmedEmail = true;
                o.Tokens.EmailConfirmationTokenProvider = EmailConfirmationTokenProvider.ProviderName;

                // Kaba kuvvete karşı hesap kilidi (oran sınırlamaya ek olarak).
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<GameVaultDbContext>()
            .AddSignInManager<AppSignInManager>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<EmailConfirmationTokenProvider>(EmailConfirmationTokenProvider.ProviderName)
            .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
            .AddErrorDescriber<TurkishIdentityErrorDescriber>();

        // PBKDF2-HMAC-SHA512 (Identity v3 biçimi). OWASP 2023 önerisi: en az 210.000 iterasyon.
        // Eski (daha düşük iterasyonlu) hash'ler başarılı girişte otomatik yeniden hash'lenir.
        services.Configure<PasswordHasherOptions>(o => o.IterationCount = 210_000);

        // Parola sıfırlama bağlantıları 30 dakika geçerli; parola değişince güvenlik damgası
        // yenilendiğinden aynı bağlantı ikinci kez kullanılamaz.
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromMinutes(30));
        // E-posta doğrulama bağlantıları 24 saat geçerli; doğrulama sonrası güvenlik damgası yenilenir (tek kullanım).
        services.Configure<EmailConfirmationTokenProviderOptions>(o => o.TokenLifespan = EmailConfirmationTokenProvider.Lifespan);
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

        services.AddSingleton<DbTicketStore>();
        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = isProduction ? "__Host-gv_session" : "gv_session";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = isProduction ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            o.Cookie.Path = "/";
            o.ExpireTimeSpan = TimeSpan.FromDays(7);
            o.SlidingExpiration = true;
            o.LoginPath = "/Account/Login";
            o.LogoutPath = "/Account/Logout";
            o.AccessDeniedPath = "/Account/Login";
            o.ReturnUrlParameter = "returnUrl";
        });
        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<DbTicketStore>((o, store) => o.SessionStore = store);

        // Parola doğrulandıktan sonra ikinci adım beklenirken kullanılan kısa ömürlü ara çerez. Yalnızca kullanıcı
        // kimliğini (Data Protection ile şifreli) taşır; oturum açtırmaz. "Bu cihazı hatırla" seçeneği yoktur.
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, o =>
        {
            o.Cookie.Name = isProduction ? "__Host-gv_2fa" : "gv_2fa";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = isProduction ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            o.Cookie.Path = "/";
            o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            o.SlidingExpiration = false;
        });

        // Varsayılan olarak her sayfa kimlik doğrulama ister; herkese açık sayfalar [AllowAnonymous] ile işaretlenir.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        // Data Protection anahtarları veritabanında: tüm instance'lar aynı çerezleri ve tokenları çözebilir.
        DataProtectionSetup.Configure(services, config, builder.Environment);

        services.AddAntiforgery(o =>
        {
            o.Cookie.Name = isProduction ? "__Host-gv_csrf" : "gv_csrf";
            o.Cookie.SecurePolicy = isProduction ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.FormFieldName = "__RequestVerificationToken";
        });

        services.Configure<CookieTempDataProviderOptions>(o =>
        {
            o.Cookie.Name = "gv_tempdata";
            o.Cookie.SecurePolicy = isProduction ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            o.Cookie.SameSite = SameSiteMode.Lax;
        });

        // Türkçe karakterler sayısal varlıklara dönüştürülmeden yazılır; HTML'e özel karakterler
        // (<, >, &, tırnaklar) her zaman kodlanmaya devam eder.
        services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o =>
            o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));

        services.AddRazorPages()
            .AddMvcOptions(o => TurkishModelBindingMessages.Apply(o.ModelBindingMessageProvider))
            .AddViewOptions(o => o.HtmlHelperOptions.ClientValidationEnabled = false);

        services.Configure<RequestLocalizationOptions>(o =>
        {
            var tr = new CultureInfo("tr-TR");
            o.DefaultRequestCulture = new RequestCulture(tr);
            o.SupportedCultures = [tr];
            o.SupportedUICultures = [tr];
            o.RequestCultureProviders.Clear();
        });

        // --- Ters proxy: X-Forwarded-* yalnızca tanımlı proxy/ağlardan kabul edilir ---
        var proxy = config.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>() ?? new ReverseProxyOptions();
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.ForwardLimit = 1;
            o.KnownProxies.Clear();
            o.KnownIPNetworks.Clear();
            foreach (var ip in proxy.KnownProxies)
            {
                o.KnownProxies.Add(System.Net.IPAddress.Parse(ip));
            }

            foreach (var network in proxy.KnownNetworks)
            {
                o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });

        services.AddHsts(o =>
        {
            o.MaxAge = TimeSpan.FromDays(180);
            o.IncludeSubDomains = false;
            o.Preload = false;
        });

        services.AddHealthChecks().AddDbContextCheck<GameVaultDbContext>("database");

        // --- Uygulama servisleri ---
        services.AddSingleton<CoverPolicy>();
        services.AddSingleton<Display>();
        services.AddScoped<LoginTiming>();
        services.AddScoped<TotpReplayGuard>();
        services.AddScoped<AccountEmails>();
        services.AddScoped<LibraryService>();
        services.AddScoped<ReferenceDataService>();
        services.AddScoped<CollectionService>();
        services.AddScoped<StatsService>();
        services.AddScoped<SuggestionService>();
        services.AddScoped<AccountService>();
        services.AddScoped<ImportService>();
        services.AddScoped<DbRateLimiter>();
        services.AddScoped<DemoDataSeeder>();
        services.AddSingleton<OutboxSignal>();
        services.AddScoped<EmailOutbox>();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddHostedService<EmailDispatchService>();
        services.AddHostedService<MaintenanceService>();

        // --- RAWG oyun kataloğu ---
        // IHttpClientFactory: bağlantı havuzu ve DNS yenilemesi fabrika tarafından yönetilir.
        services.AddHttpClient(RawgOptions.HttpClientName, (sp, client) =>
            {
                var o = sp.GetRequiredService<IOptions<RawgOptions>>().Value;
                client.BaseAddress = new Uri(o.BaseUrl);
                // GameCatalog kendi zaman aşımını uygular; bu değer yalnızca ikinci bir güvenlik sınırıdır.
                client.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds + 2);
                client.MaxResponseContentBufferSize = 1024 * 1024;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("GameVault/1.0");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            // Çerez gönderilmez, yönlendirme izlenmez: RAWG'a giden istek yalnızca arama ve anahtardan oluşur.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                UseCookies = false,
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.All
            })
            // Varsayılan HttpClient logları istek adresini yazar; RAWG anahtarı adreste taşındığı için kapatılır.
            .RemoveAllLoggers();
        services.AddSingleton<GameCatalog>();

        return builder;
    }

    public static WebApplication UseGameVault(this WebApplication app)
    {
        var security = app.Services.GetRequiredService<IOptions<SecurityOptions>>().Value;

        if (app.Configuration.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>()?.IsConfigured == true)
        {
            app.UseForwardedHeaders();
        }

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            // Üretimde ayrıntılı hata (stack trace, SQL) istemciye gönderilmez.
            app.UseExceptionHandler("/Error");
        }

        if (app.Environment.IsProduction() && security.EnableHsts)
        {
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/Error", "?code={0}");
        app.UseMiddleware<SecurityHeadersMiddleware>();

        if (app.Environment.IsProduction() && security.UseHttpsRedirection)
        {
            app.UseHttpsRedirection();
        }

        app.UseRequestLocalization();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                // asp-append-version ile sürümlenen dosyalar uzun süre önbelleğe alınabilir.
                ctx.Context.Response.Headers.CacheControl = ctx.Context.Request.Query.ContainsKey("v")
                    ? "public, max-age=31536000, immutable"
                    : "public, max-age=3600";
            }
        });

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            // Yalnızca "Healthy"/"Unhealthy" döner; bağlantı ya da hata ayrıntısı içermez.
            ResultStatusCodes =
            {
                [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy] = StatusCodes.Status200OK,
                [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded] = StatusCodes.Status200OK,
                [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            }
        }).AllowAnonymous();
        app.MapGet("/health/live", () => Results.Text("Healthy")).AllowAnonymous();

        app.MapRazorPages();
        return app;
    }
}
