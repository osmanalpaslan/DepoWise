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

- **Durum:** 🟡 PLANLANDI — henüz başlanmadı (mimari kararı verildi 2026-07-23).
- **Nerede kaldık:** Karar aşaması bitti: masaüstü SQLite kalacak, sunucu+web PostgreSQL'e taşınacak,
  ücretsiz servis (Neon/Supabase) ile, kademeli, gerçek veriye dokunmadan.
- **Sıradaki adım:** Kullanıcı "başla" derse → Faz 1'den başla (aşağıdaki yol haritası).

**Yol haritası (5 faz):**
| Faz | Ne yapılır | Durum |
|---|---|---|
| 1 | Ücretsiz PostgreSQL kur (Neon/Supabase), bağlantıyı doğrula | ⬜ |
| 2 | 52 şema adımını (migration) PostgreSQL diline çevir | ⬜ |
| 3 | Sunucu veri katmanını (okuma/yazma) PostgreSQL'e uyarla | ⬜ |
| 4 | **En zor parça:** eşitleme kodunu iki veritabanına birden (masaüstü SQLite ↔ sunucu PostgreSQL) çalışır hâle getir | ⬜ |
| 5 | Gerçek verinin KOPYASIYLA prova → sağlamsa yeni makineleri yönlendir; eski sunucu yedekte kalır | ⬜ |

**Bilinen risk / not:** En çetin parça Faz 4 (eşitleme). SQLite gevşek, PostgreSQL katı tiplidir
(para yazı, tarih sayı, evet/hayır 0-1 olarak saklanıyor → her biri gözden geçirilecek). Ücretsiz
servis uzak olduğu için ağ gecikmesi olabilir; toplu sorgu gerekebilir.

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
