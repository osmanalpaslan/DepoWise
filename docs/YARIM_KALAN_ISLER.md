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

### 1. Kota İzleme "Online" dedup (aynı kullanıcı web+masaüstü = 1)
- **Sorun:** Aynı kullanıcı hem web hem masaüstünden girmişse **2** değil **1** online sayılmalı (anlık login değil, kullanıcı-online durumu).
- **Aşamalar:** (a) Online sayımını yapan yeri bul (`Program.cs` "son 5 dakika" bellek-içi izleme). (b) Sayımı kullanıcı bazında **DISTINCT** yap (aynı kullanıcı tek sayılsın). (c) Web ekranını doğrula.
- **Testler:** Aynı kullanıcı 2 platformdan aktif → online=1 · farklı kullanıcılar ayrı sayılır · birim test.

### 2. Logolar (kaliteyi koruyarak projeye ekleme)
- **Kaynak:** `C:\Users\Osman Alpaslan\Desktop\Logo Dosyalarım` (dosya adları ortam/yer belirtiyor).
- **Aşamalar:** (a) Dosyaları incele (hangi ortam/yer). (b) Web ve masaüstünde doğru yerlere yerleştir. (c) **Düşük çözünürlüğe düşürme; kaliteyi koru.** (d) Login/başlık/favicon gibi yerlere bağla.
- **Testler:** Web + masaüstünde logo net görünür (bulanık değil) · farklı ekran/DPI'da bozulmaz.

---

## B. Onay / Aksiyon Bekleyenler (senden)

- **Personel ekranını gözden geçir** (canlıda): tek ekranda **"Uygulama erişimi ver"** (kullanıcı adı/şifre/rol),
  **☐ Saha personeli**, **unvan listesi + "+"**, uyarı penceresi. Beğendin mi, değişiklik ister misin?
- **Masaüstü:** açık makineler 60 sn içinde **1.0.43** güncelleme uyarısı alır; güncelleyip ekranları gör.

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
- ✅ **Firma silince 401 + firmalar yüklenmiyordu (ADR-068)** — süper admin, **içinde çalıştığı** firmayı silince
  token'daki firma geçersiz kalıyor, sonraki **her istek 401** dönüyordu (liste yüklenmiyor, ekranda silinmiş firma
  kalıyor, tekrar silme 401). Artık: firma **silinmişse** süper admin **home firmasına düşer** (oturum yaşar);
  firma **hiç yoksa** (sahte id) fail-closed korunur.
- ✅ **SİLMEDE WEB TAM OTORİTER (ADR-069)** — web'de silinen kayıt **makinelerin yerel DB'sinden de düşer**
  (silme artık LWW'yi aşar) **ve** sunucuda silinen kayıt **cihaz push'uyla diriltilemez**. Silme dışındaki
  LWW davranışı korundu. Unvan tanımları (`personnel_titles`) senkron listesine eklendi. 3 yeni test.
- ✅ **Masaüstü firma ekle/sil web ile eşitlenmiyordu (ADR-071 + ADR-072)** — kök neden: masaüstü Firma Tanım **yalnız yerel
  DB'ye** yazıyordu ve firmalar iş senkronunda hiç yoktu → sunucuya ulaşmıyordu. Artık **firmalar sunucu-otoriteli**
  ve **OFFLINE-FIRST kuyruk** (ADR-072): işlem önce **yerele** yazılır + **kuyruğa** alınır, internet gelince
  **sırayla** işlenir. Yeniden denemede **hata düşmez** (idempotent: aynı işlem tekrar gelirse mükerrer kayıt/hata yok).
  Eşitleme sırası: **1) firma → 2) sabit tanımlar/lookup → 3) iş kayıtları** (paralel değil, sırayla).
- ⚠️ **KRİTİK OLAY — sunucu diski doldu (ADR-070):** `/data` (974 MB) %100 doldu → SQLite yazamadı →
  **login dahil tüm API 500** (tam kesinti). Sebep: her masaüstü paketi ~85 MB ve eski paketler hiç
  temizlenmiyordu (11 paket = 892 MB). Eski paketler silindi (disk %100 → %17) ve **otomatik saklama
  politikası** eklendi (en yeni 3 paket tutulur). Hafızaya kaydedildi.
- ✅ **CANLIYA ALINDI (12.07):** API + Web yayında (health 200). **Masaüstü 1.0.42 YAYINLANDI.**
  Yayın sırasında **süper admin canlı girişi doğrulandı** → ADR-064 tümüyle kapandı. Test **262/262**.

> Önceki oturumlarda tamamlananların tam listesi: `DEVAM.md` §2 ve `docs/DECISIONS.md` (ADR-062/063).
