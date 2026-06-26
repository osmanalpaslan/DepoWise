// Araç sayacı kuralları — .NET MeterRule ile aynı. Sayaç geriye gidemez.

export class MeterBackwardError extends Error {}

// İleri-yön: yeni > mevcut ise ilerler (true); aksi halde dokunmaz (false, geçmiş kaydı engellemez).
export const shouldAdvance = (current: number, incoming: number): boolean => incoming > current;

// Doğrudan düzenleme: yeni < mevcut ise geçersiz.
export const isValidDirectSet = (current: number, incoming: number): boolean => incoming >= current;

export function setMeter(current: number, incoming: number): number {
  if (!isValidDirectSet(current, incoming)) {
    throw new MeterBackwardError(`Sayaç geriye alınamaz: mevcut ${current}, girilen ${incoming}.`);
  }
  return incoming;
}

export interface VehicleTemplate {
  vehicleTypeId?: string | null;
  categoryId?: string | null;
  brandId?: string | null;
  vehicleModelId?: string | null;
  productionYear?: number | null;
  defaultMeterUnit?: string;
}

export interface VehicleDraft {
  vehicleTypeId?: string | null;
  categoryId?: string | null;
  brandId?: string | null;
  vehicleModelId?: string | null;
  productionYear?: number | null;
  meterUnit?: string;
}

// Şablon boş alanları doldurur; kullanıcı değeri öncelikli (.NET ApplyTemplate ile aynı).
export function applyTemplate(draft: VehicleDraft, tpl: VehicleTemplate): VehicleDraft {
  return {
    ...draft,
    vehicleTypeId: draft.vehicleTypeId ?? tpl.vehicleTypeId ?? null,
    categoryId: draft.categoryId ?? tpl.categoryId ?? null,
    brandId: draft.brandId ?? tpl.brandId ?? null,
    vehicleModelId: draft.vehicleModelId ?? tpl.vehicleModelId ?? null,
    productionYear: draft.productionYear ?? tpl.productionYear ?? null,
    meterUnit: draft.meterUnit && draft.meterUnit !== "km" ? draft.meterUnit : (tpl.defaultMeterUnit ?? draft.meterUnit ?? "km"),
  };
}
