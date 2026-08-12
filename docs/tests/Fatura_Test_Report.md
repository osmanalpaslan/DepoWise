# Fatura Ekranı — Test Raporu (G4-2)

**Tarih:** 2026-08-12 · **Kapsam:** yalnız yeni eklenen Fatura ekranı ve servisi (CLAUDE.md §7.1)
**Şema sürümü:** 67 (Migration067_Invoices)

---

## 1. Özet

| | |
|---|---|
| Yeni test | **38** (InvoiceTests 33 + InvoiceSyncTests 5) |
| Geçen | 38 |
| Başarısız | 0 |
| Tüm paket | **1757 geçti / 0 başarısız / 35 atlandı** (PostgreSQL ortamı yok) |
| Release derlemesi | 0 hata |
| Bulunan hata | 1 (yetki kataloğu / ekran eşleşmesi — aşağıda) |

---

## 2. Kritik senaryolar (kullanıcının açıkça istediği ikisi)

| Kod | Senaryo | Beklenen | Sonuç |
|---|---|---|---|
| **I01** | Aynı `operation_id` ile iki kez fatura gönder | fatura=1, cari=1, stok=1; stok 20 değil **10**, bakiye −2400 değil **−1200** | ✅ |
| **I02** | Aşamanın ortasında hata (stok yetersiz satış faturası) | fatura=0, satır=0, cari=0, stok belgesi=0, hareket=0, bakiye=0 | ✅ |

Bu ikisi **kısmi kayıt olamayacağını** ispatlar: cari borcu yazılıp stok yazılmadan kalamaz.

---

## 3. Coverage Matrix (CLAUDE.md §7.13)

| Madde | Durum | Not |
|---|---|---|
| Form Açıldı | ✅ | Masaüstü + web, yetkisiz kullanıcıya kapalı |
| Yeni Kayıt | ✅ | B1 alış, B2 satış, B4 stoksuz (hizmet) fatura |
| Düzenleme | ✅ | E1 — yalnız bilgi alanları; tutar/satır değişmez |
| Silme | ✅ (yok) | Fiziksel silme yolu **yazılmadı**; D1–D5 iptal senaryoları |
| Arama | ✅ | Fatura no / karşı belge no / cari kodu-ünvanı, SQL tarafında |
| Filtre | ✅ | Yön (alış/satış), durum (yürürlükte/iptal), cari, tarih aralığı |
| Grid | ✅ | Sunucu tarafı sayfalama (50), tüm kayıtlar RAM'e çekilmiyor |
| Doğrulamalar | ✅ | G1–G7: satırsız, negatif/sıfır miktar, %100 üstü oran, ters vade, boş satır, op_id eksik, sıfır tutar |
| Yetki | ✅ | F1 yetkisiz, F2 yalnız görüntüleme, F3/F4 tenant sızıntısı |
| Hata Mesajları | ✅ | Türkçe, satır numarası veren ("3. satır: miktar sıfırdan büyük olmalıdır.") |
| Database | ✅ | Migration067 boş ve dolu DB'de idempotent; tekil indeksler doğrulandı |
| Offline | ✅ | Masaüstü yerel servisi doğrudan çağırır (ağ gerekmez) |
| Sync | ✅ | InvoiceSyncTests: kapsam, FK sırası, kaynak sırası, yetki bağı |
| Performans | ✅ | Liste tek sorgu + tek sayım; malzeme seçimi aranabilir (200 sınırında sessiz kesilme yok) |
| UI | ⚠️ | Ekranlar derlendi; **elle tıklama testi yapılmadı** (§6) |
| UX | ✅ | İptal ne yapacağını yazıyor; çift kayıt koruması kullanıcıya görünür |
| Security | ✅ | Parametreli sorgu, deny-by-default, firma sınırı, çift gönderim koruması |

---

## 4. Test grupları (38 test)

**A · Toplam hesabı (4)** — iskonto matrahtan düşer, KDV iskontolu tutar üzerinden, tevkifat KDV
üzerinden; oranlar koddan değil veriden (%1/%10/%20 aynı fonksiyondan); kuruş yuvarlaması.

