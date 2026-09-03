---
paths:
  - "tests/**/*"
  - "**/*.{test,spec}.{ts,tsx}"
---
# Test
- Deterministik ve izole test; üretim DB/secret kullanılmaz.
- Kritik: tenant, permission, rollback, concurrency, negatif stok, sayaç geriye gitme, idempotency, offline kalıcılık.
- Flaky testi retry ile gizleme.
- COMODO kanıtı host, mutlak DB yolu, WAL ve yeniden açılış kalıcılığını içerir.

## Testler NASIL çalıştırılır (2026-09-04, zorunlu)
**Tek yol:** `powershell -File scripts/run_tests.ps1` (filtre için `-Filter "KUR"`).

**Elle `dotnet build ... && dotnet test` YAZMA.** Nedeni gerçek bir olaydır: 2026-09-04'te iki koşu
aynı anda çalıştı, birincisi ikili dosyaları kilitledi, ikincisinin **derlemesi çöktü ama koşu devam
edip ESKİ kodu test etti ve "hepsi geçti" dedi.** İki kusur birleşmişti:
1. Aynı anda iki koşu engellenmiyordu.
2. `dotnet build ... | tail -n 3 && dotnet test` kalıbında **boru, derlemenin çıkış kodunu yutuyor**
   (`tail` hep 0 döner) → `&&` derleme başarısızken bile ilerliyor. **Sessizce yanlış yeşil sonuç.**

Betik ikisini de kapatır: sistem geneli kilit + derleme çıkış kodunun gerçekten kontrolü
(derleme çökerse test **çalıştırılmaz**).

**Geçici veritabanları:** testler `%TEMP%` altında SQLite dosyası üretir (191 sınıf; xUnit her test
metodu için sınıfı yeniden oluşturduğundan koşu başına ~10.000 dosya). `TempVeritabaniTemizligi`
her koşunun **başında** önceki artıkları süpürür — birikim tek koşulukla sınırlı kalır. Yeni test
sınıfı yazarken geçici dosyaları `depowise_` veya `dw_` ön ekiyle adlandır ki süpürgeye takılsın.
