---
name: server-disk-full-release-packages
description: Fly.io diski güncelleme paketleriyle dolunca tüm API 500 verir; saklama politikası ve müdahale
metadata: 
  node_type: memory
  type: project
  originSessionId: b6c1eef5-05a1-4e5e-a21a-2092438a9228
---

12.07.2026'da canlı sunucuda **tam kesinti** yaşandı: Fly.io kalıcı diski (`/data`, ~974 MB) doldu → SQLite `database or disk is full` → **login dahil her API ucu 500**. Kök neden: her masaüstü paketi ~85 MB ve `/data/releases` altında hiç temizlenmiyordu (11 paket = 892 MB); sunucu DB'si sadece 1 MB.

**Why:** Disk dolması sessiz değil ÖLÜMCÜL bir arızadır — SQLite hiçbir şey yazamaz, sistem komple durur. ~1 GB disk ÷ 85 MB paket = yalnızca ~11 sürümlük tavan.

**How to apply:** Kalıcı çözüm eklendi: `ReleaseStore.SaveAsync` → `PruneOld()`, en yeni `KeepCount=3` paket dışındakileri otomatik siler (ADR-070). Bir daha olursa teşhis: `flyctl ssh console --config fly.toml -C "df -h /data"`. Müdahale: `/data/releases` altındaki eski `.pkg` dosyalarını sil (en güncel sürümü koru), sonra yeniden yayınla. Paket boyutu büyürse `KeepCount` düşür veya `fly volumes extend` ile diski büyüt. İlişkili: [[pending-work-tracker-file]].
