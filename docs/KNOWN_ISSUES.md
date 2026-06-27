# KNOWN ISSUES

## Açık
- **R5:** Web ve masaüstü health şu an DB'ye fiilen bağlanmıyor (web config-kontrolü, masaüstü yerel SQLite write/read). Gerçek PostgreSQL bağlantı health'i Faz 02'de eklenecek. Etki: düşük.
- **R6:** `dotnet test` çıktısında MSBuild "MSB4011 Directory.Build.props ikinci kez içe aktarıldı" benzeri bilgi mesajı görülebilir; build/test sonucunu etkilemiyor. Etki: kozmetik.
- **R2:** Üretim hosting, object storage, e-posta ve code-signing sağlayıcıları maliyet değerlendirmesi yapılmadan seçilmeyecek. Etki: yayın (Faz 15-17) öncesi.
- **R3:** Otomatik döviz kuru kaynağı kesinleşmedi; manuel kur + tarihçe güvenli fallback olarak tasarlanacak. Etki: para/maliyet modülleri (Faz 06+).
- **R4:** Yerel PostgreSQL geliştirme örneği henüz kurulu değil. SQLite şeması testlerle doğrulandı; PG tarafında migration SQL üretildi ama **canlı DB'ye uygulanmadı**. Etki: orta (Faz 03 öncesi PG örneği gerekebilir).
- **R7:** PG migration ↔ SQLite şema eşitliği şu an manuel/elle korunuyor (iki ayrı tanım). İleride şema sözleşme/parite testi düşünülmeli. Etki: orta (drift riski).
- **R22:** Code-signing (imzalı dağıtım) henüz yapılmadı; maliyetli kalem, yayın öncesi karara bırakıldı. İmzasız sürümde updater kullanıcıya şeffaf uyarı verir (signedWarning). Etki: orta (yayın öncesi).
- **R21:** UpdateService dosya tabanlı kurulum/rollback mantığı + testleri hazır; gerçek HTTP indirme transport, masaüstü güncelleme UI ekranı (yüzde göstergesi) ve canlı uygulama dosyalarının değişimi henüz bağlanmadı. Etki: orta.
- **R20:** SyncServer push'ta `accepted` işlemler şu an `sync_inbox` + `server_changes` feed'ine yazılıyor; gerçek iş tablolarına apply (upsert) iş-servisleriyle bağlanacak. Idempotency/doğrulama/conflict çekirdeği hazır. Etki: orta.
- **R19:** Sync HTTP transport katmanı (push/pull endpoint'leri), DPAPI `ISecretProtector` gerçek implementasyonu, retry/backoff ve 0-100 non-blocking ilerleme UI henüz yok (servis mantığı + testler hazır). Etki: orta.
- **R18:** Fotoğraf optimizasyonu (max 1200px/JPEG kalite) henüz uygulanmadı — şu an içerik passthrough saklanıyor (yalnız boyut/MIME/magic-byte doğrulanıyor). Gerçek resize için image lib (ör. SixLabors.ImageSharp) eklenecek. Etki: düşük (güvenlik kontrolleri tam; yalnız boyut optimizasyonu eksik).
- **R17:** İçe aktarım şu an yalnız malzeme seti (dry-run+commit). Araç/diğer setler aynı desenle (`ImportRow`/dry-run) eklenecek. Ayrıca commit'te mevcut kod "updated" sayılıyor ama alanlar güncellenmiyor (idempotent no-op); gerçek güncelleme akışı sonra. Etki: orta.
- **R16:** Talep PDF binary üretimi şu an yalnız .NET (QuestPDF). Web tarafı aynı `RequestPdfModel`'i kullanıyor ama binary render hattı (ör. server-side PDF lib) henüz eklenmedi. Etki: düşük (web PDF sonraki bir adımda).
- **R15:** Günlük faaliyet bakımında `MaintenanceService.Save` ve `daily_activities` insert ayrı transaction'larda (MaintenanceService kendi tx'ini commit eder). Her ikisi de idempotent → retry ile tutarlı; nadir partial-fail penceresinde bakım kaydı oluşup faaliyet referansı eksik kalabilir (retry düzeltir). İleride tek tx'e alınabilir. Etki: düşük.
- **R14:** `MaintenanceService.GetAlerts` GROUP BY + MAX(created_at) ile en-son bakımı seçerken SQLite bare-column davranışına dayanıyor; aynı created_at'te tie belirsiz olabilir (testlerde saat ilerletilerek garanti). İleride pencere fonksiyonu/alt sorgu ile sağlamlaştırılabilir. Etki: düşük.
- **R13:** Stok bakiyesi material-global (şube bazlı değil); transfer net-zero. Şube bazlı bakiye + şube negatif kontrolü sonraki fazda. Etki: orta (çok şubeli stok ayrımı henüz yok).
- **R11:** `material_compatible_vehicles.vehicle_id` şu an FK'siz serbest metin (vehicles tablosu Faz 08). Faz 08'de FK + referans bütünlüğü eklenecek. Etki: düşük (geçici).
- **R12:** Malzeme listesinde LIKE araması varsayılan SQLite (ASCII case-insensitive); Türkçe duyarsız LIKE override henüz eklenmedi (CLAUDE.md AlpDepo standardı). Gerekirse Faz 07+ eklenir. Etki: düşük.
- **R10:** Personel ve firma/şube modüllerinin UI ekranları (liste/form/import-export) henüz bağlanmadı; servis + iş kuralları + testler hazır. İlgili ekranlar sonraki UI fazlarında MenuBuilder/AccessControl ile bağlanacak. Etki: orta.
- **R9:** Masaüstü shell şu an **preview admin oturumu** ile menüyü gösteriyor (login akışı Faz 05). Yetki mantığı testlerle doğrulandı; gerçek oturum + firma override tema Faz 05'te bağlanacak. Etki: orta (UI önizleme).
- **R8:** Web `getServerSession` henüz oturum çözmüyor (imzalı cookie + DB session lookup Faz 05'e bırakıldı); şu an fail-closed null döner → `/api/v1/me` daima 401. Davranış güvenli; işlevsel oturum web tarafında Faz 05'te bağlanacak. Etki: orta.

## Kapatılan
- Büyük tek prompt yerine faz bazlı çalışma paketi oluşturuldu.
- Proje adı ve dosyalar DepoWise olarak standartlaştırıldı.
- CLAUDE.md ↔ V6 analiz çelişki taraması yapıldı; çelişki yok (Faz 00).
- COMODO güvenli çalıştırma zinciri (hook + UseAppHost=false + mutlak DB yolu) doğrulandı (Faz 00).
- R1 (kaynak kod yoktu): Faz 01'de çözüm iskeleti kuruldu, baseline build+test+web build yeşil.
- `next` CVE-2025-66478: 15.5.19 yamalı sürüme yükseltilerek kapatıldı (Faz 01).
