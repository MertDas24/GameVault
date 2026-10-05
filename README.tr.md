<div align="center">

# 🎮 GameVault

**Kişisel oyun kütüphanesi: ne oynadığını, neyi bitirdiğini ve sırada ne olduğunu takip et.**

![.NET 10](https://img.shields.io/badge/.NET_10-512BD4?logo=dotnet&logoColor=white)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-Razor_Pages-512BD4?logo=dotnet&logoColor=white)
![EF Core](https://img.shields.io/badge/EF_Core_10-512BD4?logo=dotnet&logoColor=white)
![SQL Server](https://img.shields.io/badge/SQL_Server-CC2927?logo=microsoftsqlserver&logoColor=white)
![ASP.NET Core Identity](https://img.shields.io/badge/Auth-ASP.NET_Core_Identity-0A7BBB)
![Docker](https://img.shields.io/badge/Docker-2496ED?logo=docker&logoColor=white)
![GitHub Actions](https://img.shields.io/badge/GitHub_Actions-2088FF?logo=githubactions&logoColor=white)
![Tests](https://img.shields.io/badge/tests-xUnit_%2B_Playwright-25A162)
![License: MIT](https://img.shields.io/badge/license-MIT-yellow)

[English README](README.md)

</div>

GameVault, her kullanıcının yalnızca kendisinin görebildiği bir oyun kütüphanesi tuttuğu, sunucu tarafında render
edilen bir ASP.NET Core web uygulamasıdır. Kullanıcı kapak, platform ve türleriyle oyun ekler; durum, süre ve puan
takibi yapar; koleksiyonunu arar ve filtreler, istatistiklerini görür, verisinin tamamını dışa aktarabilir ya da
silebilir. Arayüz Türkçe ve koyu temalıdır.

> Bu bir **portfolyo projesidir**. Yerelde ve CI'da çalışır; hiçbir ortama dağıtılmamıştır ve gerçek kullanıcı
> verisi toplamaz.

## Öne çıkanlar

- **Kullanıcı bazlı veri izolasyonu.** Her sorgu giriş yapan kullanıcıyla sınırlıdır. Entegrasyon testleri, bir
  kullanıcının oyunun kimliğini bilse bile başka bir kullanıcının oyunlarını okuyamadığını, düzenleyemediğini,
  silemediğini ve dışa aktaramadığını, istemcinin gönderdiği sahiplik alanlarının da yok sayıldığını doğrular.
- **İptal edilebilir sunucu tarafı oturumlar.** Kimlik doğrulama çerezi yalnızca SQL Server'daki bir oturum
  kaydına referans tutar (`ITicketStore`). Çıkış, parola değişikliği, parola sıfırlama ya da hesap silme oturumu
  anında geçersiz kılar.
- **Varsayılan olarak güvenli.** Açıkça muaf tutulmayan her sayfa giriş ister. Uygulama katı bir Content Security
  Policy gönderir, her formda antiforgery doğrulaması çalışır ve giriş yanıtları hesabın var olup olmadığını belli
  etmez.
- **Birden fazla instance'ta çalışan oran sınırlama.** Tüm instance'lar tek bir atomik SQL `MERGE` sayacını
  paylaşır, Redis gerekmez.
- **Güvenilir e-posta.** Doğrulama ve parola sıfırlama e-postaları veritabanındaki bir kuyruktan (outbox)
  gönderilir ve hata durumunda artan aralıklarla yeniden denenir.
- **Gerçek testler.** 140'tan fazla entegrasyon testi `WebApplicationFactory` üzerinden gerçek bir SQL Server'a karşı
  çalışır. Playwright E2E testleri masaüstü ve mobil akışları kapsar, axe-core da erişilebilirliği (WCAG 2.1 A/AA)
  denetler.
- **Sıkı derleme.** Uyarılar hata sayılır, analizörler `latest-recommended` seviyesinde çalışır, paket sürümleri
  merkezi olarak yönetilip kilitlenir ve CI migration'ların modelle uyumlu olduğunu kontrol eder.

## Özellikler

| Alan | İçerik |
|---|---|
| Kütüphane | Kapak odaklı kartlar; ada göre arama; durum, platform, tür ve favori filtreleri; 7 sıralama; sayfalama |
| Oyunlar | Ad, kapak (8 hazır kapak ya da izinli https adresi), açıklama, platform, en fazla 5 tür, durum, süre, 1–10 puan, notlar, başlama ve bitirme tarihleri, favori |
| İstatistikler | Toplam oyun ve süre, ortalama puan, tamamlama oranı, durum dağılımı, en çok kullanılan platform ve türler, en çok oynananlar |
| Hesap | E-posta doğrulamalı kayıt, parola sıfırlama, görünen ad, parola değiştirme, diğer oturumları kapatma, JSON dışa aktarma, hesap silme |
| Arayüz | Masaüstünde yan menü, mobilde alt gezinme çubuğu; klavyeyle kullanım; `aria-invalid` ile form hataları; azaltılmış hareket desteği |

Durumlar: Başlamadım · Oynuyorum · Bitirdim · Ara verdim · Bıraktım · İstek listesi.

## Teknoloji

| Katman | Seçim |
|---|---|
| Çalışma zamanı | .NET 10 (LTS), ASP.NET Core Razor Pages (sunucu tarafı render, JavaScript yalnızca iyileştirme için) |
| Veri | SQL Server, Entity Framework Core 10, sürümlü migration'lar |
| Kimlik doğrulama | ASP.NET Core Identity, sunucu tarafı oturum saklayan cookie kimlik doğrulaması |
| E-posta | MailKit (SMTP), yerelde Mailpit |
| Testler | xUnit v3, WebApplicationFactory, AngleSharp, Playwright (Chromium), axe-core |
| Araçlar | Docker, Docker Compose, GitHub Actions, CodeQL, Dependabot, gitleaks |
| Stil | Elle yazılmış, token tabanlı CSS. Ön yüz derleme adımı yoktur. |

## Mimari

Katmanlı bir çözüme sahip tek bir ASP.NET Core uygulaması:

```
GameVault.Entities    → EF Core varlıkları (kullanıcılar, kütüphane oyunları, türler, platformlar, oturumlar…)
GameVault.DataAccess  → DbContext ve migration'lar
GameVault.Models      → Doğrulamalı girdi modelleri, sorgu normalleştirme, DTO'lar
GameVault.Business    → Kütüphane, istatistik, hesap, oran sınırlama ve e-posta servisleri
GameVault.Web         → Razor Pages arayüzü, kimlik doğrulama, güvenlik başlıkları, başlangıç doğrulaması
tests/GameVault.Tests → Birim ve entegrasyon testleri (gerçek SQL Server)
tests/GameVault.E2E   → Playwright tarayıcı testleri
```

Tasarım kararları ve gerekçeleri: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Yerelde çalıştırma

### Seçenek A: Windows ve LocalDB

Gereksinimler: .NET SDK 10.0.401 veya daha yenisi, SQL Server LocalDB (Visual Studio ile gelir).

```powershell
dotnet tool restore
dotnet run --project GameVault.Web --launch-profile http
```

http://localhost:5226 adresini açın. Development ortamında `GameVaultDev` veritabanı otomatik oluşturulur ve
migration'lar uygulanır.

Yeni hesaplar giriş yapmadan önce e-postalarını doğrulamalıdır. Yerel e-postaları http://localhost:8025 adresinde
görmek için [Mailpit](https://mailpit.axllent.org/) çalıştırın. Development ortamı yerel olmayan bir SMTP sunucusuyla
başlamayı reddeder; gerçek bir e-posta asla gönderilmez.

**İsteğe bağlı demo verisi** (kurgusal, yalnızca Development):

```powershell
dotnet user-secrets set "Seed:DemoPassword" "<en az 10 karakter>" --project GameVault.Web
dotnet run --project GameVault.Web -- seed-demo
```

Bu komut `demo@gamevault.test` hesabını ve 9 kurgusal oyunu oluşturur.

### Seçenek B: Docker Compose (SQL Server, Mailpit ve uygulama)

```bash
cp .env.example .env        # kendi MSSQL_SA_PASSWORD değerinizi girin
docker compose up --build
```

Uygulama http://localhost:8080, Mailpit http://localhost:8025 adresinde çalışır.

## Testler

```powershell
dotnet build GameVault.slnx -c Release          # derleme + analizörler (uyarılar hata sayılır)
dotnet test tests/GameVault.Tests               # birim + entegrasyon (LocalDB ya da GAMEVAULT_TEST_SQLSERVER gerekir)
powershell -ExecutionPolicy Bypass -File tests/GameVault.E2E/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test tests/GameVault.E2E                 # tarayıcı testleri
```

Her entegrasyon test sınıfı geçici bir `GameVaultTest_*` veritabanı oluşturur ve iş bitince siler. Test altyapısı
bu önek dışındaki bir veritabanına karşı çalışmayı reddeder.

## Belgeler

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): mimari ve tasarım kararları
- [docs/THREAT_MODEL.md](docs/THREAT_MODEL.md): tehdit modeli
- [docs/OPERATIONS.md](docs/OPERATIONS.md): yapılandırma başvurusu ve üretim kontrol listesi
- [CONTRIBUTING.md](CONTRIBUTING.md) · [SECURITY.md](SECURITY.md)

## Lisans

[MIT](LICENSE). Hazır kapak görselleri [tools/generate-covers.js](tools/generate-covers.js) ile üretilmiş özgün
SVG'lerdir.
