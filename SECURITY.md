# Güvenlik politikası

## Destek kapsamı

| Sürüm | Güvenlik düzeltmesi |
|---|---|
| `main` dalındaki son sürüm | Evet |
| Daha eski commit'ler | Hayır |
| Eski JWT API (`GameVault.API`; bu depoda yer almaz) | Hayır |

Kapsam: bu depodaki uygulama kodu, migration'lar, Dockerfile ve GitHub Actions iş akışları.
Kapsam dışı: uygulamayı çalıştıran üçüncü taraf altyapı (barındırma, SQL Server, SMTP sağlayıcısı) ve
hizmet dışı bırakma (DoS) testleri.

## Güvenlik açığı bildirme

**Güvenlik açıklarını herkese açık issue, PR ya da tartışma olarak paylaşmayın.**

Deponun **Security → Report a vulnerability** bölümünden GitHub'ın özel güvenlik açığı bildirimi özelliğini kullanın.
Depo sahibi bu özelliği ayarlardan (Settings → Code security → Private vulnerability reporting) etkinleştirmelidir.
Özellik etkin değilse depo sahibinin GitHub profilindeki iletişim kanalını kullanın ve ayrıntı göndermeden önce
güvenli bir kanal isteyin.

Bildirimde şunları paylaşın:
- Etkilenen sayfa/uç nokta ve sürüm (commit)
- Yeniden üretme adımları ve etkisi
- Varsa öneriniz

Lütfen gerçek kullanıcı verisine erişmeyin, veriyi değiştirmeyin ve başkalarının hesaplarını denemeyin. Test için kendi
yerel kurulumunuzu kullanın.

## Yanıt süreci

Bu proje gönüllü olarak sürdürülür; kesin bir yanıt süresi taahhüt edilmez. Bildirim alındığında doğrulanacak,
düzeltme hazırlanacak ve düzeltme yayınlandıktan sonra (isteğe bağlı olarak bildiren kişiye teşekkürle) bir güvenlik
duyurusu yapılacaktır.

## Uygulanan önlemler

Özet: [docs/THREAT_MODEL.md](docs/THREAT_MODEL.md).
