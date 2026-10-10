using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using GameVault.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Tests;

/// <summary>Koleksiyonlar: oyun formundan oluşturma, filtre, yönetim sayfası, kurallar, izolasyon ve dışa/içe aktarma.</summary>
public sealed class CollectionTests(GameVaultFactory factory) : IClassFixture<GameVaultFactory>
{
    private sealed record User(AppClient Client, Guid Id, string Email, int Pc);

    private async Task<User> NewUserAsync()
    {
        var email = $"coll-{Guid.NewGuid():N}@example.test";
        var client = factory.CreateAppClient();
        await client.RegisterAsync(email);
        var id = await factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        return new User(client, id, email, await client.GetPlatformIdAsync());
    }

    private Task<Dictionary<string, int>> CollectionIdsAsync(Guid userId) =>
        factory.WithDbAsync(db => db.Collections.Where(c => c.UserId == userId).ToDictionaryAsync(c => c.Name, c => c.Id));

    private Task<List<string>> CollectionsOfGameAsync(Guid gameId) =>
        factory.WithDbAsync(db => db.LibraryGames.Where(g => g.Id == gameId)
            .SelectMany(g => g.Collections).OrderBy(c => c.Name).Select(c => c.Name).ToListAsync());

    private static async Task<List<string>> LibraryTitlesAsync(AppClient client, string query)
    {
        var (response, doc) = await client.GetPageAsync("/Library" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return doc.QuerySelectorAll(".card-title").Select(t => t.TextContent.Trim()).Order().ToList();
    }

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static Task<HttpResponseMessage> EditAsync(User user, Guid gameId, string title, params (string Key, string Value)[] extra) =>
        user.Client.PostFormAsync($"/Library/{gameId}/Edit", AppClient.GameFields(title, user.Pc, "NotStarted", extra));

    [Fact]
    public async Task New_collections_typed_in_the_game_form_are_created_and_filterable()
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            var weekend = await user.Client.CreateGameAsync("Hafta Sonu Oyunu", user.Pc, "NotStarted", ("Input.NewCollections", " Hafta  sonu , Arkadaşlarla, hafta sonu "));
            await user.Client.CreateGameAsync("Başka Oyun", user.Pc);

            var ids = await CollectionIdsAsync(user.Id);
            Assert.Equal(["Arkadaşlarla", "Hafta sonu"], ids.Keys.Order());
            Assert.Equal(["Arkadaşlarla", "Hafta sonu"], await CollectionsOfGameAsync(weekend));

            Assert.Equal(["Hafta Sonu Oyunu"], await LibraryTitlesAsync(user.Client, $"?collection={Id(ids["Hafta sonu"])}"));

            var (_, details) = await user.Client.GetPageAsync($"/Library/{weekend}");
            var tags = details.QuerySelectorAll("a.tag").Select(a => (a.TextContent, a.GetAttribute("href"))).ToList();
            Assert.Contains(("Hafta sonu", $"/Library?collection={Id(ids["Hafta sonu"])}"), tags);
        }
    }

    [Fact]
    public async Task Editing_a_game_adds_existing_collections_and_unchecking_removes_them()
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            var created = await user.Client.PostFormAsync("/Collections?handler=Create", [new("name", "Favori RPG'ler")]);
            Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
            var collectionId = (await CollectionIdsAsync(user.Id))["Favori RPG'ler"];
            var game = await user.Client.CreateGameAsync("Düzenlenecek", user.Pc);

            var (_, form) = await user.Client.GetPageAsync($"/Library/{game}/Edit");
            Assert.NotNull(form.QuerySelector($"input[name='Input.CollectionIds'][value='{Id(collectionId)}']"));

            Assert.Equal(HttpStatusCode.Redirect, (await EditAsync(user, game, "Düzenlenecek", ("Input.CollectionIds", Id(collectionId)))).StatusCode);
            Assert.Equal(["Favori RPG'ler"], await CollectionsOfGameAsync(game));

            Assert.Equal(HttpStatusCode.Redirect, (await EditAsync(user, game, "Düzenlenecek")).StatusCode);
            Assert.Empty(await CollectionsOfGameAsync(game));
            Assert.Contains("Favori RPG'ler", (await CollectionIdsAsync(user.Id)).Keys);
        }
    }

    [Theory]
    [InlineData("", "boş olamaz")]
    [InlineData("a,b", "virgül")]
    [InlineData("Bu koleksiyon adı kırk karakterden daha uzundur", "en fazla 40")]
    public async Task Invalid_collection_names_are_rejected(string name, string expected)
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            var response = await user.Client.PostFormAsync("/Collections?handler=Create", [new("name", name)], "/Collections");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains(expected, (await user.Client.ParseAsync(response)).QuerySelector("#new-collection-error")!.TextContent);
            Assert.Empty(await CollectionIdsAsync(user.Id));
        }
    }

    [Fact]
    public async Task Duplicate_names_are_rejected_case_insensitively_on_create_and_rename()
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            await user.Client.PostFormAsync("/Collections?handler=Create", [new("name", "Retro")], "/Collections");
            await user.Client.PostFormAsync("/Collections?handler=Create", [new("name", "Modern")], "/Collections");

            var duplicate = await user.Client.PostFormAsync("/Collections?handler=Create", [new("name", "retro")], "/Collections");
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

            var ids = await CollectionIdsAsync(user.Id);
            var rename = await user.Client.PostFormAsync($"/Collections?handler=Rename&id={Id(ids["Modern"])}", [new("name", "RETRO")], "/Collections");
            Assert.Equal(HttpStatusCode.Redirect, rename.StatusCode);
            Assert.Equal(["Modern", "Retro"], (await CollectionIdsAsync(user.Id)).Keys.Order());

            // Aynı adın yalnızca büyük/küçük harfini değiştirmek serbesttir.
            await user.Client.PostFormAsync($"/Collections?handler=Rename&id={Id(ids["Modern"])}", [new("name", "MODERN")], "/Collections");
            Assert.Contains("MODERN", (await CollectionIdsAsync(user.Id)).Keys);
        }
    }

    [Fact]
    public async Task Deleting_a_collection_keeps_its_games()
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            var game = await user.Client.CreateGameAsync("Kalacak Oyun", user.Pc, "NotStarted", ("Input.NewCollections", "Silinecek"));
            var id = (await CollectionIdsAsync(user.Id))["Silinecek"];

            var response = await user.Client.PostFormAsync($"/Collections?handler=Delete&id={Id(id)}", [], "/Collections");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Empty(await CollectionIdsAsync(user.Id));
            Assert.Equal(1, await factory.WithDbAsync(db => db.LibraryGames.CountAsync(g => g.Id == game)));
        }
    }

    [Fact]
    public async Task A_game_can_be_in_at_most_ten_collections()
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            var names = string.Join(',', Enumerable.Range(1, 11).Select(i => $"K{i}"));
            var response = await user.Client.PostFormAsync("/Library/New", AppClient.GameFields("Çok Koleksiyonlu", user.Pc, "NotStarted", ("Input.NewCollections", names)));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("en fazla 10 koleksiyonda", (await user.Client.ParseAsync(response)).QuerySelector("#Input_CollectionIds-error")!.TextContent);
            Assert.Empty(await CollectionIdsAsync(user.Id));
        }
    }

    private static Task<HttpResponseMessage> ToggleFromCardAsync(User user, Guid gameId, int collectionId) =>
        user.Client.PostFormAsync($"/Library/{gameId}?handler=Collection",
            [new("collectionId", Id(collectionId)), new("returnUrl", "/Library")], "/Library");

    [Fact]
    public async Task Library_card_adds_and_removes_a_game_from_a_collection()
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            await user.Client.PostFormAsync("/Collections?handler=Create", [new("name", "Hafta sonu")]);
            var collectionId = (await CollectionIdsAsync(user.Id))["Hafta sonu"];
            var game = await user.Client.CreateGameAsync("Karttan Eklenen", user.Pc);

            var (_, page) = await user.Client.GetPageAsync("/Library");
            var toggle = page.QuerySelector($".card-collections form[action^='/Library/{game}'] button.collection-toggle");
            Assert.NotNull(toggle);
            Assert.Equal("false", toggle.GetAttribute("aria-pressed"));

            var added = await ToggleFromCardAsync(user, game, collectionId);
            Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
            Assert.Equal("/Library", added.Headers.Location!.OriginalString);
            Assert.Equal(["Hafta sonu"], await CollectionsOfGameAsync(game));

            (_, page) = await user.Client.GetPageAsync("/Library");
            Assert.Equal("true", page.QuerySelector(".card-collections button.collection-toggle")!.GetAttribute("aria-pressed"));

            Assert.Equal(HttpStatusCode.Redirect, (await ToggleFromCardAsync(user, game, collectionId)).StatusCode);
            Assert.Empty(await CollectionsOfGameAsync(game));
        }
    }

    [Fact]
    public async Task Library_card_respects_the_ten_collection_limit()
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            var names = string.Join(',', Enumerable.Range(1, 10).Select(i => $"K{i}"));
            var game = await user.Client.CreateGameAsync("Dolu Oyun", user.Pc, "NotStarted", ("Input.NewCollections", names));
            await user.Client.PostFormAsync("/Collections?handler=Create", [new("name", "K11")]);
            var eleventh = (await CollectionIdsAsync(user.Id))["K11"];

            Assert.Equal(HttpStatusCode.Redirect, (await ToggleFromCardAsync(user, game, eleventh)).StatusCode);
            Assert.Equal(10, (await CollectionsOfGameAsync(game)).Count);
            Assert.DoesNotContain("K11", await CollectionsOfGameAsync(game));
        }
    }

    [Fact]
    public async Task Library_card_toggle_rejects_games_and_collections_of_another_user()
    {
        var alice = await NewUserAsync();
        var bob = await NewUserAsync();
        using (alice.Client)
        using (bob.Client)
        {
            var aliceGame = await alice.Client.CreateGameAsync("Alice'in Oyunu", alice.Pc, "NotStarted", ("Input.NewCollections", "Alice Özel"));
            var aliceCollection = (await CollectionIdsAsync(alice.Id))["Alice Özel"];
            var bobGame = await bob.Client.CreateGameAsync("Bob'un Oyunu", bob.Pc, "NotStarted", ("Input.NewCollections", "Bob Özel"));
            var bobCollection = (await CollectionIdsAsync(bob.Id))["Bob Özel"];

            // Bob kendi oyununu Alice'in koleksiyonuna, Alice'in oyununu kendi koleksiyonuna ekleyemez ya da çıkaramaz.
            Assert.Equal(HttpStatusCode.NotFound, (await ToggleFromCardAsync(bob, bobGame, aliceCollection)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await ToggleFromCardAsync(bob, aliceGame, bobCollection)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await ToggleFromCardAsync(bob, aliceGame, aliceCollection)).StatusCode);

            Assert.Equal(["Alice Özel"], await CollectionsOfGameAsync(aliceGame));
            Assert.Equal(["Bob Özel"], await CollectionsOfGameAsync(bobGame));
        }
    }

    [Fact]
    public async Task Collections_of_another_user_cannot_be_used_renamed_deleted_or_listed()
    {
        var alice = await NewUserAsync();
        var bob = await NewUserAsync();
        using (alice.Client)
        using (bob.Client)
        {
            await alice.Client.CreateGameAsync("Alice'in Gizli Oyunu", alice.Pc, "NotStarted", ("Input.NewCollections", "Alice Özel"));
            var aliceCollection = (await CollectionIdsAsync(alice.Id))["Alice Özel"];
            var bobGame = await bob.Client.CreateGameAsync("Bob'un Oyunu", bob.Pc);

            // Bob, Alice'in koleksiyon kimliğini kendi oyununa bağlayamaz.
            var attach = await EditAsync(bob, bobGame, "Bob'un Oyunu", ("Input.CollectionIds", Id(aliceCollection)));
            Assert.Equal(HttpStatusCode.OK, attach.StatusCode);
            Assert.Contains("Geçersiz koleksiyon", (await bob.Client.ParseAsync(attach)).QuerySelector("#Input_CollectionIds-error")!.TextContent);
            Assert.Empty(await CollectionsOfGameAsync(bobGame));

            // Filtre Alice'in oyunlarını sızdırmaz.
            Assert.Empty(await LibraryTitlesAsync(bob.Client, $"?collection={Id(aliceCollection)}"));

            Assert.Equal(HttpStatusCode.NotFound,
                (await bob.Client.PostFormAsync($"/Collections?handler=Rename&id={Id(aliceCollection)}", [new("name", "Bob ele geçirdi")], "/Collections")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await bob.Client.PostFormAsync($"/Collections?handler=Delete&id={Id(aliceCollection)}", [], "/Collections")).StatusCode);

            var (_, bobPage) = await bob.Client.GetPageAsync("/Collections");
            Assert.DoesNotContain("Alice Özel", bobPage.Body!.TextContent);
            var (_, bobForm) = await bob.Client.GetPageAsync("/Library/New");
            Assert.Empty(bobForm.QuerySelectorAll("input[name='Input.CollectionIds']"));

            Assert.Equal(["Alice Özel"], (await CollectionIdsAsync(alice.Id)).Keys);

            // Bob aynı adı kendi hesabında kullanabilir: adlar kullanıcı başına benzersizdir.
            Assert.Equal(HttpStatusCode.Redirect,
                (await bob.Client.PostFormAsync("/Collections?handler=Create", [new("name", "Alice Özel")], "/Collections")).StatusCode);
        }
    }

    [Fact]
    public async Task Export_and_import_carry_collections_by_name()
    {
        var alice = await NewUserAsync();
        var bob = await NewUserAsync();
        using (alice.Client)
        using (bob.Client)
        {
            await alice.Client.CreateGameAsync("Birinci", alice.Pc, "NotStarted", ("Input.NewCollections", "Ortak, Sadece Birinci"));
            await alice.Client.CreateGameAsync("İkinci", alice.Pc, "NotStarted", ("Input.NewCollections", "Ortak"));
            await bob.Client.PostFormAsync("/Collections?handler=Create", [new("name", "ortak")], "/Collections");

            var export = await alice.Client.PostFormAsync("/Account/Export", []);
            var json = JsonNode.Parse(await export.Content.ReadAsStringAsync())!;
            var exported = json["games"]!.AsArray().Single(g => (string?)g!["title"] == "Birinci")!;
            Assert.Equal(["Ortak", "Sadece Birinci"], exported["collections"]!.AsArray().Select(n => (string?)n));

            var import = await bob.Client.PostFileAsync("/Account/Import", "file", Encoding.UTF8.GetBytes(json.ToJsonString()), "x.json", ("mode", "import"));
            Assert.Equal(HttpStatusCode.OK, import.StatusCode);

            // Bob'un mevcut "ortak" koleksiyonu yeniden kullanılır; yalnızca eksik olan oluşturulur.
            var bobCollections = await CollectionIdsAsync(bob.Id);
            Assert.Equal(["Sadece Birinci", "ortak"], bobCollections.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(["Birinci", "İkinci"], await LibraryTitlesAsync(bob.Client, $"?collection={Id(bobCollections["ortak"])}"));
        }
    }

    [Fact]
    public async Task Deleting_the_account_removes_its_collections()
    {
        var user = await NewUserAsync();
        using (user.Client)
        {
            await user.Client.CreateGameAsync("Silinecek Hesap Oyunu", user.Pc, "NotStarted", ("Input.NewCollections", "Geçici"));

            var response = await user.Client.PostFormAsync("/Account/Delete", [
                new("Input.Password", AppClient.DefaultPassword),
                new("Input.ConfirmEmail", user.Email),
                new("Input.Acknowledge", "true")]);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Empty(await CollectionIdsAsync(user.Id));
            Assert.Equal(0, await factory.WithDbAsync(db => db.Users.CountAsync(u => u.Id == user.Id)));
        }
    }
}
