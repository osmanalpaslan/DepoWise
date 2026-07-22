---
name: platform-priority-desktop-first
description: "Masaüstü öncelikli geliştir/test et ama web'i de eksik bırakma (kullanıcı kuralı)"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: df9ffe0c-3fa7-4050-a259-1e49741f175d
---

Kullanıcı geliştirmeleri ağırlıklı **masaüstü** uygulamada test ediyor ve sorunsuz işleyiş için önceliği
masaüstüne veriyor — AMA web de aynı geliştirmeyi almalı, eksik bırakılmamalı.

**Why:** Kullanıcı günlük kullanımı masaüstünden yapıyor; sorunları orada görüyor. Bir sorun masaüstünde
varsa çoğu zaman web'de de vardır.

**How to apply:** Her iş biriminde önce masaüstünü yap+test et, hemen ardından web karşılığını tamamla
(aynı ADR/commit grubu). İş, web karşılığı yapılmadan "tamam" sayılmaz. Kural dosyası:
`.claude/rules/platform-priority.md`. İlgili: [[yatirimci-oncesi-oncelikler]].
