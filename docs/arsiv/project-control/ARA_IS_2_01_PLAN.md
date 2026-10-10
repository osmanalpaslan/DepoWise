# ARA İŞ 2 + YENİ ARA İŞLER — BİRLEŞİK UYGULAMA PLANI (2026-08-29) — ⛔ KOD YOK · UYGULAMA ONAYI BEKLİYOR

> Kaynak: [ARA_IS_2_00_ANALIZ.md](ARA_IS_2_00_ANALIZ.md) + kullanıcının 2026-08-29 devam promptu.
> Production'a bağlanılmadı; kod/migration/deploy YOK.

## 1. KAPSAM TESPİTİ — "Yeni İş A–G" ↔ "ARA İŞ 2 İş 1–7" BİREBİR AYNI İŞLER

Kullanıcının devam promptundaki yeni işler, ARA İŞ 2 analizindeki işlerin kendisidir (yeni kapsam YOK):

| Yeni İş | = ARA İŞ 2 | Not |
|---|---|---|
| A1 Fotoğraf | İş 1 | + web görüntüleme/silme kontrolü (zaten PK-F2 kapsamında) |
| A2 Yakıt rapor tarih | İş 2 (PK-T1/T2/T3) | analizdeki bulgu aynen teyit edildi |
| A3 Diğer tarih alanları taraması | İş 2 / PK-T4 | SALT tarama + rapor; kendiliğinden düzeltme YOK |
| B Yakıtı Veren | İş 3 | "Yakıtı Alan"a uygulanmaz |
| C Günlük raporlar | İş 4 | fuel-daily + stock-movements-daily |
| D Günlük Faaliyet Detay | İş 5 | tarih zorunlu · çoklu tip seçimi · boş seçim=tümü |
| E Rapor yetkileri uyumu | İş 4+5'in yapısal parçası | 9. anahtar `report_daily_activity` dahil üç katman süzme |
| F Custom Rapor | İş 6 | **PAKET-1 DIŞI — ayrı faz** (kod yok) |
| G Ekip+Onay | İş 7 | **PAKET-1 DIŞI — ayrı faz** (kod yok) |

## 2. KARAR KAYDI (kullanıcı, 2026-08-29 devam promptu §III)

**PK-F1=A · PK-F2=EVET · PK-F3=yalnız Düzenle modu + Silme yetkisi · PK-F4=A · PK-F5=A ·
PK-T1=A · PK-T2=EVET · PK-T3=A · PK-T4=A · PK-V1=A · PK-G1=A · PK-G2=A · PK-D1=A ·
İş 6 = ayrı faz (kodlanmaz) · İş 7 = ayrı faz (kodlanmaz).**

Bekleyen YENİ ürün kararı YOK — tek eksik: kullanıcının **UYGULAMA BAŞLASIN onayı**.

## 3. UYGULAMA SIRASI (kullanıcı §IV — S adımları)

### S1 — İş 2: Yakıt raporu düzeltmeleri
- **S1a (PK-T2):** Masaüstü yakıt yazım tarihi düzeltmesi — `FuelViewModel` dağıtım + depo dolum tarih
  dönüşümleri `ReportDateRange.ToMs` semantiğine (UTC gece yarısı; web ile birebir). Web'e DOKUNULMAZ
  (doğru olduğu kanıtlı). Düzenleme yolu varsa aynı dönüşüm orada da doğrulanır.
- **S1b (PK-T1):** `FuelConsumption` yalnız aralıkta verisi olan araçları listeler (tam-filo sözleşmesi
  YALNIZ bu raporda değişir). `FuelConsumptionTests` tam-filo kilidi bilinçli olarak yeni sözleşmeye
  güncellenir (raporda açıkça belirtilir); `vehicle`/`vehicle-daily`/`acc-cash` tam-filo REGRESYON
  testleriyle korunur.
- **S1c (PK-T3=A):** canlı kayıtlara DOKUNULMAZ (eski masaüstü fişleri bir gün erken görünmeye devam —
  bilinçli kabul; ileride ayrı onaylı düzeltme işi mümkün).
