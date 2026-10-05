# Dağıtım ve işletim

Bu belge, seçilen dağıtım yolunu ve işletim yönergelerini açıklar. Uygulama henüz hiçbir ortama dağıtılmadı.
Buradaki adımlar üretim öncesi kontrol listesidir; bir üretim onayı ya da denetimi değildir.

## Seçilen dağıtım yolu

**Docker imajı + TLS sonlandıran ters proxy + yönetilen SQL Server.**

```
İnternet ──HTTPS──> Ters proxy (ör. Caddy / nginx / bulut yük dengeleyici)
                        │ HTTP (özel ağ), X-Forwarded-For / -Proto
                        ▼
                 gamevault (Docker, :8080, root olmayan kullanıcı) ──TLS──> SQL Server (ör. Azure SQL Database)
                        │
                        └──SMTP (STARTTLS/SSL)──> E-posta sağlayıcısı
```

Tek bir Linux VM (Docker + Caddy) ya da konteyner çalıştırabilen herhangi bir PaaS bu yapıyı karşılar.
Birden fazla instance çalıştırılabilir: oturumlar, Data Protection anahtarları ve oran sınırı sayaçları veritabanındadır.

### Ücret ve hesap gereksinimleri

| Hizmet | Gereksinim |
|---|---|
| Sunucu/konteyner barındırma | Ücretli bir VM ya da PaaS hesabı (sağlayıcıya göre değişir). |
| SQL Server | Yönetilen hizmet (ör. Azure SQL Database; ücretsiz katman/teklif koşullarını sağlayıcıdan kontrol edin) ya da kendi sunucunuzda SQL Server Express (ücretsiz, veritabanı başına boyut sınırı var). Developer sürümü **üretimde kullanılamaz**. |
| SMTP | Bir e-posta gönderim hizmeti hesabı (çoğu ücretli ya da kotalı). |
| Alan adı + TLS | Alan adı ücretli. Caddy ya da Let's Encrypt ile sertifika ücretsizdir. |
| GitHub | Actions dakikaları genel depolarda ücretsiz; özel depolarda plana göre kotalıdır. CodeQL ve dependency review özel depolarda GitHub Code Security/Advanced Security gerektirir. |

## Üretim ortam değişkenleri

.NET yapılandırmasında `:` ayracı, ortam değişkenlerinde `__` olarak yazılır.

| Değişken | Zorunlu | Açıklama |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | evet | `Production` (imajda varsayılan). |
| `ConnectionStrings__DefaultConnection` | evet | SQL Server bağlantısı. `Encrypt=True` kullanın; `TrustServerCertificate=True` **kullanmayın**. En az yetkili bir veritabanı kullanıcısıyla bağlanın. |
| `App__PublicBaseUrl` | evet | `https://…` genel adres. E-posta bağlantıları bununla kurulur. |
| `App__DisplayTimeZone` | hayır | Varsayılan `Europe/Istanbul`. |
| `Email__Host`, `Email__Port` | evet | SMTP sunucusu. |
| `Email__Security` | evet | `StartTls` ya da `SslOnConnect`. `None` üretimde reddedilir. |
| `Email__Username`, `Email__Password` | sağlayıcıya göre | Sır; gizli değişken olarak verin. |
| `Email__FromAddress`, `Email__FromName` | evet / hayır | Gönderen adresi. |
| `AllowedHosts` | önerilir | Ör. `gamevault.example.com`. Host başlığı filtrelemesi. |
| `ReverseProxy__KnownProxies__0` | proxy varsa | Proxy'nin IP adresi. Tanımlanmazsa `X-Forwarded-*` yok sayılır; bu durumda IP tabanlı oran sınırı proxy IP'sine uygulanır. |
| `ReverseProxy__KnownNetworks__0` | alternatif | Ör. `10.0.0.0/8`. |
| `Rawg__ApiKey` | hayır | RAWG API anahtarı; sır olarak verin. Boşsa RAWG araması kapalıdır. Tanımlıysa `media.rawg.io` kapak izin listesine ve CSP'ye otomatik eklenir. |
| `Rawg__BaseUrl`, `Rawg__TimeoutSeconds`, `Rawg__FailureCooldownSeconds` | hayır | Varsayılanlar `https://api.rawg.io/api/`, 5 sn, 30 sn. Adres https olmalı ve `/` ile bitmeli. |
| `RateLimiting__CatalogPerUser__*` | hayır | Kullanıcı başına RAWG arama sınırı (varsayılan 15 dakikada 60). |
| `Covers__AllowedHosts__0` | hayır | Harici kapak görselleri için izinli alan adı. Boşsa yalnızca hazır kapaklar kullanılır. |
| `Security__UseHttpsRedirection` | hayır | Varsayılan `true`. Yalnızca TLS'i proxy zorunlu kılıyorsa kapatın. |
| `Security__EnableHsts` | hayır | Varsayılan `true`. HSTS yalnızca Production ortamında ve HTTPS isteklerinde gönderilir. |
| `RateLimiting__*` | hayır | Sınır ayarları (bkz. `DbRateLimiter.cs`). |
| `Library__MaxGamesPerUser` | hayır | Varsayılan 5000. |
| `DataProtection__Certificate__Path`, `__Password` | evet | Data Protection anahtarlarını şifreleyen sertifika (bkz. aşağıda). |
| `EmailOutbox__*` | hayır | Yeniden deneme sayısı (`MaxAttempts`, 6), bekleme (`BaseDelaySeconds` 30, ikiye katlanır, en fazla `MaxDelaySeconds` 1800), tamamlanan işlerin saklanma süresi (`RetentionDays`, 7). |

