# ARA İŞ 6 — Yakıt Dağıtımları ekranı: görünmeyen kayıtlar · sayfalama · arama

> **Kullanıcı talebi:** 2026-09-04 · **Öncelik:** ara iş (fazların önüne geçer)
> **Alındığı an:** MOB-W + TRF-01 yayınlandıktan, süit 3320/0 yeşil olduktan ve FAZ C bittikten
> hemen sonra; `STK-12`'ye **henüz başlanmamıştı** → yarım kalmış iş yok, hiçbir ekran riske girmedi.

---

## 1. Kullanıcının bildirdikleri (Yakıt Dağıtımları ekranı)

1. **Eski kayıtlar görünmüyor.** Raporda **02.08.2026** tarihli bir yakıt dağıtımı var, ama Yakıt
   Dağıtımları ekranında o kayıt görüntülenemiyor. Kullanıcının ifadesi: *"daha önceki tarihli
   kayıtları göremiyor olabilirim"* → tekil bir kayıt sorunu değil, **bir sınıf sorun**; önüne
   geçilmesi isteniyor.
2. **Liste sayfalanmıyor.** Tablo bütün kayıtları listelemeye çalışıyor; **hem webde hem masaüstünde**
   sayfa aşağı doğru uzuyor. İstenen: **Malzemeler ve Araçlar tablosundaki gibi** — seçilen sayfa
   boyutu kadar kayıt + sayfalar arası geçiş.
3. **Arama/filtre yok, mevcut arama düğmesi çalışmıyor.**
   - Ekranda **tarih bazlı** ve **araç bazlı** arama yapılabilmeli. Bu bugün yalnız raporda mümkün,
     ama **raporda düzenleme yapılamıyor** — kullanıcının kaydı bulup **düzenlemesi** gerekiyor.
   - Ekrandaki mevcut arama düğmesi **çalışmıyor**; ad/kod sorgulanamıyor.
   - Arama alanı **Sorgula düğmesine bağlanacak**; sorgu **yalnız bu düğme ve Enter tuşu** ile
     çalışacak (yazarken anlık arama YOK).
4. **Aynı sorunları yaşayacak başka ekranlar varsa** tespit edilip aynı iyileştirmeler oraya da
   uygulanacak.

## 2. Kullanıcının koyduğu çalışma kuralları (bu iş için bağlayıcı)

- Test masaüstünde yapıldı; **ama bu, hataların webde de olduğu anlamına gelmez** — webde hiç
  olmayabilir de. **İki ortam da ayrı ayrı analiz edilecek.**
- **Ortamlardan biri analiz edilmeden işleme geçilmeyecek.**
- İsteklerle ilgili ve onlardan **etkilenen bütün alanlar** eksiksiz kontrol edilecek.
- **Çalışan hiçbir yapı bozulmayacak.**
- Çalışma **tam ve eksiksiz** olacak.

## 3. Durum

| Aşama | Durum |
|---|---|
| Talep kaydı | ✅ (bu dosya) |
| Masaüstü analizi | ⏳ |
| Web analizi | ⏳ |
| Etkilenen diğer ekranların tespiti | ⏳ |
| Uygulama | ⏳ |
| Test | ⏳ |
| Yayın | ⏳ |

## 4. Analiz

_(iki ortam da incelendikçe buraya yazılacak)_
