# DepoWise belge haritası (2026-10-10)

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
| Test ortamı ve listesi | `TEST_ORTAMI.md`, `TEST_LISTESI.md`, `TEST_EVIDENCE.md` |
| Arayüz | `UI_DESIGN_SPEC.md`, `UI_CHANGELOG.md`, `EKRAN_PARITE_HARITASI.md` |
| Kullanıcı kılavuzu | `KULLANIM_KILAVUZU.md`, `USER_GUIDE.md` |

## Arşiv — `arsiv/` (yalnız geçmiş; iş yaparken TARAMA)
- `arsiv/kok/` — eski giriş/plan dosyaları (DEVAM.md, V6 analizi DepoWise.md, PROJE_GELISTIRME_PLANI.md …).
  ⚠ V6 analizi eski mimariyi (Next.js, Neon, 6 rol) anlatır; gerçek mimari için `CLAUDE.md` §2.
- `arsiv/rapor/` — biten fazların analiz/denetim/uygulama raporları (FAZ3, MS1A, PAKET1, UI modernizasyon, Neon dönemi …).
- `arsiv/project-control/` — biten iş paketlerinin (STK, ARA İŞ, FAZ A–K …) analiz ve planları.
- `arsiv/kurallar/` — terk edilen Next.js web kuralı.
- `arsiv/QA_MOTORU_V2.md` — kapsamlı denetim modu (yalnız kullanıcı açıkça isterse).
