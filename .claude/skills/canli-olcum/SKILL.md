---
name: canli-olcum
description: DepoWise canlı (üretim) veritabanında YALNIZ OKUYAN ölçüm/doğrulama yaparken kullan — yeni uyarı/eşik kuralını yayından önce gerçek veride saymak, bir hatanın canlı veride olup olmadığını kanıtlamak, kayıt/trafik/boyut ölçmek. "canlıda kaç tane", "gerçek veride dene", "üretimde kontrol et" isteklerinde ve her yeni uyarı/eşik kuralı öncesinde çağrılır. Yazma, silme, şema değişikliği YASAK.
---

# Canlı ölçüm — salt okuma (DepoWise)

**Değişmez kurallar**
- Üretim DB (Supabase PG17, proje *Alpnex*, `depowise_prod`) üzerinde YALNIZ `SELECT`. INSERT/UPDATE/DELETE/DDL,
  `VACUUM`, fonksiyon çağıran yan etkili sorgu YOK. Gerçek firma **Oze İnşaat** verisi sadece okunur.
- Bağlantı dizesi/şifre ASLA ekrana, loga, commit'e yazılmaz. Sonuçta kişisel veri (telefon, TC vb.) gösterme;
  sayı/özet ver.
- Ağır sorgu yok: her sorguda `LIMIT`, tarih aralığı; sık tekrar etme (Supabase ücretsiz trafik ~5 GB/ay).

## Bağlantı
- Yerel araçlar **pooler** adresini kullanır (bu PC'de IPv6 yok): bağlantı `$TEMP/pgconn.txt` dosyasında
  (yoksa Supabase CLI/API'den alınır — kullanıcıya şifre SORULMAZ, ekrana yazılmaz).
- psql: `%LOCALAPPDATA%\DepoWise\pgsql17\pgsql\bin\psql.exe` (yerel test sunucusunun ikilileri).
- Oturumu salt-okuma zorla:
```powershell
$env:PGOPTIONS = "-c default_transaction_read_only=on -c statement_timeout=20000"
& "$env:LOCALAPPDATA\DepoWise\pgsql17\pgsql\bin\psql.exe" (Get-Content "$env:TEMP\pgconn.txt" -Raw).Trim() -At -f <sorgu.sql>
Remove-Item Env:PGOPTIONS
```
Sorguyu dosyaya yaz (scratchpad), komut satırına gömme. Çıktıyı özetle; ham satırları kullanıcıya dökme.

## Tipik kullanım
1. **Yeni uyarı/eşik kuralı (yayından ÖNCE zorunlu):** kuralın SQL karşılığını yaz, firma bazında kaç uyarı üreteceğini
   say. Beklenmedik çoksa (ör. yakıt sapmasında ortalama 40+ sahte uyarı üretti → medyan ile 6) kuralı düzelt, tekrar say.
2. **Hata kanıtı:** "veri mi yanlış, gösterim mi?" — ilgili kayıtları sayıyla/kimlikle doğrula; kök nedeni yaz.
3. **Boyut/trafik:** `pg_database_size`, tablo satır sayıları, sık çağrılan ucun sorgu sayısı.
4. **Firma kapsamı:** sorgularda `company_id` filtresi; firma adı → kimlik eşlemesi tek sorguyla.

## Rapor
Kullanıcıya sade dil: ne ölçüldü, sonuç (sayı), karar (kural aynen / değişti). Sorgu dosyasını `scripts/`'e koyma;
tekrar gerekecekse `docs/`'ta sorgu metnini (bağlantısız) sakla.
