using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using GameVault.Entities;
using GameVault.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Tests;

/// <summary>JSON içe aktarma: dışa aktarma ile gidiş-dönüş, kontrol modu, kopya önleme, hatalı kayıtlar ve sahiplik.</summary>
public sealed class ImportTests(GameVaultFactory factory) : IClassFixture<GameVaultFactory>
{
    private const string Format = "gamevault-export/v1";

    private async Task<(AppClient Client, Guid Id)> NewUserAsync()
    {
        var email = $"import-{Guid.NewGuid():N}@example.test";
        var client = factory.CreateAppClient();
        await client.RegisterAsync(email);
        var id = await factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        return (client, id);
    }

    private static Task<HttpResponseMessage> ImportAsync(AppClient client, byte[] file, string mode = "import") =>
        client.PostFileAsync("/Account/Import", "file", file, "gamevault-export.json", ("mode", mode));

    private static byte[] Json(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());

    private static JsonObject Game(string title, string platform = "PC", string status = "not-started") => new()
    {
        ["title"] = title,
        ["platform"] = platform,
        ["genres"] = new JsonArray(),
        ["status"] = status,
        ["playtimeHours"] = 0,
        ["isFavorite"] = false
    };

    private static byte[] File(params JsonObject[] games) =>
        Json(new JsonObject { ["format"] = Format, ["games"] = new JsonArray(games) });

    private Task<List<LibraryGame>> GamesOfAsync(Guid userId) =>
        factory.WithDbAsync(db => db.LibraryGames.AsNoTracking().Include(g => g.Genres).Include(g => g.Platform)
            .Where(g => g.UserId == userId).OrderBy(g => g.Title).ToListAsync());

