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

---

## Kendi notlarım

(Buraya kendi anladıklarını yaz. Bir konuyu kendi cümlelerinle yazamıyorsan, o konuya geri dön.)
