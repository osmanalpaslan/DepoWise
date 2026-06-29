# Sunucu Yedek (Bulut) — Backend Sözleşmesi

Masaüstü uygulaması her yerel yedeği (günlük otomatik + manuel) **bulut yedek sunucusuna** yükler.
Sunucu yedekleri **HİÇBİR ZAMAN silmez**; tüm masaüstü makinelerin tüm günlük yedeklerini sonsuza dek saklar.
Yerelde (her makinede) 30 günlük rotasyon devam eder; sunucu tam arşivdir.

> Durum: **İstemci tarafı hazır** (`BackupUploadService`, Ayarlar'da URL+token, Yedek Yönetimi'nde "Sunucuya Yükle" + otomatik).
> **Backend (bu uç) ayrıca kurulup yayınlanmalıdır.** Aşağıdaki tek endpoint yeterlidir.

## Endpoint

```
POST {BackupServerUrl}
Authorization: Bearer {token}            # opsiyonel ama önerilir
Content-Type: multipart/form-data
```

### Form alanları
| Alan | Tip | Açıklama |
|------|-----|----------|
| `company` | text | Firma (tenant) id |
| `machine` | text | Masaüstü makine adı (Environment.MachineName) — saklama klasörü/etiketi |
| `filename` | text | Orijinal dosya adı (örn. `depowise_yedek_2026-06-29.db`) |
| `file` | binary | SQLite yedek dosyası (application/octet-stream) |

### Yanıt
- **2xx** → başarı (istemci "Yüklendi" gösterir).
- **4xx/5xx** → gövdedeki metin kullanıcıya hata olarak gösterilir.

## Listeleme — Süper Admin ekranı (iki tarih arası)

```
GET {BackupServerUrl}?company={c}&from=YYYY-MM-DD&to=YYYY-MM-DD
Authorization: Bearer {token}
```
Yanıt (200) — JSON dizi:
```json
[
  { "machine": "DESKTOP-A1", "filename": "depowise_yedek_2026-06-29.db", "date": "2026-06-29T03:00:00Z", "sizeBytes": 95420416 }
]
```

## Toplu Silme — Süper Admin (iki tarih arası, geri alınamaz)

```
DELETE {BackupServerUrl}?company={c}&from=YYYY-MM-DD&to=YYYY-MM-DD
Authorization: Bearer {token}
```
Yanıt (200): `{ "deleted": 12 }`. Yalnız `from`–`to` (dahil) aralığındaki kayıtları siler.
**Bu, otomatik retention DEĞİLDİR** — yalnız Süper Admin'in bu ekrandan tetiklediği kasıtlı temizliktir.

## Sunucu tarafı kuralları (zorunlu)
1. **Silme YOK / üzerine yazma YOK.** Aynı `machine`+`filename` tekrar gelirse yeni sürüm olarak sakla
   (örn. sona timestamp ekle). Hiçbir retention/temizlik çalıştırma.
2. Dosyaları **firma + makine** bazında ayır: `/{company}/{machine}/{filename}`.
3. Token doğrula (Bearer). Geçersizse 401/403.
4. Disk/nesne depolama tercih edilir (S3/Blob veya dosya sistemi); kapasite sınırsız kabul edilir.

## İstemci yapılandırması
Ayarlar → **Sunucu Yedek (Bulut)**: `Sunucu Adresi (URL)` + `Erişim Anahtarı (Bearer Token)`.
Tanımlandığında her yerel yedek otomatik yüklenir; ayrıca Yedek Yönetimi'nde "Sunucuya Yükle" ile elle gönderilir.
İlgili anahtarlar: `backup.server_url`, `backup.server_token` (firma bazlı `app_settings`).
