# Öneriler — Firma Yetki Kontrol (#5) ve Personel+Yetki Birleştirme (#6)

> Durum: **#5 UYGULANDI (web).** **#6 ONAY BEKLİYOR** (Fikir A seçildi, taslak hazır).
> Son güncelleme: 2026-07-11.

---

## #5 — Firma Yetki Kontrol ekranı yeni tasarım — ✅ UYGULANDI (web, 2026-07-11)

> Kullanıcı taslağı beğendi → `CompanyPermissions.razor` yeni tasarıma geçirildi (özet kutular, arama,
> gruplama, 3 durumlu kontrol, değişiklik sayacı + yapışkan kaydet). API sözleşmesi korundu.

### (Orijinal öneri — arşiv)

**Neden değişiklik?** Mevcut ekran; düz bir modül tablosu + tek "Firmaya Özel Kısıt" onay kutusu +
renkli etiketlerden oluşuyor. "Kısıt" kavramı ve neyin kime etki ettiği net değil.

**Görsel taslak (tıklayıp deneyebilirsiniz):**
- Canlı önizleme (artifact): _sohbetteki bağlantı_
- Repoda kalıcı kopya: [`docs/mockups/firma-yetki-v2.html`](mockups/firma-yetki-v2.html) — çift tıklayıp tarayıcıda açın.

**Yeni tasarımın getirdikleri:**
1. **Üstte özet kutular:** kaç ekran *Serbest*, kaç *Yalnız Admin*, kaç *Global kilit* — tek bakışta durum.
2. **Ekran arama kutusu:** "yakıt", "rapor" yazınca ilgili ekranlar süzülür.
3. **Modüller gruplanmış:** Depo & Operasyon / Araç & Bakım / Yönetim — grup başına "tümünü serbest bırak".
4. **3 durumlu net kontrol** (onay kutusu yerine): 🟢 Serbest · 🟡 Yalnız Admin · 🔒 Global kilit (değiştirilemez).
5. **Her satırda kısa açıklama** ("Ağır raporlar — sunucu yükü" gibi) → ne yaptığı belli.
6. **Altta sabit kayıt çubuğu:** "2 değişiklik bekliyor" + Kaydet / Geri al.

**Anlam (kavram sadeleştirmesi):**
- 🟢 *Serbest* = firmanın admini bu ekranı personeline açabilir.
- 🟡 *Yalnız Admin* = bu firmada bu ekranı yalnız adminler kullanır; personele verilemez.
- 🔒 *Global kilit* = tüm firmalarda kısıtlı (Firma Tanım, Kullanıcı, Yetkiler, Sunucu ekranları…), değiştirilemez.

**Onayınız hâlinde:** Bu taslak web'deki `CompanyPermissions.razor` ekranına uygulanır (mevcut API korunur;
yalnız görünüm/işleyiş yenilenir). Masaüstünde bu ekran yok → yalnız web.

---

## #6 — Personel ile Kullanıcı/Yetki ekranlarını birleştirme (fikir aşaması)

**Sizin kurgunuz:** Uygulamaya giren kişiler hem personel hem kullanıcı. Kullanıcı hesabı olmayan çok sayıda
saha personeli var. Kullanıcı mantıken personel olduğuna göre yapı nasıl kurulmalı?

Aşağıda 3 fikir var. Beğendiğinizi söylerseniz onun görsel taslağını (XML/mockup) hazırlarım.

### Fikir A — "Tek kayıt: Çalışan" (önerilen)
Tek bir **Çalışan** listesi olur. Her çalışanın altında **"Uygulama Erişimi Ver"** anahtarı bulunur.
- Anahtar **kapalı** → sadece saha personeli (ad, unvan, telefon, şube). Hesap yok.
- Anahtar **açık** → altında kullanıcı adı, şifre, rol ve **yetki matrisi** açılır (aynı ekranda).

```
Çalışan: Ahmet Yılmaz         [Şube: Merkez]  [● Uygulama erişimi: AÇIK]
  ├─ Bilgiler:  Unvan: Şoför   Telefon: 0555…
  └─ Uygulama erişimi (açık):
        Kullanıcı adı: ahmet   Rol: Personel
        Yetkiler:  [Stok ✔] [Talep ✔] [Yakıt ✔] [Rapor ✘] …
```
- **Artı:** Tek yer, kimlik tekrarı yok, yetki doğrudan kişiye bağlı, en sade zihinsel model.
- **Dikkat:** Personel kaydı olmayan mevcut hesaplar (süper admin, doğrudan açılmış adminler) için
  "personelsiz kullanıcı" durumu ayrıca yönetilebilir kalmalı; geçiş (eşleştirme) bir kez yapılır.

### Fikir B — "Ayrı ekranlar kalsın, sadece bağla"
Personel ve Kullanıcı ekranları ayrı kalır; Kullanıcı formuna **"Personel seç"** alanı eklenir.
Böylece bir kullanıcı bir personel kaydına işaret eder, ad-soyad tekrar yazılmaz.
- **Artı:** En az değişiklik, mevcut ekranlar bozulmaz.
- **Eksi:** Yine iki ekran; "kullanıcı = personel" hissi tam oturmaz.

### Fikir C — "Sekmeli tek ekran: Çalışan Yönetimi"
Tek ekran, üç sekme: **Bilgiler | Uygulama Erişimi | Yetkiler**. A'nın aynısı ama dikey akış yerine sekmeli.
- **Artı:** Kalabalık formu böler, büyük firmada daha derli toplu.
- **Eksi:** Küçük ekiplerde sekme fazla gelebilir.

**Önerim:** **Fikir A** (küçük ekipler için sade), büyürse C'ye kolayca dönüşür.
Kural gereği bu birleştirme yapılırsa **hem web hem masaüstünde** yapılır ve diğer ekranlar bozulmaz.

### ✅ Seçim: Fikir A — görsel taslak hazır (onay bekliyor)
- Canlı önizleme (artifact): _sohbetteki bağlantı_
- Repoda kalıcı kopya: [`docs/mockups/calisan-yonetimi-A.html`](mockups/calisan-yonetimi-A.html)

**Kullanıcının sorduğu kurallar ve taslaktaki karşılığı:**
1. **Aynı kişi mükerrer eklenmesi (farklı şubelerde):** Kayıtta **ad + telefon** (veya farklı şubede aynı ad)
   benzeri çalışan varsa **uyarı** çıkar, olası eşleşme listelenir → **"Birleştir"** veya **"Farklı kişi, devam et"**.
   Böylece aynı kişi farkında olmadan iki kez açılmaz.
2. **Bir personele tek kullanıcı:** Bir çalışan kaydına en fazla **bir** uygulama hesabı bağlanır; ikinci hesap engellenir.
3. **Yanlış bağlanırsa düzeltme:** Hesap bağı düzenlenebilir/kaldırılabilir; **yalnız Admin ve üstü** roller yapar.
4. **Kullanıcı seçilmezse:** Kayıtta uygulama erişimi kapalıysa **"Bu kişi saha personeli mi?"** onay penceresi açılır.

**Sıradaki adım:** Taslağı onaylarsanız Fikir A'yı **web + masaüstüne** uygularım (kurallar dahil). Değişiklik
isterseniz taslağı güncellerim.
