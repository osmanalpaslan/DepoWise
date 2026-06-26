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
