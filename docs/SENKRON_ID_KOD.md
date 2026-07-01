# DepoWise — Offline Senkron: Aktivasyon, ID/Kod Stratejisi, Eşitleme

> Çok firma + çok şube + çok makine, makineler offline çalışabilir. Bu belge çakışmasız
> ID/kod üretimini ve aktivasyon/eşitleme sürecini tanımlar. (Web/Option A fazında uygulanır.)

## 1) Aktivasyon + offline login (onaylı model)
- **Yeni/kopyalanan makine (onaysız):** login ekranı **internet ŞART** — makine sunucuya enroll olur, **Süper Admin onaylamadan çalışamaz**.
- **Onaydan sonra:** token + veri yerelde → **internet olmadan da login/çalışma** serbest.
- **Kopyalanan exe başka makinede:** yeni/onaysız cihaz olarak görünür → internet + Süper Admin onayı gerektirir → tek başına **işe yaramaz** (asıl kopya koruması budur, exe şifreleme değil).
- Makine kotası aşılırsa yeni makine aktive olamaz (sunucuda zorlanır).

## 2) ID/Kod çakışması — DOĞRU süreç
### İç birincil anahtar `id` → DEĞİŞMEZ (zaten güvenli)
- Tüm tablolarda `id = GUID` (Guid.NewGuid). GUID **küresel benzersiz** → offline farklı makineler bile **asla aynı id üretmez**. Çok firma/şube/makine sorun DEĞİL. **Değişiklik gerekmez.**

### İnsan-okur kodlar / belge numaraları → MAKİNE ÖNEKİ eklenecek
- Sorun burada: `doc_no` (TLP-YYYY-NNNN), araç `internal_code`, stok belge no gibi alanlar **sıralı** (`MAX+1`, `company_id` bazında benzersiz). İki makine **offline** iken ikisi de aynı sırayı (ör. TLP-2026-0001) üretir → **eşitlemede çakışır.**
- **Çözüm (kullanıcının önerisi doğru): her makineye kısa bir MAKİNE KODU** (enrollment/onayda Süper Admin/sunucu atar; 2-3 hane, ör. `01`, `A3`). Firma içinde benzersiz.
- Kod üreticiler bu makine kodunu **arka planda otomatik** gömer; kullanıcı elle girmez:
  - Belge no: `TLP-01-2026-0001` (makine 01), `TLP-02-2026-0001` (makine 02) → çakışma yok.
  - Araç iç kodu: kullanıcı öneki + makine segmenti + sıra (ör. `KM-01-002`).
- Sıra artık **makineye özel** (`MAX+1` yalnız o makinenin kendi kodları içinde) → makineler arası yarış yok.
- Benzersizlik index'leri `(company_id, doc_no)` aynı kalır; makine kodu gömülü olduğu için küresel benzersiz → **sunucuda yeniden numaralandırma GEREKMEZ.**

### Neden alternatifler değil
- **Sunucu-atamalı merkezi sıra:** en temiz ama **oluşturma anında online** ister → offline'ı bozar. RED.
- **Sunucudan numara bloğu tahsisi:** karmaşık. Makine öneki daha basit ve offline-dostu. TERCİH.

## 3) Eşitleme (sync) davranışı
- **Online iken anlık:** değişiklikte outbox'a yazılır + hemen push (event-driven) + periyodik yedek tur.
- **Ana ekranda "Sunucuya bağlı" göstergesi:** online/offline + son eşitleme zamanı (sunucu health ping ile).
- **Uzun offline sonrası PARÇA PARÇA gönderim:** bekleyen çok kayıt varsa outbox **sayfalar halinde** (ör. 100 işlem/istek) gönderilir, aralarında kısa bekleme/backoff → **sunucu boğulmaz.** (SyncServer.Push zaten liste alır; istemci sayfalar.)
- Çakışma politikası: kritik entity'lerde sunucu doğrulaması (LWW yok); düşük-riskli entity'lerde `version/updated_at` (mevcut SyncPolicy).

## 4) Uygulama sırası (web/Option A)
1. Enrollment/onayda **makine kodu** ata (kısa, firma içinde benzersiz) → yerelde `sync.machine_tag`, sunucuda sakla.
2. Kod/belge üreticilerini makine kodunu gömecek şekilde güncelle (id GUID'e DOKUNMA).
3. Sunucu push/pull HTTP uçları + online anlık + offline sayfalı gönderim + backoff.
4. Ana ekran bağlantı göstergesi.
