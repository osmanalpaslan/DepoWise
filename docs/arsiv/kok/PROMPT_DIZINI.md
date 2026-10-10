# DepoWise Aşamalı Prompt Dizini

## Kullanım
- Claude Code'a aynı anda yalnız bir prompt verin.
- İlk kez başlarken `00_KAYNAK_ANALIZI_VE_PLAN.md` ile başlayın.
- Claude fazı tamamladığını ve testleri yazdığını belirtmeden bir sonraki dosyaya geçmeyin.
- Bağlam dolarsa Claude önce state dosyalarını güncellesin; ardından `/compact` çalıştırıp aynı promptu "kaldığın yerden devam et" cümlesiyle tekrar verin.

## Fazlar
- `00_KAYNAK_ANALIZI_REPO_KESFI_VE_KESIN_PLAN.md` — Kaynak Analizi, Repo Keşfi ve Kesin Plan
- `01_COZUM_ISKELETI_VE_ORTAK_SOZLESMELER.md` — Çözüm İskeleti ve Ortak Sözleşmeler
- `02_VERITABANI_TEMELI_AUDIT_VE_ORTAK_VERI_KURALLARI.md` — Veritabanı Temeli, Audit ve Ortak Veri Kuralları
- `03_KIMLIK_DOGRULAMA_TENANT_VE_YETKI_SISTEMI.md` — Kimlik Doğrulama, Tenant ve Yetki Sistemi
- `04_ORTAK_UI_MENU_VE_TANIMLAR_ALAN_AYARLARI.md` — Ortak UI, Menü ve Tanımlar/Alan Ayarları
- `05_FIRMA_SUBE_SANTIYE_VE_PERSONEL.md` — Firma, Şube/Şantiye ve Personel
- `06_MALZEME_KARTLARI_VE_TEDARIKCI_TANIMLAR.md` — Malzeme Kartları ve Tedarikçi/Tanımlar
- `07_STOK_GIRIS_CIKIS_TRANSFER_VE_SAYIM.md` — Stok Giriş, Çıkış, Transfer ve Sayım
- `08_ARACLAR_ARAC_SABLONLARI_VE_SAYAC.md` — Araçlar, Araç Şablonları ve Sayaç
- `09_BAKIM_MUAYENE_SIGORTA_VE_UYARI_DONGUSU.md` — Bakım, Muayene/Sigorta ve Uyarı Döngüsü
- `10_YAKIT_SARFIYATI_VE_GUNLUK_FAALIYET.md` — Yakıt Sarfiyatı ve Günlük Faaliyet
- `11_MALZEME_TALEP_ONAY_VE_PDF.md` — Malzeme Talep, Onay ve PDF
- `12_ANA_EKRAN_UYARILAR_RAPORLAR_VE_IMPORT_EXPORT.md` — Ana Ekran, Uyarılar, Raporlar ve Import/Export
- `13_DOSYA_FOTOGRAF_AUDIT_COP_KUTUSU_VE_YEDEK.md` — Dosya/Fotoğraf, Audit, Çöp Kutusu ve Yedek
- `14_OFFLINE_SENKRONIZASYON_CIHAZ_KAYDI_VE_CAKISMALAR.md` — Offline Senkronizasyon, Cihaz Kaydı ve Çakışmalar
- `15_SETUP_GUNCELLEME_VE_COMODO_GUVENLI_CALISTIRMA.md` — Setup, Güncelleme ve COMODO Güvenli Çalıştırma
- `16_GUVENLIK_SERTLESTIRME_VE_OPERASYON_HAZIRLIGI.md` — Güvenlik Sertleştirme ve Operasyon Hazırlığı
- `17_UCTAN_UCA_DOGRULAMA_DOKUMANTASYON_VE_YAYIN_ADAYI.md` — Uçtan Uca Doğrulama, Dokümantasyon ve Yayın Adayı

## Başlama komutu
Claude Code'u proje kökünde açın ve yalnız `prompts/00_...md` içeriğini gönderin. Büyük ana promptu tek seferde analiz ettirmeyin.