- **S1d (PK-T4=A):** masaüstünde aynı hata sınıfı (yerel-ofset tarih dönüşümü) SALT taraması → bulgular
  YAYIN ÖNCESİ raporda listelenir; düzeltme AYRI karara bırakılır.
- Testler: izole 1 Ağustos/2 Ağustos deterministik senaryolar · gün sınırları (00:00:00.000 /
  23:59:59.999) · masaüstü-yazım→rapor-okuma uçtan uca gün tutarlılığı (yeni kilit) · verisi olmayan
  araç yok / olan tam · iki lehçe.

### S2 — İş 3: "Yakıtı Veren" son seçim (PK-V1=A)
- Masaüstü: yerel `user_list_preferences` (yeni anahtar = yeni satır; şema değişmez); form açılışında
  oku → kişi listesinde hâlâ varsa ön-seç; kayıt BAŞARILI olunca yaz.
- Web: sunucu tarafı kullanıcı-anahtarlı tercih (mevcut `/api/me` tercih ailesi/tema deseni).
- "Yakıtı Alan" ETKİLENMEZ; silinmiş/pasif personel ön-seçilmez; kullanıcılar arası taşma yok (testli).

### S3 — İş 4: Günlük raporlar (PK-G1=A · PK-G2=A)
- `fuel-daily` "Yakıt Tüketim — Günlük": vehicle-daily gün-bölme deseni (`ms/86400000`), ama YALNIZ
  verisi olan araç+gün satırları; oranlar günün değerlerinden; TOPLAM dönemden; günlük≡dönem tutarlılık
  testi. Kategori: `report_fuel`.
- `stock-movements-daily` "Stok Hareketleri — Günlük": gün × hareket türü özeti (İşlem Sayısı · Giriş
  Toplamı · Çıkış Toplamı); tarih kolonu `IslemTarihiSql`; filtreler mevcut `StockMovementFilterSql`
  üzerinden; miktar toplamı sınırlaması InfoNote'ta. Kategori: `report_stock`. Mevcut detay raporu AYNEN.
- `ReportArchitectureTests` katalog sayacı 22→24 (bilinçli güncelleme); izole PG parite testleri; Excel.

