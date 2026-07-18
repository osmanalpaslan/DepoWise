# Platform önceliği — Masaüstü ÖNCE, ama web EKSİK BIRAKILMAZ (kullanıcı kuralı, 2026-07-19)

Kullanıcı test ve kullanımı ağırlıklı **masaüstü** üzerinden yapıyor; sorunsuz işleyiş için öncelik masaüstündedir.

- **Her geliştirme/hata düzeltmede önce MASAÜSTÜ** çalışır ve sorunsuz hale getirilir (öncelik + test odağı burada).
- **Ama WEB de aynı geliştirmeyi almalı** — web eksik/yarım bırakılmaz. İş "tamamlandı" sayılmaz, web karşılığı
  yapılmadıkça. (CLAUDE.md §4: web ve masaüstü işlevsel olarak eşit; bu kural sıralamayı netleştirir: masaüstü
  önce yapılır/test edilir, web hemen ardından tamamlanır.)
- Kullanıcı masaüstünde bir sorun bildirdiğinde: **aynı sorun büyük olasılıkla web'de de vardır** — masaüstünü
  düzeltirken web'i de kontrol et ve düzelt.
- Sıra önemli değilse: masaüstü + web'i AYNI iş biriminde (aynı ADR/commit grubu) tamamla ki biri geride kalmasın.
