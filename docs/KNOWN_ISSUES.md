# KNOWN ISSUES

## Açık
- **R1:** Uygulama kaynak kodu henüz yok → build/test çalıştırılamadı. Faz 01 iskeleti kurulunca baseline build/test alınacak. Etki: orta (faz 01 ön koşulu).
- **R2:** Üretim hosting, object storage, e-posta ve code-signing sağlayıcıları maliyet değerlendirmesi yapılmadan seçilmeyecek. Etki: yayın (Faz 15-17) öncesi.
- **R3:** Otomatik döviz kuru kaynağı kesinleşmedi; manuel kur + tarihçe güvenli fallback olarak tasarlanacak. Etki: para/maliyet modülleri (Faz 06+).
- **R4:** Yerel PostgreSQL geliştirme örneği henüz kurulu değil (Faz 02 ön koşulu). Etki: düşük (Faz 02'de ele alınır).

## Kapatılan
- Büyük tek prompt yerine faz bazlı çalışma paketi oluşturuldu.
- Proje adı ve dosyalar DepoWise olarak standartlaştırıldı.
- CLAUDE.md ↔ V6 analiz çelişki taraması yapıldı; çelişki yok (Faz 00).
- COMODO güvenli çalıştırma zinciri (hook + UseAppHost=false + mutlak DB yolu) doğrulandı (Faz 00).
