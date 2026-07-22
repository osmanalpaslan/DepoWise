# Çok Makineli Kullanım Simülasyonu + Ölçek Raporu

**Tarih:** 2026-07-22 · **Ortam:** YEREL sunucu (`127.0.0.1:5299`), boş veritabanı, geliştirme PC'si
**Araç:** `tools/qa/multi-machine-sim.mjs` · **Kapsam:** §7 QA

> Neden canlıda değil: 10 makinenin bütün ekranlarda kayıt üretmesi gerçek firma verisini çöpe çevirirdi.
> Simülasyon her koşuda kendi firmasını/şubelerini/kullanıcılarını sıfırdan kurar.

## 1. Ne simüle edildi

10 ayrı kullanıcı (= 10 makine), 3 şube, aynı anda ve birbirine yakın zamanlarda, insan gibi
(80–400 ms "düşünme" payıyla) şu ekranlarda çalıştı:

Malzemeler · Araçlar · Personel · Stok Giriş/Çıkış · Yakıt (depo + dağıtım) · Günlük Faaliyet ·
Bakım · liste/arama ekranları · **ortak bir kaydı eş zamanlı düzenleme**

Kasıtlı "kötü" senaryolar: mükerrer kod, elde olmayan stoğun çıkışı, başka firmanın verisini görme
denemesi, 10 makinenin aynı sürümü aynı anda yazması.

## 2. Sonuç: ekranlar ne kadar başarılı

| Kontrol | Sonuç |
|---|---|
| **Düzenleme kilidi** (10 makine aynı sürümü yazıyor) | ✅ **Tam 1 kazanan, 9 × 409** — kaybolan güncelleme yok |
| Mükerrer kod | ✅ Reddedildi (her denemede) |
| Negatif stok | ✅ Engellendi (kural doğru; mesajı yanlıştı → düzeltildi, bkz. B-1) |
| Tenant sızıntısı | ✅ Yok — hiçbir listede başka firmanın kaydı görünmedi |
| Sunucu hatası (500) | ✅ Düzeltmeden sonra **sıfır** |
| Şube kapsamı / yetki | ✅ Beklendiği gibi |

**Düzeltmeden sonraki son koşu: 545 istek, 0 mantık hatası.**

## 3. Bulgular

### B-1 (YÜKSEK, düzeltildi) — İş kuralı hataları "beklenmeyen sunucu hatası" gösteriyordu
Stokta olmayan miktarı çıkarmaya çalışınca **HTTP 500** ve *"Sunucuda beklenmeyen bir hata oluştu"*
mesajı dönüyordu (simülasyonda 25 kez). Kural aslında **doğru** çalışıyor — negatif stok engelleniyor —
ama `NegativeStockException` merkezi hata katmanında tanınmadığı için genel 500'e düşüyordu.
Kullanıcı "yetersiz stok" yerine anlamsız bir hata görüyordu; teknik destek de yanlış yere bakardı.

Aynı kusur `MeterBackwardException`'da da vardı (sayaç geriye alma).

**Düzeltme:** ikisi de **400** + gerçek iş mesajı (*"Negatif stok engellendi: mevcut 70, talep 100000."*).
`UpdateFailedException` bilerek 500'de bırakıldı — dosya yolu sızdırabilir.
**Doğrulama:** tekrar koşuda 500 sayısı 25 → **0**.

### B-2 (ORTA, tasarım kararı gerekiyor) — Giriş hız sınırı ortak IP'de tıkanabilir
Giriş ucu **IP başına 30 giriş / 5 dakika** ile sınırlı (kaba kuvvet koruması — doğru bir önlem).
Fly.io arkasında gerçek kullanıcı IP'si okunuyor (`Fly-Client-IP`), yani normalde kullanıcı başına işler.

**Ama:** tek ofis internetinin (NAT) arkasındaki **30'dan fazla kişi aynı IP görünür**. Vardiya başında
hep birlikte giriş yaparlarsa 31. kişiden sonrası *"Çok fazla giriş denemesi"* alır. Bugünkü tek firma
için sorun değil; 500 kullanıcılı hedefte **kesinlikle sorun olur**.
*Öneri (uygulanmadı, senin kararın):* sınırı IP+kullanıcı adı çiftine bağla, ya da başarılı girişleri
sayma — yalnız **başarısız** denemeleri say. Böylece koruma sürer, gerçek kullanıcılar engellenmez.

## 4. Ölçek ölçümleri (500 kullanıcı sorusu)

Geliştirme PC'sinde, küçük veritabanıyla:

| Eşzamanlı | Okuma | Yazma |
|---|---|---|
| 10 | 3.807 istek/sn · p95 5 ms | 130/sn · p95 126 ms |
| 25 | 5.871 istek/sn · p95 7 ms | 455/sn · p95 98 ms |
| 50 | 6.652 istek/sn · p95 13 ms | **496/sn** · p95 143 ms |
| 100 | 5.908 istek/sn · p95 29 ms | — |
| 200 | 5.950 istek/sn · p95 51 ms | — |

**Yorum:** okuma tarafı çok rahat. Yazma ~**500/sn**'de düzleşiyor — bu SQLite'ın *tek yazıcı* sınırıdır
(aynı anda yalnız bir yazma işler). Hata oranı her seviyede **0**.

### 500 kullanıcı yeterli mi?
**Ham hız sorun değil.** Bir ERP kullanıcısı aktifken kabaca 5–10 saniyede bir istek üretir; 500 kullanıcı
≈ **50–100 istek/sn**, bunun ~%20'si yazma ≈ **10–20 yazma/sn**. Ölçülen tavanın (500 yazma/sn) çok altında.

**Gerçek duvarlar hız değil, şunlar:**
1. **SQLite tek yazıcı** — ortalama değil, *ani yığılma* riski (ay sonu, toplu içe aktarma, vardiya başı).
   500 kullanıcıda PostgreSQL'e geçiş gerekir; SQLite ayrıca **yatay ölçeklenemez** (dosya tek makinede).
2. **Tek Fly makinesi = tek arıza noktası.** Şu an yedek yok; makine düşerse herkes durur.
3. **Eşitleme hacmi.** Delta düzeltildi (bkz. `Esitleme_Test_Report.md`) ama snapshot **sayfalanmıyor**;
   ilk kurulum/tam çekme veri büyüdükçe ağırlaşır.
4. **Giriş hız sınırı** (B-2) — 500 kullanıcıda ortak IP'ler kaçınılmaz.

**Bu ölçümler geliştirme PC'sinde ve küçük veritabanında alındı.** Fly.io makinesi daha zayıftır ve
veri büyüdükçe sorgular yavaşlar; gerçek kapasite için üretim boyutlu makinede, üretim boyutlu veriyle
tekrar ölçülmeli. Bu rapor "duvar nerede" sorusunun yönünü verir, kesin kapasite belgesi değildir.

## 5. Tekrar çalıştırma

```bash
node tools/qa/multi-machine-sim.mjs http://127.0.0.1:5299 10 25
```

Yerel sunucuyu şöyle başlat (boş DB, ayrı klasör — canlıya dokunmaz):

```bash
DEPOWISE_SERVER_DATA=/tmp/simdata DEPOWISE_JWT_KEY=sim-test-key-0123456789-abcdefghijklmnop DEPOWISE_SEED_SUPERADMIN_PASSWORD=SimTest-2026 ASPNETCORE_URLS=http://127.0.0.1:5299 dotnet run --project src/DepoWise.Api/DepoWise.Api.csproj -c Release --no-launch-profile
```
