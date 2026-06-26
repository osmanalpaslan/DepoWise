// Yakıt hesapları — .NET FuelService ile aynı kurallar (saf).

export interface DepotEntry {
  liters: number;
  unitPrice: number;
}
export interface Distribution {
  liters: number;
  unitPrice: number; // snapshot
}

// Depo bakiyesi = tüm girişler − tüm dağıtımlar (tüm zamanlar).
export function depotBalance(entries: DepotEntry[], distributions: Distribution[]): number {
  const inSum = entries.reduce((a, e) => a + e.liters, 0);
  const outSum = distributions.reduce((a, d) => a + d.liters, 0);
  return inSum - outSum;
}

// Güncel yakıt fiyatı = en son depo girişi (girişler eskiden yeniye sıralı varsayılır).
export function currentFuelPrice(entriesOldestFirst: DepotEntry[]): number {
  return entriesOldestFirst.length === 0 ? 0 : entriesOldestFirst[entriesOldestFirst.length - 1]!.unitPrice;
}

// Dağıtım maliyeti snapshot fiyatla; geçmişte güncel fiyat değişse de değişmez.
export const distributionCost = (d: Distribution): number => d.liters * d.unitPrice;

// L/100km tüketim (güvenli: km<=0 ise null → veri kalitesi uyarısı).
export function consumptionPer100Km(liters: number, km: number): number | null {
  if (km <= 0) return null;
  return (liters / km) * 100;
}
