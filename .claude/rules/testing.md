---
paths:
  - "tests/**/*"
  - "**/*.{test,spec}.{ts,tsx,js}"
---
# Test

## Test düzeni — NİHAİ (kullanıcı onayı 2026-10-18; genel hâli `~/.claude/CLAUDE.md` "TEST DÜZENİ")
1. **Her iş:** hedefli testler + kritik alanda (tenant, yetki, senkron, stok/para, migration, güvenlik) mutasyon kanıtı +
   riskli alan paketi (işin dokunduğu ortak parçaları kullanan testler: senkron/migration/kopyalayıcı/PG/kaynak-metin…).
   Her iş AYRI commit.
2. **Tam takım:** iki fazda bir — AR-GE grupları: **B3 sonu · B5+B6 · B7+B8 · B9+Z**; ayrıca geniş arayüz/ortak altyapı işinden
   sonra ve **her yayından önce (zorunlu, atlanmaz)**. Arayüz değiştiyse `scripts/run_ui_tests.ps1` de.
3. Tam koşu sırasında başka test/derleme YOK (yalnız belge/analiz). Trx kopyası `artifacts/dw_tam_<faz>.trx`.
4. Tam koşuda hata → commit'lerle kaynağı bul, düzelt, alan paketi + tam koşu tekrar.
5. Yavaş test kontrolü gevşetilmeden hızlandırılır (kök neden); atlanmaz, silinmez, `Skip` edilmez.
6. Yasak: yayın öncesi tam koşuyu atlamak · PG testlerini çıkarmak · beklentiyi gevşetmek · eski trx'i "geçti" saymak
   (koşu öncesi `artifacts/test-sonuc.trx` silinir; yeni testlerin sonuçta olduğu doğrulanır).
7. Süre bildirimi + aşım kontrolü (genel kural); ölçümler hafızadaki `depowise-test-suresi` notunda.

## Genel
- Deterministik ve izole; üretim DB/sır kullanılmaz. Flaky testi retry ile gizleme.
- Kritik: tenant, yetki, rollback, eşzamanlılık, negatif stok, sayaç geriye gitme, idempotency, çevrimdışı kalıcılık, eski şema.
- **Tek yol:** `scripts/run_tests.ps1` (`-ExecutionPolicy Bypass` gerekli). Elle `dotnet build | tail && dotnet test` YAZMA —
  2026-09-04'te boru derleme hatasını yuttu ve eski ikili "hepsi geçti" dedi. Betik kilit + gerçek çıkış kodu kontrolü yapar.
- Geçici SQLite dosyalarını `depowise_` / `dw_` ön ekiyle adlandır (her koşu başında süpürülür).
- PostgreSQL testleri `[Collection("PostgresSchema")]` + `PostgresTestGuard.SkipUnlessSafe()`; ApiTestHost kullanan sınıflar da bu koleksiyonda.
- Menü/ekran/filtre sayısını sabitleyen testler (AppScreensParity, MenuRenk, MasaustuTasarimPaketi) bilinçli değişiklikte güncellenir.
