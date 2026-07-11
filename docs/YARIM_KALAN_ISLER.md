# Yarım Kalan İşler ve Testleri (Canlı Liste)

> **Bu dosya nedir?** "Yarıda kalan işlemler ne?" / "sırada ne var?" dediğinde bakılacak **tek liste**.
> Her işin **hangi aşamaları** kaldığını ve **hangi testlerin** yapılacağını gösterir. Teknik bilgi gerektirmez.
>
> **Nasıl güncel kalır?** Claude her anlamlı değişiklikten sonra bu dosyayı günceller (bir madde bitince
> "Tamamlananlar"a taşır, yeni iş çıkınca ekler). Özet burada; ayrıntı `docs/` ve `DEVAM.md`'de.
>
> Son güncelleme: **2026-07-12**

---

## A. Bekleyen İşler (sıradaki hata listesi — son promttan)

Aşağıdakiler henüz **yapılmadı**. Sıra yukarıdan aşağıya.

### 1. Silinen şubeler hâlâ her yerde listeleniyor
- **Sorun:** Silinen (pasif) şubeler, şube seçimi yapılan tüm alanlarda görünmeye devam ediyor; görünmemeli.
- **Aşamalar:** (a) Şube listeleyen ortak sorguları/uçları bul (web + masaüstü + API). (b) `is_deleted=0` filtresi eksik olan yerleri düzelt. (c) Zaten silinmiş şubeye bağlı kayıtların davranışını koru (geçmiş bozulmasın).
- **Testler:** Şube sil → tüm şube seçicilerinde (personel, kullanıcı, makine, stok, araç…) çıkmadığını doğrula · birim test: silinen şube listelenmez · build + `dotnet test`.

### 2. Firma listesi: 401 Unauthorized + silinmiş firma listeleniyor + firmalar hiç yüklenmiyor
- **Sorun:** Firma listesinde silinmiş firma görünmeye devam ediyor; tekrar silmeye çalışınca **401 Unauthorized**; ardından firmalar hiç yüklenemiyor.
- **Aşamalar:** (a) Firma listeleme + silme uçlarında yetki/oturum akışını incele (401 nereden). (b) Silinmiş firma filtresini düzelt. (c) Oturum/JWT süresi veya süper admin firma bağlamı kaynaklı 401 ise onar.
- **Testler:** Firma sil → listeden düşer · süper admin oturumu düşmeden liste yenilenir · birim test: silinmiş firma listelenmez · build + test.
- **Not:** Süper admin kilitlenme hatası (ADR-064) çözüldü; bu 401 farklı olabilir, ayrı incele.

### 3. Masaüstü firma ekle/sil web ile eşitlenmiyor
- **Sorun:** Masaüstü Firma Tanım'dan eklenen/silinen firma, zaman geçse de web ile eşitlenmiyor.
- **Aşamalar:** (a) Firmanın hangi tarafta otoriter olduğunu netleştir (firma sunucu-otoriteli mi?). (b) Masaüstü→sunucu senkron akışında firma push/pull var mı kontrol et. (c) Eksikse ekle veya kullanıcıya doğru akışı (web'den ekleme) netleştir.
- **Testler:** Masaüstünde firma ekle → web'de görün · sil → web'den düşer · senkron birim/integration testi.

### 4. Personel "Unvan" alanı sabit tanım + "+" ile yeni tanım ekleme
- **Sorun:** Unvan şu an serbest metin; sabit tanım listesi olmalı, yanında "+" ile yeni unvan eklenebilmeli.
- **Aşamalar:** (a) Unvan tanımları için veri katmanı (tablo/migration + servis). (b) API uçları (liste + ekle). (c) Web Çalışan Yönetimi ekranında seçim + "+" ekleme. (d) Masaüstünde aynısı. (Ortak ekran → **iki platform**.)
- **Testler:** Unvan ekle → listede çıkar · çalışan kaydında seçilir · tenant izolasyonu (başka firma unvanını görmez) · build + test.

### 5. Kota İzleme "Online" dedup (aynı kullanıcı web+masaüstü = 1)
- **Sorun:** Aynı kullanıcı hem web hem masaüstünden girmişse **2** değil **1** online sayılmalı (anlık login değil, kullanıcı-online durumu).
- **Aşamalar:** (a) Online sayımını yapan yeri bul (`Program.cs` "son 5 dakika" bellek-içi izleme). (b) Sayımı kullanıcı bazında **DISTINCT** yap (aynı kullanıcı tek sayılsın). (c) Web ekranını doğrula.
- **Testler:** Aynı kullanıcı 2 platformdan aktif → online=1 · farklı kullanıcılar ayrı sayılır · birim test.

### 6. Logolar (kaliteyi koruyarak projeye ekleme)
- **Kaynak:** `C:\Users\Osman Alpaslan\Desktop\Logo Dosyalarım` (dosya adları ortam/yer belirtiyor).
- **Aşamalar:** (a) Dosyaları incele (hangi ortam/yer). (b) Web ve masaüstünde doğru yerlere yerleştir. (c) **Düşük çözünürlüğe düşürme; kaliteyi koru.** (d) Login/başlık/favicon gibi yerlere bağla.
- **Testler:** Web + masaüstünde logo net görünür (bulanık değil) · farklı ekran/DPI'da bozulmaz.

---

## B. Onay / Aksiyon Bekleyenler (senden)

- **Canlı web süper admin girişini test et** (`depowise-web.fly.dev`). Çalışıyorsa ADR-064 tümüyle kapanır.
  Hâlâ "kullanıcı adı veya parola hatalı" derse farklı kök neden var → haber ver.
- **Masaüstü paketi yayını:** Çalışan Yönetimi (Faz4) ve ADR-064 fix'inin masaüstünde görünmesi için yeni
  sürüm paketlenip yayınlanmalı (`node scripts/publish_release.mjs …`). Süper admin kimliği gerekir.
- **Çalışan Yönetimi görsel onayın:** Beğendin mi, değişiklik ister misin?

---

## C. Bu Oturumda Tamamlananlar (2026-07-12)

- ✅ **Çalışan Yönetimi masaüstü (Faz4)** — web ile eşit (rozet, mükerrer uyarı, hesap açma, saha onayı, bağ kaldır). Test 253/253.
- ✅ **KRİTİK süper admin kilitlenme (ADR-064)** — firma silme süper admini pasife almaz + açılışta self-heal + regresyon testi. Canlı API redeploy edildi.

> Önceki oturumlarda tamamlananların tam listesi: `DEVAM.md` §2 ve `docs/DECISIONS.md` (ADR-062/063).
