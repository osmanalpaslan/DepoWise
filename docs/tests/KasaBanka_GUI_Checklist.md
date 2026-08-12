# Kasa / Banka — GERÇEK KULLANICI GUI TEST LİSTESİ (G4-3)

**Durum: 🔴 YAPILMADI.** Aşağıdaki maddelerin hiçbiri elle çalıştırılmadı.
Otomatik testler (45 servis testi + 6 senkron testi) geçti, ekranlar Release'te 0 hatayla derlendi —
ama **ekran davranışı kanıtlanmış değildir**. Bu dosya doldurulmadan G4-3 "kullanıcı tarafından
doğrulandı" sayılmaz.

> Test hesabı: `.env.test.local` içindeki `DEPOWISE_TEST_USER`. Gerçek yönetici hesapları
> (superadmin, mustafa.alpaslan) testte kullanılmaz.

---

## 1. Ekran açılışı

| # | Kontrol | Masaüstü | Web |
|---|---|---|---|
| 1.1 | "Kasa / Banka" ekranı menüde görünüyor mu? | ☐ | ☐ |
| 1.2 | "Tahsilat / Ödeme" ekranı menüde görünüyor mu? | ☐ | ☐ |
| 1.3 | Ekran hatasız açılıyor mu? | ☐ | ☐ |
| 1.4 | Hiç hesap yokken "Hesap bulunamadı." mesajı çıkıyor mu (boş ekran değil)? | ☐ | ☐ |

## 2. Hesap tanımı

| # | Kontrol | Masaüstü | Web |
|---|---|---|---|
| 2.1 | Yeni **kasa** hesabı oluşturulabiliyor mu? | ☐ | ☐ |
| 2.2 | Yeni **banka** hesabı oluşturulabiliyor mu? | ☐ | ☐ |
| 2.3 | Tür "Banka" seçilince banka alanları (IBAN, hesap no) **görünüyor**, "Kasa"da **gizleniyor** mu? | ☐ | ☐ |
| 2.4 | Aynı kodla ikinci hesap reddediliyor ve mesaj **anlaşılır** mı? | ☐ | ☐ |
| 2.5 | Hatalı IBAN ("TR12") reddediliyor mu? IBAN boş bırakılabiliyor mu? | ☐ | ☐ |
| 2.6 | Hesap düzenleme kaydediyor mu? | ☐ | ☐ |
| 2.7 | Hareketi olan hesap silinmeye çalışılınca **açık uyarı** çıkıyor mu ("pasif yapın")? | ☐ | ☐ |
| 2.8 | Aktif/pasif değiştirme çalışıyor mu? Pasif hesap yeni işlemde seçilemiyor mu? | ☐ | ☐ |

## 3. Tahsilat

| # | Kontrol | Masaüstü | Web |
|---|---|---|---|
| 3.1 | Tahsilat yapılabiliyor mu? | ☐ | ☐ |
| 3.2 | **Kasa bakiyesi ARTIYOR mu?** | ☐ | ☐ |
| 3.3 | **Müşterinin cari borcu AZALIYOR mu?** (Cari ekranından doğrula) | ☐ | ☐ |
| 3.4 | Yön açıklaması ("Kasa ARTAR, borcu AZALIR") ekranda görünüyor mu? | ☐ | ☐ |
| 3.5 | Cari seçilince **açık faturalar** listeleniyor mu? | ☐ | ☐ |
| 3.6 | Tahsilatta yalnız **SATIŞ** faturaları geliyor mu (alış gelmemeli)? | ☐ | ☐ |
| 3.7 | Fatura işaretlenince tutar **kalan** ile otomatik doluyor mu? | ☐ | ☐ |
| 3.8 | Kısmi tahsilat sonrası **faturanın kalanı doğru** mu? (10.000 → 4.000 → kalan 6.000) | ☐ | ☐ |
| 3.9 | Tam tahsilat sonrası fatura **açık listeden çıkıyor** mu? | ☐ | ☐ |
| 3.10 | Fatura seçilmezse "bağımsız cari hareketi" olduğu yazıyor mu? | ☐ | ☐ |
| 3.11 | Kalandan büyük tutar girilince **anlaşılır hata** çıkıyor mu? | ☐ | ☐ |

## 4. Ödeme

| # | Kontrol | Masaüstü | Web |
|---|---|---|---|
| 4.1 | Ödeme yapılabiliyor mu? | ☐ | ☐ |
| 4.2 | **Kasa/banka bakiyesi AZALIYOR mu?** | ☐ | ☐ |
| 4.3 | **Tedarikçiye olan borcumuz AZALIYOR mu?** | ☐ | ☐ |
| 4.4 | Ödemede yalnız **ALIŞ** faturaları geliyor mu? | ☐ | ☐ |
| 4.5 | Ödeme yöntemi (nakit/havale/kredi kartı/çek/senet) seçilebiliyor mu? | ☐ | ☐ |

## 5. İç transfer

| # | Kontrol | Masaüstü | Web |
|---|---|---|---|
| 5.1 | Kasa → banka transferi yapılabiliyor mu? | ☐ | ☐ |
| 5.2 | **Kaynak −X, hedef +X**, toplam **değişmiyor** mu? | ☐ | ☐ |
| 5.3 | Hiçbir cari etkilenmiyor mu? (Cari ekranından doğrula) | ☐ | ☐ |
| 5.4 | Aynı hesabı kaynak+hedef seçmek engelleniyor mu? | ☐ | ☐ |
| 5.5 | Transfer iptalinde **iki bacak birlikte** geri alınıyor mu? | ☐ | ☐ |

