# GameVault: Öğrenme ve mülakat notları

Bu not, projeyi baştan sona anlayıp mülakatta kendi cümlelerinle anlatabilmen için yazıldı. Her bölüm bir kavramı
anlatır ve kavramın koddaki yerini gösterir. Okurken ilgili dosyayı da aç. Proje ilerledikçe sondaki
**Aşama günlüğü** güncellenir.

---

## 1. 30 saniyede proje (mülakat açılışı)

> "GameVault, kullanıcıların kendi oyun kütüphanelerini tuttuğu bir ASP.NET Core Razor Pages uygulaması.
> SQL Server ve EF Core kullanıyor. Kimlik doğrulamada ASP.NET Core Identity'yi cookie ile kullanıyorum ama oturumu
> sunucu tarafında, veritabanında saklıyorum. Bu sayede çıkış ya da parola değişikliğinde oturumu anında iptal
> edebiliyorum. En çok odaklandığım konu, bir kullanıcının başka bir kullanıcının verisine erişememesiydi. Bunu
> entegrasyon testleriyle gerçek bir SQL Server'a karşı kanıtlıyorum. Projede CI, Docker ve Playwright ile E2E
> testleri de var."

Bunu ezberleme. Ama her cümlenin arkasındaki "neden"i bu notta bul.

---

## 2. Katmanlar: neden 5 proje var?

| Proje | Ne içerir | Neye bağımlı |
|---|---|---|
| `GameVault.Entities` | Veritabanı tablolarına karşılık gelen sınıflar (`LibraryGame`, `ApplicationUser`…) | hiçbir şeye |
| `GameVault.DataAccess` | `GameVaultDbContext`, migration'lar | Entities |
| `GameVault.Models` | Formdan gelen girdiler (`GameInput`), ekrana giden DTO'lar, sorgu normalleştirme | hiçbir şeye |
| `GameVault.Business` | İş kuralları: `LibraryService`, `StatsService`, `AccountService`… | DataAccess, Models |
| `GameVault.Web` | Razor Pages, kimlik doğrulama ayarları, güvenlik başlıkları | Business |