Eksik ya da geçersiz yapılandırmada uygulama başlamaz ve hangi anahtarların hatalı olduğunu (değerlerini göstermeden) listeler.

## HTTPS, çerez ve proxy

- TLS proxy'de sonlanır. Proxy `X-Forwarded-For` ve `X-Forwarded-Proto` göndermeli ve IP'si `ReverseProxy__KnownProxies` içinde olmalıdır. Aksi halde uygulama isteği HTTP sanar ve yönlendirme döngüsü oluşabilir.
- Production ortamında çerezler `__Host-` önekli, `Secure`, `HttpOnly`, `SameSite=Lax` (oturum) ve `SameSite=Strict` (CSRF).
- HSTS süresi 180 gün, `includeSubDomains` kapalı. Alan adınız için emin olduktan sonra artırabilirsiniz.

## Build ve başlatma

```bash
docker build --target runtime  -t gamevault:<sürüm> .
docker build --target migrator -t gamevault-migrator:<sürüm> .
```

## Migration uygulama sırası

1. **Yedek alın** (aşağıya bakın).
2. Migration'ları uygulamadan **ayrı bir adım** olarak çalıştırın (uygulama üretimde otomatik migration yapmaz):
   ```bash
   docker run --rm gamevault-migrator:<sürüm> --connection "<bağlantı dizesi>"
   ```
   Alternatif: SQL betiği üretip inceleyin, sonra DBA aracıyla uygulayın:
   ```bash
   dotnet ef migrations script --idempotent --project GameVault.DataAccess --startup-project GameVault.DataAccess -o migrate.sql
   ```
3. Yeni uygulama sürümünü başlatın; `/health` 200 döndükten sonra trafiği yönlendirin.

> Not: `dotnet ef migrations bundle` komutunu yerelde çalıştırmak `packages.lock.json` dosyalarına çalışma zamanı kimliği ekleyebilir. Ardından `dotnet restore GameVault.slnx --force-evaluate` çalıştırın ya da paketi Docker ile üretin.

### Veri kaybı riski taşıyan migration: `UserOwnedLibrary`

Bu migration eski global katalog yapısını (`Games`, `UserGames`, `GameGenres`) kullanıcıya ait `LibraryGames` tablosuna dönüştürür:
- Her `UserGames` kaydı, bağlı olduğu oyunun ad, açıklama ve kapak bilgisiyle birlikte `LibraryGames` kaydına kopyalanır; türler de taşınır.
- **Hiçbir kullanıcının kütüphanesinde olmayan katalog oyunları** ve katalogdaki çıkış tarihleri yeni modelde karşılığı
  olmadığı için kütüphanelere taşınmaz, ancak **silinmez**: eski tablolar dönüştürme öncesinde birebir
  `Legacy_Games`, `Legacy_GameGenres` ve `Legacy_UserGames` arşiv tablolarına kopyalanır. Bu kayıtlar hiçbir kullanıcıya
  atanmaz ve uygulamada görünmez. Eski tablolar boşsa (yeni kurulum) arşiv oluşturulmaz.
