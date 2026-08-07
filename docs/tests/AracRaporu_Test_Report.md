# Araç Raporu — Test Raporu (2026-08-07, Opus 4.8)

**Kapsam:** yeni Araç Raporu (`vehicle`) — "Genel Rapor"un (`general`) yerine. Backend hesaplama + filtreler +
web/masaüstü UI. Ortak rapor mimarisi (Birim 1-4) değişmedi.

## Sonuç özeti
- **Build:** tüm çözüm 0 hata (yalnız önceden var olan uyarılar).
- **Test:** tam paket **642/0** (11 PG atlandı) — 633→642, **+9 VehicleReportTests**. Regresyon yok.
- **ReportingTests** güncellendi (General→VehicleReport; "Araç Raporu"/14 kolon).

## Çalıştırılan senaryolar (VehicleReportTests — doğrudan SQL seed, deterministik)
| # | Senaryo | Doğrulanan |
|---|---|---|
| 1 | KM aracı (2 yakıt fişi + bakım + doğrudan parça) | mesafe=300, litre=150, ort.fiyat=41.33, yakıt=6200, tüketim=0.5, bakım=400, parça=150, toplam=6750, ₺/km=22.5, ad="Ford Cargo" |
| 2 | SAAT iş makinesi (meter_unit=hour) | Sayaç="Saat", saat=60, L/saat=1.33, ₺/saat=60 — hiçbir hesap KM'ye zorlanmadı |
| 3 | Yalnız doğrudan stok çıkışı olan araç | parça=100, toplam=100, km=0 → ₺/km=0 (sıfıra bölme koruması) |
| 4 | Yalnız bakım malzemesi olan araç | bakım=500, toplam=500 |
| 5 | Hiç maliyeti olmayan araç | satır VAR, toplam=0 (tam filo görünürlüğü) |
| 6 | TOPLAM satırı | litre=230, yakıt=9800, bakım=900, parça=250, genel toplam=10950 |
| 7 | Araç Türü filtresi | yalnız seçili tür gelir |
| 8 | Araç filtresi (çoklu) | yalnız seçili araç gelir |
| 9 | Şube filtresi (yetkili admin, açık seçim) | yalnız o şubenin aracı gelir |
| + | Tarih dışı yakıt fişi | geçerli aralıkta ELENİR (mesafe değişmez) |

## Performans
- **N+1 kaldırıldı:** eski `general` malzeme maliyetini araç başına korelasyonlu alt-sorguyla hesaplıyordu
  (N araç = N tarama). Yeni: yakıt + bakım-malzeme + doğrudan-parça araç bazında **önceden toplanmış 3 türetilmiş
  tabloya 1:1 LEFT JOIN** → tek geçiş, dış GROUP BY yok, satır çarpımı (fan-out) yok.
- İndeksler: `fuel_distributions(vehicle_id, distribution_date)`, `maintenance_materials(maintenance_id)` mevcut →
  türetilmiş toplamalar indeksli. Sonuç kümesi = araç sayısı (küçük). Tarih penceresi maliyetlere uygulanır.
- CAST yalnız TEXT-saklı sayısal alanlarda (şema gereği); her değer bir kez cast edilir.

## Görsel doğrulama
- Web: build + backend testleri yeşil; filtre UI (araç arama+Tümünü Seç/Kaldır, araç türü) eklendi.
- Masaüstü: Avalonia önizlemesi yok → binding/davranış incelendi; görsel doğrulama kullanıcıda (1.0.113).

## Riskler / notlar
- Çoklu para birimi: raporlar TL varsayar (baba TR tek-para). fx dönüşümü gelecekte (mimari hazır).
- İşçilik/sigorta/kasko/amortisman kapsam dışı (kullanıcı kararı); derived-table deseniyle kolay eklenebilir.
