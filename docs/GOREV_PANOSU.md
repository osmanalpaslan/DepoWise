# 🗂️ Görev Panosu — Nerede Kaldık? (Çok Görevli Takip)

> **Bu dosya ne işe yarar?** Aynı anda birden fazla bağımsız işi yürütürken, her işin **nerede kaldığını**
> ve **sıradaki adımını** tek yerde tutar. "X işinde nerede kalmıştık?" / "Y'ye devam edelim" dediğinde
> Claude cevabı **buradan** verir. Amaç: işler arasında geçiş yapınca hiçbir şeyin unutulmaması.
>
> **Nasıl güncel kalır?** Claude her anlamlı ilerlemeden sonra ilgili görevin **Durum / Nerede kaldık /
> Sıradaki adım** satırlarını yeniler ve commit'ler. Özet burada; teknik ayrıntı `docs/` içinde.
>
> **İlişki:** `DEVAM.md` = oturum girişi (kısa) · `docs/YARIM_KALAN_ISLER.md` = tüm bekleyen işler havuzu ·
> **bu dosya = aktif paralel işlerin durumu ve devam noktaları.**
>
> Son güncelleme: **2026-07-23**

---

## 🔒 ALTIN KURAL — Babanın gerçek verisine DOKUNMA

Bu geçiş boyunca **her işte** geçerli, istisnasız:

- **Canlı sunucudaki (`depowise-erp.fly.dev`) gerçek firma verisi asla silinmez, taşınmaz, üzerine yazılmaz.**
- PostgreSQL denemeleri **gerçek verinin KOPYASIYLA** ve **ayrı bir veritabanında** yapılır — canlıya dokunmaz.
- Eski SQLite sunucusu, yeni yapı kanıtlanana kadar **canlı ve yedekte kalır**. Baban kesintisiz kullanır.
- Test/simülasyon araçları yalnız **yerel sunucuda** ya da **ayrı test firmasında** çalışır (canlıya karşı çalışmayı reddeder).
- Silme/sıfırlama gibi geri alınamaz işlemler, açık onay olmadan **yapılmaz**.

---

## ▶️ AKTİF GÖREVLER

### Görev A — PostgreSQL geçişi (sunucu + web)
**Amaç:** Sunucu (API) ve web'in veritabanını SQLite'tan **PostgreSQL**'e (çok kullanıcıya ve yedekli
sunucuya uygun, ücretsiz başlanabilen veritabanı) taşımak. **Masaüstü SQLite'ta KALIR** (çevrimdışı
çalışması bundan geliyor). **Yeni repo AÇILMAZ** — mevcut projede, adım adım.

- **Durum:** 🟢 BAŞLADI — Faz 0 (ekran denetimi + parite). Mimari kararı 2026-07-23.
- **Nerede kaldık:** Kullanıcı A'ya başlamak istedi. İki karar eklendi: (1) PostgreSQL web'i baştan
  YAZDIRMAZ (görünüm aynı kalır); web'i beğenmeme ayrı iş → **Görev C** (tasarım, ertelendi, istekler
  toplanacak). (2) Geçiş öncesi **her ekranın masaüstü↔web alan+mantık paritesi** sağlanacak — hem
  tutarlılık hem PostgreSQL tip-hazırlığı. Başlangıç yöntemi: **önce tüm ekran haritası** (kullanıcı seçti).
- **✅ FAZ 0 TAMAMLANDI (2026-07-23) — 7 yüksek öncelikli ekran denetlendi:**
  **Araçlar** (1 bulgu, masaüstü de etkilendi) · **Malzemeler** (tam parite, bulgu yok) ·
  **Personel** (tam parite, bulgu yok) · **Stok Giriş/Çıkış** (2 bulgu) · **Günlük Faaliyet** (2 bulgu) ·
  **Yakıt** (3 bulgu) · **Bakım Takibi** (2 bulgu — en önemlisi: iptal gerekçesi web'de hiç alınmıyordu,
  audit kaydı sabit metinle doluyordu → düzeltildi). Toplam 11 gerçek bulgu, hepsi düzeltildi; 10'u
  yalnız web'i etkiledi. Raporlar `docs/tests/*_Parite_Denetimi.md` (7 dosya).
  Sütun listesinin web'de elle senkron tutulması (ortak bakım riski) = PostgreSQL Faz 3'te web ortak
  katmana bağlanınca kökten çözülecek.
