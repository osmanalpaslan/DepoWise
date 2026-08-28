# FINAL — KARAR PAKETİ (S5, 2026-08-29 · ADR-178)

> PK-FIN5=A gereği: birikmiş "karar bekleyen" maddeler TEK pakette. **Bu turda hiçbiri için KOD
> YAZILMADI** — siz karar verdikçe her biri AYRI, dar kapsamlı iş olarak açılır. Öneriler ⭐ ile.

## 1. FIN-B1 — operation_id benzersizliği eski tablolarda firma-üstü (YENİ — bu turun bulgusu)

- **Mevcut durum / sorun:** 6 eski tabloda (`stock_movements`, `vehicle_maintenances`, `fuel_*`,
  `daily_activities`, `assignment_movements`) `operation_id` TÜM firmalar genelinde benzersiz; başka
  firmada kullanılmış bir id ile gelen işlem SESSİZCE atlanıyor (200, kayıt yok). Yeni muhasebe
  tabloları zaten doğru desende: `(company_id, operation_id)`.
- **Öneri ⭐:** ileriki bir yayın penceresinde 6 tabloda indeks migration'ı (global unique → firma
  kapsamlı unique) + idempotency kontrollerine `company_id` süzgeci (kod değişikliği hazır sayılır —
  bu turda denenip şema engeline takıldığı için geri alındı). O zamana dek risk ~sıfır (GUID id'ler).
- **Risk/maliyet:** canlı tabloda indeks değişimi — düşük ama YAYIN ister; iki lehçede kanıt testi şart.
  Mevcut mimariye etkisi: yok (davranış yalnız firmalar-arası çakışma ucunda düzelir).
- **Karar:** [ ] Migration planlansın · [ ] Böyle kalsın (test kilidi FIN5 korur)

## 2. YET-01 — işlevsiz iki yetki anahtarı

- **Mevcut durum:** `btn-logo` kodda VAR OLMAYAN bir butonu, `btn-reset-db` süper-admin-yolu dışında
  kullanılmayan bir işlemi işaret ediyor; yetki ağacında görünüp hiçbir şeyi açmıyorlar (2026-08-26
  denetim bulgusu, iki kez yeniden doğrulandı).
- **Öneri ⭐:** iki anahtarı katalogdan kaldır (görsel temizlik; migration GEREKMEZ — user_permissions
  satırları zararsız yetim kalır, deny-by-default etkilenmez).
- **Risk:** yok denecek kadar az; yeniden yazım yok. **Karar:** [ ] Kaldır · [ ] Kalsın

## 3. ARC-01 — araç seçicisi firma geneli (ürün kararı)

- **Mevcut durum:** rapor filtresi ve bazı araç seçicileri şube süzmeden FİRMANIN TÜM araçlarını
  listeliyor (bilinçli eski davranış; BranchAccess VERİYİ zaten süzüyor — bu yalnız seçici listesi).
- **Öneri ⭐:** şube kapsamına göre süzülsün (kapsamlı kullanıcı yalnız kapsamındaki araçları seçebilsin).
- **Risk:** düşük; "aracımı seçemiyorum" destek çağrısı ihtimaline karşı kapsam kuralı net duyurulmalı.
- **Karar:** [ ] Süz · [ ] Böyle kalsın

## 4. STK-B2 — arama `stock_documents.note`'u kapsasın mı?

- **Mevcut durum:** stok hareket araması malzeme kodu/adı/not/fatura/belge no üzerindedir; BELGE notu
  (`stock_documents.note`) kapsam dışı (ARA-01'de de bilinçli dışarıda: açıklama/not aranmaz kuralı).
- **Öneri ⭐:** HAYIR — mevcut kural (kimlik alanları aranır) tutarlı kalsın. **Karar:** [ ] Hayır · [ ] Evet

## 5. RPR-02 — web rapor isteği oturumun ŞUBESİNİ taşımıyor (JWT'de yok)

- **Mevcut durum:** web'de rapor istekleri oturum şubesi bilgisini taşımıyor; BranchAccess KAPSAM
  süzmesi çalışıyor (sızıntı yok) ama "çalışma şubesi varsayılanı" web raporlarında uygulanamıyor.
- **Öneri ⭐:** oturum kurulumuna (Session()) şube bilgisini eklemek yerine istek gövdesine opsiyonel
  `branchId` zaten var — masaüstü paritesi için web UI'nin çalışma şubesini varsayılan filtre olarak
  GÖNDERMESİ yeterli (JWT değişmez, API sözleşmesi değişmez). Küçük web-UI işi.
- **Karar:** [ ] Web-UI varsayılanı ekle · [ ] Böyle kalsın

## 6. SNK-05 — çevrimdışı onay çakışması

- **Mevcut durum:** iki cihaz çevrimdışıyken aynı talebi farklı yönde onay/reddedebilir; senkronda
  LWW onay alanına uygulanmaz (bilinçli), çakışma eldeki kurallarla sıraya girer — davranış belgeli
  değil, "karar bekliyor" olarak duruyor.
- **Öneri ⭐:** v1 kuralını YAZILI sabitle: "sunucuya İLK ulaşan onay kazanır; sonraki değişiklik
  çakışma hatası alır" (kod çoğunlukla böyle davranıyor; iş, kanıt testi + belge). **Karar:** [ ] Sabitle · [ ] Ertele

## 7. MAK-01/b — makine aktivasyon modeli

- **Mevcut durum:** `/api/machines/register` giriş ekranından ÖNCE anonim çağrılabiliyor (makine kaydı
  akışının doğası); kota + hız sınırlayıcı + tek kullanımlık anahtar korumaları var. İki denetimde
  "değiştirilmedi" olarak bırakıldı.
- **Öneri ⭐:** böyle kalsın (kayıt akışı bunu gerektiriyor; ek sertleştirme — ör. kayıt penceresi —
  gerçek kötüye kullanım görülürse eklensin). **Karar:** [ ] Kalsın · [ ] Sertleştir

---
Not: **SNK-13** karar maddesi DEĞİLDİR — kullanıcı talimatıyla dokunulmuyor (kayıtlı bilinen davranış).
