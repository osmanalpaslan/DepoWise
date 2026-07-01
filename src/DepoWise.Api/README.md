# DepoWise.Api — Web Backend (Option A)

Masaüstüyle **AYNI** `DepoWise.Application` + `DepoWise.Infrastructure` katmanlarını kullanan ASP.NET Core
Web API. İş kuralları/yetki/tenant tek kaynaktan gelir; web ayrı bir kural kopyası TAŞIMAZ.

## Çalıştırma
```
dotnet run --project src/DepoWise.Api
```
Varsayılan veri klasörü: `src/DepoWise.Api/data` (SQLite `depowise-server.db` + `backups/` + `releases/`).
Ortam değişkeni ile taşınır: `DEPOWISE_SERVER_DATA`. İlk açılışta migration çalışır + seed admin
(`admin/admin123`, `superadmin/superadmin`).

## Endpoint'ler (iskele — çalışır durumda)
| Metod | Yol | Açıklama | Yetki |
|------|-----|----------|-------|
| GET | `/health` | Sağlık | açık |
| POST | `/api/auth/login` | Oturum + token (AuthService) | açık |
| POST | `/sync/enroll` | Makine kaydı (pending) | şirket anahtarı |
| POST | `/sync/push` | Değişiklik gönder (SyncServer) | cihaz token |
| GET | `/sync/pull?after=&limit=` | Değişiklik çek | cihaz token |
| GET | `/api/machines` | Makine listesi | oturum (admin) |
| POST | `/api/machines/{id}/approve` | Makineyi onayla/aktifleştir | oturum (admin) |
| POST | `/api/machines/{id}/revoke` | Makineyi pasife al | oturum (admin) |
| GET | `/api/releases/latest` | En güncel sürüm | açık |
| POST | `/api/releases` | Sürüm yayınla (+paket) | oturum (süper admin) |
| GET | `/api/releases/{version}/download` | Paket indir | açık |
| POST | `/api/backups` | Yedek yükle (multipart) | cihaz/oturum token |
| GET | `/api/backups?company&from&to` | Yedek listele | oturum (admin) |
| DELETE | `/api/backups?company&from&to` | Aralık sil | oturum (süper admin) |

## Güvenlik (yapıldı)
- **JWT kimlik doğrulama**: login → 12 saat geçerli imzalı token. Token yalnız kullanıcı+firma taşır; **yetkiler her istekte SUNUCUDA yeniden yüklenir** (token kurcalanamaz). İmza anahtarı `Jwt:Key` / env `DEPOWISE_JWT_KEY` (üretimde gizli ver).
- **Hata → doğru HTTP kodu**: ForbiddenException→403, geçersiz istek→400, JSON `{error}`.
- **CORS** açık (web arayüzü için; üretimde origin kısıtla).

## Üretim öncesi TODO (bilinçli iskele kararları)
- **HTTPS:** üretimde zorunlu (Let's Encrypt — ücretsiz).
- **Sync kritik doğrulama:** `/sync/push` şu an tüm işlemleri kabul eder; kritik entity'ler için sunucu-otoriteli doğrulayıcı eklenecek (SyncPolicy).
- **DB:** SQLite (tek dosya). Çok yük olursa Postgres'e taşınabilir (Infrastructure soyutlaması hazır).
- **Makine kodu / ID stratejisi:** `docs/SENKRON_ID_KOD.md` (iç id GUID; insan-okur kodlara makine öneki).
- **Web arayüz (UI):** bu proje API'dir; yönetim arayüzü (Blazor/SPA) sonraki adım.

Sözleşmeler: `docs/SERVER_BACKUP_CONTRACT.md`, `docs/UPDATE_CONTRACT.md`, `docs/SENKRON_ID_KOD.md`.
