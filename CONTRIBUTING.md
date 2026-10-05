# Katkı rehberi

Katkılarınız için teşekkürler. Başlamadan önce [README](README.tr.md) ve [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) dosyalarını okuyun.

## Geliştirme ortamı

1. .NET SDK 10.0.401 veya daha yeni bir 10.0.x feature band'i ([global.json](global.json)).
2. SQL Server: Windows'ta LocalDB (varsayılan). Diğer sistemlerde `cp .env.example .env` (parolayı değiştirin) ve
   `docker compose up -d sqlserver mailpit`; ardından uygulamayı ve testleri bu sunucuya yönlendirin:
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=127.0.0.1,14333;Database=GameVaultDev;User Id=sa;Password=<.env'deki parola>;TrustServerCertificate=True" --project GameVault.Web
   export GAMEVAULT_TEST_SQLSERVER="Server=127.0.0.1,14333;User Id=sa;Password=<.env'deki parola>;TrustServerCertificate=True"
   ```
3. `dotnet tool restore` (EF Core CLI yerel araç olarak sabitlenmiştir).
4. E-postalar (hesap doğrulama dahil) yerel Mailpit'e gider: http://localhost:8025. Kayıttan sonra giriş için
   doğrulama bağlantısını oradan açın.
5. E2E testleri için bir kez Chromium kurun (bkz. [README.tr.md](README.tr.md) > Testler).

## Çalışma akışı

1. Değişiklik için bir issue açın ya da mevcut bir issue'ya yazın (güvenlik açıkları hariç, bkz. [SECURITY.md](SECURITY.md)).
2. `main` dalından yeni bir dal açın.
3. Küçük ve odaklı commit'ler yapın; commit mesajında ne ve neden değiştiğini anlatın.
4. PR açmadan önce yerelde çalıştırın:
   ```bash
   dotnet format GameVault.slnx --verify-no-changes
   dotnet build GameVault.slnx -c Release
   dotnet test tests/GameVault.Tests
   dotnet test tests/GameVault.E2E
   ```
5. PR şablonundaki kontrol listesini doldurun.

## Kod kuralları

- Biçim [.editorconfig](.editorconfig) dosyasına göre; uyarılar hata sayılır.
- Kullanıcıya görünen metinler Türkçe.
- **Güvenlik:** Her sunucu işleminde kullanıcı kimliği yalnızca oturumdan (`CurrentUserId`) alınır. Sorgular sahibe
  göre filtrelenir; başkasına ait kayıtlar 404 döner. Girdiler varlıklara doğrudan bağlanmaz. Durum değiştiren
  işlemler POST ve antiforgery ile yapılır. Satır içi `<script>`, `style=` ve `on*=` öznitelikleri kullanılmaz (CSP).
- Yeni herkese açık sayfa gerekiyorsa `[AllowAnonymous]` ekleyin ve PR'da gerekçesini yazın.
- Şema değişikliği için migration ekleyin. Veri kaybı riski varsa PR'da açıkça belirtin ve `docs/OPERATIONS.md` dosyasını güncelleyin.
- Yeni davranış için test ekleyin. Yetkilendirme ile ilgili değişikliklerde iki kullanıcılı izolasyon testi zorunludur.
- Sır, gerçek kişisel veri ya da telif hakkı belirsiz görsel eklemeyin.

## Bağımlılıklar

Sürümler [Directory.Packages.props](Directory.Packages.props) dosyasında tutulur. Güncelleme sonrası kilit dosyalarını
yenileyin (`dotnet restore --force-evaluate`) ve değişiklik notlarını inceleyin. Güvenlik bulgularını incelemeden
zorunlu ana sürüm yükseltmesi yapmayın.

## Lisans

Proje [MIT lisansı](LICENSE) ile lisanslanmıştır. Gönderdiğiniz katkılar da aynı lisans altında yayımlanır.
