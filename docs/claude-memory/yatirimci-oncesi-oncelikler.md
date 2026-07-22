---
name: yatirimci-oncesi-oncelikler
description: "DepoWise şu an yalnız babasının şirketi için Türkiye'de kullanılacak; öncelik ekran/alan eksiklerini gidermek, maliyetli mimari yatırım yok. Yatırımcı/global ölçek işleri ve KVKK/GDPR + API dokümantasyonu sonraya ertelendi."
metadata: 
  node_type: memory
  type: project
  originSessionId: 4876dd82-2c04-499d-aca0-0c6b3b1c95ff
---

**Yakın vadeli öncelik (2026-07-16 itibarıyla):** DepoWise şu aşamada yalnızca babasının çalıştığı şirkette
kullanılacak (Türkiye, tek firma, iç kullanım). Kullanıcının hedefi: mevcut ekran ve alanlardaki eksikleri
tamamlayıp uygulamayı son haline getirmek. Bu tamamlandıktan sonra babasının şirketine satmayı deneyecek;
nasip olursa daha sonra (yatırımcı bulup) genele açmayı düşünüyor.

**Why:** Kullanıcı net şekilde belirtti: "yapacağım geliştirmeler şuan için maliyetsel geliştirmeler
olmamalı", "babam bu uygulamayı kullanacak ekran ve alanların bütün eksikliklerini gidermek" birincil hedef.
Global SaaS ölçeğinde mimari yatırım (çoklu bölge, Postgres geçişi, ödeme/faturalama altyapısı, SSO, çoklu
dil) şimdilik gündemde değil — bunlar yatırımcı/genele açılma aşamasına ertelendi.

**How to apply:** Öneri yaparken önce bu ölçek sınırını hatırla. Büyük/maliyetli mimari değişiklik önerme;
odak hep "ekran ve alan tamamlığı" (eksik alan, eksik doğrulama, eksik akış) olmalı. "Global çapta eksik"
tarzı sorular geldiğinde bunun "Türkiye'de tek firma için işlevsel eksiksizlik" anlamına geldiğini varsay,
SaaS ölçeklenebilirlik denetimine kaymadan önce doğrula.

---

**Ertelenen konular (canlıya açılma/yatırımcı aşamasında tekrar ele alınacak):**
- **KVKK/GDPR self-servis veri ihracı** — şu an yok, şimdilik gerekmiyor.
- **Genel API dokümantasyonu (OpenAPI/Swagger)** — şu an yok, şimdilik gerekmiyor.

**Why:** Kullanıcı 2026-07-16'da açıkça "bu eksiklikleri zamanı gelince bakmak için not al" dedi — şimdi
yapılmayacak ama unutulmaması istendi.

**How to apply:** Proje "canlıya çıkma", "genele açılma" veya "yatırımcı" konusu tekrar gündeme geldiğinde bu
iki maddeyi hatırlat; o ana kadar önerilerde bunlara girme.

---

**Fly.io maliyet eşiği:** Kullanıcı mevcut Fly.io maliyetinin (~1-2 $/ay) farkında ve bunu kabul ediyor.
Aylık **10 $'ı geçerse** zorlayıcı olabileceğini söyledi (2026-07-16).

**Why:** Kullanıcı maddi konuda dikkatli (bkz. CLAUDE.md §2.1 motor seçimi kuralı ile tutarlı). Bu, somut bir
eşik verdi.

**How to apply:** Altyapı/kaynak artırımı gerektiren bir öneri (yeni makine, disk büyütme, ek servis vb.)
yapmadan önce bunun aylık maliyete etkisini kabaca tahmin et ve 10 $/ay eşiğine göre kullanıcıyı uyar.
