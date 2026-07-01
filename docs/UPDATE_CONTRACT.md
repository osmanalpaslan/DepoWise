# Otomatik Güncelleme — Yapı ve Backend Sözleşmesi

Kullanıcı modeli:
1. Güncelleme paketleri **web'den yüklenir** (Süper Admin → yayın kaydı: sürüm + checksum + indirme URL).
2. Masaüstü, yeni sürümü **otomatik algılar** (açılışta + "🔄 Güncelle") → Ana Ekran'da uyarı + buton.
3. **"⬇ Güncellemeyi Yükle"** → paket sunucudan indirilir, checksum doğrulanır, kurulur.
4. **DB'ye asla dokunulmaz** — DB `%AppData%\DepoWise\Data` altında; güncelleme yalnız uygulama dizinini değiştirir.
5. **İndirme + kurulum yüzdesi Ana Ekran'da** (0–60 indirme, 60–100 kurulum).

## İstemci tarafı (bitti)
- `ReleaseService.Latest()` → en yüksek SemVer yayını (`UpdatePackage` + `DownloadUrl`).
- `UpdateService.Check()` (sürüm karşılaştır) / `ApplyUpdate()` (checksum + yedek + **rollback** + yüzde).
- `UpdateDownloadService.DownloadAsync(url, progress)` — HTTP indirme, yüzdeli.
- Dashboard: otomatik `CheckUpdate` + `InstallUpdate` (indir→kur→yeniden başlat uyarısı), ProgressBar + %.
- Şema: `app_releases.download_url` (Migration018).

## Backend (Option A — paylaşımlı .NET API; web'e geçince yazılacak)
Yayın (Süper Admin, web ekranı):
```
POST /api/releases   { version, checksumSha256, sizeBytes, minSupportedVersion, releaseNotes, signed, downloadUrl }
```
→ `ReleaseService.Publish` çağrısına karşılık gelir (app_releases'e yazar; masaüstüne sync ile iner).

Paket dosyası indirme (masaüstü):
```
GET {downloadUrl}   → paket ikili içeriği (checksumSha256 ile eşleşmeli)
```

## Kalan (paketleme formatı ile netleşecek)
`ApplyUpdate` şu an paketi stage eder + `current.txt` sürümünü yükseltir; **çalışan ikilinin fiziksel
değişimi + yeniden başlatma** (Windows'ta çalışan exe kendini yazamaz) için küçük bir dış updater/relaunch
adımı, paket formatı (zip/manifest) kesinleşince eklenecek. Checksum/rollback/DB-koruma mekanizması hazır.
