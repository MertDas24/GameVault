# Mimari ve kararlar

## Genel yapı

Tek bir ASP.NET Core uygulaması (Razor Pages) ve tek bir SQL Server veritabanı. Mikroservis, ayrı API ya da SPA yoktur.

```
GameVault.Entities    → EF Core varlıkları (ApplicationUser, LibraryGame, Genre, Platform, GameCollection, UserSession, RateLimitCounter)
GameVault.DataAccess  → DbContext, migration'lar, tasarım zamanı fabrikası
GameVault.Models      → Form girdi modelleri (doğrulama öznitelikleri), sorgu normalleştirme, DTO'lar
GameVault.Business    → Kütüphane, istatistik, hesap, oran sınırlama, e-posta, bakım servisleri
GameVault.Web         → Razor Pages arayüzü, kimlik doğrulama, güvenlik başlıkları, başlangıç doğrulaması
tests/GameVault.Tests → Birim + entegrasyon testleri (gerçek SQL Server, WebApplicationFactory)
tests/GameVault.E2E   → Playwright tarayıcı testleri (Kestrel + Chromium)
```

Katmanlı proje yapısı mevcut kod tabanından korunmuştur.

## Kararlar

| # | Karar | Gerekçe |
|---|---|---|
| 1 | **Mevcut .NET çözümü geliştirildi**, Next.js'e geçilmedi | Depoda çalışan bir .NET çözümü, migration geçmişi ve yerel SQL Server vardı; proje sahibi bu yönü seçti. |
| 2 | **.NET 8 → .NET 10 (LTS)** | .NET 8 desteği 10 Kasım 2026'da bitiyor. .NET 10, 14 Kasım 2028'e kadar destekli (kaynak: dotnetcli release metadata). |
| 3 | **SQL Server korundu** (PostgreSQL yerine) | Mevcut migration'lar SQL Server'a özgüydü ve yerel ortamda SQL Server/LocalDB vardı. Değiştirmek veri taşıma riski doğururdu. |
| 4 | **Razor Pages, sunucu tarafı render** | Tek uygulama; yerleşik antiforgery; JavaScript olmadan çalışan formlar; katı CSP ile uyumlu. |
| 5 | **Tailwind yerine elle yazılmış CSS** | Node derleme zinciri ve ek bağımlılık gerektirmez. Tasarım token'ları `:root` değişkenlerinde. |
| 6 | **JWT API kaldırıldı**, cookie + sunucu tarafı oturum | JWT çıkışta iptal edilemiyordu ve istemcinin tokenı saklaması gerekiyordu. Cookie oturumu `HttpOnly`, iptal edilebilir ve CSRF korumalı. |
| 7 | **Global oyun kataloğu kaldırıldı**, oyunlar kullanıcıya ait | Eski yapıda giriş yapan her kullanıcı ortak katalogdaki oyunları düzenleyip silebiliyordu (yetkilendirme açığı). Yeni `LibraryGames` tablosu her kaydı sahibine bağlar. |
| 8 | **Sunucu tarafı oturum** (`ITicketStore` → `UserSessions`) | Çıkış, parola değişikliği/sıfırlama ve hesap silmede oturumlar anında geçersiz olur. Kendi oturum sistemi değil, çerez kimlik doğrulamasının resmi uzantı noktası kullanıldı. |
| 9 | **Parola özeti: ASP.NET Core Identity PBKDF2-HMAC-SHA512, 210.000 iterasyon** | Argon2id için ek, bakımı daha az garantili bir kütüphane gerekirdi. Identity'nin yerleşik hasher'ı OWASP'ın PBKDF2 önerisine göre yapılandırıldı. Eski özetler girişte otomatik yeniden hesaplanır. |
| 10 | **DB tabanlı oran sınırlama** | Birden fazla instance'ta tutarlı çalışması için tek bir atomik `MERGE` sayacı kullanılır. Ek altyapı (Redis) gerekmez. Yüksek trafikte veritabanına ek yük getirir (bkz. sınırlamalar). |
| 11 | **Data Protection anahtarları veritabanında** | Tüm instance'lar aynı çerezleri ve sıfırlama tokenlarını çözebilir; yeniden başlatmada oturumlar düşmez. |
| 12 | **Platform ve tür başvuru verisi migration ile ekleniyor** | Üretimde de gerekir. Ada göre idempotent eklenir, demo verisi değildir. |
| 13 | **Tarihler UTC saklanır**, `Europe/Istanbul` ile gösterilir | `DateTime` değerleri okurken `Kind=Utc` işaretlenir. Gösterim saat dilimi `App:DisplayTimeZone` ile ayarlanır. Başlama/bitirme tarihleri `date` (saatsiz). |
| 14 | **Sunucu harici URL indirmez** | Kapaklar yerel SVG'ler ya da izin listesindeki https adresleridir; SSRF yüzeyi yoktur. Tek dosya yükleme JSON içe aktarmadır; dosya diske yazılmaz, akıştan okunur. |
| 15 | **Varsayılan olarak kimlik doğrulama zorunlu** (fallback policy) | Yeni bir sayfa yanlışlıkla herkese açık kalmaz. Herkese açık sayfalar `[AllowAnonymous]` ile işaretlenir. |
| 16 | **RAWG entegrasyonu Business katmanında, isteğe bağlı** | `GameCatalog`, `IHttpClientFactory` ile sabit bir https adresine istek atar (kullanıcı adres veremez, SSRF yüzeyi yoktur). RAWG yanıt modelleri `internal`; sayfalar yalnızca sağlayıcıdan bağımsız `CatalogGame` görür. RAWG verisi doğrudan kaydedilmez, formu doldurur ve normal doğrulamadan geçer. Zaman aşımı, sınırlı önbellek ve basit devre kesici sayesinde RAWG çökse de uygulama çalışır. Anahtar URL'de taşındığı için bu istemcinin istek logları kapalıdır. Önbellek instance başınadır; birden fazla instance'ta her biri kendi önbelleğini tutar (kabul edilen ödünleşim). |
| 17 | **Grafikler sunucuda SVG olarak çizilir** (grafik kütüphanesi yok) | CSP satır içi stile ve dış betiğe izin vermez; çoğu grafik kütüphanesi satır içi stil yazar. İki basit sütun grafiği için Razor + SVG yeterli; her grafiğin tablo görünümü var. |
| 18 | **İçe aktarma yalnızca uygulamanın kendi biçimini kabul eder** (`gamevault-export/v1`) | Dosyadaki kimlik ve sahip alanları okunmaz; her oyun formla aynı doğrulamadan geçer; aynı ad + platform atlanır (tekrar yüklemek güvenli); geçerli oyunlar tek transaction ile yazılır. İstek sınırı yalnızca bu sayfada 4 MB. |
| 19 | **Koleksiyonlar kullanıcıya ait, oyunlarla N—N** | Türler ortak başvuru verisidir; koleksiyonlar kişiseldir. Kullanıcı → koleksiyon ilişkisi cascade değil (`Restrict`): kullanıcı → oyun → bağlantı yoluyla birlikte SQL Server "birden çok cascade yolu" hatası verirdi. Hesap silinirken koleksiyonlar aynı transaction içinde açıkça silinir. |

