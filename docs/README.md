# DepoWise belge haritası (2026-10-10; M-76 güncellemesi 2026-10-26)

> Amaç: doğru bilgiye tek seferde gitmek, eski raporları taramamak. **Güncel durum tek yerde:**
> `project-control/CURRENT_PHASE.md`. Kurallar: kökteki `CLAUDE.md` + `.claude/rules/`.

## Güncel ve bağlayıcı
| Konu | Dosya |
|---|---|
| Ne yapıldı / sırada ne var | `project-control/CURRENT_PHASE.md` |
| İş listesi (⚠ "SIRADA" maddeleri kodda doğrula — çoğu kapandı) | `project-control/TASK_BACKLOG.md` |
| Mimari kararlar (5.400+ satır — **grep ile ara, baştan okuma**) | `DECISIONS.md` |
| Bilinen sorunlar | `KNOWN_ISSUES.md` |
| Yayın / sunucu / ortam | `DEPLOYMENT.md`, `OPERATIONS.md` |
| Yedek ve geri yükleme | `POSTGRES_BACKUP_RESTORE.md`, `SERVER_BACKUP_CONTRACT.md` |
| Güncelleme paketi sözleşmesi | `UPDATE_CONTRACT.md` |
| Senkron tasarımı | `SENKRON_YOL_HARITASI.md`, `SENKRON_ID_KOD.md` |
| Güvenlik | `SECURITY.md`, `GUVENLIK.md` |
| Yetki/menü tasarımı | `ADR-221/222/223-*.md` |
| Test düzeni ve çalıştırma | `../.claude/rules/testing.md` (düzen), `../scripts/run_tests.ps1` · `run_ui_tests.ps1` (tek yol), `TEST_EVIDENCE.md` |
| Arayüz | `arge/08_avalonia_tasarim_sistemi.md` (güncel tasarım sistemi), `UI_CHANGELOG.md`, `EKRAN_PARITE_HARITASI.md` |
| AR-GE (2026-10, dal `arge/2026-10-kapsamli`) | `arge/09_yol_haritasi.md` (maddeler), `arge/10_uygulama_gunlugu.md` (yapılanlar), `arge/11_kalan_riskler.md`, `arge/12_yayin_oncesi_kontrol.md`, `arge/13_kullanici_secimleri.md` |
| Kullanıcı kılavuzu | `KULLANIM_KILAVUZU.md`, `USER_GUIDE.md` |

## Arşiv — `arsiv/` (yalnız geçmiş; iş yaparken TARAMA)
- `arsiv/kok/` — eski giriş/plan dosyaları (DEVAM.md, V6 analizi DepoWise.md, PROJE_GELISTIRME_PLANI.md …).
  M-76 (2026-10-26) ile buraya taşınanlar: `UI_DESIGN_SPEC.md` (B8 öncesi tasarım; yerine `arge/08`), `TEST_LISTESI.md`
  (2026-06 elle test listesi), `TEST_ORTAMI.md` (eski yerel kurulum notu; varsayılan şifre ve Neon dönemi bilgisi içerir).
  ⚠ V6 analizi eski mimariyi (Next.js, Neon, 6 rol) anlatır; gerçek mimari için `CLAUDE.md` §2.
- `arsiv/rapor/` — biten fazların analiz/denetim/uygulama raporları (FAZ3, MS1A, PAKET1, UI modernizasyon, Neon dönemi …).
- `arsiv/project-control/` — biten iş paketlerinin (STK, ARA İŞ, FAZ A–K …) analiz ve planları.
- `arsiv/kurallar/` — terk edilen Next.js web kuralı.
- `arsiv/QA_MOTORU_V2.md` — kapsamlı denetim modu (yalnız kullanıcı açıkça isterse).
