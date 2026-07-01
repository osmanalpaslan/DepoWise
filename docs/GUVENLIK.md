# DepoWise — Güvenlik Planı (ücretsiz önlemler)

> Yazılım bilgisi gerektirmeyen özet + yol haritası. Hepsi **ücretsiz**. Öncelik sırasıyla.

## Şu an HAZIR olanlar ✅
- **Şifreler hash'li** saklanır (BCrypt) — düz metin şifre yok.
- **Deny-by-default yetki** + **tenant izolasyonu**: her firma yalnız kendi verisini görür; servis katmanında `company_id` + yetki kontrolü (47 dosyada company_id, 34 dosyada yetki/tenant guard).
- **Yetki yükseltme engeli**: kimse sahip olmadığı yetkiyi başkasına veremez.
- **Brute-force'a kısmi koruma**: login denemeleri (geliştirilebilir).
- **Makine aktivasyon altyapısı** (kopya koruması için doğru yaklaşım — aşağıda).

## "Dosya/exe şifreleme ile kopyalanmasın" — dürüst gerçek
- **exe'yi şifrelemek kopyalanmayı ENGELLEMEZ.** Dosya yine kopyalanır; ayrıca .NET uygulaması kolayca "decompile" edilebilir. Yani exe şifreleme beklediğin korumayı **vermez** ve yanlış güven duygusu yaratır.
- **Doğru ve ÜCRETSİZ kopya koruması = lisans/aktivasyon** (zaten kuruyoruz):
  - Makine sunucudan **onay almadan çalışmaz** (Süper Admin onayı + makine kotası + ilk kurulumda internet).
  - Kopyalanan exe **başka makinede açılınca aktive olmaz** → işe yaramaz.
  - Bu, exe şifrelemekten **çok daha etkili** ve bize **0 TL**.
- **Kod gizleme (obfuscation)** — ÜCRETSİZ araç var (Obfuscar) ama **DİKKAT**: Avalonia arayüz bağlamaları (binding) özellik ADLARINI kullanır; obfuscation bunları bozabilir. Yani bu projede riskli; **acele etmeyelim**, gerekirse dikkatli/kısıtlı uygularız. Öncelik değil.

## Web'e geçince yapılacak ÜCRETSİZ güvenlik (Option A)
1. **HTTPS zorunlu** (Let's Encrypt = ücretsiz sertifika): tüm masaüstü↔sunucu trafiği şifreli.
2. **Sunucu tarafı yetki/tenant zorlaması** (masaüstündeki guard'ların aynısı API'de) — istemciye güvenme.
3. **Token/gizli değer koruması**: cihaz token'ları sunucuda **hash'li**; istemcide Windows **DPAPI** ile şifreli sakla (ISecretProtector zaten var).
4. **Makine aktivasyon + kota** sunucuda zorlanır (kopya koruması burada gerçekleşir).
5. **Güncelleme paketi checksum + (opsiyonel ücretsiz) imza** — bozuk/sahte paket kurulmaz (checksum hazır).
6. **Rate limit + brute-force kilidi** login/enroll uçlarında.
7. **Yedeklerin sunucuda erişim korumalı** olması (token + tenant klasör).

## İsteğe bağlı (sonra, ücretsiz ama emek ister)
- **Veritabanı şifreleme** (SQLCipher): yerel .db dosyası şifreli olur. Ekstra bağımlılık/karmaşıklık getirir; gerçekten gerekirse eklenir. Şimdilik makine-aktivasyonu + tenant izolasyonu yeterli koruma.

**Özet öneri:** Para gerektiren bir şey YOK. Kopya koruması için exe şifreleme değil, **zaten kurduğumuz makine-aktivasyon/lisans** yolunu tamamlayalım; web'de **HTTPS + sunucu tarafı yetki zorlaması** en yüksek getiriyi verir.
