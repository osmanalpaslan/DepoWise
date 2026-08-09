# Güvenlik Olayı — Neon API Anahtarı Rotasyonu (TAMAMLANDI)

- **Olay tarihi:** 2026-08-09
- **Durum:** ✅ **KAPANDI** — sızan anahtar iptal edildi, yenisi oluşturuldu ve doğrulandı
- **Bu belge hiçbir parola, anahtar veya token DEĞERİ içermez** — yalnız tür, kimlik (id), dosya ve commit.
  Bu kural kalıcıdır: depo public'tir.

---

## 1. Olay

`.claude/settings.local.json.bak` adlı yerel ayar yedeği, `git add -A` ile **public** GitHub deposuna
commit edildi. Dosya içinde **açık metin bir Neon API anahtarı** vardı.

| | |
|---|---|
| Depo | `github.com/osmanalpaslan/DepoWise` — **PUBLIC** |
| Sızan dosya | `.claude/settings.local.json.bak` |
| Eklendiği commit | `12c54a5` |
| Çıkarıldığı commit | `ecbf762` |
| Açıkta kalma süresi | ~1 dakika (push'lar arası), ancak **geçmişte kalıcı** |
| Sızan credential | Neon API anahtarı — id **3215282**, ad `depowise-cli` |

Anahtarın **sızan kopyası ile o an aktif kullanılan kopyası aynıydı** (sha256 parmak izi karşılaştırmasıyla
doğrulandı; değer hiçbir yerde gösterilmedi).

---

## 2. Yapılanlar

| # | İşlem | Sonuç |
|---|---|---|
| 1 | Sızan anahtarın kimliği belirlendi (parmak izi eşleştirmesi) | id **3215282**, ad `depowise-cli` — hesapta **tek** anahtardı, belirsizlik yok |
| 2 | Production'da kullanılıp kullanılmadığı kontrol edildi | `depowise-erp` ve `depowise-web` secret'larında **NEON_API_KEY yok** → iptal production'ı etkilemez |
| 3 | **Yeni anahtar** oluşturuldu | id **3254340**, ad `depowise-cli-2026-08-09` |
| 4 | Yeni anahtar doğrulandı | `GET /projects/<proje>/branches` → **200** · `neonctl branches list` → çalışıyor |
| 5 | `.env.test.local` güncellendi (yalnız `NEON_API_KEY` satırı) | dosya `.gitignore` kapsamında (`.env.*`) |
| 6 | **Sızan anahtar İPTAL EDİLDİ** | `DELETE /api_keys/3215282` → **HTTP 200** |
| 7 | Eski anahtarla erişim testi | `GET /api_keys` → **401** · `GET /projects/…/branches` → **401** ✅ |
| 8 | Yeni anahtarla erişim testi | **200**, 2 dal görülüyor (`main`, `pre-ms1a`) ✅ |

### Kapsam daraltıldı
Eski anahtar **hesap düzeyindeydi** (tüm organizasyonlar/projeler). Yeni anahtar
**organizasyon `alpdepo` + yalnız `nameless-shape-66675056` projesi** kapsamındadır.
Kanıt: yeni anahtarla `GET /organizations/<org>/projects` → **404** (org geneline erişemiyor),
`GET /projects/<proje>/branches` → **200** (yalnız kendi projesi). Yani yetki gerçekten daraldı.

### Ara adımda oluşan artık anahtar
İlk denemede doğrulama yanlış uç ile yapıldığı için değeri kaybolmuş, hiç kullanılmamış bir anahtar
oluştu (id **3254337**). Bu anahtar **silindi** (HTTP 200). Başka hiçbir anahtara dokunulmadı.

---

## 3. Depo geneli secret taraması

Tüm git geçmişindeki her metin nesnesi iki bağımsız tarama ile tarandı
(desenler: `napi_`, `postgres://`, `Password=`, `FlyV1`, `ghp_`, `AKIA`, özel anahtar blokları,
`secret/api_key/access_token` atamaları).

| Bulgu | Değerlendirme |
|---|---|
| **Neon API anahtarı** — `.claude/settings.local.json.bak` (`12c54a5`, `ecbf762`) | ✅ **GERÇEK** — iptal edildi (bu belge) |
| `postgres://` — `PostgresConnectionFactory.cs`, `tools/DepoWise.Migrate/Program.cs` | ❌ yanlış alarm: `$"postgres://{b.Host}/{b.Database}"` — kod içi birleştirme, **parola yok** |
| `postgres://` — `PostgresTestGuardTests.cs` | ❌ yanlış alarm: `postgres://sahte/deneme` (test sahte değeri) |
| `Password=` — Program.cs, AuthService.cs, ApiClient.cs, Users.razor, Personnel.razor, Trash.razor vb. | ❌ yanlış alarm: C# özellik/değişken adları (`Password = null`, `HasPassword =>`, `MustChangePassword`) |
| `.env.*` dosyaları | ✅ geçmişte **hiç** yok (yalnız değersiz `.env.example`) |
| Fly API token | ✅ depoda yok (`~/.fly/config.yml` — depo dışında) |

**Sonuç: sızan tek gerçek credential Neon API anahtarıydı ve iptal edildi.**

### ⚠️ Bildirilen, otomatik işlem YAPILMAYAN bulgu
Aynı `.bak` dosyasında bir **yerel geliştirme veritabanı tohum parolası** de açık metin bulunuyor
(`DEPOWISE_SEED_SUPERADMIN_PASSWORD` için bu oturumda konulmuş geçici değer).
- **Production değildir:** Fly'daki `DEPOWISE_SEED_SUPERADMIN_PASSWORD` secret'ı **farklıdır** ve
  değiştirilmemiştir.
- Yalnız bu geliştirme makinesindeki yerel test veritabanını etkiler; sunucuya ulaşmaz.
- Kullanıcı talimatı gereği **otomatik olarak değiştirilmedi** — karar kullanıcıya bırakıldı.

---

## 4. Git geçmişi

- Sızan anahtar **hâlâ public geçmişte** duruyor: `.claude/settings.local.json.bak`,
  commit **`12c54a5`** (eklendi) ve **`ecbf762`** (kaldırıldı; blob geçmişte erişilebilir).
- **Force-push YAPILMADI**, geçmiş yeniden yazılmadı, branch yapısı korundu.
- **Risk durumu:** anahtar artık **geçersiz** olduğu için geçmişteki kopyanın **credential riski kapandı**.
  Geriye kalan yalnız "geçmişte geçersiz bir anahtar dizesi görünüyor" durumudur.
- **Ayrı iş olarak bırakıldı:** geçmiş temizliği (`git filter-repo` / BFG + force-push) — depo geçmişini
  yeniden yazacağı ve tüm klonları etkileyeceği için ayrı ve onaylı bir iş olmalıdır.

## 5. `.claude/settings.local.json.bak` durumu

| Kontrol | Sonuç |
|---|---|
| Git takibinde mi? | **Hayır** ✅ (`git ls-files` boş) |
| Çalışma ağacında kopyası var mı? | **Hayır** ✅ |
| `.gitignore` kapsamında mı? | **Evet** ✅ (`.gitignore:36` → `*.bak`) |
| `.env.test.local` takipte mi? | **Hayır** ✅ (`.gitignore:3` → `.env.*`) |
| Public geçmişte duruyor mu? | **Evet** — ancak içindeki anahtar artık geçersiz |

---

## 6. M-S1a durumu (dokunulmadı)

- Migration 062 **geri alınmadı**.
- Canlı veritabanındaki M-S1a değişiklikleri **değiştirilmedi**.
- Bu güvenlik işi nedeniyle **hiçbir yeniden deploy yapılmadı** (gerekmedi: production Neon API anahtarı
  kullanmıyor; yalnız `DEPOWISE_PG_URL` kullanıyor ve ona **dokunulmadı**).
- Geri dönüş noktası **`pre-ms1a` dalı** duruyor (yeni anahtarla doğrulandı).

---

## 7. Manuel yapılması gerekenler

Şu an için **yok**. Değerlendirmen gereken iki isteğe bağlı konu:
1. Git geçmişi temizliği (force-push gerektirir) — ayrı iş.
2. `.bak` içindeki yerel geliştirme tohum parolasının değiştirilmesi — yalnız bu makineyi etkiler.