- **Kullanıcı kararı (2026-07-23):** Web deploy VE masaüstü 1.0.88 paketi **BEKLETİLİYOR** — ikisi de
  bilinçli olarak ertelendi (masaüstü: sonraki değişikliklerle birlikte tek pakette çıkacak). Kod git'te
  hazır ve test edilmiş durumda; kullanıcı ne zaman isterse deploy/paketleme tek komutla yapılabilir.
- **⚠️ Deploy notu:** Web'deki tüm düzeltmeler (Stok/Günlük Faaliyet/Yakıt/Bakım) henüz **canlıya
  alınmadı** — yalnız git'e commit edildi, kullanıcı deploy istediğinde yapılacak.
- **✅ FAZ 1 TAMAMLANDI (2026-07-23):** Kullanıcı bulut (Neon) seçti, GitHub ile giriş yaptı, API anahtarı verdi.
  - `neonctl` kuruldu; API anahtarı `.env.test.local`'e (git-ignored) yazıldı.
  - **Yeni proje:** `depowise-dev` (id `nameless-shape-66675056`), **PostgreSQL 17**, **Frankfurt** (aws-eu-central-1),
    org `alpdepo`. Eski proje (`alpdepo`/autumn-morning-75319830) **silinmedi, dokunulmadı** (yan yana durabilir).
  - Bağlantı adresi `.env.test.local` → `DEPOWISE_PG_URL` (Npgsql biçimi, git'e girmez).
  - **Bağlantı DOĞRULANDI:** `PostgresConnectionTests` 2/2 geçti (`SELECT version()` → PostgreSQL 17, `SELECT 1+1`).
  - 🔒 Neon deneme veritabanı BOŞ; babanın canlı verisiyle ilgisi yok (altın kural korunuyor).
  - Not: ücretsiz plan (0,5 GB, 100 saat/ay, 100 proje) geliştirme için fazlasıyla yeter. En düşük ücretli
    "Launch": sabit ücret yok, kullandıkça öde (depolama ~0,35 $/GB-ay, işlem ~0,106 $/saat).
- **FAZ 2 — GERÇEK KAPSAM KEŞFEDİLDİ (2026-07-23):** İş, "52 migration çevir"den ÇOK daha büyük.
  Kod tip düzeyinde SQLite'a kilitli:
  - **84 dosya** doğrudan `SqliteConnection` tipini kullanıyor (`DbConnection` taban tipine geçmeli — ikisini
    de Npgsql + SQLite destekler).
  - **1216 parametre** `$` önekiyle (`AddWithValue("$x", ...)`); Npgsql `$` kabul etmez, `@` ister.
  - SQLite'a özel SQL: `INSERT OR IGNORE/REPLACE` (19), `strftime/datetime` (7) → PostgreSQL karşılığı.
  - SQLite'a özel çalışma-anı: `CreateFunction` (Türkçe arama), `CreateCollation` (Türkçe sıralama),
    PRAGMA'lar (32) → PostgreSQL'de ILIKE/ICU collation ile çözülecek, PRAGMA yok.
  - **İyi haber:** çoğu MEKANİK ve GÜVENLİ — her adımda 569 test masaüstünün (SQLite) çalıştığını kanıtlar,
    baban hiç etkilenmez. ID'ler zaten TEXT/GUID (PostgreSQL'e uygun), AUTOINCREMENT neredeyse yok (1).