## İstek akışı

```
ForwardedHeaders (yalnızca tanımlı proxy) → ExceptionHandler/HSTS (Production) → StatusCodePages
→ SecurityHeaders (CSP, no-store) → HttpsRedirection (Production) → Localization (tr-TR)
→ StaticFiles → Routing → Authentication (cookie → UserSessions) → Authorization (fallback: giriş gerekli)
→ Razor Pages / Health checks
```

## Veri modeli

- `AspNetUsers` (Identity) 1—N `LibraryGames` (**cascade delete**)
- `AspNetUsers` 1—N `UserSessions` (**cascade delete**)
- `LibraryGames` N—1 `Platforms` (restrict), N—N `Genres` (`LibraryGameGenres`, oyun silinince cascade)
- `AspNetUsers` 1—N `Collections` (restrict; hesap silmede açıkça silinir), `LibraryGames` N—N `Collections` (`LibraryGameCollections`, oyun ya da koleksiyon silinince bağlantı cascade)
- Kısıtlar: puan 1–10, süre 0–100000, durum 0–5 (CHECK); platform ve tür adları benzersiz; koleksiyon adı kullanıcı başına benzersiz `(UserId, Name)`
- İndeksler: `(UserId, DateAdded)`, `(UserId, Title)`, `(UserId, Status)`, `(UserId, IsFavorite)`
