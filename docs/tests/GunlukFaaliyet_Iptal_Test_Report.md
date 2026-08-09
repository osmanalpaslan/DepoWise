# Günlük Faaliyet — İptal (İş 2) Test Raporu

- **Tarih:** 2026-08-09
- **Kapsam (§7.1):** YALNIZ Günlük Faaliyet ekranı (web + masaüstü) ve ona bağlı Bakım/Stok zinciri.
  Başka ekran test edilmedi; genel regresyon yapılmadı (kullanıcı istemedi).
- **Test hesabı:** yerel geliştirme sunucusu (`localhost:5224`, yerel SQLite) + yerel `superadmin`.
  Canlı Neon veritabanına ve canlı sunucuya HİÇBİR test bağlanmadı.

## 1. Otomatik testler

| Küme | Sonuç |
|---|---|
| `DailyActivityCancelTests` (İş 2'ye özel) | 14 / 14 geçti |
| Tüm takım (`DepoWise.Tests`) | **825 geçti · 0 başarısız · 14 atlandı** (2 dk 27 sn) |

Atlanan 14 test = PostgreSQL testleri (`PostgresTestGuard` boş test veritabanı ortam değişkeni
tanımlı olmadığı için bilinçli olarak atladı — güvenlik kilidi çalışıyor).

İş 2'ye özel testlerin kapsadığı davranışlar:
- Faaliyet iptali bağlı bakımı da iptal eder, malzemeler stoğa geri döner.
- **Rollback kanıtı:** işlemin ortasında hata üretildiğinde faaliyet, bakım ve stok TAMAMEN eski hâline döner.
- Zaten iptal edilmiş faaliyet tekrar iptal edilemez (net hata mesajı).
- "Bakım Ekibi Stoğundan Kullanıldı" satırları stoğa geri EKLENMEZ (merkez depodan hiç düşülmemişti).
- Yetkisiz kullanıcı iptal edemez (servis katmanında engellenir).
- İptal edilenler listede varsayılan gelmez; `includeCancelled` ile gelir.

## 2. Uçtan uca (web arayüzü) doğrulama

Yerel API + yerel web ile gerçek tarayıcı üzerinden:

| Senaryo | Beklenen | Gerçekleşen |
|---|---|---|
| Bakım faaliyeti (10 adet malzeme) oluştur | stok 100 → 90 | ✅ 90 |
| Listede buton adı | "İptal Et" | ✅ |
| Butona bas | Onay penceresi bağlı bakım + miktarı yazar | ✅ "bağlı bakım kaydı ve 10 adet malzeme çıkışı… Araç sayacı geri alınmaz. İşlem geri alınamaz." |
| Onayla | "Faaliyet iptal edildi" | ✅ |
| Liste (varsayılan) | iptal edilen kayıt GÖRÜNMEZ | ✅ 0 kayıt |
| "İptal edilenleri göster" kutusu | kayıt görünür, üstü çizili + "İptal edildi" rozeti, "İptal Et" butonu YOK | ✅ |
| Stok bakiyesi | 100 (geri döndü) | ✅ 100 |
| Stok hareketleri | `opening 100`, `usage 10`, `usage_reverse 10` | ✅ (bakiye elle değiştirilmedi, defter üzerinden) |
| Faaliyet kaydı | `is_deleted=1`, `version=2` | ✅ |
| Bağlı bakım kaydı | `is_cancelled=1`, `version=2` | ✅ |
| Denetim kaydı (audit) | `daily_activity/reverse` + `vehicle_maintenance/reverse` | ✅ |
| Araç sayacı | geri ALINMAZ (1200 kalır) | ✅ 1200 |
| Tarayıcı konsolu | hata yok | ✅ (yalnız sunucu yeniden başlatma anındaki bağlantı uyarıları) |

## 3. Bulunan hata (bu testlerle yakalandı ve düzeltildi)

**B-1 · Öncelik: Yüksek · `/api/daily/grid` "İptal edilenleri göster" parametresini iletmiyordu.**
- Tekrar üretme: web'de kutuyu işaretle → liste yine boş.
- Muhtemel neden: servis ve web hazırdı, API ucuna `includeCancelled` parametresi eklenmemişti.
- Çözüm: `/api/daily/grid` ve `/api/daily/grid/export` uçlarına `bool? includeCancelled` eklendi;
  Excel dışa aktarımı da ekranda görünen kümeyle aynı oldu.
- Yeniden test: API `includeCancelled=false → 0 kayıt`, `true → 1 kayıt (isCancelled=true)` ✅; web ✅.
- Not: Yakıt uçları (`/api/fuel`, `/api/fuel/depot`) kontrol edildi, onlarda eksik YOK.

## 4. Coverage Matrix (§7.13)

| Madde | Durum |
|---|---|
| Form Açıldı | ✅ |
| Yeni Kayıt | ✅ (bakım faaliyeti + malzeme) |
| Düzenleme | — (bu işin kapsamında değil; ayrı iş) |
| Silme / İptal | ✅ |
| Arama / Filtre / Grid | ✅ (liste, sayfalama, iptal filtresi) |
| Doğrulamalar | ✅ (zaten iptal edilmiş kayıt) |
| Yetki | ✅ (servis katmanı; UI ve doğrudan API çağrısı aşamaz) |
| Hata Mesajları | ✅ (teknik olmayan, açık) |
| Database | ✅ (bakiye, hareket defteri, versiyon, audit) |
| Offline | — (masaüstü yerel SQLite üzerinde aynı servis; ayrı senaryo çalıştırılmadı) |
| Sync | — (bu işte şema/alan değişmedi, senkron sözleşmesi aynı) |
| Performans | ✅ (tek transaction, ek sorgu yükü yok) |
| UI | ✅ (üstü çizili + rozet, kutu, buton adı) |
| UX | ✅ (onay penceresi NE OLACAĞINI önceden yazar) |
| Security | ✅ (yetki servis katmanında; `CancelInTransaction` iç kullanım, yetkilendirme çağırana ait) |

## 5. Riskler / kalan notlar

- İptal geri ALINAMAZ (kullanıcı kararı K4/Y4). Kullanıcı yeniden girer.
- Araç sayacı bilinçli olarak geri alınmaz — sayaç geriye gitmez kuralı korunur.
- Masaüstü tarafı derleme (XAML derlemesi dâhil) + servis testleriyle doğrulandı; masaüstü arayüzü
  otomatik sürülemediği için görsel doğrulama kullanıcı tarafında yapılır.