- Aralık dışı değerler kırpılır: puan 1–10 dışındaysa boşaltılır, süre 0–100000 aralığına çekilir. Özgün değerler arşivde kalır.
- `Down` ile geri alınabilir: arşiv varsa kütüphanede olmayan katalog oyunları, türleri ve çıkış tarihleri geri yüklenir ve
  arşiv tabloları kaldırılır. Yeni şemaya özgü bilgiler kaybolur: yeni durumlar ("Ara verdim", "İstek listesi") `Backlog`'a
  döner, 2000 karakteri aşan notlar kısalır, görünen ad, oturumlar ve e-posta kuyruğu silinir.
- Hesap silindiğinde kullanıcının `Legacy_UserGames` satırları da silinir (not ve puan içerir). Ortak katalog
  (`Legacy_Games`) kişisel veri olmadığı için korunur.
- Arşivi inceledikten ve gerekmediğine karar verdikten sonra, **yedek aldıktan sonra** elle kaldırabilirsiniz:
  `DROP TABLE [Legacy_GameGenres]; DROP TABLE [Legacy_UserGames]; DROP TABLE [Legacy_Games];`
  Kaldırıldıktan sonra `Down` kütüphanede olmayan katalog oyunlarını geri getiremez.
- Eski hesaplar `EmailConfirmed = 0` ise giriş yapamaz. Bu kullanıcılar "Doğrulama bağlantısını yeniden gönder" ya da
  "Parolamı unuttum" akışıyla adreslerini doğrulayabilir (sıfırlama bağlantısını açmak adresi doğrulanmış sayar).

**Mevcut verisi olan bir veritabanında bu migration'ı yedek almadan ve açık onay olmadan çalıştırmayın.** Önce yedeğin bir kopyası üzerinde deneyin.

## Sağlık kontrolü

- `GET /health/live`: süreç ayakta mı (bağımlılık kontrolü yok). Liveness probe için.
- `GET /health`: veritabanına erişilebiliyor mu. Yalnızca `Healthy`/`Unhealthy` döner, ayrıntı içermez. Readiness probe için.

## Loglama ve hata takibi

- Production'da loglar stdout'a JSON olarak yazılır (`appsettings.Production.json`). Konteyner platformunun log toplayıcısıyla alın.
- Parola, token, çerez, e-posta içeriği ve sıfırlama bağlantıları loglanmaz. `Microsoft.AspNetCore` kategorisi `Warning` seviyesindedir; bu, istek URL'lerini (sıfırlama tokenı içerebilir) loglayan satırları kapalı tutar. **Bu seviyeyi Information'a düşürmeyin.** Proxy erişim loglarının da sorgu dizelerini kaydedebileceğini unutmayın; proxy'de `/Account/ResetPassword` için sorgu dizesi loglamayı kapatın.
- Harici hata takip servisi (Sentry, Application Insights vb.) entegre edilmedi. Eklenirse istek gövdesi, çerez ve sorgu dizesi gönderimini kapatın.

## Data Protection anahtarları

