# Ekran Parite Haritası — Masaüstü ↔ Web (Faz 0, Adım 0.1)

> Bu dosya **kaba haritadır** (isim eşleştirme, dosya bazlı). Alan/mantık düzeyinde denetim henüz
> yapılmadı — o, her ekran ele alınırken (Adım 0.2+) yapılacak. Amaç: nereden başlayacağımıza
> birlikte karar vermek.
>
> Son güncelleme: 2026-07-23

## Her iki tarafta da olan ekranlar (parite denetimi adayı)

| # | Ekran | Masaüstü | Web | Öncelik önerisi |
|---|---|---|---|---|
| 1 | Malzemeler | MaterialsView | /materials | 🔴 Yüksek (en çok kullanılan) |
| 2 | Araçlar | VehiclesView | /vehicles | 🔴 Yüksek (kullanıcı "kritik ekranım" dedi) |
| 3 | Personel | PersonnelView | /personnel | 🔴 Yüksek |
| 4 | Stok Giriş/Çıkış | StockEntryView | /stock | 🔴 Yüksek |
| 5 | Stok Sayım | StockCountView | /stock/count | 🟡 Orta |
| 6 | Günlük Faaliyet | DailyActivityView | /daily | 🔴 Yüksek |
| 7 | Yakıt | FuelView | /fuel | 🔴 Yüksek |
| 8 | Bakım | MaintenanceView | /maintenance | 🔴 Yüksek |
| 9 | Muayene/Sigorta | InspectionView | /inspection | 🟡 Orta |
| 10 | Talepler | RequestsView | /requests | 🟡 Orta |
| 11 | Tanımlar (birim/kategori/marka vb.) | SettingsView (LookupSection) | /definitions | 🟡 Orta |
| 12 | Malzeme Şablonları | (Tanımlar içinde) | /material-templates | 🟡 Orta |
| 13 | Araç Şablonları | VehicleTemplatesView | /vehicle-templates | 🟡 Orta |
| 14 | Şubeler/Şantiyeler | BranchesView | /branches | 🟢 Düşük |
| 15 | Firmalar | CompaniesView | /companies | 🟢 Düşük (çoğu süper admin) |
| 16 | Kullanıcılar | UsersView | /users | 🟡 Orta |
| 17 | Yetkiler | PermissionsView (tek ekran, sekmeli) | /permissions + /role-permissions + /company-permissions (3 ayrı sayfa) | ⚠️ **Yapısal fark** — aşağıda not |
| 18 | Yetki Şablonları | PermissionTemplatesView | /permission-templates | 🟢 Düşük |
| 19 | Makine Yönetimi | MachineManagementView | /machines | 🟢 Düşük |
| 20 | Çöp Kutusu | TrashView | /trash | 🟢 Düşük |
| 21 | Denetim Kaydı (audit) | AuditLogView | /audit | 🟢 Düşük |
| 22 | Uyarılar | AlertsView | /alerts | 🟢 Düşük |
| 23 | Raporlar | ReportsView | /reports | 🟡 Orta |
| 24 | Yedekleme (firma) | BackupView | /backup | 🟢 Düşük |
| 25 | Sunucu Yedekleri (süper admin) | ServerBackupsView | /server-backups | 🟢 Düşük |
| 26 | Sürümler/Güncellemeler | ReleasesView | /releases | 🟢 Düşük |
| 27 | Tema | ThemeSettingsView | /theme | 🟢 Düşük |
| 28 | Geliştirici Ayarları | DeveloperSettingsView | /developer | 🟢 Düşük |
| 29 | Giriş | LoginWindow | /login | 🟢 Düşük (akış farklı olmak zorunda) |

## Yalnız web'de olan (masaüstünde karşılığı yok — çoğu süper admin/sunucu işlemi)
- Kota İzleme `/quota-monitor`
- Firma Kalıcı Silme `/purge-company`
- Firma İş Verisini Sıfırla `/reset-company-business`
- Makine Yedekleri `/machine-backups`
- Sunucu Durumu `/server-status`
- (Bunlar muhtemelen kasıtlı — sunucu tarafı işlemler, masaüstünde olmaması normal olabilir. Tek tek teyit edilecek.)

## Yalnız masaüstünde olan (web'de karşılığı yok)
- Hakkında (AboutView)
- İçe/Dışa Aktarma (ImportExportView) — web'de bazı ekranlara gömülü olabilir, teyit edilecek
- Bileşen Galerisi (ComponentGalleryView) — geliştirici aracı, muhtemelen kasıtlı
- Senkron Penceresi (SyncWindow) — kavram olarak masaüstüne özgü (çevrimdışı çalışma), web'de anlamı yok

## ⚠️ Yapısal fark notu — Yetkiler
Masaüstünde **tek ekran, sekmeli**; web'de **3 ayrı sayfa**. Bu bir "eksik" olmayabilir (web'de URL
bazlı gezinme daha doğal) ama davranış aynı mı (aynı yetkiyi iki yerden farklı gösterip göstermediği)
denetlenmeli. Parite sırasına göre ele alınacak.

## Öneri: başlangıç sırası
Kullanıcının "en kritik ekranım" dediği **Araçlar** ve en sık kullanılan **Malzemeler**'den başlamak
mantıklı. İkisi de zaten düzenleme kilidi almış ekranlar, üzerlerinde tazeyiz.
