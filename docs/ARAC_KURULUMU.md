# Claude Code araç kurulumu (MCP + Skill) — Alpnex

> Kurulum tarihi: **2026-09-03** · Makine: geliştirme PC'si (MUSTAFAALPASLAN değil)
> Bu dosya **kayıt ve ikinci PC talimatıdır**. Uygulama koduyla ilgisi yoktur.

---

## 1. Özet — ne kuruldu, ne kurulmadı

| Araç | Durum | Neden |
|---|---|---|
| **Serena** (kod zekâsı) | ✅ Kuruldu, **AÇIK**, salt-okuma | C# sembol arama/referans izleme; bu projenin asıl kazancı |
| **frontend-design** (beceri) | ✅ Kuruldu, **AÇIK** | Anthropic'in kendi becerisi; kod çalıştırmaz, ağa çıkmaz |
| **alpnex-arayuz-hareket** (beceri) | ✅ Yazıldı, **AÇIK** | Proje-yerel; hazır "motion" becerileri React içindi, bizde React yok |
| **Context7** (dokümantasyon) | ⚠️ Tanımlı ama **KAPALI** | Açık **kritik güvenlik açığı** var — aşağıda |
| **Playwright** (tarayıcı) | ⚠️ Tanımlı ama **KAPALI** | Yerleşik tarayıcı araçlarıyla büyük ölçüde aynı iş + riskli araç içeriyor |

---

## 2. Kurulan yazılımlar (makine düzeyinde)

Bunlar **projeye değil, bilgisayara** kuruldu. Hepsi geri alınabilir.

| Yazılım | Sürüm | Neden gerekti | Kaldırma |
|---|---|---|---|
| `uv` (Python paket yöneticisi) | 0.12.9 | Serena Python ile çalışıyor | `winget uninstall astral-sh.uv` |
| Python (uv'nin kendi kopyası) | 3.13.15 | Serena'nın çalışma ortamı | uv ile birlikte gider |
| `serena-agent` | 1.7.0 | Kod zekâsı sunucusu | `uv tool uninstall serena-agent` |
| **.NET 10 Runtime** | 10.0.11 | Serena'nın C# dil sunucusu (Roslyn) **.NET 10 istiyor** | `winget uninstall Microsoft.DotNet.Runtime.10` |

### ⚠️ .NET 10 hakkında — derleme hattı ETKİLENMEDİ

Yalnızca **Runtime** kuruldu, **SDK kurulmadı**. Kurulum sonrası doğrulandı:

```
dotnet --list-sdks   ->  8.0.422   (DEĞİŞMEDİ - tek SDK)
dotnet --version     ->  8.0.422
```

Yani `dotnet build` / `dotnet publish` **hâlâ SDK 8'i kullanıyor**; masaüstü paketi zaten
self-contained çıktığı için makinedeki runtime'lardan etkilenmiyor. API/Web ise Fly.io'da
kendi konteynerinde çalışıyor. **Yayın süreci değişmedi.**

---

## 3. Güvenlik incelemesi — bulgular

Üç MCP sunucusu resmi kaynaklarından (GitHub + NVD güvenlik veritabanı) incelendi.

### 🔴 Context7 — CVE-2026-75130 (CVSS 9.0, KRİTİK)

- **Ne oluyor:** Saldırgan sahte bir kütüphane kaydedip "Özel AI Talimatları" alanına
  zararlı metin koyabiliyor. Sıradan bir dokümantasyon sorgusu bu metni yapay zekânın
  bağlamına sokuyor.
- **Belgelenen etki:** **ortam dosyalarından kimlik bilgisi sızdırma** ve **dosya silme**.
- **Yama durumu net değil:** açık "2.1.2 ve öncesi" diye kayıtlı, güncel sürüm 4.0.4 — ama
  sorun **sunucu tarafındaki içerik denetiminde**, istemci sürümü yükseltmek tek başına
  kapatmıyor. Üreticinin kendi `SECURITY.md` dosyasında olaydan hiç söz edilmiyor.
- **Bizim için neden kritik:** Bu depo **herkese açık** ve `.env.test.local` içinde gerçek
  kimlik bilgileri var. Açığın belgelenen etkisi tam olarak bu dosyaları hedefliyor.
- **Karar:** tanımlandı ama **kapalı**. Kullanım kararı kullanıcıya bırakıldı.

**Not — teknik uygunluk aslında yüksekti.** Kapsam ölçüldü (parça sayısı):
Avalonia resmî doküman **29.194** · Npgsql **7.138** · ClosedXML **1.066** · QuestPDF **928** ·
LiveCharts2 **891** · Semi.Avalonia **339** (kullandığımız tema) · MudBlazor **2.031** ·
Dapper **262**. Yani araç işe yarardı; **engel teknik değil, güvenlik.**

### 🟡 Playwright MCP — Microsoft resmî, ama riskli araç içeriyor

- Sahibi gerçekten Microsoft, lisans Apache-2.0, aktif bakımda. Eski bir açık
  (CVE-2025-9611) **kapatılmış**.
- **Sorun:** `browser_run_code_unsafe` aracı, Microsoft'un kendi ifadesiyle
  *"uzaktan kod çalıştırma eşdeğeri"* ve **varsayılan olarak açık**; kısıtlama bayrakları
  bunu kapatmıyor.
- **Ayrıca gereksiz:** Claude Code'un **yerleşik tarayıcı araçları** (sayfa açma, tıklama,
  form doldurma, ekran görüntüsü, konsol/ağ okuma) aynı işi zaten yapıyor ve bu projede
  yayın kontrollerinde kullanıldı.
