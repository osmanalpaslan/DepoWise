# Personel Ekranı — Masaüstü ↔ Web ↔ Veritabanı Parite Denetimi

**Tarih:** 2026-07-23 · **Kapsam:** Faz 0 (3. ekran) · **Sonuç: parite TAM — gerçek bulgu yok.** Kod değişmedi.

## Not: tek giriş noktası
Araçlar/Malzemeler'in aksine Personel'de "hızlı düzenle" penceresi yok — her iki tarafta da tek form.
Bu, olası ayrışma noktası sayısını doğal olarak azaltıyor.

## ✅ Eşit çıkanlar
| Konu | Durum |
|---|---|
| Form alanları (Ad Soyad, Unvan, Telefon, Şube, Aktif, Saha Personeli, Hesap bağlama) | Masaüstü = Web ✅ |
| Zorunlu: Ad Soyad | Aynı mesaj ✅ |
| Telefon biçim uyarısı (yumuşak, geçilebilir) | Aynı mesaj, aynı merkezi kural (`FieldChecks.PhoneLooksValid`) ✅ |
| **Mükerrer kişi tespiti** (isim+telefon benzeri kayıt) | Aynı sunucu sorgusu, aynı akış ✅ |
| **"Saha personeli mi?" otomatik sorusu** (hesap yok + bağlanmıyor + işaretli değilse) | Kod düzeyinde neredeyse birebir aynı (yorumlar dahil) ✅ |
| Kullanıcı hesabı bağlama yetkisi (`CanManageAccounts` / `Auth.IsAdmin`) | İkisi de aynı merkezi `AccessControl.IsAdmin` sonucuna dayanıyor ✅ |
| Kayıt işlemi | Masaüstü de web de AYNI `PersonnelService` sınıfına düşüyor (mimari olarak ayrışma imkânsız) ✅ |
| Düzenleme kilidi | Var ✅ |
| Sütun/filtre listesi | Yok (sabit liste, iki tarafta da) — ayrışma riski yok ✅ |

## Sonuç
Bu ekranda kod, baştan itibaren parite gözetilerek yazılmış. Denetim düzeltme gerektirmedi.

## Sıradaki
Stok Giriş/Çıkış.
