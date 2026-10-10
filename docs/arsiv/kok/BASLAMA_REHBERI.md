# DepoWise Başlama Rehberi

1. Bu paketin içeriğini **boş veya mevcut DepoWise proje klasörünün köküne** kopyalayın.
2. Mevcut kod varsa önce yedeğini alın ve Git durumunu kontrol edin.
3. Claude Code'u proje klasöründe açın.
4. `CLAUDE.md` dosyasının kökte olduğunu doğrulayın.
5. `prompts/00_KAYNAK_ANALIZI_REPO_KESFI_VE_KESIN_PLAN.md` dosyasını açın, tamamını Claude Code'a gönderin.
6. Claude yalnız Faz 00'ı bitirsin. Sonuçta test/kanıt ve "sıradaki tek iş" bölümünü kontrol edin.
7. Sonraki oturumda bir sonraki prompt dosyasını verin.

## Geliştirme bilgisayarında çalıştırma
- Bu makinede COMODO kurulu değil (2026-07-09'dan itibaren, ADR-056) — proje EXE/BAT'a çift tıklama yasağı kalktı.
- Önerilen yöntem yine de: Derleme `dotnet build`; Çalıştırma `dotnet run --project src/DepoWise.Desktop` veya `dotnet <tam DLL yolu>`.
- Uygulama içindeki tanılama ekranında process host'un `dotnet`, DB yolunun `%LOCALAPPDATA%\DepoWise\Data` altında ve WAL'ın aktif olduğunu doğrulayın.
- Eğer ileride tekrar COMODO'lu bir makinede çalışılırsa: `docs/COMODO_RUNBOOK.md`'deki eski kurallar ve hook geri eklenmelidir.

## Claude zorlanırsa
Claude'a şu kısa mesajı verin:

> Önce PROJECT_STATE, DECISIONS, KNOWN_ISSUES ve TEST_EVIDENCE dosyalarını güncelle. Sonra bu fazda tamamlananları ve kalan tek işi yaz. Sonraki faza geçme.

Ardından `/compact` çalıştırın ve aynı faz promptunu "state dosyalarından kaldığın yerden devam et" diyerek tekrar verin.
