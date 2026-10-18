# DepoWise Güvenlik Runbook

> 2026-10-18 (AR-GE M-13 / F-050) koda göre yeniden yazıldı. Önceki sürüm terk edilen Next.js uygulamasını (`apps/web`)
> anlatıyordu (CSP, CSRF, `ratelimit.ts`); o yapı kullanılmıyor. Değişiklik yaptıkça bu belge de güncellenir.

## Sırlar (secrets)
- Sırlar koda yazılmaz: Fly secret (`flyctl secrets`) + yerelde `.env.*.local` (repoya girmez). Ekrana/loga yazılmaz.
- API: `DEPOWISE_JWT_KEY` (zorunlu, **en az 32 bayt**; üretimde yoksa ya da kısaysa API açılmaz), `DEPOWISE_PG_URL`
  (tanımsızsa SQLite), `DEPOWISE_ISTEMCI_IP_ANAHTARI` (web ile AYNI değer; aşağıda "Hız sınırı").
- Masaüstü yayın hesabı: Windows kullanıcı ortam değişkeni (`DEPOWISE_ADMIN_USER/PASS`). Yedek şifreleme anahtarı: DPAPI
  dosyası + özel hafıza deposu (`scripts/yedek_sifre.ps1`, M-08).

## Kimlik doğrulama
- Parola özeti: PBKDF2-HMAC-SHA256, **600.000 tur**, 16 bayt tuz (`PasswordHasher`). Eski (100.000) özetler doğru şifreyle
  girişte yeni turla yeniden yazılır (`AuthService.YenidenOzetle`).
- Kullanıcı adı kilidi: 5 ardışık hatalı deneme → 5 dk (`AuthService.MaxFailures/LockWindow`); `/login` ve masaüstü
  `sync-login` jetonu aynı kuralı uygular.
- Oturum jetonu: JWT (HS256), 12 saat, `/api/auth/refresh` ile kayar. Yetkiler jetondan değil her istekte veritabanından.
  **Oturum dönemi** (`users.token_epoch`, M-10): şifre değişince/sıfırlanınca artar → önceki jetonlar 401.
- Masaüstü cihaz jetonu: `EnrollmentService.RotateDeviceToken` (eskisi anında geçersiz), sızıntıda `RevokeDevice`.

## Hız sınırı (`RateLimiter`, `Program.cs`)
- Giriş / şube şifresi: 30 deneme / 5 dk / adres · anonim liste uçları 120 / dk · makine kaydı ve indirme 30 / 5–10 dk ·
  yedek yükleme 60 / saat.
- Adres: `Fly-Client-IP`. Web kullanıcıları için web sunucusu gerçek adresi **imzalı başlıkla** iletir
  (`IstemciAdresiImzasi`, HMAC-SHA256, M-11); imza tutmazsa ya da anahtar tanımsızsa başlık yok sayılır.

## HTTP güvenliği
- CORS: yalnız web kökeni (`https://depowise-web.fly.dev`; ek köken `DEPOWISE_CORS_ORIGINS`). Tarayıcıdan API'yi doğrudan
  çağıran ekran yoktur (web Blazor Server, istekleri sunucudan yapar).
- API başlıkları: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`.
- Web başlıkları: `nosniff`, `DENY`, `Referrer-Policy: same-origin`, `X-Permitted-Cross-Domain-Policies: none`, HSTS
  (üretim). **CSP bilinçli olarak yok** (Blazor + MudBlazor satır içi betik; gerçek tarayıcıda ölçülmeden eklenmez).
- Parametreli SQL (Dapper); dosya yüklemede uzantı/MIME/imza baytı/boyut kontrolü (`FileValidation`).
- Hata yanıtı ham istisna metnini istemciye vermez (`{"error":"..."}`); ayrıntı yalnız sunucu logunda.

## Log maskeleme (`LogRedactor`)
- password/token/secret/authorization/bağlantı dizesi/session/cookie değerleri ve `Bearer …` jetonları `***` olur.
- Uygulandığı yerler: API 500 hata logu ve bakiye hesaplama hatası (`Program.cs`), masaüstü eşitleme günlüğü
  (`SyncLog`), güncelleme günlüğü (`UpdateService`). Erişim logu yalnız metot/yol/durum/süre yazar (sorgu dizesi yok).

## Yedek
- Günlük şifreli yedek (AES-256, PBKDF2 200.000) → Google Drive; düzenli geri yükleme provası (`scripts/yedek_prova.ps1`).

## Bilinen açıklar / bekleyenler
- Güncelleme paketlerinin dijital imzası + süper admin MFA (K-20 / M-01): yayın öncesi hatırlatma (`docs/arge/12`).
- Sohbet merkezi (`ChatHub`) oturum dönemini kontrol etmiyor (`docs/arge/11`).
- Kod imzalama, bağımsız sızma testi: bekliyor.

## Audit
- Kritik değişiklikler `AuditWriter` ile (kim, firma, ne, ne zaman). Ham jeton/parola audit'e yazılmaz.
