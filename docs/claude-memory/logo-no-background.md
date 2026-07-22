---
name: logo-no-background
description: Logoların arkasına arka plan/beyaz kutu konmaz; yalnız logo gösterilir
metadata: 
  node_type: memory
  type: feedback
  originSessionId: b6c1eef5-05a1-4e5e-a21a-2092438a9228
---

Logo ve sembol **hiçbir yerde** arka plan kutusuna (beyaz yuvarlak kutu vb.) sarılmaz. Şeffaf PNG doğrudan kullanılır — masaüstü giriş ekranı, masaüstü kenar çubuğu, web üst barı, web giriş kartı.

**Why:** Koyu temada logo (lacivert ağırlıklı) kaybolmasın diye kendi kararımla beyaz kutular eklemiştim; kullanıcı bunu açıkça reddetti: *"arka plan olmamalı sadece logo olmalı"*. Kullanıcı kontrast ödünleşimini bilerek kabul etti (ADR-075).

**How to apply:** Logo yerleştirirken arka plan/kutu ekleme. Koyu temada kontrast şikâyeti gelirse çözüm arka plan eklemek DEĞİL, koyu tema için **açık renkli logo varyantı** üretmektir. Masaüstü giriş ekranı sembol logosunu (`Assets/app-icon.png`) kullanır. İlişkili: [[pending-work-tracker-file]].