### S4 — İş 5: "Günlük Faaliyet — Detay" (PK-D1=A)
- Yeni kategori "Günlük Faaliyet" + yeni yetki anahtarı `report_daily_activity` (AppModules'a eklenir —
  serbest-metin anahtar → MIGRATION YOK; PK-R3 gereği herkese kapalı başlar; CategoryModule + etiket +
  web ikon case'leri eklenir).
- Paylaşımlı `DailyActivityTypeOptions` (6 tip tek kaynak; MovementTypeOptions deseni; Web csproj
  paylaşımlı dosya listesine eklenir).
- YENİ filtre: ActivityType çoklu seçim (sabit liste; boş seçim = TÜM tipler) — 6 dosyalık zorunlu
  zincir (ReportCatalog bayrağı · ReportRequest SONA alan · API DTO+2 uç · Reports.razor · 
  ReportsViewModel · ReportsView.axaml) + `ReportFilterParityTests` Map satırı.
- Rapor: `RequiresDate=true` · `DataModule:"daily_activity"` · Date+Branch+Vehicle+ActivityType ·
  şube kapsamı `ReportScope.BranchSql(s, req, "da.op_branch_id")` · `is_deleted=0` · kolonlar: Tarih ·
  Kayıt Tipi · Araç · Nereden→Nereye · Operatör · Süre (gün) · Açıklama. Katalog sayacı → 25.

### S5 — İş 1: Fotoğraf (PK-F1=A · F2 · F3 · F4=A · F5=A)
- **Masaüstü sunucu-otoriteli geçiş:** ekleme/listeleme/silme mevcut `/photos` API uçlarına bağlanır
  (Evrak `OrgServerClient` multipart deseni). Çevrimdışı: net uyarı, kayıt kaydedilir, fotoğraf
  çevrimiçiyken eklenir (PK-F4=A).
- **PK-F5=A (fırsatçı taşıma):** kayıt detayı açıldığında yereldeki fotoğraflar bir defalık sunucuya
  yüklenir — YALNIZ EKLEME (silme/değiştirme yok); sha256 ile mükerrer önlenir; başarısızlık sessizce
  yerel gösterime düşer. Bu, canlıya tek yazma yüzeyidir ve YAYIN ÖNCESİ raporda ayrıca vurgulanır.
- **Web (PK-F2):** kayıtlı fotoğraf görüntüleme + silme UI bağlanır (ölü kod diriltilir; şablon ekranı
  deseni). Görüntüleme salt-okunur alanda; silme yalnız düzenleme bağlamında.
- **Silme kapısı (PK-F3):** iki platformda yalnız Düzenle modunda + `Delete` yetkisiyle görünür/çalışır
  (masaüstünde düğme `CanDelete`'e bağlanır ve düzenleme formuna taşınır; servis kapısı aynen `Delete`).
- Uygulama sırasında doğrulanacak (karar değil): web'de düzenleme dışı bir "kayıt detay görüntüleme"
  alanı varsa fotoğraflar orada da salt-okunur gösterilir; yoksa asgari kapsam düzenleme formu +
  görüntüleme bloğudur. Şablon fotoğraf ekranlarına DOKUNULMAZ.

### S6 — Kapanış doğrulaması
İlgili test aileleri + YENİ testler → izole PG süiti → TAM süit → 3 Release build → PK-T4 tarama
raporu → belgeler (CURRENT_PHASE · MASTER_ROADMAP · bu dosya · KNOWN_ISSUES gerekirse) → commit+push →
**YAYIN ÖNCESİ RAPORU (kullanıcının 14 maddelik şablonu) → DUR → "YAYINLA" onayı bekle.**

## 4. KORUNANLAR (her adımda değişmez)
ADR-181 raporları (vehicle-daily · 8 kategori) · mevcut `vehicle`/`stock-movements`/`fuel-depot` SQL'leri
(S1b dışında `fuel` da yalnız araç-listesi kriteri değişir) · Excel Merkezi · Barkod/QR · Dashboard ·
Global Arama · stok/senkron sözleşmesi · SNK-13 · M import · tenant/BranchAccess/soft-delete/ceiling/
export kapıları · migration kataloğu (azami 81) · `PostgresTestGuard` çift kilidi · kullanıcının 2
takip-dışı dosyası.

## 5. PAKET-2 (bu fazda KODLANMAZ — yalnız plan)
- **İş 6 Custom Rapor:** ayrı faz; kendi analiz→karar→migration onayı→test→yayın döngüsü.
  Çerçeve: beyaz-listeli kaynak/kolon/filtre; serbest SQL YOK; `custom_report_defs` tablosu (yalnız
  CREATE, duyuru deseni) + senkron kaydı; rapor başına dinamik yetki anahtarı + sahibine otomatik yetki;
  `Run/Dispatch` dinamik çözümleyici; grant-yazma servislerinde genişletme. PK-C1..C3 o fazda kesinleşir.
- **İş 7 Ekip+Onay:** ayrı faz; 3 alt adım (E-a Ekip Tanımı → E-b zincir motoru [v1 yalnız Malzeme
  Talebi, süreç başında zincir SNAPSHOT] → E-c Onaylamalarım + Uyarılar + red açıklaması API süzmesi).
  Yeni tablolar + migration + senkron/ayna kararı; SNK-05 "ilk geçerli onay kazanır" ve "onayda LWW
  yasak" bağlayıcı; PK-E1..E6 uç durumlarla (2 kişilik zincir, tek kişilik zincir, hiyerarşi değişimi)
  o fazın karar paketinde kesinleşir.

## 6. YAYIN KURALI
PAKET-1 bitince yayın ERTELENMEZ: yayın öncesi rapor hazırlanır, "YAYINLA" onayı istenir. O anki havuz:
**M + O + FIN düzeltmeleri (082 HARİÇ) + Rapor Ara İşi (ADR-181) + ARA İŞ 2 PAKET-1** — tamamı
migration'sız; **canlı şema 81 KALIR**; Migration082 ÇALIŞTIRILMAZ; geri dönüş = önceki imaja dönüş.