- **Önerilen plan (adım adım, her biri test edilir, küçük commit'ler):**
  1. **Temel:** kod `SqliteConnection` yerine `DbConnection` (her veritabanı) desin → 569 test yeşil kalmalı.
  2. **Parametreler:** `$` → `@` (dikkatli, C# `$"..."` interpolasyonuna dokunmadan) → 569 yeşil.
  3. **Lehçe SQL:** `INSERT OR IGNORE` → `ON CONFLICT`, tarih fonksiyonları → 569 yeşil.
  4. **Migration'lar:** 52 şema PostgreSQL'de de çalışsın (tipler) → Neon'da test.
  5. **Çalışma-anı:** Türkçe arama/sıralama PostgreSQL karşılığı; PRAGMA'ları SQLite'a özel bırak.
  6. **Uçtan uca:** sunucuyu Neon'a bağlayıp doğrula.
- **Dürüst not:** Bu, tüm geçişin EN BÜYÜK ve en hassas parçası — tek oturumluk iş değil. Ama her adım
  geri alınabilir + test edilir; istediğin an durulabilir. Masaüstü hiçbir adımda bozulmaz (SQLite'ta kalır).
- **Sıradaki adım:** Kullanıcı onayıyla Adım 1'den (güvenli temel) başla.

**Yol haritası:**
| Faz | Ne yapılır | Durum |
|---|---|---|
| **0** | **Ekran denetimi + parite** — her ekran: masaüstü=web=veritabanı aynı (alan+mantık). PostgreSQL'e model hazırlığı da bu. Ekran ekran, kısa rapor + küçük commit | 🟢 başladı (haritalama) |
| 1 | Ücretsiz PostgreSQL kur, bağlantıyı doğrula | ✅ **TAMAM** — Neon (bulut, ücretsiz, Frankfurt, PG17) projesi `depowise-dev` kuruldu; Npgsql ile bağlantı 2 testle doğrulandı |
| 2 | 52 şema adımını (migration) PostgreSQL diline çevir | ⬜ |
| 3 | Sunucu veri katmanını (okuma/yazma) PostgreSQL'e uyarla | ⬜ |
| 4 | **En zor parça:** eşitleme kodunu iki veritabanına birden (masaüstü SQLite ↔ sunucu PostgreSQL) çalışır hâle getir | ⬜ |
| 5 | Gerçek verinin KOPYASIYLA prova → sağlamsa yeni makineleri yönlendir; eski sunucu yedekte kalır | ⬜ |

**Bilinen risk / not:** En çetin parça Faz 4 (eşitleme). SQLite gevşek, PostgreSQL katı tiplidir
(para yazı, tarih sayı, evet/hayır 0-1 olarak saklanıyor → her biri gözden geçirilecek). Ücretsiz
servis uzak olduğu için ağ gecikmesi olabilir; toplu sorgu gerekebilir. **Faz 0 parite işi migrasyondan
bağımsız çalışır, babanın verisine dokunmaz** (normal uygulama geliştirmesi).

---

### Görev B — Babanın masaüstü uygulaması (paralel geliştirmeler)
**Amaç:** Geçiş sürerken babanın günlük kullandığı uygulamaya istenen geliştirmeleri yapmak.
Bu görev **Görev A'dan bağımsız** ilerler; masaüstü zaten SQLite'ta kaldığı için geçişten etkilenmez.

- **Durum:** 🟢 DEVAM EDİYOR (backlog `docs/YARIM_KALAN_ISLER.md`).
- **Nerede kaldık:** Son biten iş — **düzenleme kilidi** (Malzeme/Araç/Personel/Bakım Tanımı) + eşitleme
  defter düzeltmesi; masaüstü **1.0.87 yayında**.
- **Sıradaki adım (kullanıcı seçecek):** Giriş hız sınırı kararı · Giriş-Çıkış çoklu malzeme ·
  makine bazlı güncelleme yetkisi · Yedek ekranları. Yeni istek geldikçe buraya eklenir.

---

### Görev C — Web görünüm/tasarım iyileştirmeleri (ertelendi)
**Amaç:** Kullanıcı mevcut web tasarımını sevmiyor. Bu **görünüm** işidir (veritabanından bağımsız).
- **Durum:** ⏸️ ERTELENDİ — istekler toplanacak.
- **Nerede kaldık:** Karar: PostgreSQL bunu gerektirmiyor; ayrı iş. Parite için bir ekrana dokununca
  küçük görünüm iyileştirmeleri o an yapılabilir; büyük yeniden tasarım ayrıca planlanır.
- **Sıradaki adım:** Kullanıcı beğenmediği noktaları söyledikçe buraya not düş.

---

## ✅ TAMAMLANAN BÜYÜK KİLOMETRE TAŞLARI (kısa)
- Eşitleme çekirdeği **Z1–Z5** (tek sync kapısı, retry+poison, durum paneli) — canlı.
- Eşitleme **defter delta düzeltmesi** (stok hareketleri artık delta'ya giriyor) — canlı.
- **Düzenleme kilidi** (aynı kayıt iki kişide → ikincisi ezmez, sorar) — canlı.
- **Çok makineli simülasyon** aracı + iş-kuralı hatalarının 500 yerine 400 dönmesi — canlı.
- Masaüstü **1.0.87** yayında.

## Nasıl kullanılır (kullanıcı için)
- "**A'da / PostgreSQL'de nerede kaldık?**" → Görev A satırlarını okurum.
- "**B'ye / babanın uygulamasına dön**" → Görev B'den devam ederim.
- Yeni bağımsız iş verirsen → buraya yeni bir **Görev C/D...** açar, durumunu takip ederim.
