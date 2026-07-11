# DepoWise — Çok Makineli Senkron Yol Haritası

> **Sorun (özet):** Aynı firmada birden çok masaüstü olduğunda, bir makinenin girdiği veri diğerinde
> görünmüyordu (masaüstü sunucuya gönderiyor ama geri çekmiyordu; yalnız tanımları çekiyordu). Ayrıca
> stok bakiyesi istemci-otoriteli olduğu için makineler birbirini ezebiliyordu.
>
> Son güncelleme: 2026-07-11

## Mevcut mimari (kısa)
- **Gönderme (push):** Masaüstü → `/api/sync/business-push` → `BusinessSyncService.Apply` (generic upsert, LWW). Tek yön.
- **Tanım çekme:** `LookupSyncService` yalnız lookup/tanımları çeker (marka, birim, şube…). İş verisini çekmezdi.
- **Kimlikli senkron çekirdeği** (operation_id + outbox/inbox + `server_changes` feed + conflict) Faz 14'te kuruldu ama iş verisine bağlanmadı (R20).

## Aşamalı plan

### ✅ Aşama 2a — İş verisi GERİ-ÇEKME (görünürlük) — YAPILDI (2026-07-11)
- Yeni uç: `GET /api/sync/business-pull` → firmanın iş snapshot'ı (oturum firması zorlanır).
- Masaüstü: `BusinessSyncPullService.PullAsync` → sunucudan çeker → `BusinessSyncService.ApplyPull` ile yerele uygular (LWW, trusted).
- Tetik: giriş sonrası + periyodik (~3 dk) + "Eşitle" butonu (gönder → sonra çek).
- **stock_balances HARİÇ** (türetilmiş; 2b'de sunucu-otoriteli olacak).
- Sonuç: B makinesi artık A'nın malzeme/araç/bakım/yakıt/personel/talep/hareket kayıtlarını **görür**.
- Kanıt: `BusinessSyncTests` — çok-makineli görünürlük + hariç-tutma testleri (246/246 yeşil).
- **Görünürlük yeni masaüstü paketiyle (1.0.35) canlıya yansır.**

### ⏳ Aşama 2b — Stok bakiyesi SUNUCU-OTORİTELİ (ezilme çözümü)
- Sunucu, tüm makinelerden gelen `stock_movements` (hareket defteri; benzersiz kimlikli, birikimli) üzerinden
  `stock_balances`'ı **kendisi yeniden hesaplasın** (istemci snapshot'ına güvenme).
- Geri-çekmede stock_balances artık sunucudan (doğru, birleşik) gelir; hariç-tutma kaldırılır.
- Böylece iki makine aynı malzemede işlem yapsa da bakiye doğru toplanır, ezilmez.

### ⏳ Aşama 2c — Tam kimlikli çift-yönlü senkron (ileri çakışmalar)
- Aynı kaydı iki makine aynı anda düzenlerse: operation_id + base_version + conflict çözümü (çekirdek hazır, R20).
- İş yazmaları outbox'a → `/sync/push` → sunucu iş-servisleriyle apply → `server_changes` feed → `/sync/pull`.
- LWW yerine gerçek çakışma yönetimi. En sağlam ama en büyük iş; ölçek/çok-yoğun çok-makineli kullanım öncesi.

## Notlar
- 2a görünürlüğü çözer (çoğu veri benzersiz kimlikli → güvenle eklenir). 2b stok doğruluğunu, 2c ileri çakışmaları çözer.
- Hepsi ücretsiz geliştirme; sunucu maliyeti değişmez.