**Neden böyle?**
- **Sorumluluk ayrımı.** Bir sayfa veritabanına doğrudan değil, servise gider. Böylece iş kuralı (örneğin "en fazla 5
  tür seçilebilir") tek bir yerde durur.
- **Entity ile Model'i ayırmak.** Formdan gelen veri doğrudan `LibraryGame` entity'sine bağlanmaz. Bağlansaydı,
  kötü niyetli bir kullanıcı forma `UserId` alanı ekleyip oyunu başkasına atayabilirdi. Bu açığın adı
  **mass assignment** (over-posting). `GameInput` sınıfında `UserId` alanı hiç yoktur.
- **Neden mikroservis değil?** Tek bir ekip ve tek bir veritabanı var. Mikroservis burada yalnızca karmaşıklık
  getirirdi. Mülakatta "bilinçli olarak monolit" demek iyi bir cevaptır.

---

## 3. Bir isteğin yolculuğu: "Oyunu düzenle" butonuna basınca ne olur?

Örnek istek: `POST /Library/{id}/Edit`

1. **Middleware zinciri** çalışır ([ServiceRegistration.cs](../GameVault.Web/Infrastructure/ServiceRegistration.cs),
   `UseGameVault`). Önce güvenlik başlıkları eklenir, sonra statik dosya kontrolü, yönlendirme, **Authentication**
   (cookie'den kullanıcının kim olduğu bulunur) ve **Authorization** (giriş yapmış mı?) gelir.
2. **Authentication:** Cookie'nin içinde kullanıcı bilgisi değil, yalnızca bir oturum anahtarı vardır.
   [DbTicketStore.cs](../GameVault.Web/Infrastructure/DbTicketStore.cs) bu anahtarla `UserSessions` tablosundan
   oturumu okur. Kayıt yoksa (çıkış yapılmışsa) kullanıcı anonim sayılır.
3. **Antiforgery:** Formdaki gizli token doğrulanır. Token yoksa istek reddedilir (CSRF koruması).
4. **Model binding ve doğrulama:** Form alanları `GameInput` nesnesine doldurulur. `[Required]`, `[Range]` gibi
   öznitelikler kontrol edilir (`ModelState.IsValid`).
5. **Page handler:** [Edit.cshtml.cs](../GameVault.Web/Pages/Library/Edit.cshtml.cs) içindeki `OnPostAsync` çalışır
   ve `library.UpdateAsync(CurrentUserId, id, Input)` çağrılır.
   - `CurrentUserId` istemciden **gelmez**. Sunucudaki oturumdan okunur
     ([AppPageModel.cs](../GameVault.Web/Pages/AppPageModel.cs)).
6. **Servis:** [LibraryService.cs](../GameVault.Business/Library/LibraryService.cs) oyunu
   `OwnedBy(userId).Where(g => g.Id == id)` ile arar. Oyun başkasınınsa sorgu boş döner ve sayfa **404** verir.
7. **EF Core** değişiklikleri SQL'e çevirip veritabanına yazar.
8. **Post/Redirect/Get:** Başarılı olursa sayfa `/Library/{id}` adresine yönlendirir. Böylece tarayıcı yenilendiğinde
   form tekrar gönderilmez.

> **Mülakat sorusu:** "Başka bir kullanıcının oyununa erişilirse neden 403 değil de 404 dönüyorsun?"
> **Cevap:** 403, "bu kayıt var ama sana ait değil" demek olur ve kayıtların varlığını sızdırır. 404 ise hiçbir
> bilgi vermez.

---

## 4. Kimlik doğrulama (Authentication): "Sen kimsin?"

- **ASP.NET Core Identity** kullanıcı kaydını, parola özetlemeyi, hesap kilitlemeyi ve e-posta doğrulama tokenlarını
  hazır olarak sağlar. Kendi parola sistemini yazmak güvenlik açısından risklidir.
- **Parola özeti:** PBKDF2-HMAC-SHA512, 210.000 tekrar (`PasswordHasherOptions.IterationCount`). Parolalar asla düz
  metin olarak saklanmaz. Özet bilerek yavaş hesaplanır, böylece kaba kuvvet saldırısı pahalı olur.
- **Cookie mi JWT mi?** Projenin eski sürümü JWT kullanıyordu. Değiştirme sebepleri:
  - JWT, süresi dolana kadar geçerli kalır. Çıkış yapıldığında onu "iptal etmek" zordur.
  - JWT'yi tarayıcıda saklamak gerekir. `localStorage`'da durursa XSS ile çalınabilir.
  - Cookie ise `HttpOnly` olduğu için JavaScript tarafından okunamaz, sunucuda saklanan oturum sayesinde de anında
    iptal edilebilir.
  - **JWT ne zaman doğru seçim olur?** Mobil uygulama ya da başka servisler bir API'ye erişecekse. Burada yalnızca
    tarayıcı var.
- **Sunucu tarafı oturum (`ITicketStore`):** Cookie yalnızca bir oturum kimliği taşır, asıl bilgi
  `UserSessions` tablosundadır. Çıkış, parola değişikliği ya da hesap silme kaydı siler ve eski cookie anında
  işe yaramaz hale gelir. Test: `AuthTests.Logout_requires_post_and_invalidates_the_server_side_session`.
- **Kullanıcı numaralandırma (enumeration) koruması:** "Bu e-posta kayıtlı değil" gibi mesajlar verilmez. Hatalı
  girişte hem mesaj aynıdır hem de yanıt süresi eşitlenir ([LoginTiming.cs](../GameVault.Web/Infrastructure/LoginTiming.cs)).

## 5. Yetkilendirme (Authorization): "Buna izinli misin?"

- **Fallback policy:** `SetFallbackPolicy(RequireAuthenticatedUser)`. Yeni eklenen her sayfa varsayılan olarak giriş
  ister. Herkese açık sayfalar `[AllowAnonymous]` ile tek tek işaretlenir. Unutulan bir sayfa yanlışlıkla açık
  kalmaz ("secure by default").
- **Kaynak sahipliği:** Rol tabanlı yetki burada yetmez, çünkü her kullanıcı aynı role sahip. Önemli olan, kaydın o
  kullanıcıya ait olması. Bu kontrol servis katmanındaki `OwnedBy(userId)` filtresiyle yapılır.
- **Testler:** [IsolationTests.cs](../tests/GameVault.Tests/IsolationTests.cs). Alice bir oyun oluşturur, Bob o oyunu
  okumayı, düzenlemeyi, silmeyi ve dışa aktarmayı dener; hepsinin başarısız olduğu doğrulanır.

---

## 6. Veritabanı ve EF Core

- **Code-first + migration:** Tablolar C# sınıflarından üretilir. Her şema değişikliği bir migration dosyasıdır
  (`GameVault.DataAccess/Migrations`). Bu dosyalar Git'te tutulur, yani veritabanı şemasının da sürüm geçmişi olur.
- **İndeksler:** `(UserId, DateAdded)`, `(UserId, Title)` vb. Sorguların hepsi `UserId` ile başladığı için indeksler de
  `UserId` ile başlar.
- **Cascade delete:** Kullanıcı silinince oyunları ve oturumları da silinir. Hesap silme özelliği bu sayede
  "artık veri" bırakmaz.
- **CHECK kısıtları:** Puan 1–10 aralığında olmalı. Bu kural yalnızca C#'ta değil, veritabanında da zorunlu
  ("defense in depth").
- **Tarihler UTC olarak saklanır**, ekranda `Europe/Istanbul` saatine çevrilir.
- **`ExecuteDeleteAsync`:** Silme işlemi, kaydı önce belleğe yüklemeden tek bir `DELETE ... WHERE` sorgusuyla yapılır.

## 7. Diğer güvenlik önlemleri (kısa liste)

| Tehdit | Önlem | Nerede |
|---|---|---|
| CSRF | Antiforgery token, `SameSite` cookie | Razor Pages varsayılanı |
| XSS | Razor çıktıyı otomatik kodlar ve sıkı bir CSP (`default-src 'self'`) gönderilir | [SecurityHeadersMiddleware.cs](../GameVault.Web/Infrastructure/SecurityHeadersMiddleware.cs) |
| Kaba kuvvet / spam | Veritabanında atomik `MERGE` sayacıyla oran sınırlama | [DbRateLimiter.cs](../GameVault.Business/Security/DbRateLimiter.cs) |
| Açık yönlendirme | `returnUrl` yalnızca yerel adres olabilir | `AppPageModel.SafeLocalUrl` |
| SSRF | Sunucu harici URL indirmez; kapak adresleri izin listesinden seçilir | [CoverPolicy.cs](../GameVault.Business/Covers/CoverPolicy.cs) |
| Yanlış yapılandırma | Uygulama başlarken ayarları doğrular, hatalıysa başlamaz | [StartupValidation.cs](../GameVault.Web/Infrastructure/StartupValidation.cs) |

> **Neden oran sınırlamada Redis değil de SQL kullanıldı?** Uygulama birden fazla sunucuda çalışırsa sayaçların
> ortak olması gerekir. Zaten SQL Server var; ek bir altyapı kurmak yerine `MERGE ... WITH (HOLDLOCK)` ile atomik bir
> sayaç kullanıldı. Ödünleşimi: yüksek trafikte veritabanına yük biner.

## 8. Arka plan işleri: Outbox deseni

Sorun: Kayıt sırasında e-posta göndermek sayfanın içinde yapılırsa, SMTP sunucusu çöktüğünde kayıt da başarısız olur
ya da e-posta kaybolur.

Çözüm (**Transactional Outbox**):
1. Sayfa, e-postayı gönderilecek olarak `OutboxEmails` tablosuna yazar ([EmailOutbox.cs](../GameVault.Business/Email/EmailOutbox.cs)).
2. Arka planda çalışan bir `BackgroundService` ([EmailDispatchService.cs](../GameVault.Business/Email/EmailDispatchService.cs))
   tabloyu okuyup e-postaları gönderir. Başarısız olursa artan aralıklarla (exponential backoff) yeniden dener.
3. Uygulama yeniden başlasa bile kuyruktaki e-postalar kaybolmaz.

Ödünleşim: "En az bir kez" teslim. Çok nadir durumlarda aynı e-posta iki kez gidebilir.

## 9. Dependency Injection yaşam süreleri (sık sorulur)

[ServiceRegistration.cs](../GameVault.Web/Infrastructure/ServiceRegistration.cs) içinde:
- **Scoped** (`LibraryService`, `DbContext`): HTTP isteği başına bir nesne. `DbContext` thread-safe olmadığı için
  scoped olmalıdır.
- **Singleton** (`DbTicketStore`, `CoverPolicy`): Uygulama boyunca tek bir nesne. Singleton bir sınıf, scoped bir
  `DbContext`'i doğrudan alamaz. Bu yüzden `DbTicketStore` her işlemde `IServiceScopeFactory` ile yeni bir scope açar.
  Bu ayrıntı mülakatta "captive dependency" sorusu olarak karşına çıkabilir.
- **Hosted service** (`EmailDispatchService`): Uygulamayla birlikte başlayan arka plan işi.

## 10. Testler

- **Birim testi:** Tek bir sınıfı izole test eder ([UnitTests.cs](../tests/GameVault.Tests/UnitTests.cs)). Örneğin
  sorgu parametrelerinin normalleştirilmesi.
- **Entegrasyon testi:** `WebApplicationFactory` uygulamanın tamamını bellekte başlatır. Testler gerçek HTTP
  istekleri gönderir; middleware, auth, veritabanı, hepsi gerçek çalışır.
  - **Neden InMemory veritabanı değil de gerçek SQL Server?** EF InMemory; `MERGE`, CHECK kısıtları, cascade ve
    transaction davranışlarını SQL Server gibi uygulamaz. Testler geçer ama gerçek veritabanında hata çıkabilir.
  - Her test sınıfı kendi geçici veritabanını (`GameVaultTest_*`) oluşturup siler. Testler birbirini etkilemez.
- **E2E testi:** Playwright gerçek bir Chromium açar ve kullanıcı gibi tıklar. Masaüstü, mobil, klavye ve
  erişilebilirlik (axe-core) senaryolarını kapsar.
- **Gerçek bir ders:** README ekran görüntülerini alırken masaüstündeki filtre panelinin bozuk olduğu fark edildi.
  E2E testleri bunu yakalayamamıştı, çünkü yalnızca "öğe görünüyor mu?" diye bakıyorlardı. Artık filtrelerin yan yana
  dizildiğini koordinatlarla kontrol eden bir test var. *Görünür olmak, doğru görünmek demek değildir.*

## 11. Olası mülakat soruları

1. Neden Razor Pages, neden React değil? *Tek uygulama, SEO ve ilk yükleme hızlı, antiforgery yerleşik, JavaScript
   olmadan da çalışıyor. Bu ölçekte SPA'nın getirdiği karmaşıklığa gerek yoktu.*
2. Kullanıcı A, B'nin verisini nasıl göremez? *(Bölüm 3, 5)*
3. Oturumu nasıl iptal ediyorsun? *(Bölüm 4)*
4. Parolaları nasıl saklıyorsun? *(Bölüm 4)*
5. Migration nedir, production'da nasıl uygularsın? *(Bölüm 6, [OPERATIONS.md](OPERATIONS.md))*
6. E-posta servisi çökerse ne olur? *(Bölüm 8)*
7. Scoped ile Singleton farkı nedir? *(Bölüm 9)*
8. Testlerin neden gerçek veritabanı kullanıyor? *(Bölüm 10)*
9. Bu projede neyi farklı yapardın? *Dürüst cevap ver. Örneğin oran sınırlama yüksek trafikte Redis'e taşınabilir
   ya da Identity'nin PBKDF2'si yerine Argon2id kullanılabilir.*

---

## Aşama günlüğü

Her aşamada ne yapıldığı ve ne öğrenilebileceği.

### Aşama 1: Git, lisans, README
- Depo `git init -b main` ile oluşturuldu. Commit e-postası olarak GitHub'ın **noreply** adresi kullanıldı, böylece
  kişisel e-posta herkese açık geçmişte görünmez.
- İlk commit'ten önce dosyalarda sır taraması yapıldı. `.gitignore`, `.env`, `*.pfx` ve benzeri dosyaları dışarıda
  tutar.
- **MIT lisansı:** Herkes kodu kullanabilir; telif satırı korunmalıdır.

### Aşama 2: README içeriği ve ekran görüntüleri
- Ekran görüntüleri elle alınmıyor, bir Playwright testi üretiyor
  ([ReadmeScreenshots.cs](../tests/GameVault.E2E/ReadmeScreenshots.cs)). Test yalnızca `GAMEVAULT_SCREENSHOTS_DIR`
  ortam değişkeni verilince çalışır (`Assert.SkipWhen`). Veri kurgusaldır ve geçici test veritabanında oluşturulur.
- README'deki diyagramlar **Mermaid** ile yazıldı. GitHub bunları otomatik olarak çizer.
- Bulunan hata: `<details>` öğesine verilen `display: contents`, yeni Chromium sürümlerinde `::details-content`
  kutusu yüzünden çalışmıyor. Düzen artık bu numaraya dayanmıyor.

### Aşama 3: Kimlik doğrulama, yetkilendirme ve izolasyon testleri
Mevcut testler zaten kapsamlıydı. Eklenen testler, gerçek boşlukları kapatıyor:
- **Tüm endpoint'leri tarayan yetki testi**
  ([AuthorizationCoverageTests.cs](../tests/GameVault.Tests/AuthorizationCoverageTests.cs)). Eski test korumalı
  sayfaları elle yazılmış bir listeden kontrol ediyordu; listeye eklenmeyen yeni bir sayfa hiç test edilmiyordu.
  Yeni test `EndpointDataSource` ile uygulamadaki **tüm** adresleri bulur ve iki şeyi doğrular:
  1. Herkese açık adresler, testteki `PublicRoutes` listesiyle birebir aynıdır. Yeni bir herkese açık sayfa
     eklemek, bu listeyi bilerek değiştirmeyi gerektirir.
  2. Geri kalan her adres, anonim kullanıcıyı giriş sayfasına yönlendirir.
- **Testin testi (mutation check):** Testin gerçekten bir şey yakaladığını görmek için `Privacy` sayfasından
  `[AllowAnonymous]` geçici olarak silindi; test kırıldı, sonra kod eski hâline getirildi. Hiç kırılmayan bir test
  hiçbir şeyi korumuyor olabilir.
- **Servis katmanı testi** (`IsolationTests.Service_layer_enforces_ownership_without_the_page_layer`): Sayfalar
  atlanıp `LibraryService` ve `StatsService` doğrudan Bob'un kimliğiyle çağrılır. Sahiplik kuralı servis katmanında
  olduğu için, ileride eklenecek bir API ya da arka plan işi de aynı korumadan yararlanır (*defense in depth*).
  Testin başında sahibinin (Alice) kaydı görebildiği kontrol edilir. Bu "pozitif kontrol" olmasaydı, servis her
  şeye `null` dönse bile test geçerdi.

> **Mülakat sorusu:** "Testlerinin doğru şeyi test ettiğinden nasıl emin oluyorsun?"
> **Cevap:** Kodu bilerek bozup testin kırıldığını görüyorum (mutation testing'in elle yapılan hali). Bir de
> pozitif kontrol ekliyorum: "başkası göremez" testinin yanında "sahibi görebilir" kontrolü olmalı.

### Aşama 4: GitHub Actions (CI)
**CI (Continuous Integration):** Her push'ta kodun otomatik olarak derlenip test edilmesi. Amaç, "benim
bilgisayarımda çalışıyordu" sorununu ortadan kaldırmak.

[ci.yml](../.github/workflows/ci.yml) dosyasını okurken dikkat edilecek noktalar:
- **Paralel işler (jobs):** Test, Docker ve sır taraması ayrı makinelerde aynı anda çalışır.
- **Geçici SQL Server:** Testler için her çalıştırmada yeni bir SQL Server konteyneri açılır. Parolası rastgele
  üretilir ve `::add-mask::` ile loglarda gizlenir.
- **`--locked-mode`:** Paketler yalnızca `packages.lock.json` dosyasındaki sürümlerle yüklenir. Bu, birinin araya
  girip farklı bir paket sürümü yüklemesine karşı bir önlemdir (*supply chain security*).
- **Action'ların SHA ile sabitlenmesi:** `actions/checkout@v7` yerine `actions/checkout@3d3c42e…` yazılır.
  Etiket (tag) sonradan değiştirilebilir, commit SHA'sı değiştirilemez.
- **`permissions: contents: read`:** İş akışı yalnızca kodu okuyabilir. Ele geçirilse bile depoya yazamaz.
- **`has-pending-model-changes`:** Entity sınıfı değiştirilip migration eklenmeyi unutulursa CI kırılır.

Bu aşamada yapılanlar:
- CI'ın Docker dışındaki adımları yerelde çalıştırıldı. Gitleaks (geçmişte sır taraması) ve actionlint (iş akışı
  sözdizimi denetimi) temiz çıktı.
- SQL Server zamanında hazır olmazsa artık açık bir hata mesajı veriliyor. Önceden testler anlaşılmaz bir bağlantı
  hatasıyla düşüyordu. *İyi bir CI, hatanın nedenini söyler.*
- Test sonuçları TRX dosyası olarak kaydediliyor. Başarısız bir testin ayrıntısı Actions sayfasından indirilebilir.
- README'ye CI durum rozeti eklendi. Rozet, depo GitHub'a yüklendiğinde çalışır.

> **Mülakat sorusu:** "CI ile CD arasındaki fark nedir?"
> **Cevap:** CI her değişikliği otomatik olarak derleyip test eder. CD (Continuous Delivery/Deployment) testleri
> geçen sürümü otomatik olarak yayına alır. Bu projede yalnızca CI var, çünkü proje yayına alınmıyor.

### Aşama 5: Docker ve Docker Compose
**Docker:** Uygulamayı, çalışması için gereken her şeyle (.NET runtime, dosyalar) birlikte bir **imaja** paketler.
İmaj her makinede aynı şekilde çalışır. **Compose** ise birden fazla konteyneri (uygulama, SQL Server, Mailpit)
tek komutla birlikte başlatır.

[Dockerfile](../Dockerfile) dosyasını okurken dikkat edilecek noktalar:
- **Çok aşamalı derleme (multi-stage build):** Kod, büyük SDK imajında derlenir. Son imaja yalnızca derlenmiş
  dosyalar ve küçük ASP.NET runtime imajı girer. Böylece son imajda derleyici ve kaynak kod olmaz; imaj küçülür ve
  saldırı yüzeyi azalır.
- **Katman önbelleği:** Önce yalnızca `.csproj` ve `packages.lock.json` kopyalanıp `restore` yapılır, kod daha
  sonra kopyalanır. Kod değiştiğinde paketler yeniden indirilmez.
- **`USER $APP_UID`:** Uygulama root olarak çalışmaz. Bir açık bulunsa bile saldırgan konteynerde root yetkisi
  kazanmaz. CI bunu ayrıca kontrol eder.
- **Sır yok:** Parolalar imaja gömülmez, çalışma zamanında ortam değişkeni olarak verilir. `.dockerignore`, `.env`
  ve sertifika dosyalarının imaja kopyalanmasını engeller.
- **Ayrı migration imajı (`migrator`):** Veritabanı şeması, uygulama başlamadan önce bir kez güncellenir. Birden
  fazla uygulama kopyası aynı anda migration çalıştırmaya çalışmaz.

[compose.yaml](../compose.yaml) dosyasında:
- **Servis adları ağ adresidir:** Uygulama veritabanına `Server=sqlserver` diye bağlanır. Compose, servisler için
  ortak bir iç ağ kurar.
- **`127.0.0.1:8080:8080`:** Port yalnızca bu bilgisayardan erişilebilir. Sadece `8080:8080` yazılsaydı, aynı Wi-Fi
  ağındaki herkes erişebilirdi. (RabbitMQ'da gördüğün sorunun aynısı.)
- **`healthcheck` + `depends_on: condition: service_healthy`:** Uygulama, SQL Server sorgu kabul etmeye
  başlayana kadar bekler. Yalnızca "konteyner başladı" demek, "veritabanı hazır" demek değildir.
- **`${MSSQL_SA_PASSWORD:?...}`:** Parola `.env` dosyasında tanımlı değilse Compose başlamaz. Varsayılan bir parola
  bilerek verilmez.

Bu aşamada yapılanlar:
- Bu makinede Docker olmadığı için imajlar CI'da derleniyor. Yerelde, Dockerfile ve compose dosyasındaki beş imaj
  etiketinin registry'lerde gerçekten var olduğu doğrulandı.
- SQL Server imajı yalnızca amd64 için yayımlandığından compose dosyasına `platform: linux/amd64` eklendi. Apple
  Silicon Mac'lerde emülasyonla çalışır.

> **Mülakat sorusu:** "Konteyner ile sanal makine arasındaki fark nedir?"
> **Cevap:** Sanal makine kendi işletim sistemi çekirdeğini çalıştırır. Konteyner ise ana makinenin çekirdeğini
> paylaşır ve yalnızca süreçleri izole eder. Bu yüzden konteyner daha hafiftir ve saniyeler içinde başlar.

### Aşama 6: RAWG entegrasyonu (harici API)
Kullanıcı oyunu isimle arar, bir sonucu seçer, "Oyun ekle" formu dolar, kontrol edip kaydeder.

**Katmanlara dağılım:**

| Katman | Dosya | Görevi |
|---|---|---|
| Models | [CatalogDtos.cs](../GameVault.Models/Catalog/CatalogDtos.cs) | `CatalogGame`: RAWG'dan bağımsız model. Sayfalar yalnızca bunu görür. |
| Business | [RawgModels.cs](../GameVault.Business/Catalog/RawgModels.cs) | RAWG'ın JSON biçimi. `internal`, katman dışına çıkmaz. |
| Business | [RawgMapping.cs](../GameVault.Business/Catalog/RawgMapping.cs) | Güvenilmeyen veriyi temizler; RAWG platform ve türlerini GameVault listelerine eşler. |
| Business | [GameCatalog.cs](../GameVault.Business/Catalog/GameCatalog.cs) | HTTP çağrısı, timeout, önbellek, devre kesici |
| Entities/DataAccess | `LibraryGame.RawgId`, `ReleaseDate` + migration | Kalıcı veri, tekrar eklemeyi engelleyen indeks |
| Web | [Search.cshtml](../GameVault.Web/Pages/Library/Search.cshtml), [New.cshtml.cs](../GameVault.Web/Pages/Library/New.cshtml.cs) | Arama ekranı, formun doldurulması |

> **Neden DTO ayrı?** RAWG yarın JSON biçimini değiştirirse ya da IGDB'ye geçilirse yalnızca `RawgModels` ve
> `RawgMapping` değişir. Sayfalar ve veritabanı etkilenmez. Buna **Anti-Corruption Layer** denir: dış sistemin
> modeli kendi modeline sızmaz.

**Kavramlar:**
- **`IHttpClientFactory`:** Her istekte `new HttpClient()` yazmak bağlantı (socket) tüketir; tek bir statik
  `HttpClient` ise DNS değişikliklerini kaçırır. Fabrika ikisini de çözer. Adlandırılmış istemci (`"rawg"`) temel
  adresi, timeout'u ve başlıkları tek yerde tanımlar.
- **Singleton ve HttpClient:** `GameCatalog` singleton'dır (önbelleği tutar). `HttpClient`'ı constructor'da alıp
  saklamaz; her çağrıda fabrikadan ister. Bu, 9. bölümdeki "captive dependency" tuzağının bir başka örneği.
- **Timeout:** İstek 5 saniyede bitmezse iptal edilir. `CancellationTokenSource.CreateLinkedTokenSource`, hem kullanıcı
  sayfayı kapatırsa hem de süre dolarsa isteği durdurur. İki durum ayrı ele alınır: kullanıcı iptali bir hata
  değildir.
- **Devre kesici (circuit breaker):** RAWG hata verirse 30 saniye boyunca yeniden denenmez. Çökmüş bir servise her
  kullanıcı isteğinde 5 saniye beklemek, hem kullanıcıyı hem de RAWG'ı yorar. Anahtar reddedilirse (401) bekleme
  süresi daha uzundur.
- **Önbellek:** Aramalar 1 saat, oyun bilgileri 24 saat saklanır. Arama yapıldığında her sonuç ayrıca oyun kimliğiyle
  önbelleğe girer, böylece "Seç" butonu RAWG'a yeni istek atmaz. Önbelleğin boyutu sınırlıdır (`SizeLimit`), bu
  sayede binlerce farklı arama belleği şişiremez.
- **Güvenilmeyen veri:** RAWG'dan gelen ad kırpılır, görsel adresi yalnızca `https://media.rawg.io` ise kabul edilir,
  tarih sıkı biçimde ayrıştırılır. Bir testte betik içeren sahte bir oyun adı kullanılıyor; Razor bunu kodluyor.
- **Tekrar ekleme iki katmanda engellenir:** Servis önce kontrol eder ve kullanıcıya düzgün bir mesaj gösterir.
  Aynı anda gelen iki istek bu kontrolü birlikte geçebilir (*race condition*); o durumda veritabanındaki benzersiz
  indeks ikinciyi reddeder. Filtreli indeks (`WHERE RawgId IS NOT NULL`) sayesinde elle eklenen oyunlar bu kurala
  takılmaz.

**API anahtarının güvenliği:**
- Anahtar kodda ya da `appsettings` dosyalarında yok. Yerelde **User Secrets**'ta durur. User Secrets dosyası proje
  klasörünün dışında, `%APPDATA%\Microsoft\UserSecrets\` altındadır; bu yüzden Git'e hiç girmez.
- RAWG anahtarı URL'de taşır (`?key=...`). .NET, HttpClient loglarında query kısmını zaten `?*` olarak maskeliyor.
  Bu projede ikinci bir önlem olarak RAWG istemcisinin istek logları tamamen kapatıldı (`RemoveAllLoggers`). Bir test
  anahtarın loglarda ve sayfalarda geçmediğini doğrular.
- **Mutasyon kontrolünden çıkan ders:** İlk yazılan test `RemoveAllLoggers` satırı silindiğinde de geçiyordu, çünkü
  .NET'in maskelemesi zaten koruyordu. Test, bizim eklediğimiz katmanı hiç denetlemiyordu. Teste "RAWG istemcisi hiç
  log yazmıyor" kontrolü eklenince satır silindiğinde kırılır hâle geldi.
- gitleaks'e RAWG anahtarı için özel bir kural eklendi. Anahtar yanlışlıkla bir dosyaya yazılıp commit edilirse CI
  kırılır.

**Veri gizliliği:** RAWG'a yalnızca arama metni ve anahtar gider. Bir test, giden isteğin query'sinde tam olarak
`search`, `page_size` ve `key` olduğunu, çerez ya da e-posta bulunmadığını kontrol eder. Kapak görsellerini tarayıcı
doğrudan RAWG'dan yüklediği için RAWG kullanıcının IP adresini görebilir. Bu durum Gizlilik sayfasında açıkça yazıyor.

> **Mülakat sorusu:** "Harici bir API çökerse uygulaman ne yapar?"
> **Cevap:** Hiçbir şey çökmez. Timeout isteği 5 saniyede keser, hata yakalanır ve kullanıcıya "şu an ulaşılamıyor,
> elle ekleyebilirsin" mesajı gösterilir. Devre kesici 30 saniye boyunca yeni istek atmaz. Kütüphane, istatistikler
> ve elle ekleme RAWG'dan tamamen bağımsızdır; bunu testler doğrular.

> **Mülakat sorusu:** "Neden Polly ya da `AddStandardResilienceHandler` kullanmadın?"
> **Cevap:** Bu ölçekte timeout ve basit bir devre kesici yeterliydi; ek bir paket bağımlılığı getirmek istemedim.
> Arama etkileşimli bir işlem olduğu için yeniden deneme (retry) bilerek yok: kullanıcıyı 3 deneme boyunca
> bekletmek yerine hemen mesaj göstermek daha iyi. Trafik artarsa Microsoft.Extensions.Http.Resilience'a geçilebilir.

### Ara adım: aynı adlı oyun uyarısı
- **Kural mı, uyarı mı?** RAWG oyununun tekrar eklenmesi **kural**dır (veritabanı engeller). Aynı adlı oyun ise
  **uyarı**dır, çünkü aynı oyunu iki platformda ayrı tutmak meşru bir kullanım. Uyarı kullanıcıya karar bırakır:
  "Yine de ekle" düğmesi var.
- **Kontrol nerede?** Kural servis katmanında durur (her yoldan geçerli olmalı). Uyarı ise sayfa katmanındadır, çünkü
  bu bir kullanıcı arayüzü akışı.
- **Küçük ama önemli bir ayrıntı:** Bir formda Enter'a basıldığında tarayıcı **ilk** gönder düğmesini kullanır.
  "Yine de ekle" ilk sırada olsaydı Enter tuşu uyarıyı sessizce atlardı. Bu yüzden onay düğmesi normal düğmeden sonra
  geliyor ve bir test bunu kontrol ediyor.
- Karşılaştırma büyük/küçük harf duyarsız. Bunu C# değil, SQL Server'ın harmanlaması (collation, `CI` = case
  insensitive) sağlıyor.

### Aşama 7: "Ne oynasam?"
Sıradaki oyunlar ("Başlamadım", "Ara verdim", isteğe bağlı olarak "İstek listesi") arasından platform, tür ve en
fazla tahmini süreye göre rastgele bir öneri yapar.

- **Yeni veri: `EstimatedHours`.** "Oynama süresi" (şimdiye kadar oynadığın) ile "Tahmini süre" (oyunu bitirmek için
  gereken) farklı kavramlar. İkisini aynı alana koymak, filtreyi anlamsız yapardı. RAWG'ın `playtime` alanı (ortalama
  oynanma süresi, 0 = bilinmiyor) RAWG'dan eklenen oyunlarda bu alanı otomatik doldurur.
- **Rastgele seçim veritabanında yapılır** ([SuggestionService.cs](../GameVault.Business/Library/SuggestionService.cs)):
  önce filtrelere uyan oyunlar sayılır, ardından `Skip(rastgele).Take(1)` ile tek bir kayıt çekilir. Kütüphanenin
  tamamı belleğe yüklenmez. 5000 oyunluk bir kütüphanede de yalnızca iki küçük sorgu çalışır. `OrderBy(Id)`
  zorunludur: sırasız bir `Skip`'in sonucu SQL'de tanımsızdır.
- **Bilinmeyen değerler sessizce kaybolmaz:** "En fazla 10 saat" filtresinde tahmini süresi girilmemiş oyunlar
  elenir, ama kaç tane olduğu kullanıcıya söylenir. Kullanıcı neden az sonuç gördüğünü anlar.
- **"Başka öner":** Az önce önerilen oyun bir sonraki öneride hariç tutulur. Tek aday varsa hariç tutma yok
  sayılır; aksi hâlde "oyun yok" demek yanlış olurdu.
- **Geçersiz girdi hata sayfası değildir:** `?platform=abc&maxHours=999999` gibi bozuk adresler filtresiz öneri üretir.
  Değerler `SuggestionFilter.Normalize` ile temizlenir.
- **"Oynamaya başla":** Oyunu "Oynuyorum" yapar, başlama tarihi boşsa bugünü yazar.
  - **Saat dilimi tuzağı:** "Bugün" UTC'ye göre hesaplansaydı, Türkiye'de gece 00:00–03:00 arasında tarih bir gün
    geri giderdi. Tarih, uygulamanın gösterim saat dilimine (`Europe/Istanbul`) göre hesaplanıp servise parametre
    olarak verilir. Servis "bugün"ü kendisi bilmez; bu da onu test edilebilir yapar.
- **Testler:** Öneri yalnızca sıradaki oyunlardan geliyor mu? Bunu doğrulamak için seçim 40 kez tekrarlanır ve iki
  adayın da görüldüğü kontrol edilir (tesadüfen geçme olasılığı yaklaşık 10⁻¹²). Ayrıca başka kullanıcının oyununun
  ne önerilebildiği ne de "Oynamaya başla" ile değiştirilebildiği test edilir. 3. aşamadaki yetki kapsama testi yeni
  `/Suggest` sayfasını kendiliğinden bulup korumalı olduğunu doğruladı; o testin amacı buydu.
- **Erişilebilirlik:** Öneri ve RAWG arama sayfaları axe-core denetimine eklendi.

> **Mülakat sorusu:** "Veritabanından rastgele bir satırı nasıl seçersin?"
> **Cevap:** `ORDER BY NEWID()` kısa ama tüm tabloyu sıralar. Ben önce `COUNT` alıp rastgele bir konum seçiyorum,
> sonra `ORDER BY Id OFFSET n ROWS FETCH NEXT 1` ile tek satırı çekiyorum. Kullanıcının kütüphanesi `UserId`
> indeksiyle zaten küçük bir kümeye indiği için iki yöntem de hızlı; ama ikincisi büyük tablolarda da ölçeklenir.

### Aşama 8: İstatistik grafikleri
İstatistik sayfasına iki grafik eklendi: **aylık etkinlik** (son 12 ayda başlanan ve bitirilen oyunlar) ve **puan
dağılımı** (1–10). Platform, tür ve "en çok oynananlar" listeleri de çubuklu hâle geldi.

- **Neden grafik kütüphanesi (Chart.js vb.) yok?** Uygulamanın CSP'si `script-src 'self'` ve `style-src 'self'`.
  Yani CDN'den betik yüklenemez ve satır içi `style="..."` yasak. Çoğu grafik kütüphanesi satır içi stil yazar. Bunun
  yerine grafikler sunucuda, Razor ile **SVG** olarak çizilir
  ([_ColumnChart.cshtml](../GameVault.Web/Pages/Shared/_ColumnChart.cshtml)). SVG'nin `x`, `height`, `fill` gibi
  öznitelikleri CSP'ye takılmaz; renkler zaten CSS sınıflarından gelir. Sayfa JavaScript olmadan da çalışır.
- **Yüzde + piksel hilesi:** Sütunların yatay konumu yüzde (`x="25%"`), yüksekliği piksel. Grafik ekran genişliğine
  uyar ama yazılar büyüyüp küçülmez (`viewBox` ile ölçekleseydik mobilde yazılar okunmaz hâle gelirdi).
- **Eksen hesabı** ([Charts.cs](../GameVault.Web/Infrastructure/Charts.cs), `ChartScale.Axis`): en fazla 4 aralık,
  adımlar 1-2-5-10-20-50… dizisinden. 7 → 0/2/4/6/8 olur, 23 → 0/10/20/30 olur. Saf bir fonksiyon olduğu için birim
  testi kolay.
- **Sorgular:** Puan dağılımı ve aylık sayımlar veritabanında `GROUP BY` ile yapılır. Oyunlar belleğe çekilmez.
  Boş aylar sonradan sıfırla doldurulur; böylece grafik her zaman 12 sütun gösterir.
- **"Bugün" yine dışarıdan gelir:** Aşama 7'deki saat dilimi tuzağı burada da geçerli. Ekim ayının ilk saatlerinde
  UTC'ye göre hâlâ Eylül olabilir. Sayfa, gösterim saat dilimindeki bugünü servise parametre olarak verir.
- **Tarih kaynağı:** Aylık grafik, kullanıcının girdiği başlama/bitirme tarihlerinden (`DateOnly`) hesaplanır,
  `DateAdded`'dan değil. Böylece "kütüphaneye ne zaman eklendi" yerine "ne zaman oynandı" gösterilir. Saat dilimi
  dönüşümü de gerekmez.
- **Renk seçimi ölçülerek yapıldı:** İki serinin renkleri (mavi #4a90e0, turuncu #c98330) koyu zemin üzerinde
  açıklık, renk körlüğü ayrımı ve kontrast açısından bir doğrulama betiğiyle kontrol edildi. Mavi-turuncu çifti
  en yaygın renk körlüğü türlerinde de ayırt edilir. Renk tek başına bilgi taşımaz: lejant, araç ipucu (`<title>`)
  ve tablo da var.
- **Erişilebilirlik:** Her grafiğin altında "Tablo olarak göster" (`<details>`) vardır. Ekran okuyucu kullanıcıları
  değerleri buradan okur. axe-core denetimi artık grafikler çizilmişken ve tablolar açıkken de çalışıyor.
  Yüksek kontrast modunda (`forced-colors`) çubuklar sistem renklerine döner.
- **Testler:** Puan dağılımı, 12 aylık pencerenin sınırları (pencere dışındaki eski ve gelecek tarihler sayılmaz),
  boş grafik yerine açıklama metni, sayfada hiç `style` özniteliği olmaması (CSP regresyonu) ve eksen hesabı.

> **Mülakat sorusu:** "Grafik için neden hazır bir kütüphane kullanmadın?"
> **Cevap:** Sıkı bir CSP'm var ve ihtiyacım iki basit sütun grafiği. Kütüphane eklemek, CSP'yi gevşetmek
> (`unsafe-inline`) ya da yeni bir bağımlılığı güncel tutmak demekti. SVG'yi sunucuda üretmek yaklaşık 160 satır.
> Grafik JavaScript olmadan çalışıyor ve test edilebiliyor. İhtiyaç büyürse (etkileşimli filtre, zoom) bir kütüphane
> eklemeyi o zaman değerlendiririm.

### Aşama 9: JSON'dan içe aktarma
Hesap ayarlarındaki "İçe aktar", uygulamanın kendi dışa aktarma dosyasını (`gamevault-export/v1`) okuyup oyunları
kütüphaneye ekler. Böylece veri taşınabilirliği iki yönlü olur: indirdiğin dosyayı başka bir hesaba ya da sıfırdan
kurulmuş bir veritabanına geri yükleyebilirsin.

- **Dosya bir kullanıcı girdisidir, güvenilmez.** [ImportService.cs](../GameVault.Business/Account/ImportService.cs)
  dosyadaki `id`, `userId` ve hesap bilgilerini hiç okumaz. Oyunlar her zaman oturumdaki kullanıcıya, yeni kimlikle
  eklenir. Bu, formdaki "mass assignment" korumasının dosya versiyonu.
- **Aynı kurallar, tek yer:** Her oyun, formla eklenen oyunlarla aynı `ValidateAsync` metodundan geçer: puan 1–10,
  kapak adresi izin listesinde, tarihler makul vb. Doğrulamayı ikinci kez yazmak, iki kuralın zamanla ayrışması demekti.
- **Toplu işte N+1 sorgu tuzağı:** `ValidateAsync` normalde her oyun için platform ve türleri veritabanından kontrol eder.
  5000 oyunluk bir dosyada bu 10.000 sorgu olurdu. İçe aktarmada platform ve tür listesi bir kez yüklenip
  (`ReferenceCache`) tüm oyunlar için bellekten kontrol ediliyor.
- **İdempotent (tekrarlanabilir) tasarım:** Kütüphanede aynı ad + platformla ya da aynı RAWG kimliğiyle zaten olan
  oyunlar atlanır. Aynı dosyayı iki kez yüklemek kopya oluşturmaz. Kullanıcı yarıda kesilen bir işlemi gönül rahatlığıyla
  tekrarlayabilir.
- **Kısmi başarı + tek transaction:** Hatalı kayıtlar satır numarasıyla raporlanır ve atlanır. Geçerli olanlar tek bir
  `SaveChanges` ile eklenir; ya hepsi yazılır ya hiçbiri. Kütüphane sınırı (5000) aşılacaksa hiçbir şey yazılmaz.
- **"Kontrol et" (dry run):** Aynı kod yolunu çalıştırır ama veritabanına yazmaz. Kullanıcı önce sonucu görür.
- **İstek boyutu sınırı sayfa bazında:** Uygulamanın genel istek sınırı 128 KB. Yalnızca bu sayfada
  `[RequestSizeLimit]` ile 4 MB'a çıkarıldı; diğer sayfalar etkilenmedi. Dikkat: entegrasyon testlerinin kullandığı
  TestServer bu sınırı uygulamaz. Sınırın gerçekten çalıştığını, gerçek Kestrel'de 640 KB'lık dosya yükleyen E2E
  testi kanıtlıyor.
- **JSON güvenliği:** `MaxDepth = 16` (derin iç içe dosyalarla yığın tüketimini önler), yorum ve sondaki virgül kabul
  edilmez. Bir oyunda tür hatası varsa (ör. `"playtimeHours": "çok"`) yalnızca o oyun hatalı sayılır: oyunlar önce
  `JsonElement` olarak okunup tek tek çözülür.

> **Mülakat sorusu:** "Kullanıcının yüklediği dosyayı işlerken nelere dikkat edersin?"
> **Cevap:** Boyut (istek ve dosya sınırı), biçim (beklenen format alanı), yapı (JSON derinliği), içerik (her alan
> normal formla aynı doğrulamadan geçer), sahiplik (dosyadaki kimlikler yok sayılır), oran sınırı ve atomiklik
> (tek transaction). Dosyayı diske yazmıyorum, akıştan okuyorum; böylece geçici dosya temizliği ya da yol (path)
> saldırısı gibi sorunlar da oluşmuyor.

---

## Kendi notlarım

(Buraya kendi anladıklarını yaz. Bir konuyu kendi cümlelerinle yazamıyorsan, o konuya geri dön.)
