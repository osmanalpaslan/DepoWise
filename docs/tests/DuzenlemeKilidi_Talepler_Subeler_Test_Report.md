# Test Raporu — Düzenleme Kilidi: Talepler + Şube/Şantiye (İş #6)

Tarih: **2026-08-09** · Kapsam: **yalnız değiştirilen ekranlar** (Talepler, Şube/Şantiye) — CLAUDE.md §7.1
Migration: **YOK** · Production yazma/deploy: **YOK**

---

## 1. Bulunan açık (kırmızı testle kanıtlandı)

`material_requests` ve `branches` tablolarında `version` sütunu **vardı** ve her `UPDATE`'te
ilerliyordu, ama **hiçbir yerde kontrol edilmiyordu**. Sonuç: iki kullanıcı aynı talebi/şubeyi
düzenlediğinde ikincisi birincisinin değişikliğini **sessizce eziyordu** (uyarı yok, iz yok).

Kırmızı kanıt: `EditLockCoverageTests` derlenmedi — `RequestEditData.Version`, `BranchRow.Version`
ve `Update(..., expectedVersion)` **yoktu**. Yani bu iki ekranda kilit *zayıf* değil, **hiç yoktu**.

## 2. Düzeltme

Mevcut `EditLockGuard` deseni (Malzeme/Araç/Personel/Bakım Tanımı'nda zaten kullanılan) bu iki
servise uygulandı. **Yeni mekanizma icat edilmedi, yeni tablo/sütun eklenmedi.**

| Katman | Değişiklik |
|---|---|
| Servis | `RequestService.Update(..., expectedVersion)` · `BranchService.Update(..., expectedVersion)` |
| Okuma | `RequestEditData.Version` · `BranchRow.Version` (sona eklendi → geriye uyumlu) |
| API | `RequestDto.Version` · `BranchDto.Version` → eski sürüm **409 Conflict** |
| Masaüstü | Talepler + Şubeler: form açılışında sürüm saklanır, 409'da "Kaydı yenile / Formda kal" |
| Web | `Requests.razor` + `Branches.razor`: aynı sürüm alışverişi |

Ek sağlamlaştırma: `RequestService.Update`'in `UPDATE`'ine `company_id` koşulu eklendi
(tenant kontrolü zaten yukarıda vardı; bu ikinci savunma hattı).

## 3. Senaryolar

Kullanıcının istediği senaryo, her iki kayıt türü için ayrı ayrı:
**A açar · B açar · A kaydeder · B eski sürümle kaydetmeye çalışır · B reddedilir · A'nın değişikliği korunur.**

| # | Senaryo | Talep | Şube |
|---|---|---|---|
| 1 | Sürüm okunabiliyor (form açılışı) | ✅ | ✅ |
| 2 | Eski sürümle kaydetme **reddedilir** | ✅ | ✅ |
| 3 | Reddedilince **ilk kaydedenin verisi korunur** | ✅ | ✅ |
| 4 | Doğru sürümle kaydedilir, sürüm ilerler | ✅ | ✅ |
| 5 | Sürüm gönderilmezse eski davranış korunur (geriye uyumlu) | ✅ | ✅ |
| 6 | Başka firmanın kaydı, sürüm doğru olsa bile reddedilir | ✅ | ✅ |
| 7 | Redde **yan etki yok** (talep kalemleri / şube şifresi değişmez) | ✅ | ✅ |
| 8 | HTTP hattında **409** döner (400/500 değil) | ✅ | ✅ |

7 numaralı senaryo özellikle önemli: talepte `UPDATE` başarısız olsa bile eski kod kalemleri
`DELETE`+`INSERT` edecekti. Kilit reddi artık kalemlere **dokunulmadan önce** atılır; ayrıca
transaction commit edilmediği için hiçbir değişiklik kalmaz. Testle doğrulandı.

8 numaralı senaryo da gerekliydi: masaüstü `OrgServerClient` ve web sayfaları 409'u
"kayıt değişti" uyarısına çevirir; başka bir kod dönerse kullanıcı yanlış mesaj görür.

## 4. Coverage Matrix (§7.13)

| Alan | Durum |
|---|---|
| Form Açıldı · Düzenleme · Doğrulamalar · Yetki · Hata Mesajları | ✅ |
| Database (transaction / rollback / audit) | ✅ |
| Security (tenant izolasyonu, çift gönderim, race condition) | ✅ |
| UI / UX (409 → "Kaydı yenile / Formda kal") | ✅ (masaüstü + web) |
| Yeni Kayıt · Silme · Arama · Filtre · Grid · Offline · Sync · Performans | değişmedi → kapsam dışı (§7.1) |

## 5. Test sonuçları

| Paket | Sonuç |
|---|---|
| `EditLockCoverageTests` (SQLite, servis) | **12 / 12** |
| `ApiEditLockTests` (gerçek HTTP hattı) | **8 / 8** |
| `PostgresEditLockTests` (PostgreSQL, servis) | **6 / 6** |
| SQLite tam paket | **915 geçti / 0 başarısız / 31 atlandı** |
| `dotnet build DepoWise.sln` | **0 hata** |

Atlanan 31 test = `DEPOWISE_PG_URL` tanımsızken atlanan PostgreSQL testleri (ayrı koşuluyor).

## 6. Risk ve açık uçlar

- **Geriye uyumluluk:** sürüm gönderilmeyen eski çağrılar (ör. güncellenmemiş masaüstü paketi)
  aynen çalışmayı sürdürür — kilit yalnız sürüm gönderildiğinde devreye girer. Bu bilinçlidir:
  aksi halde eski istemciler kilitlenirdi.
- **Web hata metni (P3, kapsam dışı):** web `ApiClient` sunucu hatasını ham JSON olarak gösteriyor
  (`Hata 409: {"error":"..."}`). Kilit çalışıyor ama mesaj kullanıcı dostu değil. Bu tüm web
  sayfalarını ilgilendirir; bu işte değiştirilmedi (§7.1 — yalnız değişen ekran).
- **Yakıt kayıtları** hâlâ düzenlenemez (ekle-only defter, §4) → kilit uygulanamaz, gerekmiyor.