    [Fact]
    public async Task Exported_file_imports_into_another_account_with_all_fields()
    {
        var (alice, aliceId) = await NewUserAsync();
        var (bob, bobId) = await NewUserAsync();
        using (alice)
        using (bob)
        {
            var switchId = await alice.GetPlatformIdAsync("Nintendo Switch");
            var rpg = await alice.GetGenreIdAsync("Rol Yapma (RPG)");
            await alice.CreateGameAsync("Gidiş Dönüş", switchId, "Completed",
                ("Input.Rating", "9"), ("Input.PlaytimeHours", "42"), ("Input.IsFavorite", "true"),
                ("Input.Notes", "Notlarım"), ("Input.Description", "Açıklama"), ("Input.CoverChoice", "builtin:forest"),
                ("Input.StartDate", "2024-01-02"), ("Input.CompletionDate", "2024-03-04"), ("Input.ReleaseDate", "2023-05-06"),
                ("Input.EstimatedHours", "50"), ("Input.GenreIds", rpg.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            await alice.CreateGameAsync("İkinci", await alice.GetPlatformIdAsync());

            var export = await alice.PostFormAsync("/Account/Export", []);
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            var file = await export.Content.ReadAsByteArrayAsync();

            var response = await ImportAsync(bob, file);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var doc = await bob.ParseAsync(response);
            Assert.Contains("2 oyun kütüphanene eklendi", doc.QuerySelector("#import-result")!.TextContent);

            var aliceGames = await GamesOfAsync(aliceId);
            var bobGames = await GamesOfAsync(bobId);
            Assert.Equal(2, bobGames.Count);
            Assert.Empty(bobGames.Select(g => g.Id).Intersect(aliceGames.Select(g => g.Id)));

            var game = bobGames.Single(g => g.Title == "Gidiş Dönüş");
            Assert.Equal(bobId, game.UserId);
            Assert.Equal("Nintendo Switch", game.Platform.Name);
            Assert.Equal(["Rol Yapma (RPG)"], game.Genres.Select(g => g.Name));
            Assert.Equal(GameStatus.Completed, game.Status);
            Assert.Equal(9, game.Rating);
            Assert.Equal(42, game.PlaytimeHours);
            Assert.True(game.IsFavorite);
            Assert.Equal("Notlarım", game.Notes);
            Assert.Equal("Açıklama", game.Description);
            Assert.Equal("/covers/forest.svg", game.CoverImageUrl);
            Assert.Equal(new DateOnly(2024, 1, 2), game.StartDate);
            Assert.Equal(new DateOnly(2024, 3, 4), game.CompletionDate);
            Assert.Equal(new DateOnly(2023, 5, 6), game.ReleaseDate);
            Assert.Equal(50, game.EstimatedHours);
            Assert.Equal(aliceGames.Single(g => g.Title == "Gidiş Dönüş").DateAdded, game.DateAdded, TimeSpan.FromMilliseconds(10));

            // Alice'in kütüphanesi değişmedi.
            Assert.Equal(2, aliceGames.Count);
            Assert.All(aliceGames, g => Assert.Equal(aliceId, g.UserId));
        }
    }

    [Fact]
    public async Task Check_mode_reports_counts_without_writing_anything()
    {
        var (client, userId) = await NewUserAsync();
        using (client)
        {
            var response = await ImportAsync(client, File(Game("Bir"), Game("İki")), mode: "check");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var text = (await client.ParseAsync(response)).QuerySelector("#import-result")!.TextContent;
            Assert.Contains("henüz hiçbir şey eklenmedi", text);
            Assert.Contains("2 oyundan 2 tanesi eklenebilir", Normalize(text));
            Assert.Empty(await GamesOfAsync(userId));
        }
    }

    [Fact]
    public async Task Importing_the_same_file_twice_does_not_create_duplicates()
    {
        var (client, userId) = await NewUserAsync();
        using (client)
        {
            var rawg = Game("RAWG Oyunu");
            rawg["rawgId"] = 4242;
            var file = File(Game("Tekrar"), rawg);

            Assert.Equal(HttpStatusCode.OK, (await ImportAsync(client, file)).StatusCode);
            var second = await ImportAsync(client, file);

            var doc = await client.ParseAsync(second);
            Assert.Contains("0 oyun kütüphanene eklendi", doc.QuerySelector("#import-result")!.TextContent);
            Assert.Equal(2, doc.QuerySelectorAll("#import-skipped li").Length);
            Assert.Equal(2, (await GamesOfAsync(userId)).Count);
        }
    }

    [Fact]
    public async Task Same_title_on_another_platform_and_duplicates_inside_the_file_are_handled()
    {
        var (client, userId) = await NewUserAsync();
        using (client)
        {
            // Aynı ad farklı platformda ayrı bir oyundur; aynı ad + platform dosyada iki kez geçerse ikincisi atlanır.
            var response = await ImportAsync(client, File(Game("Çift"), Game("çift"), Game("Çift", "Nintendo Switch")));

            var doc = await client.ParseAsync(response);
            Assert.Single(doc.QuerySelectorAll("#import-skipped li"));
            var games = await GamesOfAsync(userId);
            Assert.Equal(2, games.Count);
            Assert.Equal(["Nintendo Switch", "PC"], games.Select(g => g.Platform.Name).Order());
        }
    }

    [Fact]
    public async Task Invalid_records_are_reported_and_valid_ones_are_still_imported()
    {
        var (client, userId) = await NewUserAsync();
        using (client)
        {
            var badRating = Game("Puanı Bozuk");
            badRating["rating"] = 11;
            var badCover = Game("Kapağı Bozuk");
            badCover["coverImageUrl"] = "https://evil.example/x.png";
            var badType = Game("Tür Hatası");
            badType["playtimeHours"] = "çok";
            var badGenre = Game("Türü Yok");
            badGenre["genres"] = new JsonArray("Olmayan Tür");

            var response = await ImportAsync(client, File(
                Game("Geçerli"), Game("Platformsuz", "Atari Jaguar"), badRating, badCover, badType, badGenre,
                Game("Durumu Bozuk", status: "Completed"), Game("   ")));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var doc = await client.ParseAsync(response);
            var errors = doc.QuerySelectorAll("#import-errors li").Select(li => Normalize(li.TextContent)).ToList();
            Assert.Equal(7, errors.Count);
            Assert.Contains(errors, e => e.Contains("#2") && e.Contains("Bilinmeyen platform"));
            Assert.Contains(errors, e => e.Contains("#3") && e.Contains("Puan 1 ile 10 arasında olmalı"));
            Assert.Contains(errors, e => e.Contains("#4"));
            Assert.Contains(errors, e => e.Contains("#5") && e.Contains("beklenen türde değil"));
            Assert.Contains(errors, e => e.Contains("#6") && e.Contains("Bilinmeyen tür"));
            Assert.Contains(errors, e => e.Contains("#7") && e.Contains("Bilinmeyen durum"));
            Assert.Contains(errors, e => e.Contains("#8") && e.Contains("Oyun adı zorunludur"));

            Assert.Equal(["Geçerli"], (await GamesOfAsync(userId)).Select(g => g.Title));
        }
    }

    [Theory]
    [InlineData("bu json değil", "geçerli bir JSON dosyası değil")]
    [InlineData("""{"format":"baska-uygulama/v1","games":[]}""", "Game Vault dışa aktarma dosyası değil")]
    [InlineData("""{"games":[{"title":"Biçimsiz","platform":"PC"}]}""", "Game Vault dışa aktarma dosyası değil")]
    [InlineData("""{"format":"gamevault-export/v1","games":[]}""", "Dosyada hiç oyun yok")]
    public async Task Unreadable_or_foreign_files_are_rejected_without_writing(string content, string expectedMessage)
    {
        var (client, userId) = await NewUserAsync();
        using (client)
        {
            var response = await ImportAsync(client, Encoding.UTF8.GetBytes(content));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(expectedMessage, (await client.ParseAsync(response)).QuerySelector("[role=alert]")!.TextContent);
            Assert.Empty(await GamesOfAsync(userId));
        }
    }

    [Fact]
    public async Task Missing_file_is_a_validation_error()
    {
        var (client, _) = await NewUserAsync();
        using (client)
        {
            var response = await client.PostFormAsync("/Account/Import", [new("mode", "import")]);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Owner_and_id_fields_in_the_file_are_ignored()
    {
        var (alice, aliceId) = await NewUserAsync();
        var (bob, bobId) = await NewUserAsync();
        using (alice)
        using (bob)
        {
            var existingId = await alice.CreateGameAsync("Alice'in Oyunu", await alice.GetPlatformIdAsync());
            var hostile = Game("Sızma Denemesi");
            hostile["id"] = existingId.ToString();
            hostile["userId"] = aliceId.ToString();
            var file = Json(new JsonObject
            {
                ["format"] = Format,
                ["account"] = new JsonObject { ["email"] = "alice@example.test" },
                ["userId"] = aliceId.ToString(),
                ["games"] = new JsonArray(hostile)
            });

            Assert.Equal(HttpStatusCode.OK, (await ImportAsync(bob, file)).StatusCode);

            var aliceGames = await GamesOfAsync(aliceId);
            Assert.Equal(["Alice'in Oyunu"], aliceGames.Select(g => g.Title));
            var bobGame = Assert.Single(await GamesOfAsync(bobId));
            Assert.NotEqual(existingId, bobGame.Id);
        }
    }

    [Fact]
    public async Task Files_larger_than_the_limit_are_rejected()
    {
        var (client, userId) = await NewUserAsync();
        using (client)
        {
            var big = new byte[GameVault.Business.Account.ImportService.MaxFileBytes + 1];
            Array.Fill(big, (byte)' ');

            var response = await ImportAsync(client, big);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await GamesOfAsync(userId));
        }
    }

    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>Kullanıcı başına oyun sınırı düşük uygulama.</summary>
public sealed class SmallLibraryFactory : GameVaultFactory
{
    protected override IDictionary<string, string?> Settings =>
        new Dictionary<string, string?>(base.Settings) { ["Library:MaxGamesPerUser"] = "3" };
}

public sealed class ImportLimitTests(SmallLibraryFactory factory) : IClassFixture<SmallLibraryFactory>
{
    [Fact]
    public async Task Import_that_would_exceed_the_library_limit_writes_nothing()
    {
        var email = $"limit-{Guid.NewGuid():N}@example.test";
        using var client = factory.CreateAppClient();
        await client.RegisterAsync(email);
        await client.CreateGameAsync("Mevcut", await client.GetPlatformIdAsync());

        var games = new JsonArray(
            new JsonObject { ["title"] = "A", ["platform"] = "PC" },
            new JsonObject { ["title"] = "B", ["platform"] = "PC" },
            new JsonObject { ["title"] = "C", ["platform"] = "PC" });
        var file = Encoding.UTF8.GetBytes(new JsonObject { ["format"] = "gamevault-export/v1", ["games"] = games }.ToJsonString());

        var response = await client.PostFileAsync("/Account/Import", "file", file, "x.json", ("mode", "import"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sınırını aşıyor", (await client.ParseAsync(response)).QuerySelector("[role=alert]")!.TextContent);
        Assert.Equal(1, await factory.WithDbAsync(db => db.LibraryGames.CountAsync(g => g.User.Email == email)));
    }
}
