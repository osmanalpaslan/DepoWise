# FAZ J — Canlıya geçiş: güvenlik sertleştirme · API sürümleme · yük testi

> **Durum:** ✅ TAMAMLANDI · **2026-09-05** · ADR-218

---

## 1. Güvenlik sertleştirme

### Ölçüm: neyin zaten olduğu
Sertleştirmenin büyük kısmı **zaten yerindeydi** ve bu iş onları tekrar etmedi:

| Koruma | Durum |
|---|---|
| Kimlik doğrulama | JWT Bearer, tüm iş uçlarında `RequireAuthorization()` |
| Yetki | `AccessControl` deny-by-default; kapı **servis katmanında** (masaüstünün çevrimdışı yolu da korunur) |
| Tenant izolasyonu | `company_id` daima oturumdan; çocuk tablolarda ebeveyn üzerinden kapsam |
| Hız sınırı | Giriş (30/5dk) · genel (120/dk) · makine (30/5dk) |
| Paket bütünlüğü | SHA-256 **fail-closed** (ADR-200) |
| Sır sızıntısı | `LogRedactor`; şifre/jeton/bağlantı dizesi loglanmaz |
| HTTPS | Fly zorluyor; web'de `UseHsts` |

### Bulunan boşluk: tarayıcı güvenlik başlıkları
Web ve API **hiçbir güvenlik başlığı göndermiyordu**. Eklendi:

| Başlık | Neden |
|---|---|
| `X-Content-Type-Options: nosniff` | Tarayıcı içerik tipini **tahmin etmesin** — yanlış tiple servis edilen bir dosya betik sanılıp çalıştırılabilir |
| `X-Frame-Options: DENY` | Uygulama başka sitenin çerçevesine konulamaz (tıklama hırsızlığı). Alpnex hiçbir yerde iframe içinde kullanılmıyor |
| `Referrer-Policy` | Dış bağlantıya giderken tam adres (içinde kayıt kimliği olabilir) karşı tarafa **sızmasın** |
| `X-Permitted-Cross-Domain-Policies: none` | Eski eklenti tabanlı çapraz alan erişimi kapalı |

### ⚠️ CSP bilinçli olarak EKLENMEDİ
`Content-Security-Policy`, Blazor Server + MudBlazor'ın satır içi betik/stil kullanımı yüzünden
dikkatli ayar ister. Yanlış bir politika arayüzü **sessizce bozar**: ekran açılır, düğmeler çalışmaz.
Kullanıcının babası başka bir şehirde ve tek başına çalışıyor — ölçmeden eklenen bir CSP,
koruduğundan fazlasını kırardı. Gerçek tarayıcıda doğrulanarak, ayrı bir iş olarak yapılmalıdır.

## 2. API sürümleme kararı

**Karar: sürüm öneki YOK — mevcut durum korunuyor.** (`CLAUDE.md` §4'te zaten kayıtlı: uçlar
`/api/...` altındadır, `/api/v1` kullanılmaz.)

Gerekçe bu turda yeniden ölçüldü ve hâlâ geçerli:

- İstemci ve sunucu **birlikte yayınlanıyor**; masaüstü kendi migration kataloğunu açılışta uyguluyor.
  Tek bir kurulum evreni var — "eski istemci + yeni API" uzun süre birlikte yaşamıyor.
- Uyumsuzluk riski `GNC-02` ile **görünür** hâle getirildi (ADR-215): desteklenmeyen sürüm kullanıcıya
  söyleniyor. Sürümleme bunun yerine geçecek bir sorun çözmüyor.
- Sürüm öneki eklemek 200'den fazla ucu ve iki istemciyi birden değiştirmek demek; **ölçülmüş bir
  fayda olmadan** bu kadar geniş bir dokunuş, protokolün "en küçük doğru değişiklik" ilkesine aykırı.

Bu satır artık "yapılacak" değil, **verilmiş karar**dır. Gerçek çok-sürümlü ihtiyaç doğarsa (harici
entegrasyon, üçüncü taraf istemci) yeniden açılır.

## 3. Yük testi

`scripts/loadtest.mjs` **zaten mevcut** (kurulum gerektirmez, yalnız Node): belirtilen URL'e N
eşzamanlı istekle yüklenir; **req/s · p50/p95/max gecikme · hata oranı** raporlar.

**Canlıya yük testi UYGULANMADI — bilinçli.** Üretimde babanın gerçek verisi var ve tek makine
çalışıyor; yapay yük, gerçek kullanıcının işini yavaşlatır ya da kesintiye uğratır. Yük testi izole
bir ortamda anlamlıdır; araç hazır, ihtiyaç doğduğunda (kullanıcı sayısı artınca) çalıştırılır.

Bunun yerine bu turda **gerçek darboğaz ölçülüp giderildi**: `stock_movements` ve
`vehicle_maintenances` tablolarında liste sorgusunu destekleyen indeks yoktu (FAZ I / Migration091).
Bu, uydurma bir yük senaryosundan daha somut bir kazançtır.
