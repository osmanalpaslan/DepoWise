# DepoWise — Yayın Adayı (Release Candidate)

**Sürüm:** 1.0.0-rc
**Tarih:** 2026-06-27
**Branch/commit:** master (Faz 17)

## Artefakt
- Masaüstü publish (Release, framework-dependent): `artifacts/rc/desktop/` (54 dosya).
- Paket: `DepoWise-desktop-1.0.0-rc.zip` (~246 MB; artifacts/ git'e dahil DEĞİL).

### Checksum (SHA-256)
| Artefakt | SHA-256 |
|---|---|
| DepoWise.Desktop.dll | `2627A0F1081CAC6DDEA13E623A75DB060089F3AA22403FB2C6BB6CA09F32A448` |
| DepoWise-desktop-1.0.0-rc.zip | `69A7E9CF81B43AD4363459F2AC237D70DE680E09C4119FB73D581AEB777CD062` |

> Üretim için: bu paket `ReleaseService.Publish` ile zip SHA-256'sı checksum olarak yayınlanır; updater indirme sonrası bu değerle doğrular (bozuk paket kurulmaz).

## Test özeti (temiz koşu, 2026-06-27)
| Koşu | Komut | Sonuç |
|---|---|---|
| .NET çözüm build | `dotnet build DepoWise.sln -c Debug` | exit 0, 0 hata |
| .NET test | `dotnet test ...` | **187/187 geçti** |
| .NET publish (Release) | `dotnet publish ... -c Release` | exit 0 |
| Web typecheck | `npx tsc --noEmit` | exit 0 |
| Web lint | `npx next lint` | temiz |
| Web build | `npx next build` | başarılı |
| Web test | `node --test` | **66/66 geçti** |
| Repo secret tarama | `git grep` | temiz; `.env` izlenmiyor |
| npm audit | `npm audit` | 9 advisory (dev/build araçları, runtime yok — R23) |

## Kabul testleri kapsamı (kanıtlı)
Tenant izolasyonu, deny-by-default yetki, stok transaction + negatif stok + concurrency + idempotency,
ters kayıt, sayaç geriye gitmeme + log, bakım %85/95/100 + uyarı temizleme, talep onay stok değiştirmez,
yakıt fiyat snapshot + sayaç, offline kalıcılık + retry idempotency + revoked 403 + pull rollback,
update checksum/rollback, backup integrity_check + restore, dosya magic-byte/boyut, güvenlik başlıkları/
rate-limit/CSRF/redaction, COMODO dotnet host + gerçek DB kalıcılık. Bkz. `TEST_EVIDENCE.md`.

## Yayın engelleri / açık riskler
- **UI ekranları (R10):** operasyonel modüllerin servis+iş kuralı+testleri tam; Avalonia/React ekran bağlama tamamlanmadan **son kullanıcı yayını yapılmamalı.**
- **Web oturum/login akışı (R8/R9)** ve **yerel PostgreSQL canlı migration (R4/R7)** bağlanmalı.
- **Code-signing (R22)** yayın öncesi; imzasız sürümde şeffaf uyarı.
- Sync transport/UI (R19), push apply (R20), updater transport/UI (R21), web PDF (R16), şube-bazlı stok (R13).

## Karar
**Çekirdek (servis + iş kuralı + testler) yayın adayı olgunluğunda.** Son kullanıcıya genel yayın için
yukarıdaki UI/entegrasyon engelleri (özellikle R10, R8/R9, R4/R7) kapatılmalıdır. Backend/iş mantığı RC.
