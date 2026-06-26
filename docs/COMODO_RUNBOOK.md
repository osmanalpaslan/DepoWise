# DepoWise COMODO Güvenli Geliştirme Runbook

## Sorunun nedeni
Geliştirme makinesindeki Auto-Containment imzasız proje apphost EXE veya BAT dosyasını sanal alanda çalıştırabilir. Bu durumda uygulama sanal bir DB'ye yazar; gerçek DB boş görünür.

## Zorunlu çalışma yöntemi
- Doğrudan proje EXE/BAT çalıştırma.
- Normal güvenilir terminalde `dotnet build` kullan.
- Uygulamayı `dotnet run --project src/DepoWise.Desktop` veya `dotnet <tam-yol>/DepoWise.Desktop.dll` ile aç.
- Debug derlemede `UseAppHost=false`.

## Gerçek veri yolu
- DB mutlak `%LOCALAPPDATA%\DepoWise\Data\<environment>\depowise.db` yolunda olmalı.
- Relative path veya çalışma klasörüne bağlı DB kullanılmamalı.
- Başlangıç logu: process path/host, DB tam yolu, SQLite journal_mode ve health test sonucu.

## Her kritik testte kanıt
1. Host process `dotnet`.
2. DB tam yolu gerçek LocalAppData altında.
3. `PRAGMA journal_mode` = WAL.
4. Bir test kaydı oluşturulup okunabiliyor.
5. Uygulama kapatılıp yeniden açıldığında kayıt aynı DB'de mevcut.
6. Sanal/alternatif DB dosyası bulunmadığı veya kullanılmadığı kayıt altına alınmış.

## Release ayrımı
Bu kurallar geliştirme makinesi içindir. Personel kurulum paketleri ayrı release pipeline ile üretilir. Code-signing yayın öncesinde değerlendirilir.