**B · Akış (4)** — alış (stok girer, cari alacaklanır) · satış (stok çıkar, cari borçlanır) ·
numara serisi ilerler (A00000001 → A00000002) · stok etkilemeyen hizmet faturası.

**C · Idempotency + atomiklik (3)** — I01, I02 ve doğrulama hatasında serinin bile ilerlememesi.

**D · İptal (5)** — ters kayıt üretir/silmez · çift iptal engellenir · gerekçe zorunlu ·
iptal edilmiş fatura düzenlenemez · **tüketilmiş malın iptali reddedilir** (yarım iptal yok).

**E · Düzenleme (1)** — bilgi alanları değişir, tutar ve cari etkisi aynı kalır.

**F · Yetki (4)** — yetkisiz · yalnız görüntüleme · başka firmanın carisi · başka firmanın faturası.

**G · Doğrulama (7)** — yukarıdaki matriste listelendi.

**H · Defter sınırı (2)** — cari hareketi kaynak belgeye bağlı ve elle girilemez
(kullanıcı aynı borcu ikinci kez giremez) · stok hareketi normal defterde, fatura yalnız referans tutar.

**Senkron (5)** — kapsam · FK sırası · kaynak sırası · yetki bağı · etkilerin yeniden üretilmemesi.

---

## 5. Bulunan hata ve düzeltmesi

**BULGU-1 — `invoices` modülü yetki ağacına eklendi ama ekranı yoktu.**
`ScreenTreeParityTests.A10` kırıldı: yetki kataloğunda bir modül varken hiçbir ekranın onu
kullanmaması, o yetkinin **verilebilir ama hiçbir işe yaramaz** olması demekti.
Düzeltme: `AppScreens`'e `accounting.invoices` ve `accounting.invoices.new` eklendi; ardından
parite testleri masaüstü ve web ekranlarının **gerçekten var olmasını** talep etti (S7/S8/A2/A3) —
ekranlar yazılana kadar kırık kaldılar. Test gevşetilmedi.

**Taban çizgisi güncellemeleri (gevşetme değil):** S13 masaüstü menü bağlantı sayısı 42→44,
S14 web 49→51. Beklenen değerler bilinçli eklenen iki ekranla eşitlendi; testlerin iddiası aynı kaldı.

---

## 6. Yapılmayanlar / riskler

- **GUI tıklama testi yapılmadı.** Masaüstü ve web ekranları derlendi (Release 0 hata) ve tüm iş
  kuralları servis katmanında test edildi; ancak ekranlar elle açılıp tıklanmadı. Bu yüzden
  §7.9 (UI) ve §7.10 (UX) maddeleri **kanıtlanmış sayılmıyor**.
- **Belge serisi / KDV oranı yönetim ekranı yok.** API ve servis hazır; varsayılan seri ilk
  faturada otomatik oluşur ("A", 8 hane). Yönetim ekranı G4-4'e bırakıldı.
- **35 PostgreSQL testi atlandı** — izole PG ortamı yok; production PG test için kullanılmadı.
- **Alış faturası iptali stok çıkışıdır.** Mal tüketilmişse iptal reddedilir (D5 ile test edildi).
  Bu doğru davranıştır ama kullanıcı için sürpriz olabilir; ekranda uyarı metni gösteriliyor.
- **Ambient transaction'da yeniden deneme yok.** `StockBalanceWriter` CAS çakışmasında retry
  yalnız kendi transaction'ını açan yolda çalışır; fatura yolunda istisna çağırana çıkar ve tüm
  işlem geri alınır. Yoğun eş zamanlı fatura kesiminde kullanıcı "tekrar deneyin" görebilir.

---

## 7. Tahmini kapsam

Fatura ekranının iş kuralı yüzeyi (doğrulama, toplam, yetki, firma sınırı, idempotency, iptal,
düzenleme politikası, senkron) **kapsandı**. Kapsanmayan alan: görsel/etkileşim katmanı (elle test).
