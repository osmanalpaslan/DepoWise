# 13 — Ürün sahibinin seçimleri (2026-10-13, `Alpnex Seçim Listesi.xlsx`)

> Kaynak: kullanıcının doldurduğu Excel (masaüstü kopyası; şablon `Alpnex_Secim_Listesi.xlsx`). Kararlar sayfasında
> seçimler "Not" sütununa yazıldı — esas alınan odur. Bu dosya B fazlarının BAĞLAYICI kapsamıdır.

## A) Kararlar
| No | Karar | Yorum / uygulama notu |
|---|---|---|
| K-01 | **b** — hakediş/taşeron yapılmaz; hakediş programı + Logo/Mikro'ya aktarım | M-77 (hakediş) kapsam dışı |
| K-02 | **a** — ön muhasebe kalır; Logo/Mikro aktarım dosyası + e-Fatura XML içe alma | M-52 kapsamda |
| K-03 | **a** — e-İrsaliye için önce mali müşavir; o güne kadar irsaliye no/tarih alanı (M-32) | entegratör yok |
| K-04 | **a** — onayda ilk onay kazanır, diğeri çakışma listesine | M-18; CLAUDE.md §3 ile uyumlu |
| K-05 | **a** — bakım malzemesi + açılış stoğu eksiye düşebilir (bilinçli istisna) ama uyarı verir | §3'e istisna olarak yazılacak |
| K-06 | **a** — çevrimdışı eksi stok kabul edilir + "eksi stok oluştu" uyarısı/listesi | M-16 |
| K-07 | **a** — yakıt/bakım/talepte ezme yok, çakışma listesi | M-18 |
| K-08 | **a** — web çift kayıt düzeltilir | M-04 |
| K-09 | **a** — siparişe bağlı malda "stoğu etkilesin" kapalı + çift giriş uyarısı; uzun vadede faturayı mal kabulden oluştur | M-24, M-25 |
| K-10 | **a** — "İptal … Kaydet" (Kaydet en sağda) | M-61 |
| K-11 | **a** — ana düğme sabit kehribar | M-59 |
| K-12 | **a** — .NET 10 geçişine hemen başla (dalda; yayın ayrı karar) | M-71 |
| K-13 | **a** — para işlemlerinde onay; toplu girişte sonda tek onay | M-51, M-34 |
| K-14 | **a** — puantaj şimdilik yok | — |
| K-15 | **değişiklik yok** — kullanıcı: "firma bilgileri yalnız süper admine listeleniyor, diğer firmalara süper admin verilmeyecek; sorun değil." ⚠ Not: `/api/public/companies` (Program.cs:436) GİRİŞ ÖNCESİ, kimlik doğrulamasız tüm firma adlarını döndürür (ekrandan bağımsız). Kullanıcıya bildirildi; kararı değişmedikçe dokunulmaz. | M-13'ten "firma listesi gizleme" çıkarıldı |
| K-16 | **a** — GitHub deposu özel (ücret çıkarsa önce sorulur) | |
| K-17 | **a** — güncelleme en fazla 7 gün ertelenir; kritik güvenlik güncellemesi zorunlu | M-12 |
| K-18 | **a** — uygulama içi hatırlatma | M-42 kullanıcı seçimiyle "Daha sonra" |
| K-19 | **a + çift yön** — kiralık makine için otomatik cari borç YOK, maliyet/gelir raporu; ama iki yön desteklenmeli: firma başkasından kiralar (maliyet/borç) VE nadiren başkasına kiraya verir (gelir/alacak). Borç/alacak faturayla, her iki yönde. | M-39 kapsamı genişler |
| K-20 | **b** — paket imzası + süper admin MFA ŞİMDİ YAPILMAZ. **Canlıya alma öncesi hatırlatılacak** → `12_yayin_oncesi_kontrol.md` | M-01 ertelendi |
| K-21 | **a** — belge numarasına bilgisayar kodu (`TLP-2026-A3-0042`) | M-78 |
| K-22 | **b** (önerimizden farklı) — aynı kod iki bilgisayarda açılırsa sistem otomatik ek koyar (`MLZ-100-2`) | M-78 içinde; kullanıcıya bildirim + birleştirme olanağı önerilir |

## B) Maddeler
- **Yapılacak (önerilen/onaylı):** M-02…M-41 (M-01 ve M-42 hariç), M-46…M-48, M-51…M-56, M-59…M-64, M-66…M-74, M-76, M-78…M-85.
- **İsteğe bağlıyken "Evet":** M-43, M-44, M-49, M-50, M-57, M-58, M-65, M-75.
- **İsteğe bağlı + "Önerine uyuyorum":** M-31, M-38, M-40, M-82 → düşük öncelikte, ilgili fazın sonunda yapılır.
- **Ertelenen:** M-01 (K-20 — canlıya alma öncesi hatırlatma), M-42 ("Daha sonra").
- **Yapılmayacak:** M-45, M-77.