Anahtar halkası `DataProtectionKeys` tablosunda tutulur (tüm instance'lar paylaşır) ve saklama sırasında bir X.509
sertifikasıyla (RSA özel anahtarlı PKCS#12) şifrelenir. Sertifika veritabanında **değil**, ayrı bir sır olarak durur;
yalnızca veritabanı dökümüne erişen biri çerez ya da token üretemez.

| Değişken | Açıklama |
|---|---|
| `DataProtection__Certificate__Path` | Üretimde **zorunlu**. Konteynere salt okunur bağlanan .pfx dosyası (ör. Docker/Kubernetes secret). |
| `DataProtection__Certificate__Password` | .pfx parolası; gizli değişken olarak verin. |
| `DataProtection__PreviousCertificates__N__Path` / `__Password` | Rotasyon sonrası eski anahtarları çözmek için önceki sertifikalar. |

Production ortamında sertifika tanımlı değilse, dosya yoksa, açılamıyorsa ya da RSA özel anahtarı içermiyorsa uygulama
başlamaz ve hatalı anahtarı (değer göstermeden) bildirir. Development/Test ortamında sertifikasız çalışabilir; o zaman
anahtarlar şifrelenmeden saklanır.

Örnek sertifika üretimi (yerel bir makinede; .pfx dosyasını depoya eklemeyin):

```bash
openssl req -x509 -newkey rsa:3072 -sha256 -days 730 -nodes -subj "/CN=GameVault Data Protection" \
  -keyout dp.key -out dp.crt
openssl pkcs12 -export -inkey dp.key -in dp.crt -out gamevault-dp.pfx   # parola sorar
shred -u dp.key                                                         # açık özel anahtarı silin
```

**Gereksinimler ve etkiler:**
- **Yeniden başlatma:** Anahtarlar veritabanında olduğundan yeniden başlatma oturumları etkilemez; aynı sertifika bağlanmalıdır.
- **Birden fazla instance:** Hepsi aynı veritabanını ve aynı sertifikayı kullanmalıdır. Farklı sertifikalı bir instance
  mevcut anahtarları çözemez; o instance'a düşen kullanıcılar oturumsuz görünür.
- **Sertifika rotasyonu:** Yeni sertifikayı `Certificate`, eskisini `PreviousCertificates__0` olarak verin. Yeni anahtarlar
  yenisiyle şifrelenir; eskiler okunmaya devam eder. Eski anahtarlar süresi dolup (varsayılan 90 gün) kullanılmaz hale
  geldikten sonra eski sertifikayı kaldırabilirsiniz. Eski sertifika erken kaldırılırsa etkisi, anahtarların kaybıyla aynıdır.
- **Yedekten geri yükleme:** Veritabanı yedeği anahtarları şifreli içerir. Geri yüklenen veritabanını kullanabilmek için
  yedek alındığı tarihte geçerli olan sertifika(lar) da gerekir. Sertifikaları veritabanı yedeğinden **ayrı** bir yerde
  (ör. anahtar kasası) saklayın.
- **Anahtar kaybı ya da değişimi:** Tüm oturumlar kapanır (kullanıcılar yeniden giriş yapar), bekleyen parola sıfırlama
  ve e-posta doğrulama bağlantıları geçersiz olur ve outbox'ta bekleyen e-postaların gövdesi çözülemediği için bu işler
  `Failed` olarak kapanır. Kullanıcı verisi (kütüphane, hesaplar, parola özetleri) kaybolmaz.

## Yedekleme ve geri yükleme

- **Yönetilen SQL (ör. Azure SQL):** Otomatik yedekleme ve zamanda geri dönüş (PITR) özelliğini etkinleştirin; saklama süresini belirleyin.
- **Kendi SQL Server'ınız:**
  ```sql
  BACKUP DATABASE [GameVault] TO DISK = N'/var/opt/mssql/backup/gamevault-YYYYMMDD.bak' WITH COMPRESSION, CHECKSUM;
  RESTORE VERIFYONLY FROM DISK = N'/var/opt/mssql/backup/gamevault-YYYYMMDD.bak' WITH CHECKSUM;
  ```
  Geri yükleme (önce uygulamayı durdurun):
  ```sql
  RESTORE DATABASE [GameVault] FROM DISK = N'/var/opt/mssql/backup/gamevault-YYYYMMDD.bak' WITH REPLACE, CHECKSUM;
  ```
- Yedekleri sunucu dışında, erişimi kısıtlı ve şifreli bir yerde saklayın. Geri yüklemeyi periyodik olarak ayrı bir ortamda test edin.
- Hesap silme işlemi yedekleri etkilemez; gizlilik sayfası bunu belirtir. Yedek saklama süresini buna göre belirleyin.

## Sürüm geri alma ve migration uyumluluğu

- Geri alma için bir önceki imaj etiketiyle yeniden başlatın. Yalnızca **şema değişikliği içermeyen** sürümlerde bu tek başına yeterlidir.
- Şema değiştiren sürümlerde tercih edilen yol şudur: migration'ları geriye uyumlu yazın (önce kolon ekle, kodu dağıt, sonraki sürümde eskiyi kaldır). Böylece eski uygulama yeni şemayla çalışabilir.
- `UserOwnedLibrary` geriye uyumlu **değildir**. Eski JWT API bu şemayla çalışmaz. Geri dönmek gerekirse ya yedekten geri yükleyin ya da `efbundle` ile hedef migration'a inin:
  ```bash
  docker run --rm gamevault-migrator:<sürüm> 20260825160808_AddIdentityTables --connection "<bağlantı dizesi>"
  ```

## Veritabanı yetkileri (öneri)

Uygulama kullanıcısı `db_datareader` ve `db_datawriter` rollerine sahip olmalı. Migration'lar ayrı, DDL yetkili bir kullanıcıyla çalıştırılmalı.
Oran sınırlayıcı `MERGE` kullandığından yalnızca veri yazma yetkisi yeterlidir.
