---
name: guvenli-commit
description: DepoWise'da commit/push yaparken kullanıcının commitlenmemiş işlerini (sohbet/SignalR) commit'e karıştırmadan YALNIZ Claude'un değişikliklerini kaydeder. Her commit öncesi, özellikle Program.cs veya csproj gibi kullanıcının da değiştirdiği dosyalara dokunulduğunda kullan. "commit et", "kaydet", "push" ve her faz sonu kaydında geçerlidir.
---

# Güvenli commit (DepoWise)

**Kural:** kullanıcının commitlenmemiş çalışması ASLA commit'e girmez, silinmez, ezilmez, `reset/checkout/stash`
edilmez. Örnek dosyalar: `src/DepoWise.Api/Program.cs` (sohbet bölümleri), `ChatHub.cs`, `DepoWise.Desktop.csproj`,
`DepoWise.Web.csproj`, `ChatDock.razor`, `Services/ApiClient.cs`, `.gitignore`, `docs/ARAC_KURULUMU.md`, `docs/kilavuzlar/`,
`.claude/skills/alpnex-arayuz-hareket/`. Liste değişebilir → her seferinde `git status` + `git diff`'e bak.

## Adımlar
1. `git status --short` → dosyaları üçe ayır: (a) yalnız benim değiştirdiklerim, (b) yalnız kullanıcınınkiler,
   (c) İKİMİZİN de değiştirdiği (karışık). Emin değilsen `git diff <dosya>` ile hunk'lara bak.
2. (a) → `git add <dosya>` (ASLA `git add -A` / `git add .`).
3. (c) karışık dosya → **HEAD + yalnız benim yamam** index'e:
   - Yamayı dosyaya yazılmış bir node betiği olarak tut (`[eski, yeni]` çiftleri, her eski metin TAM 1 kez eşleşmeli).
   - Çalışma kopyasına uygula (kullanıcının hunk'ları korunur), sonra AYNI yamayı `git show HEAD:<dosya>` üstüne uygula
     → `git hash-object -w <geçici>` → `git update-index --cacheinfo 100644,<sha>,<dosya>`.
   - Hazır araç: `node scripts/kismi_stage.mjs <dosya> <yama.mjs> work|stage` (yama dosyasını scratchpad'e yaz).
   - Bir yamayı HEAD'e iki kez uygulama: önceki commit'te girdiyse yalnız YENİ yamayı stage et.
4. `git diff --cached --stat` + karışık dosyada `git diff --cached <dosya>` → yalnız benim satırlarım mı? Kontrol ŞART.
5. Commit mesajı: Türkçe, ne + neden; sonunda attribution satırı (sistem hatırlatmasındaki). Mesajı dosyaya yazıp
   `git commit -F` ile ver (heredoc içi kaçış tuzakları). Push öncesi `CURRENT_PHASE.md`'ye kısa girdi.
6. `git push`. Sonra `git status`: kullanıcının dosyaları hâlâ "M" olarak duruyor olmalı (dokunulmadı).

## Tuzaklar
- Bash heredoc'u (tırnaklı `<<'EOF'` dahil) `\\`'ı tek `\`'e indirebilir → ters eğik çizgi içeren yama/betiği Write
  aracıyla yaz, sonra `grep` ile doğrula.
- CRLF/LF: yama betiği dosyanın satır sonunu algılayıp korur; tüm dosyayı yeniden biçimlendiren diff = hata, geri al.
- Kullanıcının dosyasında hata görürsen düzeltme — kullanıcıya bildir.
