---
name: sync-partial-apply-timeout
description: "İş verisi push'ta araçlar sunucuya ulaşmama kök nedeni — transaction'sız apply + istemcinin sunucu hata yanıtını yok sayması"
metadata: 
  node_type: memory
  type: project
  originSessionId: f405ce3e-bf5a-467f-bbcd-beacdc20dbb6
  modified: 2026-07-19T13:48:02.647Z
---

Büyük firmada (OZE, 2508 malzeme + 94 araç) masaüstünden yapılan iş-verisi push'ta **araçlar sunucuya hiç ulaşmıyordu** (sunucuda 2508 malzeme, 0 araç). Kök neden iki katman:

1. **Sunucu `ApplyCore` transaction'sızdı** → her upsert ayrı commit (fsync) → 2508+ satır dakikalarca → 120s'de yarıda kesiliyordu. FK-güvenli sırada `materials` `vehicles`'tan ÖNCE geldiği için malzemeler yazılıp araçlar sıraya gelmeden push kopuyordu (kısmi/atomik-olmayan apply). Düzeltme: `ApplyCore` tek `BEGIN/COMMIT` (2026-07-19, commit 84e1537).

2. **İstemci sunucu yanıtını yok sayıyor:** `BusinessSyncPushService.PushAsync` yalnız `resp.IsSuccessStatusCode`'a bakar; sunucunun döndürdüğü `{upserted, skipped, errors}` GÖRÜLMEZ. Bu yüzden yarıda kesilen/atlanan satırlar sessizce kaybolur, kullanıcı "eşitlendi" sanır. Bu latent gap hâlâ açık — push sonucu kullanıcıya yansıtılmalı (ileride).

**Teşhis yöntemi (tekrar gerekirse):** `DEPOWISE_ADMIN_USER/PASS` ile login → `/api/auth/select-company` (OZE=`23d7d158cdb94d3cb485da247ebb8283`) → `/api/sync/business-pull` (tablo satır sayıları, `company_id`-only süzülür, şube süzmesi YOK) → yerel DB (`%LOCALAPPDATA%\DepoWise\Data\Development\depowise.db`, WinGet sqlite3) satır sayısıyla karşılaştır. Yerel araç sayısını doğrudan `/api/sync/business-push`'a POST edip yanıtı (`upserted/skipped/errors`) okumak sunucu-tarafını kesin sınar.

**Sonuç:** Veri kaybı olmadı; 94 araç yerelde duruyordu, düzeltilmiş sunucuya push edildi (`upserted:94`). Süper adminin çok-firma yereli sorunu TETİKLEMEZ (push `company_id`'ye süzülür). İlgili: [[platform-priority-desktop-first]].