- **Karar:** tanımlandı ama **kapalı**. Açılırsa yalnız kendi uygulamamıza yönlendirilecek
  şekilde sınırlandı (`--isolated`, `--allowed-origins`).

### 🟢 Serena — kabul edildi, kısıtlanarak

- MIT lisans, aktif (~28.800 yıldız), **kaynak kodu hiçbir yere yüklemiyor** — indeksleme
  tamamen yerel (`.serena/` klasörü).
- Riski: kabuk komutu çalıştırma ve dosya yazma araçları var. **İkisi de kapatıldı**
  (`~/.serena/serena_config.yml` → `excluded_tools`), çünkü Claude Code'un kendi
  `Bash`/`Edit`/`Write` araçları zaten var **ve onlar sizin izin kurallarınızdan geçiyor**;
  Serena'nınkiler geçmezdi.
- Kullanım istatistiği gönderimi kapatıldı (`SERENA_USAGE_REPORTING=false`).
- Panel penceresinin her açılışta tarayıcı açması kapatıldı.

**Kapatılan Serena araçları:** `execute_shell_command`, `create_text_file`, `replace_content`,
`replace_in_files`, `insert_after_symbol`, `insert_before_symbol`, `replace_symbol_body`,
`rename_symbol`, `safe_delete_symbol`. → Serena **yalnız okur**.

---

## 4. Dosyalar — ne nereye yazıldı

| Dosya | Git'te mi? | İçerik |
|---|---|---|
| `.mcp.json` | ❌ **ignore** | MCP sunucu tanımları (makineye özel mutlak yollar) |
| `.claude/settings.local.json` | ❌ takipsiz | Hangi sunucu açık/kapalı + eklenti |
| `.serena/` | ❌ **ignore** | Serena'nın yerel önbelleği, logları, proje ayarı |
| `.claude/skills/alpnex-arayuz-hareket/SKILL.md` | ✅ takipsiz (yeni) | Proje-yerel hareket becerisi |
| `.gitignore` | ✅ **değişti** | `.serena/` ve `.mcp.json` eklendi |
| `CLAUDE.md` | ✅ **değişti** | Kısa "araçları nasıl kullan" bölümü |
| `~/.serena/serena_config.yml` | — (proje dışı) | Serena global güvenlik ayarı |

**Hiçbir uygulama kodu (`src/`, `tests/`) değiştirilmedi.**

---

## 5. Açma / kapama

Dosya: `.claude/settings.local.json`

```json
"enabledMcpjsonServers": ["serena"],
"disabledMcpjsonServers": ["context7", "playwright"]
```

- **Bir aracı açmak için:** adını `disabledMcpjsonServers` listesinden çıkar,
  `enabledMcpjsonServers` listesine ekle. Claude Code'u yeniden başlat.
- **Kapatmak için:** tersi.

> Context7'yi açmadan önce §3'teki güvenlik uyarısını okuyun. Açmaya karar verirseniz
> **yalnız tanınmış kütüphane adlarıyla** kullanın (MudBlazor, Avalonia, Npgsql gibi);
> tanımadığınız/yeni bir kütüphane adını sorgulatmayın — açığın giriş noktası tam orası.

---

## 6. İkinci PC'de kurulum

`.mcp.json` ve `.serena/` git'e girmiyor (yolları bu makineye özel). İkinci PC'de:

```bash
winget install --id astral-sh.uv --exact
winget install --id Microsoft.DotNet.Runtime.10 --exact
uv tool install serena-agent
uv tool update-shell
serena init
serena project create --name DepoWise --language csharp
serena project health-check
```

Sonra bu makinedeki `.mcp.json` dosyasını örnek alıp yolları o makineye göre yaz
(`serena.exe` konumu ve proje klasörü değişir).

---

## 7. Bağlam (context) maliyeti

Claude Code MCP araçlarını **gecikmeli yüklüyor**: oturum başında yalnız araç *isimleri*
duruyor, ayrıntılı tanımlar ancak araç gerçekten kullanılınca geliyor.

| Öğe | Oturum başı maliyet | Kullanıldığında |
|---|---|---|
| Serena (açık) | ~120 token (isimler) | Kullanılan aracın tanımı kadar |
| Context7 / Playwright (kapalı) | **0** | — |
| Beceri açıklamaları (2 beceri) | ~100–150 token | Çağrılınca tam metin |
| Bu doküman | **0** | Sadece açıkça okutulursa |

Yani kurulumun **sürekli** maliyeti oturum başına birkaç yüz token — pratikte ihmal
edilebilir. Asıl maliyet araçlar *kullanıldığında* oluşur ve o zaman zaten karşılığında
iş yapılıyor demektir.

`/context` komutu o anki dağılımı, `/usage` ise harcamayı gösterir.
