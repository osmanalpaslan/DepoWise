# DepoWise — Ücretsiz Test Ortamı (bulut YOK)

Proje aşamasında; satışta gerçek VPS kiralanacak. Şimdilik güncelleme/kurulum/veri akışını **kendi
bilgisayarında/ağında ÜCRETSİZ** test et. Kart/kayıt gerekmez.

## A) Tek makine (mekaniği gör — en hızlı)
1. **API**: `dotnet run --project src/DepoWise.Api`  → http://localhost:5224
2. **Web konsol**: `dotnet run --project src/DepoWise.Web`  → giriş: superadmin / superadmin
3. **Masaüstü**: normal aç. Sunucuya bağlanması için `app_settings`'e (o firmanın) `update.server_url` =
   `http://localhost:5224` yaz (Setup otomasyonu gelince otomatik olacak; şimdilik elle/geçici).
4. **Güncelleme akışı testi**: Web'de sürüm yayınla (API `/api/releases` — form: version/checksum/file) →
   Masaüstü Ana Ekran "🔄 Güncelle" → "Yeni sürüm mevcut" → "⬇ Güncellemeyi Yükle" → indirme+kurulum %'si →
   checksum/rollback + DB korunur.

## B) LAN (gerçek çok-makine senkron)
- API'yi bir PC'de çalıştır; o PC'nin yerel IP'sini bul (ör. 192.168.1.10).
- Diğer masaüstülerde `update.server_url` = `http://192.168.1.10:5224`.
- İki makineden veri gir → senkron (push/pull) ile karşılıklı akışı gör. Hepsi ücretsiz.

## C) İnternetten erişim (geçici, ücretsiz)
- **Cloudflare Tunnel** (`cloudflared`) veya **ngrok** ile yerel API'yi geçici public URL'e aç:
  `cloudflared tunnel --url http://localhost:5224` → verilen https adresini `update.server_url` yap.
- Kart/sunucu kiralama YOK; sadece test için.

## Satışa geçince
Aynı .NET API + Blazor standart olduğu için **herhangi bir ucuz Linux VPS'e** (ör. Hetzner ~€4/ay) tek seferde
kurulur (SQLite dosyası kalıcı diskte). Lock-in yok. O aşamada gerçek domain + HTTPS (Let's Encrypt) eklenir.

## Not — masaüstü↔API bağlama durumu
- **Güncelleme kontrolü** artık `update.server_url` tanımlıysa **API'den** gelir (`UpdateApiClient`).
- Yedek yükleme (`backup.server_url`) ve senkron (enrollment/push/pull) istemcileri de aynı API'ye
  yönlendirilecek — sıradaki entegrasyon adımı.