## 6. Ters kayıt (silme yok)

| # | Kontrol | Masaüstü | Web |
|---|---|---|---|
| 6.1 | Hareketlerde **"Sil" butonu YOK**, yalnız "Ters Kayıt" var mı? | ☐ | ☐ |
| 6.2 | Gerekçesiz ters kayıt engelleniyor mu? | ☐ | ☐ |
| 6.3 | Ters kayıt ne yapacağını **önceden açıkça** yazıyor mu? | ☐ | ☐ |
| 6.4 | Ters kayıt sonrası kasa bakiyesi eski hâline dönüyor mu? | ☐ | ☐ |
| 6.5 | Ters kayıt sonrası cari bakiyesi eski hâline dönüyor mu? | ☐ | ☐ |
| 6.6 | Ters kayıt sonrası **faturanın kalanı geri artıyor** mu? | ☐ | ☐ |
| 6.7 | İptal edilen hareket listede **görünüyor** ama bakiyeye **girmiyor** mu? | ☐ | ☐ |
| 6.8 | İkinci kez iptal denenince engelleniyor mu? | ☐ | ☐ |

## 7. Gösterim ve kullanılabilirlik

| # | Kontrol | Masaüstü | Web |
|---|---|---|---|
| 7.1 | Tutarlar 2 basamak (1.234,56 / 1234.56) ve **okunur** mu? | ☐ | ☐ |
| 7.2 | Ondalık ayracı Türkiye kullanımında karışıklık yaratmıyor mu? | ☐ | ☐ |
| 7.3 | Tarih alanları GG/AA/YYYY ve takvim doğru çalışıyor mu? | ☐ | ☐ |
| 7.4 | Negatif bakiye (kasa açığı) **görünür** mü, gizlenmiyor mu? | ☐ | ☐ |
| 7.5 | Yürüyen bakiye sütunu doğru ilerliyor mu? | ☐ | ☐ |
| 7.6 | Toplam ("Toplam: X TL · N hesap") doğru mu? | ☐ | ☐ |
| 7.7 | **Tablo satırına metne tıklayınca satır seçiliyor mu?** (G3 davranışı bozulmamış olmalı) | ☐ | — |
| 7.8 | Metin kopyalanabiliyor mu (SelectableTextBlock bozulmamış)? | ☐ | — |
| 7.9 | Checkbox/NumericUpDown'a tıklayınca satır seçimi araya girmiyor mu? | ☐ | — |

## 8. Şube ve yetki

| # | Kontrol | Masaüstü | Web |
|---|---|---|---|
| 8.1 | Şube seçilerek girildiğinde yalnız o şubenin + firma geneli hesapları görünüyor mu? | ☐ | ☐ |
| 8.2 | Başka şubenin hesabına işlem yazılamıyor mu? | ☐ | ☐ |
| 8.3 | Yetkisiz kullanıcı ekranı **hiç görmüyor** mu? | ☐ | ☐ |
| 8.4 | Salt-okunur kullanıcı butonları **görmüyor** ve işlem yapamıyor mu? | ☐ | ☐ |
| 8.5 | Kendisinde kasa/banka yetkisi olmayan kullanıcı bu yetkiyi **devredemiyor** mu? | ☐ | ☐ |
| 8.6 | `/screen-visibility` üzerinden ekran yalnız masaüstü / yalnız web yapılabiliyor mu? | ☐ | ☐ |

## 9. Çevrimdışı ve senkron

| # | Kontrol | Sonuç |
|---|---|---|
| 9.1 | Masaüstü **çevrimdışıyken** hesap açılabiliyor mu? | ☐ |
| 9.2 | Çevrimdışı tahsilat yapılabiliyor mu? | ☐ |
| 9.3 | Bağlantı gelince kayıtlar sunucuya gidiyor mu? | ☐ |
| 9.4 | **Web'de aynı kayıt görünüyor** mu? | ☐ |
| 9.5 | Aynı kayıt ikinci kez senkronlanınca **duplicate oluşmuyor** mu? | ☐ |
| 9.6 | İkinci makinede aynı kayıt tekrar oluşmuyor mu? | ☐ |

## 10. Çift kayıt koruması (gerçek kullanıcı davranışı)

| # | Kontrol | Masaüstü | Web |
|---|---|---|---|
| 10.1 | Kaydet'e **hızlıca iki kez** basınca ikinci tahsilat oluşmuyor mu? | ☐ | ☐ |
| 10.2 | Web'de sayfa yenilenip form tekrar gönderilirse ikinci kayıt oluşmuyor mu? | ☐ | ☐ |
| 10.3 | Aynı işlem hem web'den hem masaüstünden yapılırsa ne oluyor? (beklenen: iki AYRI işlem — farklı `operation_id`; kullanıcı bunu görebiliyor mu?) | ☐ | ☐ |

---

## Bulunan hatalar

| # | Ekran | Adım | Beklenen | Gerçekleşen | Öncelik |
|---|---|---|---|---|---|
| | | | | | |
