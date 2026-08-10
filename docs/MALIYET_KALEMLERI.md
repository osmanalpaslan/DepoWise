# DepoWise — Maddi (Ücretli) Kalemler ve Yayın Bütçesi

> **Amaç:** Para gerektiren her kalem burada toplanır. Geliştirme aşamasında **hiçbiri harcanmaz**;
> hepsinin **ücretsiz geliştirme karşılığı** yazılır. Canlıya (para kazanmaya) geçerken bu liste
> gözden geçirilir ve gerekenler alınır. Bu dosya **sürekli güncel tutulur**.
>
> Son güncelleme: 2026-07-11 · Durum: geliştirme (ücretsiz aşama)

Öncelik: **Ş** = canlıya geçerken şart · **G** = büyüyünce/gerekince · **İ** = isteğe bağlı iyileştirme

| # | Kalem | Ne işe yarar (sade) | Öncelik | Yaklaşık maliyet | Geliştirme aşamasında ücretsiz karşılığı |
|---|---|---|---|---|---|
| 1 | **Sunucu yükseltme** (Fly.io RAM/işlemci) | Aynı anda daha çok kullanıcıyı kaldırmak (şu an 256 MB × 1 makine — çok küçük) | G | ~$5–40/ay (boyuta göre) | Şu an ücretsiz küçük makinede çalışıyor; kod bu artışa hazır (para gerektirmez, tek ayar) |
| 2 | **PostgreSQL yönetilen veritabanı** | Gerçek eşzamanlı yazma + birden çok sunucu makinesi (200-300 eşzamanlı için şart) | G | Ücretsiz katman var (Neon/Supabase, sınırlı); ölçek için ~$10–50/ay | Kodu PostgreSQL'e **taşınabilir** hale getir (ücretsiz yerel/ücretsiz-katman PG ile geliştirilir) |
| 3 | **Code-signing sertifikası** | Masaüstü kurulumunda "bilinmeyen yayıncı" uyarısını kaldırır (kullanıcı güveni) | Ş | ~$100–400/yıl | İmzasız sürümde şeffaf uyarı zaten var; kod imzalamaya hazır |
| 4 | **Nesne depolama** (fotoğraf/dosya) | Çok sayıda fotoğrafı ucuz ve ölçekli saklamak | G | Ücretsiz katman var (Cloudflare R2 vb.); sonra kullanım kadar | Şu an dosyalar yerel/sunucu diskinde; kod "sağlayıcı arayüzü" ile değiştirilebilir yazıldı |
| 5 | **E-posta/SMS sağlayıcı** | Şifre sıfırlama e-postası, bildirim vb. (eğer eklenirse) | İ | Ücretsiz katman var; sonra hacme göre | Şu an gerek yok; eklenirse ücretsiz katmanla test edilir |
| 6 | **Alan adı (domain)** | `depowise.com` gibi kendi adresin (fly.dev yerine) | İ | ~$10–15/yıl | Geliştirmede `*.fly.dev` adresleri ücretsiz kullanılıyor |
| 7 | **Bağımsız güvenlik testi (pentest)** | Uzman biri sistemi dışarıdan zorlayıp açık arar | İ | Değişken (yüksek) | Temel güvenlik + kendi güvenlik taramalarımız ücretsiz yapılır |
| 8 | **Sunucu yedeği (dış depolama)** | Sunucu verisinin başka bir yerde otomatik yedeği | G | Ücretsiz katman var; sonra kullanım kadar | Yerel/sunucu yedeği kodda var; dış depolama sağlayıcısı sonra bağlanır |

| 9 | **Yük / dayanıklılık testi** (eşzamanlı kullanıcı simülasyonu) | "Kaç kullanıcıda çöker?" sorusunu **tahminle değil ölçümle** cevaplamak; sunucu yükseltmesine para harcamadan önce doğrulama | G | Değişken — bilinmiyor (araç/ortam kirası; küçük ölçekte ücretsiz araçlarla yapılabilir) | **`PRF-01` (plan §6) ücretsizdir:** koddaki darboğazlar okunarak haritalanır. Ücretli yük testi ancak yatırım sonrası, kararı *doğrulamak* için |
| 10 | **Denetim (audit) kaydı uzun süreli saklama / arşiv** | Kurumsal müşteride "kim ne zaman ne değiştirdi" kaydının yıllarca saklanması + arşivden sorgulanabilmesi | G | Ücretsiz katman var; hacim büyüyünce depolama kadar | Şema hazır (`audit_logs.before_json/after_json`); `LOG-01`+`LOG-02` veriyi üretir, saklama aynı veritabanında başlar |

## Canlıya geçerken minimum "şart" kalemler
- **Code-signing (#3)** — kullanıcı güveni için (istersen imzasız da yayınlanabilir, uyarı çıkar).
  ⚠️ **Otomatik güncellemeyle doğrudan ilgili:** `GNC-01` "izin sormadan indir/kur" hedefi imzasız
  pakette Windows/SmartScreen uyarısıyla karşılaşabilir → sessiz kurulum deneyimi için #3 gerekir.
- 200-300 eşzamanlı hedefi varsa: **Sunucu yükseltme (#1) + PostgreSQL (#2)**.
  *(2026-07-24: PostgreSQL **zaten canlıda** — Neon ücretsiz katman. #2 artık "ölçek için yükseltme".)*

## Bilinçli olarak maliyet kalemi OLMAYANLAR
- **Sürekli bağlantı (WebSocket/SignalR)** — `KARAR-5` + plan §7 `Y-5`: gereksiz olduğu **analizle**
  gösterildi. Masaüstü periyodik senkron `SNK-02`/`SNK-03` ile akıllandırıldı (seçici kadans +
  hata halinde geri çekilme). Sunucuya kalıcı bağlantı yükü **bilerek** eklenmiyor.
- **Kuyruk / Redis / harici monitoring** — `KARAR-5` + `Y-1`, `Y-4`: mevcut yük buna uzak.

## Notlar
- Bu kalemlerin **hiçbiri** geliştirmeyi durdurmaz; hepsi ücretsiz karşılıklarıyla ilerler.
- Yeni bir maddi ihtiyaç çıktıkça bu tabloya eklenir.
- **Fiyat uydurulmaz.** Kesin bilinmeyen kalem "değişken / bilinmiyor" yazılır.
- Bu dosya, ana planın **§7 YATIRIM SONRASI İŞLER** bölümünün *para* karşılığıdır; ikisi birlikte okunur.
- **Son güncelleme: 2026-08-10** — #9, #10 eklendi; code-signing ↔ otomatik güncelleme bağı ve
  "bilinçli olarak maliyet olmayanlar" bölümü yazıldı (kullanıcının uzun vadeli gereksinim listesi).
