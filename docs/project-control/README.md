# Alpnex — Proje Kontrol Sistemi (Tek Doğru Kaynak)

> Oluşturuldu: **2026-08-11** · Bu klasör projenin **kalıcı hafızasıdır**.
> Sohbet geçmişi kaynak DEĞİLDİR. Yeni bir oturumda **önce bu klasör okunur**.

---

## 🔻 HER OTURUMDA OKUMA SIRASI (zorunlu)

```
1. CURRENT_PHASE.md   → neredeyiz, sıradaki görev ne?
2. MASTER_ROADMAP.md  → faz sırası ve bağımlılıklar
3. TASK_BACKLOG.md    → görevin ayrıntısı ve kabul ölçütü
4. git status + git log → kayıt ile GERÇEK durum uyuşuyor mu?
5. Fark varsa → GERÇEK durum esastır; farkı raporla ve bu dosyaları düzelt
```

**Hiçbir görev bu dosyalar güncellenmeden "tamamlandı" sayılmaz.**

---

## Dosyalar

| Dosya | İçerik |
|---|---|
| [`CURRENT_PHASE.md`](CURRENT_PHASE.md) | Aktif faz, son tamamlanan iş, **SIRADAKİ İŞ** |
| [`MASTER_ROADMAP.md`](MASTER_ROADMAP.md) | Tüm fazlar, sıra, bağımlılık ağacı, maliyet sınıfı |
| [`TASK_BACKLOG.md`](TASK_BACKLOG.md) | Tüm görevler (ID, durum, bağımlılık, kabul ölçütü) |
| [`PARITY_MATRIX.md`](PARITY_MATRIX.md) | Web ↔ Masaüstü ekran ve özellik paritesi |
| [`AUDIT_2026-08-11.md`](AUDIT_2026-08-11.md) | Kapsamlı denetim bulguları (sync, update, yetki, DB, API, sektör, ön muhasebe) |
| [`OPEN_QUESTIONS.md`](OPEN_QUESTIONS.md) | Kullanıcı kararı bekleyen konular |

## Bu klasörün DIŞINDAKİ bağlayıcı kayıtlar

| Dosya | Rolü |
|---|---|
| `../../CLAUDE.md` | Çalışma kuralları (bağlayıcı) |
| `../DECISIONS.md` | Mimari karar kayıtları (ADR) |
| `../KNOWN_ISSUES.md` | Bilinen hatalar |
| `../DEPLOYMENT.md` | Ortam değişkenleri + deploy kontrol listesi |
| `../POSTGRES_BACKUP_RESTORE.md` | Yedek/geri yükleme prosedürü |
| `../OPERATIONS.md` | Operasyon runbook |
| `../PROJE_DURUMU_VE_ILERLEME.md` | **Geçmiş** ilerleme günlüğü (2026-08-11 öncesi) |

> ⚠️ `PROJE_DURUMU_VE_ILERLEME.md` ve `PROJE_GELISTIRME_PLANI.md` **arşiv** niteliğindedir.
> 2026-08-11'den itibaren **bu klasör bağlayıcıdır**; çelişkide bu klasör kazanır.

---

## Yeni ekran/özellik geliştirirken zorunlu kontrol listesi

Her yeni iş için şunların **hepsi** cevaplanır (biri atlanırsa iş bitmemiştir):

- [ ] Web tarafı yapıldı mı?
- [ ] Masaüstü tarafı yapıldı mı? (yoksa **bilinçli fark** olarak PARITY_MATRIX'e yazıldı mı?)
- [ ] API ucu var mı, iki ortam da **aynı** ucu mu kullanıyor?
- [ ] DB değişikliği gerekiyor mu → migration + iki lehçe (SQLite/PostgreSQL)
- [ ] `AppModules.All` yetki kataloğuna eklendi mi?
- [ ] Ekran görünürlük sistemine dahil oldu mu? *(GRN-01 tamamlanınca)*
- [ ] Senkron gerekiyor mu → `BusinessSyncService.Tables`
- [ ] Çevrimdışı davranışı tanımlandı mı?
- [ ] Yetki kontrolü **hem UI hem servis** katmanında mı?
- [ ] Tenant (firma) izolasyonu yazma **ve** okuma yollarında mı?
- [ ] Test eklendi mi?
- [ ] Bu dosyalar güncellendi mi?
