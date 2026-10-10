---
paths:
  - "tests/**/*"
  - "**/*.{test,spec}.{ts,tsx,js}"
---
# Test
- Deterministik ve izole; üretim DB/sır kullanılmaz. Flaky testi retry ile gizleme.
- Kritik: tenant, yetki, rollback, eşzamanlılık, negatif stok, sayaç geriye gitme, idempotency, çevrimdışı kalıcılık, eski şema.
- **Tek yol:** `scripts/run_tests.ps1` (`-ExecutionPolicy Bypass` gerekli). Elle `dotnet build | tail && dotnet test` YAZMA —
  2026-09-04'te boru derleme hatasını yuttu ve eski ikili "hepsi geçti" dedi. Betik kilit + gerçek çıkış kodu kontrolü yapar.
- Geçici SQLite dosyalarını `depowise_` / `dw_` ön ekiyle adlandır (her koşu başında süpürülür).
- PostgreSQL testleri `[Collection("PostgresSchema")]` + `PostgresTestGuard.SkipUnlessSafe()`; ApiTestHost kullanan sınıflar da bu koleksiyonda.
- Menü/ekran/filtre sayısını sabitleyen testler (AppScreensParity, MenuRenk, MasaustuTasarimPaketi) bilinçli değişiklikte güncellenir.
