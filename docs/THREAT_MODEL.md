# Tehdit modeli

Kısa, uygulamaya özgü bir tehdit modeli. Kapsam: tek bir ASP.NET Core uygulaması ve tek bir SQL Server veritabanı.
Aşağıdaki önlemlerin varlığı testlerle doğrulanmıştır (bkz. `tests/`), ancak bu, uygulamanın her tehdide karşı
güvenli olduğu anlamına gelmez. Bağımsız bir güvenlik incelemesi yapılmamıştır.

## Korunan varlıklar

| Varlık | Neden önemli |
|---|---|
| Parola özetleri | Sızarsa çevrimdışı kırma saldırısına açılır; kullanıcılar parolalarını başka yerlerde de kullanıyor olabilir. |
| Oturumlar ve parola sıfırlama bağlantıları | Ele geçirilirse hesabın tamamı ele geçirilir. |
| Kullanıcı kütüphanesi (oyunlar, notlar, puanlar) | Kişisel veri; notlar serbest metin olduğundan hassas bilgi içerebilir. |
| E-posta adresleri | Kişisel veri; hesap varlığı bilgisi hedefli saldırılarda kullanılır. |
| Data Protection anahtarları | Çerezleri ve sıfırlama tokenlarını korur; sızarsa oturum sahteciliği mümkün olur. |
| Bağlantı dizesi ve SMTP bilgileri | Veritabanına ve e-posta gönderimine doğrudan erişim sağlar. |

## Güven sınırları

1. **Tarayıcı ↔ uygulama:** Tarayıcıdan gelen her şey (form alanları, sorgu parametreleri, çerezler, başlıklar) güvenilmezdir.
2. **Ters proxy ↔ uygulama:** `X-Forwarded-*` başlıklarına yalnızca `ReverseProxy:KnownProxies/KnownNetworks` ile tanımlı adreslerden gelirse güvenilir.
3. **Uygulama ↔ SQL Server:** Uygulama, veritabanında yalnızca uygulama kullanıcısının yetkileriyle çalışmalıdır (bkz. OPERATIONS.md).
4. **Uygulama ↔ SMTP sağlayıcısı:** Yalnızca parola sıfırlama e-postaları gönderilir.
5. **Tarayıcı ↔ harici kapak sunucuları:** İzin listesindeki alan adlarından görselleri tarayıcı doğrudan yükler; sunucu bu adreslere istek yapmaz.

## Giriş noktaları

- Herkese açık: `/`, `/Privacy`, `/Account/Login`, `/Account/Register`, `/Account/ForgotPassword`, `/Account/ResetPassword`, `/Error`, `/health`, `/health/live`, statik dosyalar.
- Kimlik doğrulama gerektiren: `/Library/**`, `/Stats`, `/Account/Settings`, `/Account/Export`, `/Account/Delete`, `/Account/Logout` (POST).
- Operasyon: migration paketi (`efbundle`), geliştirme komutu `seed-demo` (yalnızca Development).

## Kötüye kullanım senaryoları ve önlemler

