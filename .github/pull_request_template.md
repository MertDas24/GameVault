## Ne değişti?

<!-- Kısa açıklama ve ilgili issue (ör. Closes #12). -->

## Nasıl test edildi?

- [ ] `dotnet format GameVault.slnx --verify-no-changes`
- [ ] `dotnet build GameVault.slnx -c Release` (uyarısız)
- [ ] `dotnet test tests/GameVault.Tests`
- [ ] `dotnet test tests/GameVault.E2E` (arayüz değişikliği varsa)

## Kontrol listesi

- [ ] Yeni sunucu işlemleri kimlik ve sahiplik kontrolü yapıyor (userId yalnızca oturumdan).
- [ ] Dış girdiler sunucuda doğrulanıyor; güncellenebilir alanlar açıkça listelenmiş.
- [ ] Durum değiştiren işlemler POST + antiforgery kullanıyor.
- [ ] Şema değiştiyse migration eklendi ve veri kaybı riski açıklandı.
- [ ] Log, hata mesajı ya da testlerde sır/kişisel veri yok.
- [ ] Belgeler (README, docs/) güncellendi.

## Ekran görüntüleri (arayüz değişikliği varsa)
