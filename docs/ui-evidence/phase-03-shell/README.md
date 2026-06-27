# Faz 3 — Kabuk ekran görüntüleri (kullanıcı tarafından alınacak)

**Neden otomatik alınamadı:** COMODO Auto-Containment geliştirme makinesinde imzasız proje EXE'sini izole eder; asistan uygulamayı çalıştırıp otomatik ekran görüntüsü üretemez (CLAUDE.md §0).

## Nasıl alınır (dotnet host — güvenli)
1. Masaüstündeki **"DepoWise (Gercek DB)"** kısayoluyla uygulamayı aç (veya:
   `"C:\Program Files\dotnet\dotnet.exe" "D:\DepoWise\src\DepoWise.Desktop\bin\Debug\net8.0\DepoWise.Desktop.dll"`).
2. Pencereyi sırasıyla **1366×768** ve **1920×1080** boyutuna getir.
3. Her boyutta ekran görüntüsü al ve bu klasöre kaydet:
   - `shell-1366x768.png`
   - `shell-1920x1080.png`
4. İstenirse menü daraltılmış (panel kapalı) hali için: `shell-collapsed.png`.

## Doğrulanacak davranışlar (manuel smoke)
- İkon rayı (sol ~56px) + açıklamalı panel (~210px) + üst bar (~60px) görünür.
- Menü gruplarının chevron'u açılıp kapanıyor; seçili modülde mavi vurgu, alt menüde koyu seçili satır.
- Üst bardaki ☰ ile panel daralıp genişliyor; içerik kaybolmuyor.
- "Eşitle" butonu ve kullanıcı avatarı görünür.
- Pencere sürükleme/minimize/maximize bozulmamış (native başlık çubuğu korundu).