| Senaryo | Önlem | Doğrulama |
|---|---|---|
| Parola deneme / kimlik bilgisi doldurma | IP ve hesap bazında DB tabanlı oran sınırı; 5 hatalı denemede 15 dk hesap kilidi; genel hata mesajı | `RateLimitTests`, `AuthTests` |
| Hesap varlığını öğrenme (kayıt/giriş/sıfırlama/yeniden gönderim) | Tek tip hata mesajı; kayıtlı olmayan e-postada da parola hash doğrulaması; kayıt, sıfırlama ve doğrulama bağlantısı isteğine her durumda aynı yanıt (kayıtlı adrese bilgilendirme e-postası gider); oran sınırı bilinmeyen adreslere de aynı biçimde uygulanır; e-posta arka planda gönderilir | `AuthTests`, `PasswordResetTests`, `EmailConfirmationTests`, `RateLimitTests` |
| Başkasının adresiyle kayıt | E-posta doğrulanmadan giriş yapılamaz; doğrulama bağlantısı 24 saat geçerli, tek kullanımlık, yalnızca POST ile tüketilir | `EmailConfirmationTests` |
| Veritabanı dökümüyle çerez/token üretme | Data Protection anahtarları üretimde veritabanı dışında tutulan bir sertifikayla şifrelenir; sertifikasız üretim başlangıcı reddedilir | `DataProtectionTests`, `UnitTests.Production_refuses_to_start_without_a_usable_data_protection_certificate`, CI Docker işi |
| E-posta içeriğinin sızması | Outbox gövdesi Data Protection ile şifreli; gönderim bitince alıcı ve gövde silinir; loglarda alıcı/token yok | `OutboxTests` |
| Başka kullanıcının verisine erişim (IDOR) | Her sorgu oturumdaki userId ile filtrelenir; başkasına ait kayıt 404 döner; userId istemciden alınmaz | `IsolationTests` |
| Mass assignment | Form yalnızca `GameInput` alanlarını kabul eder; varlığa açık alan eşlemesi | `IsolationTests.Client_supplied_owner_fields_are_ignored_on_create` |
| CSRF | Tüm POST'larda antiforgery doğrulaması; SameSite=Lax oturum çerezi; durum değiştiren GET yok | `AuthTests.Post_without_antiforgery_token_is_rejected` |
| XSS | Razor varsayılan HTML kodlaması; HTML kabul edilmez; CSP satır içi betik/stile izin vermez | `LibraryTests.User_content_is_rendered_as_plain_text`, `SecurityHeadersTests` |
| SQL enjeksiyonu | EF Core parametreli sorguları; ham SQL yalnızca parametreli interpolasyonla (`SqlQuery($"...")`) | `SearchTests` |
| Açık yönlendirme | `returnUrl` yalnızca `Url.IsLocalUrl` ise kullanılır | `AuthTests.Login_ignores_external_return_urls` |
| Sıfırlama bağlantısı zehirleme (Host başlığı) | Bağlantı `App:PublicBaseUrl` ile kurulur | `PasswordResetTests.Reset_link_uses_configured_public_base_url...` |
| Çalınmış oturum çerezi | Sunucu tarafı oturum; çıkış/parola değişikliği/sıfırlama/hesap silmede kayıt silinir | `AuthTests.Logout_...`, `AccountTests` |
| SSRF | Sunucu hiçbir kullanıcı URL'sini indirmez; kapak adresleri izin listesiyle sınırlı | `UnitTests.External_urls_*`, `LibraryTests.External_cover_urls_outside_the_allow_list_are_rejected` |
| Büyük istek / kaynak tüketimi | 128 KB istek gövdesi, form değer sınırları, sayfa boyutu 12/24/48, kullanıcı başına 5000 oyun, dışa aktarma oran sınırı | `SecurityHeadersTests.Oversized_request_body_is_rejected`, `SearchTests` |
| Paylaşılan önbellekten veri sızması | Dinamik yanıtlar `Cache-Control: no-store` | `SecurityHeadersTests` |
| Clickjacking | `frame-ancestors 'none'` ve `X-Frame-Options: DENY` | `SecurityHeadersTests` |
| Hata ayrıntısı sızması | Üretimde genel hata sayfası; sağlık kontrolü yalnızca durum döndürür | `SecurityHeadersTests` |

## Bilinen artık riskler

- **Doğrulanmamış hesap bilgisi:** Giriş sayfası, yalnızca doğru parola girildiğinde hesabın doğrulanmamış olduğunu söyler. Bu bilgi yalnızca parolayı bilen birine verilir.
- **Zamanlama farkları:** Kayıt ve giriş yollarında parola özeti maliyeti eşitlenir, ancak veritabanı ve kuyruk işlemleri nedeniyle küçük süre farkları tamamen ortadan kalkmaz. Oran sınırı uygulanır.
- **Sertifika ve veritabanı birlikte ele geçirilirse** Data Protection koruması aşılır. Sertifikayı veritabanı yedeklerinden ayrı saklayın.
- **Oturum ve sıfırlama tokenları için izleme/uyarı yoktur.** Şüpheli giriş bildirimleri uygulanmadı.
- **E-posta teslimi "en az bir kez"dir:** Nadir bir çökme durumunda aynı e-posta (aynı Message-ID ile) iki kez gidebilir.
- **Migration arşivi (`Legacy_*` tabloları)** eski şemadan yükseltilen veritabanlarında eski katalog verisini tutar. Kullanıcının arşiv satırları hesap silmede silinir; ortak katalog elle kaldırılana kadar kalır.
- **İki faktörlü doğrulama yoktur.**
- **Kapak izin listesindeki alan adları** tarayıcıya kullanıcının IP adresini gösterir. Yalnızca güvenilen alan adları eklenmelidir.
