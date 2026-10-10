---
paths:
  - "apps/web/**/*.{ts,tsx}"
---
# Web/API
- TypeScript strict; `any` yalnız dar kapsam ve gerekçeyle.
- API girdisi Zod; standart hata + correlation id.
- Tenant session'dan; payload company_id reddedilir.
- Drizzle sorgularında tenant ve soft-delete helper zorunlu.
- Para floating point değil; decimal/currency güvenli taşınır.
- Route testleri: başarı, validation, permission, tenant, rate limit ve hata.
