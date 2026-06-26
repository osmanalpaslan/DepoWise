# KNOWN ISSUES

## Açık
- **R5:** Web ve masaüstü health şu an DB'ye fiilen bağlanmıyor (web config-kontrolü, masaüstü yerel SQLite write/read). Gerçek PostgreSQL bağlantı health'i Faz 02'de eklenecek. Etki: düşük.
- **R6:** `dotnet test` çıktısında MSBuild "MSB4011 Directory.Build.props ikinci kez içe aktarıldı" benzeri bilgi mesajı görülebilir; build/test sonucunu etkilemiyor. Etki: kozmetik.
- **R2:** Üretim hosting, object storage, e-posta ve code-signing sağlayıcıları maliyet değerlendirmesi yapılmadan seçilmeyecek. Etki: yayın (Faz 15-17) öncesi.
- **R3:** Otomatik döviz kuru kaynağı kesinleşmedi; manuel kur + tarihçe güvenli fallback olarak tasarlanacak. Etki: para/maliyet modülleri (Faz 06+).
- **R4:** Yerel PostgreSQL geliştirme örneği henüz kurulu değil. SQLite şeması testlerle doğrulandı; PG tarafında migration SQL üretildi ama **canlı DB'ye uygulanmadı**. Etki: orta (Faz 03 öncesi PG örneği gerekebilir).
- **R7:** PG migration ↔ SQLite şema eşitliği şu an manuel/elle korunuyor (iki ayrı tanım). İleride şema sözleşme/parite testi düşünülmeli. Etki: orta (drift riski).
- **R8:** Web `getServerSession` henüz oturum çözmüyor (imzalı cookie + DB session lookup Faz 05'e bırakıldı); şu an fail-closed null döner → `/api/v1/me` daima 401. Davranış güvenli; işlevsel oturum web tarafında Faz 05'te bağlanacak. Etki: orta.

## Kapatılan
- Büyük tek prompt yerine faz bazlı çalışma paketi oluşturuldu.
- Proje adı ve dosyalar DepoWise olarak standartlaştırıldı.
- CLAUDE.md ↔ V6 analiz çelişki taraması yapıldı; çelişki yok (Faz 00).
- COMODO güvenli çalıştırma zinciri (hook + UseAppHost=false + mutlak DB yolu) doğrulandı (Faz 00).
- R1 (kaynak kod yoktu): Faz 01'de çözüm iskeleti kuruldu, baseline build+test+web build yeşil.
- `next` CVE-2025-66478: 15.5.19 yamalı sürüme yükseltilerek kapatıldı (Faz 01).
