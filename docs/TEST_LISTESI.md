# DepoWise — Test Listesi

> Kullanıcının elle test edeceği maddeler. Test edilmemiş işler buraya eklenir.
> Test edilince başına `[x]` koy. Build: uygulamayı kapat → "DepoWise (Gercek DB)" kısayolundan aç.

## Bekleyen testler (2026-06-28)

### Ana Ekran (Dashboard)
- [ ] KPI kartlarına tıklayınca ilgili ekran açılıyor (Araç/Malzeme/Düşük Stok/Bekleyen Talep).
- [ ] **Uyarıya tıklayınca uyarıya sebep olan kaydın DETAYI açılıyor (düzenleme değil):**
  - [ ] Bakım uyarısı → Bakım Takibi › Araç Bakımları'nda ilgili aracın en son bakım kaydı seçili (detay paneli).
  - [ ] Muayene/Sigorta uyarısı → Araçlar'da ilgili aracın detayı (salt).
  - [ ] Düşük stok uyarısı (malzeme bazlı) → ilgili malzemenin detayı.
  - [ ] Yakıt: depo kalanı toplam alınanın %20 ve altına düşünce "Yakıt Azaldı/Tükendi" uyarısı → Yakıt › Özet'te kalan görünüyor.

### Menü
- [ ] Açılışta hiçbir menü grubu açık değil (sol tık yapılmadan alt sekme listelenmiyor).
- [ ] Malzemeler altında "Kategoriler" yok.
- [ ] Günlük Faaliyet ayrı ana menü; Muayene/Sigorta Araçlar altında; Personel ayrı ana menü.

### Malzeme Giriş-Çıkış
- [ ] Giriş / Çıkış / Transfer; Personel (Şoför) + Teslim Eden/Transfer Araç alanları.
- [ ] Fatura/İrsaliye, Sipariş Fişi, Veresiye Fişi alanları kaydediliyor.
- [ ] Hareket satırında "İptal" (ters kayıt) → stok geri alınıyor.

### Stok Sayım
- [ ] Malzeme seç → sistem stoğu + sayılan + gerekçe → fark stoğa yansıyor.
- [ ] Fark 0 sayım da kaydediliyor (raporda görünür).

### Günlük Faaliyet
- [ ] Tek "Yeni Kayıt Oluştur" + Kayıt Tipi (Hareket/Transfer/Bakım) forma göre değişiyor.
- [ ] Araç alanında araçlar listeleniyor (ara/seç). Transfer → araç pasife alınıyor.
- [ ] Bakım: Alt Bakım "+" ile yeni alt bakım; malzeme → stok düşümü.
- [ ] Gün filtresi + alt özet ("N faaliyet — X bakım, Y hareket"). Satır "Sil".

### Muayene / Sigorta
- [ ] Sonuç = Geçti/Kaldı/Ertelendi; Ertelendi → Erteleme Tarihi (zorunlu).
- [ ] Ertelendi'de uyarı erteleme tarihine göre çalışıyor (Yaklaşıyor/Süresi geçti).

### Personel
- [ ] Liste + yeni/düzenle (ad/unvan/telefon/şube/aktif) + sil.

### Raporlar
- [ ] Grafik alanı yok; sadece tablo.
- [ ] Rapor tipleri: Genel / Stok Durumu / Stok Sayım / Yakıt Tüketim / Bakım / Depo Girişi / Talep — Sorgula + PDF/Excel.

### İmport / Export
- [ ] Export: Malzemeler/Araçlar/Personel/Muayene/Bakım/Talepler → Excel.
- [ ] Örnek Excel İndir (şablon) her entity için.
- [ ] İçe Aktar: Malzemeler / Araçlar / Bakım / Muayene-Sigorta (dry-run önizleme + sonuç).

### Yönetim
- [ ] Çöp Kutusu: silinen kayıt görünüyor + geri yükle.
- [ ] Sistem Logu: işlemler listeleniyor (salt okunur, silinemez).
- [ ] superadmin/superadmin ile Firma Tanım + Güncelleme Yönetimi görünüyor; admin'de görünmüyor.
- [ ] Kullanıcılar: şifre değiştir / sil (admin+süper-admin); süper-admin kayıtları diğer rollere görünmez.
- [ ] Ekran Bilgisi / Basit Ekran Bilgisi butonları yalnız süper-admin'de.
