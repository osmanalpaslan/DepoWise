---
paths:
  - "apps/web/**/schema*.ts"
  - "apps/web/**/migrations/**/*"
  - "src/DepoWise.Infrastructure/**/*.cs"
---
# Veritabanı ve senkron
- Şema yalnız migration ile değişir.
- Stok hareket defteri ana kaynak; bakiye transaction içinde güncellenir.
- Operation id idempotent; retry ikinci hareket üretmez.
- Stok/sayaç/yakıt/bakım/onayda LWW yasaktır.
- Pull geçersiz sayfada rollback; cursor ilerlemez.
- Keyset sıralaması kararlı ve benzersizdir.
