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

### 1. Firma listesi: 401 Unauthorized + silinmiş firma listeleniyor + firmalar hiç yüklenmiyor
- **Sorun:** Firma listesinde silinmiş firma görünmeye devam ediyor; tekrar silmeye çalışınca **401 Unauthorized**; ardından firmalar hiç yüklenemiyor.
- **Aşamalar:** (a) Firma listeleme + silme uçlarında yetki/oturum akışını incele (401 nereden). (b) Silinmiş firma filtresini düzelt. (c) Oturum/JWT süresi veya süper admin firma bağlamı kaynaklı 401 ise onar.
- **Testler:** Firma sil → listeden düşer · süper admin oturumu düşmeden liste yenilenir · birim test: silinmiş firma listelenmez · build + test.
- **Not:** Süper admin kilitlenme hatası (ADR-064) çözüldü; bu 401 farklı olabilir, ayrı incele.

### 2. Masaüstü firma ekle/sil web ile eşitlenmiyor
- **Sorun:** Masaüstü Firma Tanım'dan eklenen/silinen firma, zaman geçse de web ile eşitlenmiyor.
- **Aşamalar:** (a) Firmanın hangi tarafta otoriter olduğunu netleştir (firma sunucu-otoriteli mi?). (b) Masaüstü→sunucu senkron akışında firma push/pull var mı kontrol et. (c) Eksikse ekle veya kullanıcıya doğru akışı (web'den ekleme) netleştir.
- **Testler:** Masaüstünde firma ekle → web'de görün · sil → web'den düşer · senkron birim/integration testi.

### 3. Kota İzleme "Online" dedup (aynı kullanıcı web+masaüstü = 1)
- **Sorun:** Aynı kullanıcı hem web hem masaüstünden girmişse **2** değil **1** online sayılmalı (anlık login değil, kullanıcı-online durumu).
- **Aşamalar:** (a) Online sayımını yapan yeri bul (`Program.cs` "son 5 dakika" bellek-içi izleme). (b) Sayımı kullanıcı bazında **DISTINCT** yap (aynı kullanıcı tek sayılsın). (c) Web ekranını doğrula.
- **Testler:** Aynı kullanıcı 2 platformdan aktif → online=1 · farklı kullanıcılar ayrı sayılır · birim test.

### 4. Logolar (kaliteyi koruyarak projeye ekleme)
- **Kaynak:** `C:\Users\Osman Alpaslan\Desktop\Logo Dosyalarım` (dosya adları ortam/yer belirtiyor).
- **Aşamalar:** (a) Dosyaları incele (hangi ortam/yer). (b) Web ve masaüstünde doğru yerlere yerleştir. (c) **Düşük çözünürlüğe düşürme; kaliteyi koru.** (d) Login/başlık/favicon gibi yerlere bağla.
- **Testler:** Web + masaüstünde logo net görünür (bulanık değil) · farklı ekran/DPI'da bozulmaz.

---

## B. Onay / Aksiyon Bekleyenler (senden)

- **Personel ekranını gözden geçir** (canlıda): tek ekranda **"Uygulama erişimi ver"** (kullanıcı adı/şifre/rol),
  **☐ Saha personeli**, **unvan listesi + "+"**, uyarı penceresi. Beğendin mi, değişiklik ister misin?
- **Masaüstü:** açık makineler 60 sn içinde **1.0.40** güncelleme uyarısı alır; güncelleyip ekranı gör.

---

## C. Bu Oturumda Tamamlananlar (2026-07-12)

- ✅ **KRİTİK süper admin kilitlenme (ADR-064)** — firma silme süper admini pasife almaz + açılışta self-heal + regresyon testi. Canlı API redeploy edildi.
- ✅ **#6 NİHAİ: Fikir A — tek ekran + koşullar (ADR-067)** — web + masaüstü:
  - **Personel ekranında hesap açma** ("Uygulama erişimi ver" → kullanıcı adı/şifre/rol) + "Hesabı kaldır".
  - **☐ Saha personeli** kutucuğu; hesap yoksa/açılmıyorsa + kutucuk işaretsizse **uyarı penceresi** (işaretliyse çıkmaz).
  - **Unvan sabit tanım listesi + "+"** ile yeni tanım ekleme · mükerrer kişi uyarısı · bir personele tek hesap.
  - Kullanıcılar ekranındaki "Personel seç (bağla)" + PERSONEL sütunu ikinci yol olarak duruyor.
  - *(Kısa geçmiş: önce B (ayrı ekran) yapıldı, beğenilmedi → A'ya dönüldü, koşullar korundu.)*
- ✅ **Silinen şubeler her yerde listeleniyordu (ADR-066)** — kök neden: şubeler sunucu-otoriteli ama masaüstü
  yerel kopyası sunucudan yalnız **upsert** ediliyordu; silinenler yerelde kalıyordu. Artık her girişte sunucu
  şube listesi **aynalanır** (sunucuda olmayan yerel şube pasife alınır). Regresyon testi eklendi.
- ✅ **CANLIYA ALINDI (12.07):** API + Web yeniden yayınlandı (health 200; `/api/personnel-titles` ayakta).
  **Masaüstü 1.0.40 YAYINLANDI** (self-contained 85.4 MB, checksum `6fcd76b3…`; sunucuda "en güncel = 1.0.40").
  Yayın sırasında **süper admin canlı girişi doğrulandı** → ADR-064 tümüyle kapandı. Test 258/258.

> Önceki oturumlarda tamamlananların tam listesi: `DEVAM.md` §2 ve `docs/DECISIONS.md` (ADR-062/063).
